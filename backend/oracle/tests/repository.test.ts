// ---------------------------------------------------------------------------
// Tests  Repository Layer (Database Integration)  Tick Model
// ---------------------------------------------------------------------------
// Verifies architecture invariants against a real PostgreSQL database:
//   - UPSERT: storeOracleTick updates, never duplicates
//   - Retention: deleteExpiredData uses tick_time, not created_at
//   - Playable buckets: exactly 24, ascending, excludes newest/oldest
//   - trackBucketExists: correct existence check
//
// Requires TEST_DATABASE_URL environment variable.
// Skips gracefully if not set.
// ---------------------------------------------------------------------------

import { describe, it, expect, beforeAll, afterAll, beforeEach } from 'vitest';
import { Pool } from 'pg';
import {
  storeOracleTick,
  getTicksForHour,
  getLatestTickTime,
  storeTrackBucket,
  trackBucketExists,
  getPlayableTrackBuckets,
  deleteExpiredData,
} from '../src/db/repository';
import type {
  OracleTickInput,
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
const TICK_INTERVAL_MS = 2000;

function makeTick(tokenMint: string, tickTime: Date, price: number): OracleTickInput {
  return {
    token_mint: tokenMint,
    tick_time: tickTime,
    oracle_price: price,
    publish_time: new Date(),
    source_slot: 12345,
  };
}

function makeBucket(tokenMint: string, hourStart: Date, version: string = '2'): TrackBucketInput {
  const blob = Buffer.from(new Int16Array([100, 200, 300, 400]).buffer);
  const meta: NormalizationMeta = {
    min_price: 90,
    max_price: 150,
    scale_factor: 1.0,
    point_count: 4,
    max_delta_per_step: 0.03,
    source_tick_count: 1800,
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

describeDB('Repository  Database Integration (Tick Model)', () => {
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
    await pool.query('DELETE FROM oracle_ticks');
  });

  // =========================================================================
  // Tick UPSERT
  // =========================================================================

  describe('storeOracleTick()  UPSERT invariant', () => {
    it('inserts a new tick', async () => {
      const tickTime = new Date('2026-02-24T10:00:02.000Z');
      const tick = makeTick('TOKEN_A', tickTime, 100.5);

      await storeOracleTick(pool, tick);

      const ticks = await getTicksForHour(pool, 'TOKEN_A', new Date('2026-02-24T10:00:00.000Z'));
      expect(ticks.length).toBe(1);
      expect(ticks[0].oracle_price).toBe(100.5);
    });

    it('updates (not duplicates) when storing same token+tick_time', async () => {
      const tickTime = new Date('2026-02-24T10:00:02.000Z');

      await storeOracleTick(pool, makeTick('TOKEN_A', tickTime, 100.0));
      await storeOracleTick(pool, makeTick('TOKEN_A', tickTime, 200.0));

      const ticks = await getTicksForHour(pool, 'TOKEN_A', new Date('2026-02-24T10:00:00.000Z'));
      expect(ticks.length).toBe(1);
      expect(ticks[0].oracle_price).toBe(200.0);
    });
  });

  // =========================================================================
  // getTicksForHour
  // =========================================================================

  describe('getTicksForHour()  ordering', () => {
    it('returns ticks sorted by tick_time ASC', async () => {
      const base = new Date('2026-02-24T10:00:00.000Z');

      // Insert in random order
      await storeOracleTick(pool, makeTick('TOKEN_A', new Date(base.getTime() + 6000), 103));
      await storeOracleTick(pool, makeTick('TOKEN_A', new Date(base.getTime() + 2000), 101));
      await storeOracleTick(pool, makeTick('TOKEN_A', new Date(base.getTime() + 4000), 102));

      const ticks = await getTicksForHour(pool, 'TOKEN_A', base);
      expect(ticks.length).toBe(3);

      for (let i = 1; i < ticks.length; i++) {
        expect(ticks[i].tick_time.getTime()).toBeGreaterThan(ticks[i - 1].tick_time.getTime());
      }
    });

    it('does not return ticks from other hours', async () => {
      const hour10 = new Date('2026-02-24T10:00:00.000Z');
      const hour11 = new Date('2026-02-24T11:00:00.000Z');

      await storeOracleTick(pool, makeTick('TOKEN_A', new Date(hour10.getTime() + 2000), 100));
      await storeOracleTick(pool, makeTick('TOKEN_A', new Date(hour11.getTime() + 2000), 200));

      const ticks10 = await getTicksForHour(pool, 'TOKEN_A', hour10);
      expect(ticks10.length).toBe(1);
      expect(ticks10[0].oracle_price).toBe(100);
    });
  });

  // =========================================================================
  // getLatestTickTime
  // =========================================================================

  describe('getLatestTickTime()', () => {
    it('returns null when no ticks exist', async () => {
      const latest = await getLatestTickTime(pool, 'NONEXISTENT');
      expect(latest).toBeNull();
    });

    it('returns the latest tick time', async () => {
      const t1 = new Date('2026-02-24T10:00:02.000Z');
      const t2 = new Date('2026-02-24T10:00:04.000Z');

      await storeOracleTick(pool, makeTick('TOKEN_A', t1, 100));
      await storeOracleTick(pool, makeTick('TOKEN_A', t2, 101));

      const latest = await getLatestTickTime(pool, 'TOKEN_A');
      expect(latest!.getTime()).toBe(t2.getTime());
    });
  });

  // =========================================================================
  // trackBucketExists
  // =========================================================================

  describe('trackBucketExists()', () => {
    it('returns false when no bucket exists', async () => {
      const exists = await trackBucketExists(pool, 'TOKEN_A', new Date('2026-02-24T10:00:00.000Z'), '2');
      expect(exists).toBe(false);
    });

    it('returns true after storing a bucket', async () => {
      const hour = new Date('2026-02-24T10:00:00.000Z');
      await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));

      const exists = await trackBucketExists(pool, 'TOKEN_A', hour, '2');
      expect(exists).toBe(true);
    });
  });

  // =========================================================================
  // Retention Logic
  // =========================================================================

  describe('deleteExpiredData()  retention invariant', () => {
    it('deletes ticks older than retention window based on tick_time', async () => {
      const now = new Date();
      const oldTick = new Date(now.getTime() - 30 * ONE_HOUR_MS);
      const recentTick = new Date(now.getTime() - 10 * ONE_HOUR_MS);

      await storeOracleTick(pool, makeTick('TOKEN_A', oldTick, 100));
      await storeOracleTick(pool, makeTick('TOKEN_A', recentTick, 200));

      const { deletedTicks } = await deleteExpiredData(pool, 26);
      expect(deletedTicks).toBe(1);
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
  });

  // =========================================================================
  // Playable Bucket Rules
  // =========================================================================

  describe('getPlayableTrackBuckets()  rolling window invariant', () => {
    it('returns buckets in ascending order', async () => {
      const now = floorToHour(new Date());

      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
      }

      const buckets = await getPlayableTrackBuckets(pool, 'TOKEN_A', 26, '2');

      for (let i = 1; i < buckets.length; i++) {
        expect(buckets[i].track_hour_start_utc.getTime())
          .toBeGreaterThan(buckets[i - 1].track_hour_start_utc.getTime());
      }
    });

    it('returns exactly 24 playable buckets from a full window', async () => {
      const now = floorToHour(new Date());

      for (let i = 0; i < 26; i++) {
        const hour = new Date(now.getTime() - (25 - i) * ONE_HOUR_MS);
        await storeTrackBucket(pool, makeBucket('TOKEN_A', hour));
      }

      const buckets = await getPlayableTrackBuckets(pool, 'TOKEN_A', 26, '2');
      expect(buckets.length).toBe(24);
    });

    it('returns empty array when no buckets exist', async () => {
      const buckets = await getPlayableTrackBuckets(pool, 'NONEXISTENT', 26, '2');
      expect(buckets).toEqual([]);
    });
  });
});
