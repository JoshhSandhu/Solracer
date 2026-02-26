# Oracle-v2  SolRacer Track Data Pipeline

Deterministic oracle tick ingestion and track bucket pipeline for the SolRacer ER multiplayer system.

This module runs independently from the Python/FastAPI backend. It samples MagicBlock oracle prices every 2 seconds and generates deterministic hourly track buckets that ER race sessions commit to on-chain.

## Architecture

```
MagicBlock Oracle Feed
       |
       v
[Tick Worker]--every 2s-->[oracle_ticks (DB)]
       |                         |
       |                         | rolling delete (>26h)
       |                         v
       +--after hour close-->[Track Generator]
                                 |
                                 v
                          [track_buckets (DB)]
                                 |
                                 v
                          [Internal API]
                                 |
                                 v
                           Unity Client
```

**Tick worker** runs a continuous `setTimeout` loop aligned to 2-second boundaries (`TICK_INTERVAL_MS = 2000`). Each tick fetches the current oracle price for every configured token and stores a row in `oracle_ticks`. The worker owns `tick_time` alignment; the oracle fetcher never produces timestamps.

**Track generation** runs inside the same tick loop. Starting at 2 minutes past each UTC hour, the worker checks the last 2 completed hours. For each token+hour that has no existing track bucket (DB-checked, crash-safe), it generates a deterministic track if at least 1200 ticks are present (`MIN_TICKS_FOR_TRACK`).

**Track pipeline** (locked order): downsample -> normalize [0,1] -> delta clamp -> quantize Int16LE + SHA-256 hash. All arithmetic uses JS `number` (f64). No float32 anywhere.

**Rolling 26-hour window** ensures old data is pruned once per hour. Retention is based on `tick_time` / `track_hour_start_utc`, not `created_at`.

## Database Tables

### `oracle_ticks`

| Column | Type | Description |
|--------|------|-------------|
| `token_mint` | TEXT | Solana token mint address (PK) |
| `tick_time` | TIMESTAMPTZ | Aligned tick timestamp, multiple of 2000ms (PK) |
| `oracle_price` | DOUBLE PRECISION | Sampled oracle price |
| `publish_time` | TIMESTAMPTZ | Oracle feed publish timestamp |
| `source_slot` | BIGINT | Solana slot at observation |
| `created_at` | TIMESTAMPTZ | Row insertion time (default `now()`) |

**Primary key**: `(token_mint, tick_time)`

### `track_buckets`

| Column | Type | Description |
|--------|------|-------------|
| `token_mint` | TEXT | Solana token mint address (PK) |
| `track_hour_start_utc` | TIMESTAMPTZ | Source hour bucket (PK) |
| `track_version` | TEXT | Normalization algorithm version (PK) |
| `normalized_points_blob` | BYTEA | Quantized Int16LE Y-values |
| `point_count` | INTEGER | Number of points in track |
| `normalization_meta` | JSONB | Scale factor, clamp, tick count, version |
| `track_hash` | TEXT | SHA-256 hex digest of blob |
| `created_at` | TIMESTAMPTZ | Row insertion time (default `now()`) |

**Primary key**: `(token_mint, track_hour_start_utc, track_version)`

## Rolling Window Rules

- **26 hours** of tick data and track buckets stored per token.
- **Newest bucket** excluded from playable set (current hour still forming).
- **Oldest bucket** excluded (about to roll off the retention window).
- **24 remaining hours** are playable track candidates.
- Retention cleanup runs once per hour, deleting on `tick_time` / `track_hour_start_utc`.

When a lobby is created, the backend randomly selects one of the 24 playable buckets. Both players race the same deterministic track.

## Expected Data Volume

| Metric | Value |
|--------|-------|
| Tick interval | 2 seconds |
| Ticks per token per hour | ~1,800 |
| Ticks per token in retention window | ~46,800 |
| Track buckets per token | ~26 |
| With 3 tokens: total tick rows | ~140,400 |

## Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `TRACK_VERSION` | `'2'` | Normalization algorithm version |
| `TICK_INTERVAL_MS` | `2000` | Tick sampling interval (ms) |
| `MAX_DELTA_PER_STEP` | `0.03` | Max delta between consecutive normalized Y-values |
| `MIN_TICKS_FOR_TRACK` | `1200` | Minimum ticks to generate a track (67% coverage) |
| `TRACK_GEN_BUFFER_MINUTES` | `2` | Minutes after hour to wait before track generation |
| `ONE_HOUR_MS` | `3600000` | One hour in milliseconds |

## Determinism Guarantees

The system must produce identical track hashes for identical tick data across any server, timezone, or restart.

- Tick timestamps are aligned to exact multiples of `TICK_INTERVAL_MS` by the worker.
- `getTicksForHour()` enforces `ORDER BY tick_time ASC` (deterministic input ordering).
- Track pipeline: downsample -> normalize -> delta clamp -> quantize is fully deterministic (no randomness, no locale dependency).
- `track_hash` = `SHA-256(normalized_points_blob)` (hash depends only on the blob).
- `track_version` pins the algorithm version; old and new versions never mix.
- UPSERT guarantees no duplicate ticks or buckets.
- All time operations use UTC (`setUTCMinutes`, `getUTCMinutes`, epoch math).

