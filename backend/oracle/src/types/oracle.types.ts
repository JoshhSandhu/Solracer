// ---------------------------------------------------------------------------
// Oracle Pipeline  Domain Types
// ---------------------------------------------------------------------------

/**
 * Row in `oracle_hourly_points`.
 * PRIMARY KEY is (token_mint, hour_start_utc).
 */
export interface OracleHourlyPoint {
  /** Solana token mint address (base-58). */
  token_mint: string;

  /** UTC hour bucket start, floored to the hour. */
  hour_start_utc: Date;

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
 * Input payload for `storeOraclePoint()`.
 * Same as `OracleHourlyPoint` but without server-generated fields.
 */
export type OraclePointInput = Omit<OracleHourlyPoint, 'created_at'>;

// ---------------------------------------------------------------------------
// Track Buckets
// ---------------------------------------------------------------------------

/**
 * Metadata about the normalization process that produced a track bucket.
 * Stored as JSONB in `normalization_meta`.
 */
export interface NormalizationMeta {
  /** Minimum raw oracle price in the source data. */
  min_price: number;

  /** Maximum raw oracle price in the source data. */
  max_price: number;

  /** Scale factor applied during int16 quantization. */
  scale_factor: number;

  /** Number of output points (must equal config TRACK_POINT_COUNT). */
  point_count: number;

  /** Maximum slope angle (degrees) applied during clamping. */
  slope_clamp_degrees: number;

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
}
