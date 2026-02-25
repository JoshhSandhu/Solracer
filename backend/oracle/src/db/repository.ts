// ---------------------------------------------------------------------------
// Oracle Pipeline Repository Layer
// ---------------------------------------------------------------------------
// All database operations for oracle points and track buckets
// Every query uses has parameters to prevent SQL injection
// ---------------------------------------------------------------------------

import type { Pool } from 'pg';
import type {
  OracleHourlyPoint,
  OraclePointInput,
  TrackBucket,
  TrackBucketInput,
  PlayableTrackBucket,
  NormalizationMeta,
} from '../types/oracle.types';

// ===== Oracle Hourly Points =====

/*
 Upsert a single oracle price sample
 overwrites with the latest data without erroring
*/
export async function storeOraclePoint(
  pool: Pool,
  data: OraclePointInput,
): Promise<void> {
  const sql = `
    INSERT INTO oracle_hourly_points
      (token_mint, hour_start_utc, oracle_price, publish_time, source_slot)
    VALUES ($1, $2, $3, $4, $5)
    ON CONFLICT (token_mint, hour_start_utc)
    DO UPDATE SET
      oracle_price  = EXCLUDED.oracle_price,
      publish_time  = EXCLUDED.publish_time,
      source_slot   = EXCLUDED.source_slot
  `;

  await pool.query(sql, [
    data.token_mint,
    data.hour_start_utc,
    data.oracle_price,
    data.publish_time,
    data.source_slot,
  ]);
}

/*
 Retrieve oracle points for a specific token + hour
*/
export async function getOraclePointsForHour(
  pool: Pool,
  tokenMint: string,
  hourStart: Date,
): Promise<OracleHourlyPoint | null> {
  const sql = `
    SELECT token_mint, hour_start_utc, oracle_price,
           publish_time, source_slot, created_at
    FROM oracle_hourly_points
    WHERE token_mint = $1
      AND hour_start_utc = $2
  `;

  const result = await pool.query(sql, [tokenMint, hourStart]);
  if (result.rows.length === 0) return null;

  return result.rows[0] as OracleHourlyPoint;
}

/*
 Get the most recent stored hour for a token
 Used by the ingestion worker startup catch-up logic to detect
 gaps and backfill missing hours
*/
export async function getLatestOracleHour(
  pool: Pool,
  tokenMint: string,
): Promise<Date | null> {
  const sql = `
    SELECT hour_start_utc
    FROM oracle_hourly_points
    WHERE token_mint = $1
    ORDER BY hour_start_utc DESC
    LIMIT 1
  `;

  const result = await pool.query(sql, [tokenMint]);
  if (result.rows.length === 0) return null;

  return result.rows[0].hour_start_utc as Date;
}

// ===== Track Buckets =====

/*
 Insert a generated track bucket
 Uses `ON CONFLICT DO UPDATE` so re-generation with the same
 version overwrites cleanly
*/
export async function storeTrackBucket(
  pool: Pool,
  data: TrackBucketInput,
): Promise<void> {
  const sql = `
    INSERT INTO track_buckets
      (token_mint, track_hour_start_utc, track_version,
       normalized_points_blob, point_count, normalization_meta, track_hash)
    VALUES ($1, $2, $3, $4, $5, $6, $7)
    ON CONFLICT (token_mint, track_hour_start_utc, track_version)
    DO UPDATE SET
      normalized_points_blob = EXCLUDED.normalized_points_blob,
      point_count            = EXCLUDED.point_count,
      normalization_meta     = EXCLUDED.normalization_meta,
      track_hash             = EXCLUDED.track_hash
  `;

  await pool.query(sql, [
    data.token_mint,
    data.track_hour_start_utc,
    data.track_version,
    data.normalized_points_blob,
    data.point_count,
    JSON.stringify(data.normalization_meta),
    data.track_hash,
  ]);
}

/*
 Return the 24 playable track buckets for a token
 Results are deterministically ordered by `track_hour_start_utc ASC`
*/
export async function getPlayableTrackBuckets(
  pool: Pool,
  tokenMint: string,
  retentionHours: number,
  trackVersion: string,
): Promise<PlayableTrackBucket[]> {
  const sql = `
    WITH window AS (
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
      FROM window
    )
    SELECT w.token_mint,
           w.track_hour_start_utc,
           w.track_version,
           w.point_count,
           w.track_hash,
           w.normalized_points_blob,
           w.normalization_meta
    FROM window w, bounds b
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
  }));
}

// ===== Retention / Cleanup =====

/*
 Delete oracle points and track buckets older than the retention window
 Retention is based on `hour_start_utc`, NOT `created_at`
 This prevents drift caused by ingestion delays or catch-up runs
*/
export async function deleteExpiredData(
  pool: Pool,
  retentionHours: number,
): Promise<{ deletedPoints: number; deletedBuckets: number }> {
  // Pre-compute cutoff in JS so the query uses `WHERE col < $1` form
  // This is index-friendly and avoids string interpolation in SQL
  const cutoff = new Date(Date.now() - retentionHours * 60 * 60 * 1000);

  const pointsResult = await pool.query(
    'DELETE FROM oracle_hourly_points WHERE hour_start_utc < $1',
    [cutoff],
  );

  const bucketsResult = await pool.query(
    'DELETE FROM track_buckets WHERE track_hour_start_utc < $1',
    [cutoff],
  );

  return {
    deletedPoints: pointsResult.rowCount ?? 0,
    deletedBuckets: bucketsResult.rowCount ?? 0,
  };
}
