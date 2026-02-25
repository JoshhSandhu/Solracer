# Oracle-v2  SolRacer Track Data Pipeline

Deterministic oracle ingestion and track bucket pipeline for the SolRacer ER multiplayer system.

This module runs independently from the Python/FastAPI backend. It ingests MagicBlock oracle price data hourly and generates normalized track buckets that ER race sessions commit to on-chain.

## Architecture

```
MagicBlock Oracle Feed
       │
       ▼
[Ingestion Worker]──hourly──▶[oracle_hourly_points (DB)]
       │                           │
       │                           │ rolling delete (>26h)
       ▼                           ▼
[Track Generator]───────────▶[track_buckets (DB)]
                                   │
                                   ▼
                            [Internal API]
                                   │
                                   ▼
                             Unity Client
```

**Ingestion worker** runs on a configurable cron schedule (default `:05` past each hour). For each configured token mint, it fetches the current oracle price and stores one row per token per hour. A startup catch-up routine detects gaps and backfills missed hours (capped at `MAX_CATCHUP_HOURS`).

**Track buckets** are deterministic normalizations of hourly oracle data. Each bucket stores quantized int16 Y-values as BYTEA, a SHA-256 `track_hash` for on-chain commitment, and normalization metadata for reproducibility.

**Rolling 26-hour window** ensures old data is pruned every cycle. Retention is based on `hour_start_utc`, not `created_at`.

## Database Tables

### `oracle_hourly_points`

| Column | Type | Description |
|--------|------|-------------|
| `token_mint` | TEXT | Solana token mint address (PK) |
| `hour_start_utc` | TIMESTAMPTZ | UTC hour bucket start (PK) |
| `oracle_price` | DOUBLE PRECISION | Sampled oracle price |
| `publish_time` | TIMESTAMPTZ | Oracle feed publish timestamp |
| `source_slot` | BIGINT | Solana slot at observation |
| `created_at` | TIMESTAMPTZ | Row insertion time (default `now()`) |

**Primary key**: `(token_mint, hour_start_utc)`

### `track_buckets`

| Column | Type | Description |
|--------|------|-------------|
| `token_mint` | TEXT | Solana token mint address (PK) |
| `track_hour_start_utc` | TIMESTAMPTZ | Source hour bucket (PK) |
| `track_version` | TEXT | Normalization algorithm version (PK) |
| `normalized_points_blob` | BYTEA | Quantized int16 Y-values |
| `point_count` | INTEGER | Number of points in track |
| `normalization_meta` | JSONB | Scale factor, clamp, version info |
| `track_hash` | TEXT | SHA-256 hex digest of blob |
| `created_at` | TIMESTAMPTZ | Row insertion time (default `now()`) |

**Primary key**: `(token_mint, track_hour_start_utc, track_version)`

## Rolling Window Rules

- **26 hours** of data stored per token.
- **Newest hour** excluded (still forming  may have incomplete data).
- **Oldest hour** excluded (about to roll off the retention window).
- **24 remaining hours** are playable track candidates.
- Data older than 26 hours is deleted on `hour_start_utc` each cycle.

When a lobby is created, the backend randomly selects one of the 24 playable buckets. Both players race the same deterministic track.

## Determinism Guarantees

Oracle-v2 enforces deterministic track bucket generation so ER race sessions can commit to track data on-chain.

Determinism requirements:
- Hour buckets use UTC floor-to-hour rounding.
- One oracle sample per token per hour.
- UPSERT guarantees no duplicate buckets.
- Track normalization produces identical output for identical inputs.
- `track_hash` is SHA-256 of `normalized_points_blob`.
- `point_count` must equal `TRACK_POINT_COUNT`.
- `track_version` identifies normalization algorithm version.

These guarantees ensure that:
`hash(track_blob) == track_hash` committed in ER race session.

## Running the Worker

```bash
cd backend/oracle

# Install dependencies
npm install

# Run in development mode
npm run dev

# Build and run production
npm run build
npm start
```

The worker will:
1. Run startup catch-up to backfill any missed hours.
2. Schedule hourly ingestion at the configured poll minute.
3. Clean up expired data after each cycle.

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DATABASE_URL` | Yes |  | PostgreSQL connection string |
| `ORACLE_SUPPORTED_TOKENS` | Yes |  | Comma-separated token mint addresses |
| `RETENTION_HOURS` | No | `26` | Rolling window size in hours |
| `TRACK_POINT_COUNT` | No | `1000` | Fixed point count per normalized track |
| `ORACLE_POLL_MINUTE` | No | `5` | Minute past hour for cron (0–59) |
| `MAX_CATCHUP_HOURS` | No | `48` | Max hours to backfill on startup |

Example `.env`:
```
DATABASE_URL=postgresql://postgres:password@db.project.supabase.co:5432/postgres
ORACLE_SUPPORTED_TOKENS=So11111111111111111111111111111111111112,DezXcbUBnEAA2F9tQupAwmAEuGPPdP4hMsp9pq2GKb8
TRACK_POINT_COUNT=1000
ORACLE_POLL_MINUTE=5
```

## Tests

```bash
# Run all unit tests (no DB required)
npm test

# Run with watch mode
npm run test:watch

# Run DB integration tests (requires TEST_DATABASE_URL)
TEST_DATABASE_URL=postgresql://user:pass@localhost:5432/test_db npm test
```

**Test structure:**
- `tests/time.test.ts`  Deterministic hour rounding invariants.
- `tests/catchup.test.ts`  Catch-up safety cap (pure logic, no DB).
- `tests/repository.test.ts`  UPSERT, retention, playable bucket rules (requires `TEST_DATABASE_URL`, skips gracefully without it).

## Project Structure

```
backend/oracle/
├── package.json
├── tsconfig.json
├── vitest.config.ts
├── src/
│   ├── index.ts                          # Barrel exports + bootstrap
│   ├── config.ts                         # Environment config
│   ├── constants.ts                      # TRACK_VERSION, defaults
│   ├── types/
│   │   └── oracle.types.ts               # Domain types
│   ├── utils/
│   │   └── time.ts                       # floorToHour()
│   ├── db/
│   │   ├── schema.sql                    # PostgreSQL DDL
│   │   ├── connection.ts                 # pg.Pool
│   │   └── repository.ts                 # DB operations
│   ├── services/
│   │   ├── oracle-fetcher.ts             # MagicBlock stub
│   │   └── track-generator.ts            # Normalization stub
│   └── workers/
│       └── oracleIngestionWorker.ts      # Cron worker
└── tests/
    ├── time.test.ts
    ├── catchup.test.ts
    └── repository.test.ts
```
