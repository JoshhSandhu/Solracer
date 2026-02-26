// ---------------------------------------------------------------------------
// Oracle Pipeline  Track Generator (Tick-Based, Production)
// ---------------------------------------------------------------------------
// Deterministic track generator: track = f(ticks for hour)
//
// Pipeline order (LOCKED  do not reorder):
//   1. Downsample N ticks to TRACK_POINT_COUNT points
//   2. Normalize to [0, 1]
//   3. Delta clamping (MUST occur AFTER normalization)
//   4. Quantize to Int16LE + SHA-256 hash
//
// All arithmetic uses JS `number` (double precision, 64-bit float)
// No float32 anywhere. Future Rust ER verification must use f64
// ---------------------------------------------------------------------------

import * as crypto from 'crypto';
import type {
  OracleTick,
  TrackBucketInput,
  NormalizationMeta,
} from '../types/oracle.types';
import {
  TRACK_VERSION,
  MAX_DELTA_PER_STEP,
} from '../constants';

// ---------------------------------------------------------------------------
// Step 1  Downsample ticks to target point count
// ---------------------------------------------------------------------------
// Deterministic nearest-neighbor downsampling.
// Index clamped to prevent out-of-bounds access.
// Ticks MUST be sorted by tick_time ASC before calling this.
// ---------------------------------------------------------------------------

function downsample(ticks: OracleTick[], targetCount: number): number[] {
  const N = ticks.length;
  const points = new Array<number>(targetCount);

  for (let i = 0; i < targetCount; i++) {
    const index = Math.min(N - 1, Math.floor(i * N / targetCount));
    points[i] = ticks[index].oracle_price;
  }

  return points;
}

// ---------------------------------------------------------------------------
// Step 2  Normalize to [0, 1]
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
    // Edge case: all values identical  flat terrain at 0.5
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
// Step 3  Delta Clamping (MUST occur AFTER normalization)
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
// Step 4  Quantize to Int16LE + SHA-256 Hash
// ---------------------------------------------------------------------------

function quantizeAndHash(y: number[], pointCount: number): { blob: Buffer; hash: string } {
  // LOCKED: Buffer.alloc (zero-initialized, NOT allocUnsafe)
  const blob = Buffer.alloc(pointCount * 2);

  for (let i = 0; i < pointCount; i++) {
    let value = Math.round(y[i] * 32767);

    // Clamp to valid range to guard against float precision edge cases
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
 * Generate a deterministic track bucket from oracle ticks for a given hour.
 *
 * Pipeline: downsample -> normalize -> delta clamp -> quantize + hash
 *
 * @param tokenMint       Token mint address
 * @param hourStart       UTC hour bucket start
 * @param ticks           Oracle ticks for this hour (sorted by tick_time ASC)
 * @param trackPointCount Target number of output points (default 1000)
 */
export async function generateTrackBucket(
  tokenMint: string,
  hourStart: Date,
  ticks: OracleTick[],
  trackPointCount: number,
): Promise<TrackBucketInput> {
  if (ticks.length === 0) {
    throw new Error('generateTrackBucket() requires at least one tick');
  }

  // Step 1: Downsample N ticks to trackPointCount points
  const rawPoints = downsample(ticks, trackPointCount);

  // Step 2: Normalize to [0, 1]
  const { normalized, min, max } = normalize(rawPoints);

  // Step 3: Delta clamp (AFTER normalization)
  const clamped = clampDeltas(normalized);

  // Step 4: Quantize + hash
  const { blob, hash } = quantizeAndHash(clamped, trackPointCount);

  const meta: NormalizationMeta = {
    min_price: min,
    max_price: max,
    scale_factor: max === min ? 0 : 1 / (max - min),
    point_count: trackPointCount,
    max_delta_per_step: MAX_DELTA_PER_STEP,
    source_tick_count: ticks.length,
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
