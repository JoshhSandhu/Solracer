// ---------------------------------------------------------------------------
// Track Generator  Determinism Tests (Tick-Based)
// ---------------------------------------------------------------------------
// Verifies the deterministic downsampling pipeline.
// Does NOT require a database connection.
// ---------------------------------------------------------------------------

import { describe, it, expect } from 'vitest';
import { generateTrackBucket } from '../src/services/track-generator';
import type { OracleTick } from '../src/types/oracle.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeTick(
  tokenMint: string,
  tickTime: Date,
  price: number,
): OracleTick {
  return {
    token_mint: tokenMint,
    tick_time: tickTime,
    oracle_price: price,
    publish_time: tickTime,
    source_slot: 123456,
  };
}

function generateTicks(
  tokenMint: string,
  hourStart: Date,
  count: number,
  basePrice: number = 100,
): OracleTick[] {
  const ticks: OracleTick[] = [];
  for (let i = 0; i < count; i++) {
    const tickTime = new Date(hourStart.getTime() + i * 2000);
    // Simulate small price movements
    const price = basePrice + Math.sin(i * 0.1) * 5;
    ticks.push(makeTick(tokenMint, tickTime, parseFloat(price.toFixed(8))));
  }
  return ticks;
}

const TOKEN_A = 'So11111111111111111111111111111111111111112';
const TOKEN_B = 'DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263';
const HOUR_1 = new Date('2026-01-15T10:00:00.000Z');
const HOUR_2 = new Date('2026-01-15T11:00:00.000Z');
const POINT_COUNT = 1000;

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe('Track Generator  Tick Determinism', () => {
  // Test A: Same ticks  identical blob + hash
  it('produces identical blob and hash for identical tick arrays', async () => {
    const ticks = generateTicks(TOKEN_A, HOUR_1, 1800);

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    expect(result1.track_hash).toBe(result2.track_hash);
    expect(result1.normalized_points_blob.equals(result2.normalized_points_blob)).toBe(true);
    expect(result1.point_count).toBe(POINT_COUNT);
    expect(result1.normalized_points_blob.length).toBe(POINT_COUNT * 2);
  });

  // Test B: Different price patterns  different hash
  it('produces different hash for different tick price patterns', async () => {
    // Pattern 1: sine wave with amplitude 5
    const ticks1 = generateTicks(TOKEN_A, HOUR_1, 1800, 100);
    // Pattern 2: different prices (use different amplitude via manual override)
    const ticks2 = generateTicks(TOKEN_A, HOUR_1, 1800, 100).map((t, i) => ({
      ...t,
      oracle_price: parseFloat((100 + Math.sin(i * 0.3) * 20).toFixed(8)),
    }));

    const result1 = await generateTrackBucket(TOKEN_A, HOUR_1, ticks1, POINT_COUNT);
    const result2 = await generateTrackBucket(TOKEN_A, HOUR_1, ticks2, POINT_COUNT);

    expect(result1.track_hash).not.toBe(result2.track_hash);
  });

  // Test C: Downsampling correctness  index clamping
  it('handles downsampling from fewer ticks than target', async () => {
    const ticks = generateTicks(TOKEN_A, HOUR_1, 500); // 500 < 1000

    const result = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    expect(result.point_count).toBe(POINT_COUNT);
    expect(result.normalized_points_blob.length).toBe(POINT_COUNT * 2);
    expect(result.normalization_meta.source_tick_count).toBe(500);
  });

  // Test D: Exact 1800 ticks  standard case
  it('handles standard 1800 ticks correctly', async () => {
    const ticks = generateTicks(TOKEN_A, HOUR_1, 1800);

    const result = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    expect(result.normalization_meta.source_tick_count).toBe(1800);
    expect(result.track_version).toBe('2');
  });

  // Test E: Blob spec validation (Int16LE, values in [0, 32767])
  it('produces valid Int16LE blob with values in [0, 32767]', async () => {
    const ticks = generateTicks(TOKEN_A, HOUR_1, 1800);
    const result = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    expect(result.normalized_points_blob.length).toBe(2000);

    for (let i = 0; i < POINT_COUNT; i++) {
      const value = result.normalized_points_blob.readInt16LE(i * 2);
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThanOrEqual(32767);
    }
  });

  // Test F: Hash is SHA-256 hex lowercase of blob only
  it('track_hash matches SHA-256 of blob', async () => {
    const crypto = require('crypto');
    const ticks = generateTicks(TOKEN_A, HOUR_1, 1800);
    const result = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    const expectedHash = crypto
      .createHash('sha256')
      .update(result.normalized_points_blob)
      .digest('hex');

    expect(result.track_hash).toBe(expectedHash);
    expect(result.track_hash).toBe(result.track_hash.toLowerCase());
  });

  // Test G: Throws on empty ticks
  it('throws when given zero ticks', async () => {
    await expect(
      generateTrackBucket(TOKEN_A, HOUR_1, [], POINT_COUNT),
    ).rejects.toThrow('requires at least one tick');
  });

  // Test H: Single tick  fills all 1000 points with same value
  it('handles single tick (all points same value)', async () => {
    const ticks = [makeTick(TOKEN_A, HOUR_1, 150.0)];
    const result = await generateTrackBucket(TOKEN_A, HOUR_1, ticks, POINT_COUNT);

    // All points should be 0.5 (single value  max === min)
    for (let i = 0; i < POINT_COUNT; i++) {
      const value = result.normalized_points_blob.readInt16LE(i * 2);
      expect(value).toBe(Math.round(0.5 * 32767));
    }
  });
});
