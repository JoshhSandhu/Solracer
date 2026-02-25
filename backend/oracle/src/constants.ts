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
 * Default slope clamp angle (degrees) applied during normalization.
 * Prevents unplayable vertical sections from extreme oracle candles.
 */
export const DEFAULT_SLOPE_CLAMP_DEGREES = 75;

/**
 * One hour in milliseconds  avoids magic numbers in time arithmetic.
 */
export const ONE_HOUR_MS = 60 * 60 * 1000;
