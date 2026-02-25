// ---------------------------------------------------------------------------
// Tests  Repository Layer (Database Integration)
// ---------------------------------------------------------------------------
// Verifies architecture invariants against a real PostgreSQL database:
//   - UPSERT: storeOraclePoint updates, never duplicates
//   - Retention: deleteExpiredData uses hour_start_utc, not created_at
//   - Playable buckets: exactly 24, ascending, excludes newest/oldest
//
// Requires TEST_DATABASE_URL environment variable.
// Skips gracefully if not set.
// ---------------------------------------------------------------------------

import { describe, it, expect, beforeAll, afterAll, beforeEach } from 'vitest';
import { Pool } from 'pg';
import {
  storeOraclePoint,
  getOraclePointsForHour,
  storeTrackBucket,
  getPlayableTrackBuckets,
  deleteExpiredData,
} from '../src/db/repository';
import type {
  OraclePointInput,
  TrackBucketInput,
  NormalizationMeta,
} from '../src/types/oracle.types';
import { floorToHour } from '../src/utils/time';

// ---------------------------------------------------------------------------
// Setup
// ---------------------------------------------------------------------------

const TEST_DATABASE_URL = process.env['TEST_DATABASE_URL'];

const shouldRun = !!TEST_DATABASE_URL;

const describeDB = shouldRun ? describe : describe.skip;

let pool: Pool;

const ONE_HOUR_MS = 60 * 60 * 1000;

function makePoint(tokenMint: string, hourStart: Date, price: number): OraclePointInput {
  return {
    token_mint: tokenMint,
    hour_start_utc: hourStart,
    oracle_price: price,
    publish_time: new Date(),
    source_slot: 12345,
  };
}

function makeBucket(tokenMint: string, hourStart: Date, version: string = '1'): TrackBucketInput {
  const blob = Buffer.from(new Int16Array([100, 200, 300, 400]).buffer);
  const meta: NormalizationMeta = {
    min_price: 90,
    max_price: 150,
    scale_factor: 1.0,
    point_count: 4,
    slope_clamp_degrees: 75,
    version,
  };
  const crypto = require('crypto');
  const hash = crypto.createHash('sha256').update(blob).digest('hex');

  return {
    token_mint: tokenMint,
    track_hour_start_utc: hourStart,
    track_version: version,
    normalized_points_blob: blob,
    point_count: 4,
    normalization_meta: meta,
    track_hash: hash,
  };
}

