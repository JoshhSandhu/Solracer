// ---------------------------------------------------------------------------
// Oracle Pipeline  Repository Layer (Tick-Based)
// ---------------------------------------------------------------------------
// All database operations for oracle ticks and track buckets.
// Every query uses parameterized queries to prevent SQL injection.
// ---------------------------------------------------------------------------

import type { Pool } from 'pg';
import type {
  OracleTick,
  OracleTickInput,
  TrackBucketInput,
  PlayableTrackBucket,
  NormalizationMeta,
} from '../types/oracle.types';
import { ONE_HOUR_MS } from '../constants';

// ===== Oracle Ticks =====

/**
 * Upsert a single oracle tick.
 * ON CONFLICT overwrites with latest data without erroring.
 */
export async function storeOracleTick(
  pool: Pool,
  data: OracleTickInput,
): Promise<void> {
  const sql = `
    INSERT INTO oracle_ticks
      (token_mint, tick_time, oracle_price, publish_time, source_slot)
    VALUES ($1, $2, $3, $4, $5)
    ON CONFLICT (token_mint, tick_time)
    DO UPDATE SET
      oracle_price  = EXCLUDED.oracle_price,
      publish_time  = EXCLUDED.publish_time,
      source_slot   = EXCLUDED.source_slot
  `;

  await pool.query(sql, [
    data.token_mint,
    data.tick_time,
    data.oracle_price,
    data.publish_time,
    data.source_slot,
  ]);
}

/**
 * Batch-upsert multiple oracle ticks in a single query.
 * Uses unnest() with typed arrays to send all rows in one round-trip,
 * drastically reducing pg protocol overhead (response packets).
 *
 * Falls back to single-row upsert if batch is empty.
 */
export async function storeOracleTicksBatch(
  pool: Pool,
  ticks: OracleTickInput[],
): Promise<void> {
  if (ticks.length === 0) return;

  // Deduplicate by (token_mint, tick_time) PostgreSQL's ON CONFLICT DO UPDATE
  // throws "cannot affect a row a second time" if the same key appears more than
  // once within the same batch. Keep the last entry (matches single-row UPSERT semantics).
  const seen = new Map<string, OracleTickInput>();
  for (const t of ticks) {
    const key = `${t.token_mint}|${t.tick_time.getTime()}`;
    seen.set(key, t); // later entries overwrite earlier ones
  }
  const deduplicated = [...seen.values()];

  // For a single tick after dedup, use the simpler query
  if (deduplicated.length === 1) {
    return storeOracleTick(pool, deduplicated[0]);
  }

  const tokenMints: string[] = [];
  const tickTimes: Date[] = [];
  const oraclePrices: number[] = [];
  const publishTimes: Date[] = [];
  const sourceSlots: number[] = [];

  for (const t of deduplicated) {
    tokenMints.push(t.token_mint);
    tickTimes.push(t.tick_time);
    oraclePrices.push(t.oracle_price);
    publishTimes.push(t.publish_time);
    sourceSlots.push(t.source_slot);
  }

  const sql = `
    INSERT INTO oracle_ticks
      (token_mint, tick_time, oracle_price, publish_time, source_slot)
    SELECT * FROM unnest(
      $1::text[],
      $2::timestamptz[],
      $3::double precision[],
      $4::timestamptz[],
      $5::bigint[]
    )
    ON CONFLICT (token_mint, tick_time)
    DO UPDATE SET
      oracle_price  = EXCLUDED.oracle_price,
      publish_time  = EXCLUDED.publish_time,
      source_slot   = EXCLUDED.source_slot
  `;

  await pool.query(sql, [
    tokenMints,
    tickTimes,
    oraclePrices,
    publishTimes,
    sourceSlots,
  ]);
}

/**
 * Get all ticks for a token within a specific hour window.
 * Returns ticks sorted by tick_time ASC (deterministic ordering).
 */
export async function getTicksForHour(
  pool: Pool,
  tokenMint: string,
  hourStart: Date,
): Promise<OracleTick[]> {
  const hourEnd = new Date(hourStart.getTime() + ONE_HOUR_MS);

  const sql = `
    SELECT token_mint, tick_time, oracle_price,
           publish_time, source_slot, created_at
    FROM oracle_ticks
    WHERE token_mint = $1
      AND tick_time >= $2
      AND tick_time < $3
    ORDER BY tick_time ASC
  `;

  const result = await pool.query(sql, [tokenMint, hourStart, hourEnd]);
  return result.rows as OracleTick[];
}

/**
 * Get the most recent tick time for a token.
 * Used for gap detection logging.
 */
