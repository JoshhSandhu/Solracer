// ---------------------------------------------------------------------------
// Oracle Pipeline  Domain Types
// ---------------------------------------------------------------------------

/**
 * Row in `oracle_ticks`.
 * PRIMARY KEY is (token_mint, tick_time).
 * tick_time is always aligned to TICK_INTERVAL_MS boundaries.
 */
export interface OracleTick {
  /** Solana token mint address (base-58). */
  token_mint: string;

  /** Aligned tick timestamp (multiple of TICK_INTERVAL_MS). */
  tick_time: Date;

  /** Oracle-reported price at sample time. */
  oracle_price: number;

  /** Original publish timestamp from the oracle feed. */
  publish_time: Date;

  /** Solana slot at which the oracle update was observed. */
  source_slot: number;

  /** Row insertion timestamp (DB default). */
  created_at?: Date;
}

/**
 * Input payload for `storeOracleTick()`.
 * Same as `OracleTick` but without server-generated fields.
 */
export type OracleTickInput = Omit<OracleTick, 'created_at'>;

// ---------------------------------------------------------------------------
// Track Buckets
// ---------------------------------------------------------------------------

/**
 * Metadata about the normalization process that produced a track bucket.
 * Stored as JSONB in `normalization_meta`.
 */
export interface NormalizationMeta {
  /** Minimum raw value in the source data (pre-normalization). */
  min_price: number;

  /** Maximum raw value in the source data (pre-normalization). */
  max_price: number;

  /** Scale factor applied during normalization (1 / (max - min)). */
  scale_factor: number;

  /** Number of output points (must equal config TRACK_POINT_COUNT). */
  point_count: number;

  /** Maximum delta between consecutive normalized Y-values. */
  max_delta_per_step: number;

  /** Number of source ticks used to generate this track. */
  source_tick_count: number;

  /** Normalization algorithm version tag. */
  version: string;
}

/**
 * Row in `track_buckets`.
 * PRIMARY KEY is (token_mint, track_hour_start_utc, track_version).
 */
export interface TrackBucket {
  /** Solana token mint address. */
  token_mint: string;

  /** Hour this track was generated from. */
  track_hour_start_utc: Date;

  /** Normalization algorithm version. */
  track_version: string;

  /**
   * Quantized int16 Y-values stored as a raw binary buffer (BYTEA).
   * X is implicit by index.
   */
  normalized_points_blob: Buffer;

  /** Number of points in the track. */
  point_count: number;

  /** Normalization parameters for reproducibility. */
  normalization_meta: NormalizationMeta;

  /**
   * SHA-256 hex digest of `normalized_points_blob`.
   * Required for on-chain track commitment in ER race sessions.
   */
  track_hash: string;

  /** Difficulty classification: 0 = Easy, 1 = Medium, 2 = Hard. */
  difficulty: number;

  /** Row insertion timestamp (DB default). */
  created_at?: Date;
}

/**
 * Input payload for `storeTrackBucket()`.
 */
export type TrackBucketInput = Omit<TrackBucket, 'created_at'>;

// ---------------------------------------------------------------------------
// Oracle Fetcher
// ---------------------------------------------------------------------------

/**
 * Raw price data returned by the oracle fetcher (MagicBlock or stub).
 * NOTE: tick_time is NOT included here. The worker owns tick_time alignment.
 */
export interface OraclePriceData {
  token_mint: string;
  price: number;
  publish_time: Date;
  source_slot: number;
}

// ---------------------------------------------------------------------------
// Playable Bucket Query Result
// ---------------------------------------------------------------------------

/**
 * Lightweight view of a playable track bucket returned by
 * `getPlayableTrackBuckets()`.
 */
export interface PlayableTrackBucket {
  token_mint: string;
  track_hour_start_utc: Date;
  track_version: string;
  point_count: number;
  track_hash: string;
  normalized_points_blob: Buffer;
  normalization_meta: NormalizationMeta;
  difficulty: number;
}