describeDB('Repository  Database Integration', () => {
  beforeAll(async () => {
    pool = new Pool({
      connectionString: TEST_DATABASE_URL,
      max: 5,
    });

    // Ensure tables exist (run schema DDL)
    const fs = require('fs');
    const path = require('path');
    const schemaPath = path.join(__dirname, '..', 'src', 'db', 'schema.sql');
    const schemaSql = fs.readFileSync(schemaPath, 'utf-8');
    await pool.query(schemaSql);
  });

  afterAll(async () => {
    if (pool) await pool.end();
  });

  beforeEach(async () => {
    // Clean slate for each test
    await pool.query('DELETE FROM track_buckets');
    await pool.query('DELETE FROM oracle_hourly_points');
  });

  // =========================================================================
  // UPSERT Behaviour
  // =========================================================================

  describe('storeOraclePoint()  UPSERT invariant', () => {
    it('inserts a new row when none exists for the token+hour', async () => {
      const hour = new Date('2026-02-24T10:00:00.000Z');
      const point = makePoint('TOKEN_A', hour, 100.5);

      await storeOraclePoint(pool, point);

      const stored = await getOraclePointsForHour(pool, 'TOKEN_A', hour);
      expect(stored).not.toBeNull();
      expect(stored!.oracle_price).toBe(100.5);
    });

    it('updates (not duplicates) when storing the same token+hour twice', async () => {
      const hour = new Date('2026-02-24T10:00:00.000Z');

      await storeOraclePoint(pool, makePoint('TOKEN_A', hour, 100.0));
      await storeOraclePoint(pool, makePoint('TOKEN_A', hour, 200.0));

      // Must have exactly 1 row, not 2
      const result = await pool.query(
        'SELECT COUNT(*) AS cnt FROM oracle_hourly_points WHERE token_mint = $1 AND hour_start_utc = $2',
        ['TOKEN_A', hour],
      );
      expect(parseInt(result.rows[0].cnt, 10)).toBe(1);

      // Price must be the LATEST value
      const stored = await getOraclePointsForHour(pool, 'TOKEN_A', hour);
      expect(stored!.oracle_price).toBe(200.0);
    });

    it('does not affect other tokens when upserting', async () => {
      const hour = new Date('2026-02-24T10:00:00.000Z');

      await storeOraclePoint(pool, makePoint('TOKEN_A', hour, 100.0));
      await storeOraclePoint(pool, makePoint('TOKEN_B', hour, 300.0));
      await storeOraclePoint(pool, makePoint('TOKEN_A', hour, 200.0));

      const a = await getOraclePointsForHour(pool, 'TOKEN_A', hour);
      const b = await getOraclePointsForHour(pool, 'TOKEN_B', hour);

      expect(a!.oracle_price).toBe(200.0);
      expect(b!.oracle_price).toBe(300.0);
    });
  });

  // =========================================================================
  // Retention Logic
  // =========================================================================

  describe('deleteExpiredData()  retention invariant', () => {
    it('deletes oracle points older than retention window based on hour_start_utc', async () => {
      const now = floorToHour(new Date());
      const oldHour = new Date(now.getTime() - 30 * ONE_HOUR_MS); // 30h ago
      const recentHour = new Date(now.getTime() - 10 * ONE_HOUR_MS); // 10h ago

      await storeOraclePoint(pool, makePoint('TOKEN_A', oldHour, 100));
      await storeOraclePoint(pool, makePoint('TOKEN_A', recentHour, 200));

      const { deletedPoints } = await deleteExpiredData(pool, 26);

      expect(deletedPoints).toBe(1);

      // Old row gone
      const oldRow = await getOraclePointsForHour(pool, 'TOKEN_A', oldHour);
      expect(oldRow).toBeNull();

      // Recent row survives
      const recentRow = await getOraclePointsForHour(pool, 'TOKEN_A', recentHour);
      expect(recentRow).not.toBeNull();
    });

    it('deletes track buckets older than retention window', async () => {
      const now = floorToHour(new Date());
      const oldHour = new Date(now.getTime() - 30 * ONE_HOUR_MS);
      const recentHour = new Date(now.getTime() - 10 * ONE_HOUR_MS);

      await storeTrackBucket(pool, makeBucket('TOKEN_A', oldHour));
      await storeTrackBucket(pool, makeBucket('TOKEN_A', recentHour));

      const { deletedBuckets } = await deleteExpiredData(pool, 26);

      expect(deletedBuckets).toBe(1);
    });

    it('does not delete data within the retention window', async () => {
      const now = floorToHour(new Date());

      // Insert hours within 26h window
      for (let i = 1; i <= 5; i++) {
        const hour = new Date(now.getTime() - i * ONE_HOUR_MS);
        await storeOraclePoint(pool, makePoint('TOKEN_A', hour, 100 + i));
      }

      const { deletedPoints } = await deleteExpiredData(pool, 26);
      expect(deletedPoints).toBe(0);
    });
  });

  // =========================================================================
  // Playable Bucket Rules
  // =========================================================================

  describe('getPlayableTrackBuckets()  rolling window invariant', () => {
    it('returns buckets in ascending order by track_hour_start_utc', async () => {
      const now = floorToHour(new Date());

      // Insert 26 buckets covering a full window
      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
      }

      const buckets = await getPlayableTrackBuckets(pool, 'TOKEN_A');

      for (let i = 1; i < buckets.length; i++) {
        const prev = buckets[i - 1].track_hour_start_utc.getTime();
        const curr = buckets[i].track_hour_start_utc.getTime();
        expect(curr).toBeGreaterThan(prev);
      }
    });

    it('excludes the newest and oldest buckets', async () => {
      const now = floorToHour(new Date());

      const hours: Date[] = [];
      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        hours.push(hour);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
      }

      const oldest = hours[0];
      const newest = hours[hours.length - 1];

      const buckets = await getPlayableTrackBuckets(pool, 'TOKEN_A');

      const bucketTimes = buckets.map((b) => b.track_hour_start_utc.getTime());

      expect(bucketTimes).not.toContain(oldest.getTime());
      expect(bucketTimes).not.toContain(newest.getTime());
    });

    it('returns exactly 24 playable buckets from a full 26-hour window', async () => {
      const now = floorToHour(new Date());

      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
      }

      const buckets = await getPlayableTrackBuckets(pool, 'TOKEN_A');

      expect(buckets.length).toBe(24);
    });

    it('returns empty array when no buckets exist', async () => {
      const buckets = await getPlayableTrackBuckets(pool, 'NONEXISTENT');

      expect(buckets).toEqual([]);
    });

    it('does not mix tokens  returns only requested token', async () => {
      const now = floorToHour(new Date());

      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
        await storeTrackBucket(pool, makeBucket('TOKEN_B', hour));
      }

      const bucketsA = await getPlayableTrackBuckets(pool, 'TOKEN_A');
      const bucketsB = await getPlayableTrackBuckets(pool, 'TOKEN_B');

      expect(bucketsA.every((b) => b.token_mint === 'TOKEN_A')).toBe(true);
      expect(bucketsB.every((b) => b.token_mint === 'TOKEN_B')).toBe(true);
    });
  });
});
