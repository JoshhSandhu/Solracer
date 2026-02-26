// ---------------------------------------------------------------------------
// Oracle Pipeline  Track Generator (Production)
// ---------------------------------------------------------------------------
// Deterministic terrain generator: track = f(token, hour, price)
//
// Oracle price seeds the terrain shape; it does not simulate price movement.
// This is architecturally honest and ER-verification compatible.
//
// Pipeline order (LOCKED do not reorder):
//   1. Seed generation
//   2. Seeded terrain generation (mulberry32 PRNG)
//   3. Smoothing (2-pass non-mutating moving average)
//   4. Normalization to [0, 1]
//   5. Delta clamping (MUST occur AFTER normalization)
//   6. Quantization to Int16LE + SHA-256 hash
//
// All arithmetic uses JS `number` (double precision, 64-bit float)
// No float32 anywhere. Future Rust ER verification must use f64
// ---------------------------------------------------------------------------

import * as crypto from 'crypto';
import type {
  OracleHourlyPoint,
  TrackBucketInput,
  NormalizationMeta,
} from '../types/oracle.types';
import {
  TRACK_VERSION,
  TERRAIN_STEP_SIZE,
  MAX_DELTA_PER_STEP,
  TERRAIN_SOFT_CLAMP,
  SMOOTHING_PASSES,
} from '../constants';

// ---------------------------------------------------------------------------
// Frozen PRNG  Mulberry32
// ---------------------------------------------------------------------------
// DO NOT CHANGE THIS EVER. This is part of the protocol.
// Future Rust ER verification depends on this exact implementation.
// ---------------------------------------------------------------------------

