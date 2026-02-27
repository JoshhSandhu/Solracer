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
export const TRACK_VERSION = '2';

/**
 * Tick sampling interval in milliseconds.
 * Worker aligns tick_time to multiples of this value.
 */
export const TICK_INTERVAL_MS = 2000;

/**
 * Maximum allowed delta between consecutive normalized Y-values.
 * Applied AFTER normalization to prevent unplayable cliff sections.
 * 0.03 means ~33 steps to traverse the full 0-1 range.
 */
export const MAX_DELTA_PER_STEP = 0.03;

/**
 * Minimum number of ticks required to generate a track for an hour.
 * If fewer ticks exist (worker was down), skip track generation.
 * 1200 out of 1800 expected = 67% coverage threshold.
 */
export const MIN_TICKS_FOR_TRACK = 1200;

/**
 * Minutes after the hour to wait before generating the previous hour's track.
 * Ensures all ticks have been written before generation.
 */
export const TRACK_GEN_BUFFER_MINUTES = 2;

/**
 * One hour in milliseconds  avoids magic numbers in time arithmetic.
 */
export const ONE_HOUR_MS = 60 * 60 * 1000;

// ---------------------------------------------------------------------------
// Difficulty Classification
// ---------------------------------------------------------------------------

/** Easy track — minimal elevation change. */
export const DIFFICULTY_EASY = 0;

/** Medium track — moderate elevation change. */
export const DIFFICULTY_MEDIUM = 1;

/** Hard track — high elevation change. */
export const DIFFICULTY_HARD = 2;

/** Score below this threshold → Easy. */
export const DIFFICULTY_THRESHOLD_EASY = 0.08;

/** Score above this threshold → Hard. Below → Medium. */
export const DIFFICULTY_THRESHOLD_HARD = 0.16;