These guarantees ensure: `sha256(track_blob) === track_hash` committed in ER race session.

## Running the Worker

```bash
cd backend/oracle

# Install dependencies
npm install

# Run in development mode (loads .env automatically)
npm run dev

# Build and run production
npm run build
npm start
```

The worker will:
1. Start a 2-second aligned tick loop.
2. Fetch and store oracle prices for all configured tokens each tick.
3. Generate track buckets for completed hours (2 minutes after hour close).
4. Run retention cleanup once per hour.

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DATABASE_URL` | Yes | | PostgreSQL connection string |
| `ORACLE_SUPPORTED_TOKENS` | Yes | | Comma-separated token mint addresses |
| `RETENTION_HOURS` | No | `26` | Rolling window size in hours |
| `TRACK_POINT_COUNT` | No | `1000` | Fixed point count per normalized track |

Example `.env`:
```
DATABASE_URL=postgresql://postgres:password@db.project.supabase.co:5432/postgres
ORACLE_SUPPORTED_TOKENS=So11111111111111111111111111111111111112,DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263
TRACK_POINT_COUNT=1000
RETENTION_HOURS=26
```

**Note:** Never commit `.env` or credentials to git. The `test:db` script requires `TEST_DATABASE_URL` to be set as an environment variable.

## Tests

```bash
# Run all unit tests (no DB required)
npm test

# Run with watch mode
npm run test:watch

# Run DB integration tests (requires TEST_DATABASE_URL)
TEST_DATABASE_URL=postgresql://user:pass@localhost:5432/test_db npm run test:db
```

**Test structure:**
- `tests/time.test.ts` - Deterministic hour rounding invariants (9 tests).
- `tests/track-generator.test.ts` - Track pipeline determinism, blob validation, hash verification, edge cases (8 tests).
- `tests/repository.test.ts` - Tick UPSERT, ordering, retention, playable bucket rules, `trackBucketExists` (13 tests, requires `TEST_DATABASE_URL`, skips gracefully without it).

## Known Minor Issues (Deferred)

The following were identified during production audits and intentionally deferred:

- **Redundant DESC indexes**: The explicit `(token_mint, tick_time DESC)` indexes duplicate what the PK B-tree provides via backward scans. Wastes disk and write overhead but causes no correctness issues.

- **`source_slot` BIGINT returned as string by pg**: The `pg` library returns `BIGINT` as a string, but the TypeScript type declares `number`. No arithmetic is performed on this field, so no runtime breakage, but the type assertion is incorrect.

- **`deleteExpiredData` runs two DELETEs without a transaction**: If the process crashes between the two statements, one table is cleaned and the other isn't. Self-correcting on the next cleanup cycle.

- **`getPool` singleton ignores `databaseUrl` on subsequent calls**: The singleton pattern returns the pool connected to the first URL. Confusing in test or multi-tenant scenarios.

- **`oracle_price` uses `DOUBLE PRECISION`**: Floating point representation risks for price data. Acceptable for track generation (quantized to Int16) but not suitable for financial accounting.

- **Playable query uses DB clock (`now()`) while retention uses JS clock (`Date.now()`)**: Clock skew between app and DB servers could briefly cause a bucket to be playable but about to be deleted, or vice versa.

- **`isDirectRun` detection is fragile**: The `process.argv[1]?.endsWith('index.ts')` fallbacks could false-positive on similarly named files.

- **Delta clamping can push values outside [0, 1]**: After normalization, delta clamping can produce values slightly below 0 or above 1. The quantizer's `[0, 32767]` clamp catches this, but it means tracks may have small plateaus at boundaries.

- **No backfill for missed ticks**: If the worker is down, missed ticks are gone permanently. If an hour has fewer than 1200 ticks, no track is generated and there is no recovery path.

- **`getTicksForHour` hardcodes hour duration**: Uses `60 * 60 * 1000` instead of the `ONE_HOUR_MS` constant.

## Project Structure

```
backend/oracle/
├── package.json
├── tsconfig.json
├── vitest.config.mts
├── src/
│   ├── index.ts                          # Barrel exports + bootstrap
│   ├── config.ts                         # Environment config
│   ├── constants.ts                      # TRACK_VERSION, TICK_INTERVAL_MS, etc.
│   ├── types/
│   │   └── oracle.types.ts               # Domain types
│   ├── utils/
│   │   └── time.ts                       # floorToHour()
│   ├── db/
│   │   ├── schema.sql                    # PostgreSQL DDL
│   │   ├── connection.ts                 # pg.Pool (with statement_timeout)
│   │   └── repository.ts                 # DB operations
│   ├── services/
│   │   ├── oracle-fetcher.ts             # MagicBlock stub
│   │   └── track-generator.ts            # Deterministic normalization pipeline
│   └── workers/
│       └── oracleIngestionWorker.ts      # Tick loop worker
└── tests/
    ├── time.test.ts
    ├── track-generator.test.ts
    └── repository.test.ts
```