export async function getLatestTickTime(
  pool: Pool,
  tokenMint: string,
): Promise<Date | null> {
  const sql = `
    SELECT tick_time
    FROM oracle_ticks
    WHERE token_mint = $1
    ORDER BY tick_time DESC
    LIMIT 1
  `;

  const result = await pool.query(sql, [tokenMint]);
  if (result.rows.length === 0) return null;

  return result.rows[0].tick_time as Date;
}

// ===== Track Buckets =====

/**
 * Insert a generated track bucket.
 * Uses ON CONFLICT DO UPDATE so re-generation with the same
 * version overwrites cleanly.
 */
export async function storeTrackBucket(
  pool: Pool,
  data: TrackBucketInput,
): Promise<void> {
  // Validate difficulty is 0, 1, or 2 before insert
  const difficulty = (data.difficulty >= 0 && data.difficulty <= 2) ? data.difficulty : 1;

  const sql = `
    INSERT INTO track_buckets
      (token_mint, track_hour_start_utc, track_version,
       normalized_points_blob, point_count, normalization_meta, track_hash,
       difficulty)
    VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
    ON CONFLICT (token_mint, track_hour_start_utc, track_version)
    DO UPDATE SET
      normalized_points_blob = EXCLUDED.normalized_points_blob,
      point_count            = EXCLUDED.point_count,
      normalization_meta     = EXCLUDED.normalization_meta,
      track_hash             = EXCLUDED.track_hash,
      difficulty             = EXCLUDED.difficulty
  `;

  await pool.query(sql, [
    data.token_mint,
    data.track_hour_start_utc,
    data.track_version,
    data.normalized_points_blob,
    data.point_count,
    JSON.stringify(data.normalization_meta),
    data.track_hash,
    difficulty,
  ]);
}

/**
 * Check if a track bucket already exists for a given token+hour+version.
 * Used for crash-safe track generation (only generate if missing).
 */
export async function trackBucketExists(
  pool: Pool,
  tokenMint: string,
  hourStart: Date,
  trackVersion: string,
): Promise<boolean> {
  const sql = `
    SELECT 1 FROM track_buckets
    WHERE token_mint = $1
      AND track_hour_start_utc = $2
      AND track_version = $3
    LIMIT 1
  `;

  const result = await pool.query(sql, [tokenMint, hourStart, trackVersion]);
  return result.rows.length > 0;
}

/**
 * Return the 24 playable track buckets for a token.
 * Results are deterministically ordered by track_hour_start_utc ASC.
 */
export async function getPlayableTrackBuckets(
  pool: Pool,
  tokenMint: string,
  retentionHours: number,
  trackVersion: string,
): Promise<PlayableTrackBucket[]> {
  const sql = `
    WITH valid_window AS (
      SELECT *
      FROM track_buckets
      WHERE token_mint = $1
        AND track_hour_start_utc > now() - ($2::int * interval '1 hour')
        AND track_version = $3
    ),
    bounds AS (
      SELECT
        MIN(track_hour_start_utc) AS oldest,
        MAX(track_hour_start_utc) AS newest
      FROM valid_window
    )
    SELECT w.token_mint,
           w.track_hour_start_utc,
           w.track_version,
           w.point_count,
           w.track_hash,
           w.normalized_points_blob,
           w.normalization_meta,
           w.difficulty
    FROM valid_window w, bounds b
    WHERE w.track_hour_start_utc > b.oldest
      AND w.track_hour_start_utc < b.newest
    ORDER BY w.track_hour_start_utc ASC
  `;

  const result = await pool.query(sql, [tokenMint, retentionHours, trackVersion]);

  return result.rows.map((row) => ({
    token_mint: row.token_mint as string,
    track_hour_start_utc: row.track_hour_start_utc as Date,
    track_version: row.track_version as string,
    point_count: row.point_count as number,
    track_hash: row.track_hash as string,
    normalized_points_blob: row.normalized_points_blob as Buffer,
    normalization_meta: row.normalization_meta as NormalizationMeta,
    difficulty: (row.difficulty as number) ?? 1,
  }));
}

// ===== Retention / Cleanup =====

/**
 * Delete oracle ticks and track buckets older than the retention window.
 * Retention is based on tick_time / track_hour_start_utc, NOT created_at.
 */
export async function deleteExpiredData(
  pool: Pool,
  retentionHours: number,
): Promise<{ deletedTicks: number; deletedBuckets: number }> {
  const cutoff = new Date(Date.now() - retentionHours * ONE_HOUR_MS);

  const ticksResult = await pool.query(
    'DELETE FROM oracle_ticks WHERE tick_time < $1',
    [cutoff],
  );

  const bucketsResult = await pool.query(
    'DELETE FROM track_buckets WHERE track_hour_start_utc < $1',
    [cutoff],
  );

  return {
    deletedTicks: ticksResult.rowCount ?? 0,
    deletedBuckets: bucketsResult.rowCount ?? 0,
  };
}
