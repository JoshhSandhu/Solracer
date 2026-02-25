-- ===========================================================================
-- Oracle Pipeline  PostgreSQL Schema
-- ===========================================================================
-- Run once against the target database.
-- Compatible with PostgreSQL 14+.
-- ===========================================================================

-- -------------------------------------------------------------------
-- oracle_hourly_points
-- -------------------------------------------------------------------
-- Stores one sampled oracle price per token per UTC hour.
-- PRIMARY KEY enforces uniqueness and provides the fastest lookup path.
-- -------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS oracle_hourly_points (
    token_mint        TEXT        NOT NULL,
    hour_start_utc    TIMESTAMPTZ NOT NULL,
    oracle_price      DOUBLE PRECISION NOT NULL,
    publish_time      TIMESTAMPTZ NOT NULL,
    source_slot       BIGINT      NOT NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (token_mint, hour_start_utc)
);

-- Descending time index for rolling-window retention queries
-- and "get latest hour" lookups.
CREATE INDEX IF NOT EXISTS idx_oracle_points_token_hour
    ON oracle_hourly_points (token_mint, hour_start_utc DESC);


-- -------------------------------------------------------------------
-- track_buckets
-- -------------------------------------------------------------------
-- Each row is a deterministic normalized track generated from one
-- hour bucket of oracle data for a specific token.
-- normalized_points_blob is BYTEA (quantized int16 array).
-- track_hash is SHA-256 hex of the blob  required for on-chain
-- track commitment in ER race sessions.
-- -------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS track_buckets (
    token_mint              TEXT        NOT NULL,
    track_hour_start_utc    TIMESTAMPTZ NOT NULL,
    track_version           TEXT        NOT NULL,
    normalized_points_blob  BYTEA       NOT NULL,
    point_count             INTEGER     NOT NULL,
    normalization_meta      JSONB       NOT NULL,
    track_hash              TEXT        NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (token_mint, track_hour_start_utc, track_version)
);

-- Descending time index for playable-bucket queries and retention cleanup.
CREATE INDEX IF NOT EXISTS idx_track_buckets_token_hour
    ON track_buckets (token_mint, track_hour_start_utc DESC);
