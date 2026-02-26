-- ===========================================================================
-- Oracle Pipeline  PostgreSQL Schema (Tick-Based)
-- ===========================================================================
-- Run once against the target database.
-- Compatible with PostgreSQL 14+.
-- ===========================================================================

-- -------------------------------------------------------------------
-- oracle_ticks
-- -------------------------------------------------------------------
-- Stores oracle price ticks sampled every 2 seconds.
-- tick_time is always aligned to 2000ms boundaries.
-- PRIMARY KEY enforces uniqueness per token + tick time.
-- -------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS oracle_ticks (
    token_mint        TEXT             NOT NULL,
    tick_time         TIMESTAMPTZ      NOT NULL,
    oracle_price      DOUBLE PRECISION NOT NULL,
    publish_time      TIMESTAMPTZ      NOT NULL,
    source_slot       BIGINT           NOT NULL,
    created_at        TIMESTAMPTZ      NOT NULL DEFAULT now(),

    PRIMARY KEY (token_mint, tick_time)
);

-- Descending time index for retention cleanup and latest-tick lookups.
CREATE INDEX IF NOT EXISTS idx_oracle_ticks_token_time
    ON oracle_ticks (token_mint, tick_time DESC);


-- -------------------------------------------------------------------
-- track_buckets
-- -------------------------------------------------------------------
-- Each row is a deterministic normalized track generated from one
-- hour of oracle ticks for a specific token.
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
