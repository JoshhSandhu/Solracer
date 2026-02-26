// ---------------------------------------------------------------------------
// Oracle Pipeline  Centralised Constants
// ---------------------------------------------------------------------------
// Values here are committed to on-chain state via ER sessions.
// Change with extreme caution  in-flight races use pinned versions.
// ---------------------------------------------------------------------------

/**
 * Current track normalization algorithm version.
 *
 * Stored in `track_buckets.track_version` and committed to ER race
 * session metadata as `program_version` / `track_version`.
 *
 * Bump this when normalization logic changes to ensure old and new
 * tracks are never mixed in the same race.
 */
export const TRACK_VERSION = '1';

/**
 * Random walk step size for terrain generation.
 * Each point is offset from the previous by `(prng() - 0.5) * TERRAIN_STEP_SIZE`.
 */
export const TERRAIN_STEP_SIZE = 0.02;

/**
 * Maximum allowed delta between consecutive normalized Y-values.
 * Applied AFTER normalization to prevent unplayable cliff sections.
 * 0.03 means ~33 steps to traverse the full 0→1 range.
 */
export const MAX_DELTA_PER_STEP = 0.03;

/**
 * Soft clamp boundaries for raw terrain before normalization.
 * Prevents runaway random walks from compressing normalization.
 */
export const TERRAIN_SOFT_CLAMP = 2.0;

/**
 * Number of smoothing passes applied to raw terrain.
 */
export const SMOOTHING_PASSES = 2;

/**
 * Window size for moving average smoothing (must be odd).
 * 5 = average of [i-2, i-1, i, i+1, i+2] for interior points.
 */
export const SMOOTHING_WINDOW = 5;

/**
 * One hour in milliseconds  avoids magic numbers in time arithmetic.
 */
export const ONE_HOUR_MS = 60 * 60 * 1000;
