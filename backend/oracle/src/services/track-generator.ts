// ---------------------------------------------------------------------------
// Oracle Pipeline  Track Generator (Stub)
// ---------------------------------------------------------------------------
// Track normalization pipeline will be implemented in a future pass.
// This file defines the interface so the ingestion worker can wire it up.
// ---------------------------------------------------------------------------

import type {
  OracleHourlyPoint,
  TrackBucketInput,
} from '../types/oracle.types';

/**
 * Generate a deterministic track bucket from oracle data for a given hour.
 *
 * **NOT IMPLEMENTED**  Normalization pipeline deferred per project plan.
 *
 * Future implementation will:
 *   1. Interpolate oracle prices to fixed `TRACK_POINT_COUNT` points.
 *   2. Normalize Y-values to 0-1 range.
 *   3. Apply slope clamping.
 *   4. Quantize to int16.
 *   5. Compute SHA-256 track_hash over the blob.
 *   6. Return a complete `TrackBucketInput`.
 *
 * @param tokenMint       Token mint address
 * @param hourStart       UTC hour bucket start
 * @param oraclePoints    Source oracle data for the hour
 * @param trackPointCount Target number of normalized points
 */
export async function generateTrackBucket(
  _tokenMint: string,
  _hourStart: Date,
  _oraclePoints: OracleHourlyPoint[],
  _trackPointCount: number,
): Promise<TrackBucketInput> {
  throw new Error(
    'generateTrackBucket() is not implemented. ' +
      'Track normalization pipeline will be added in a future pass.',
  );
}
