// ---------------------------------------------------------------------------
// Track Generator  Determinism Tests
// ---------------------------------------------------------------------------
// These tests verify the locked determinism spec of the terrain generator.
// They do NOT require a database connection.
// ---------------------------------------------------------------------------

import { describe, it, expect } from 'vitest';

// We need to import the function  but it's async and uses crypto,
// which is fine for vitest.
import { generateTrackBucket } from '../src/services/track-generator';
import type { OracleHourlyPoint } from '../src/types/oracle.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeOraclePoint(
  tokenMint: string,
  hourStart: Date,
  price: number,
): OracleHourlyPoint {
  return {
    token_mint: tokenMint,
    hour_start_utc: hourStart,
    oracle_price: price,
    publish_time: hourStart,
    source_slot: 123456,
  };
}

const TOKEN_A = 'So11111111111111111111111111111111111111112';
const TOKEN_B = 'DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263';
const HOUR_1 = new Date('2026-01-15T10:00:00.000Z');
const HOUR_2 = new Date('2026-01-15T11:00:00.000Z');
const PRICE_1 = 125.50;
const PRICE_2 = 130.75;
const POINT_COUNT = 1000;

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe('Track Generator  Determinism', () => {
  // -----------------------------------------------------------------------
  // Test A: Same inputs → identical blob + hash
  // -----------------------------------------------------------------------
  it('produces identical blob and hash for identical inputs', async () => {
    const point = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, [point], POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_A, HOUR_1, [point], POINT_COUNT);

    expect(result1.track_hash).toBe(result2.track_hash);
    expect(result1.normalized_points_blob.equals(result2.normalized_points_blob)).toBe(true);
    expect(result1.point_count).toBe(POINT_COUNT);
    expect(result1.normalized_points_blob.length).toBe(POINT_COUNT * 2); // Int16LE = 2 bytes
  });

  // -----------------------------------------------------------------------
  // Test B: Different hour → different hash
  // -----------------------------------------------------------------------
  it('produces different hash for different hour', async () => {
    const point1 = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);
    const point2 = makeOraclePoint(TOKEN_A, HOUR_2, PRICE_1);

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, [point1], POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_A, HOUR_2, [point2], POINT_COUNT);

    expect(result1.track_hash).not.toBe(result2.track_hash);
  });

  // -----------------------------------------------------------------------
  // Test C: Different token → different hash
  // -----------------------------------------------------------------------
  it('produces different hash for different token', async () => {
    const point1 = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);
    const point2 = makeOraclePoint(TOKEN_B, HOUR_1, PRICE_1);

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, [point1], POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_B, HOUR_1, [point2], POINT_COUNT);

    expect(result1.track_hash).not.toBe(result2.track_hash);
  });

  // -----------------------------------------------------------------------
  // Test D: Different price → different hash
  // -----------------------------------------------------------------------
  it('produces different hash for different price', async () => {
    const point1 = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);
    const point2 = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_2);

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, [point1], POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_A, HOUR_1, [point2], POINT_COUNT);

    expect(result1.track_hash).not.toBe(result2.track_hash);
  });

  // -----------------------------------------------------------------------
  // Test E: Blob spec validation (Int16LE, 2000 bytes, valid range)
  // -----------------------------------------------------------------------
  it('produces a valid Int16LE blob with values in [0, 32767]', async () => {
    const point = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);
    const result = await generateTrackBucket(TOKEN_A, HOUR_1, [point], POINT_COUNT);

    expect(result.normalized_points_blob.length).toBe(2000);

    for (let i = 0; i < POINT_COUNT; i++) {
      const value = result.normalized_points_blob.readInt16LE(i * 2);
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThanOrEqual(32767);
    }
  });

  // -----------------------------------------------------------------------
  // Test F: Hash is SHA-256 hex lowercase of blob only
  // -----------------------------------------------------------------------
  it('track_hash matches SHA-256 of blob', async () => {
    const crypto = require('crypto');
    const point = makeOraclePoint(TOKEN_A, HOUR_1, PRICE_1);
    const result = await generateTrackBucket(TOKEN_A, HOUR_1, [point], POINT_COUNT);

    const expectedHash = crypto
      .createHash('sha256')
      .update(result.normalized_points_blob)
      .digest('hex');

    expect(result.track_hash).toBe(expectedHash);
    // Verify lowercase
    expect(result.track_hash).toBe(result.track_hash.toLowerCase());
  });

  // -----------------------------------------------------------------------
  // Test G: Throws on empty oracle points
  // -----------------------------------------------------------------------
  it('throws when given zero oracle points', async () => {
    await expect(
      generateTrackBucket(TOKEN_A, HOUR_1, [], POINT_COUNT),
    ).rejects.toThrow('requires at least one oracle point');
  });
});