function mulberry32(a: number): () => number {
  return function (): number {
    a |= 0;
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// ---------------------------------------------------------------------------
// Step 1  Deterministic Seed
// ---------------------------------------------------------------------------

function computeSeed(tokenMint: string, hourStart: Date, oraclePrice: number): number {
  // Pipe delimiters prevent ambiguous concatenation collisions
  const seedStr = tokenMint + '|' + hourStart.toISOString() + '|' + oraclePrice.toFixed(8);
  const seedBuffer = crypto.createHash('sha256').update(seedStr).digest();
  // LOCKED: Little-Endian, offset 0
  return seedBuffer.readUInt32LE(0);
}

// ---------------------------------------------------------------------------
// Step 2  Seeded Terrain Generation
// ---------------------------------------------------------------------------

function generateRawTerrain(prng: () => number, pointCount: number): number[] {
  const y = new Array<number>(pointCount);
  y[0] = 0.5;

  for (let i = 1; i < pointCount; i++) {
    y[i] = y[i - 1] + (prng() - 0.5) * TERRAIN_STEP_SIZE;

    // Soft clamp to prevent runaway walks
    if (y[i] > TERRAIN_SOFT_CLAMP) y[i] = TERRAIN_SOFT_CLAMP;
    if (y[i] < -TERRAIN_SOFT_CLAMP) y[i] = -TERRAIN_SOFT_CLAMP;
  }

  return y;
}

// ---------------------------------------------------------------------------
// Step 3  Smoothing (Non-Mutating Moving Average)
// ---------------------------------------------------------------------------
// Edge rules (LOCKED):
//   i=0:       avg(y[0], y[1], y[2])            3 elements
//   i=1:       avg(y[0], y[1], y[2], y[3])      4 elements
//   i=2..N-3:  avg(y[i-2]..y[i+2])              5 elements
//   i=N-2:     avg(y[N-4], y[N-3], y[N-2], y[N-1])  4 elements
//   i=N-1:     avg(y[N-3], y[N-2], y[N-1])      3 elements
// ---------------------------------------------------------------------------

function smooth(y: number[]): number[] {
  const len = y.length;
  const out = new Array<number>(len);

  for (let i = 0; i < len; i++) {
    const lo = Math.max(0, i - 2);
    const hi = Math.min(len - 1, i + 2);

    let sum = 0;
    let count = 0;
    for (let j = lo; j <= hi; j++) {
      sum += y[j];
      count++;
    }
    out[i] = sum / count;
  }

  return out;
}

function applySmoothing(y: number[], passes: number): number[] {
  let current = y;
  for (let p = 0; p < passes; p++) {
    current = smooth(current);
  }
  return current;
}

// ---------------------------------------------------------------------------
// Step 4  Normalize to [0, 1]
// ---------------------------------------------------------------------------

function normalize(y: number[]): { normalized: number[]; min: number; max: number } {
  let min = y[0];
  let max = y[0];
  for (let i = 1; i < y.length; i++) {
    if (y[i] < min) min = y[i];
    if (y[i] > max) max = y[i];
  }

  const normalized = new Array<number>(y.length);

  if (max === min) {
    // Edge case: all values identical → flat terrain at 0.5
    for (let i = 0; i < y.length; i++) {
      normalized[i] = 0.5;
    }
  } else {
    const range = max - min;
    for (let i = 0; i < y.length; i++) {
      normalized[i] = (y[i] - min) / range;
    }
  }

  return { normalized, min, max };
}

// ---------------------------------------------------------------------------
// Step 5  Delta Clamping (MUST occur AFTER normalization)
// ---------------------------------------------------------------------------

function clampDeltas(y: number[]): number[] {
  const clamped = new Array<number>(y.length);
  clamped[0] = y[0];

  for (let i = 1; i < y.length; i++) {
    const delta = y[i] - clamped[i - 1];
    if (delta > MAX_DELTA_PER_STEP) {
      clamped[i] = clamped[i - 1] + MAX_DELTA_PER_STEP;
    } else if (delta < -MAX_DELTA_PER_STEP) {
      clamped[i] = clamped[i - 1] - MAX_DELTA_PER_STEP;
    } else {
      clamped[i] = y[i];
    }
  }

  return clamped;
}

// ---------------------------------------------------------------------------
// Step 6  Quantize to Int16LE + SHA-256 Hash
// ---------------------------------------------------------------------------

function quantizeAndHash(y: number[], pointCount: number): { blob: Buffer; hash: string } {
  // LOCKED: Buffer.alloc (zero-initialized, NOT allocUnsafe)
  const blob = Buffer.alloc(pointCount * 2);

  for (let i = 0; i < pointCount; i++) {
    let value = Math.round(y[i] * 32767);

    // Clamp to valid Int16 range to guard against float precision edge cases
    if (value > 32767) value = 32767;
    if (value < 0) value = 0;

    // LOCKED: Int16LE (little-endian)
    blob.writeInt16LE(value, i * 2);
  }

  // LOCKED: SHA-256 of blob ONLY, lowercase hex (Node.js default)
  const hash = crypto.createHash('sha256').update(blob).digest('hex');

  return { blob, hash };
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Generate a deterministic track bucket from oracle data for a given hour.
 *
 * Pipeline: seed → terrain → smooth → normalize → clamp → quantize → hash
 *
 * @param tokenMint       Token mint address
 * @param hourStart       UTC hour bucket start
 * @param oraclePoints    Source oracle data (uses the first point's price)
 * @param trackPointCount Target number of normalized points (default 1000)
 */
export async function generateTrackBucket(
  tokenMint: string,
  hourStart: Date,
  oraclePoints: OracleHourlyPoint[],
  trackPointCount: number,
): Promise<TrackBucketInput> {
  if (oraclePoints.length === 0) {
    throw new Error('generateTrackBucket() requires at least one oracle point');
  }

  const oraclePrice = oraclePoints[0].oracle_price;

  // Step 1: Seed
  const seed32 = computeSeed(tokenMint, hourStart, oraclePrice);

  // Step 2: Generate raw terrain
  const prng = mulberry32(seed32);
  const raw = generateRawTerrain(prng, trackPointCount);

  // Step 3: Smooth (2-pass non-mutating)
  const smoothed = applySmoothing(raw, SMOOTHING_PASSES);

  // Step 4: Normalize to [0, 1]
  const { normalized, min, max } = normalize(smoothed);

  // Step 5: Delta clamp (AFTER normalization)
  const clamped = clampDeltas(normalized);

  // Step 6: Quantize + hash
  const { blob, hash } = quantizeAndHash(clamped, trackPointCount);

  const meta: NormalizationMeta = {
    min_price: min,
    max_price: max,
    scale_factor: max === min ? 0 : 1 / (max - min),
    point_count: trackPointCount,
    max_delta_per_step: MAX_DELTA_PER_STEP,
    terrain_step_size: TERRAIN_STEP_SIZE,
    version: TRACK_VERSION,
  };

  return {
    token_mint: tokenMint,
    track_hour_start_utc: hourStart,
    track_version: TRACK_VERSION,
    normalized_points_blob: blob,
    point_count: trackPointCount,
    normalization_meta: meta,
    track_hash: hash,
  };
}
