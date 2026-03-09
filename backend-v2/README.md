# Backend-v2  SolRacer Read API

Minimal read-only API for the Unity client to access track buckets from Supabase.

No auth. No business logic. Just deterministic track access.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Health check + DB connectivity |
| `GET` | `/tokens` | List all distinct tokens |
| `GET` | `/tracks/:tokenMint` | 24 playable track buckets (metadata only) |
| `GET` | `/tracks/:tokenMint/latest` | Most recent playable track |
| `GET` | `/tracks/:tokenMint/:hourStartUTC` | Single track with base64 blob |

No other HTTP methods are accepted. No POST, PUT, or DELETE.

## Input Validation

- `tokenMint`  must be a valid Solana base58 address (`^[1-9A-HJ-NP-Za-km-z]{32,44}$`). Returns `400` if invalid.
- `hourStartUTC`  must be hour-aligned ISO 8601: `YYYY-MM-DDTHH:00:00.000Z`. Returns `400` if invalid.

## Setup

```bash
cd backend-v2
npm install
```

Create `.env`:
```env
DATABASE_URL=postgresql://user:pass@host:5432/db
TRACK_VERSION=2
RETENTION_HOURS=26
PORT=3000
```

## Development

```bash
npm run dev
```

## Production (PM2)

```bash
# Build
npm run build

# Start
pm2 start dist/server.js --name solracer-backend

# Restart
pm2 restart solracer-backend

# Logs
pm2 logs solracer-backend

# Auto-start on reboot
pm2 save
pm2 startup
```

## Cloudflare Tunnel

Exposed at `api.lynxjosh.cyou → localhost:3000`.

No HTTPS config needed  Cloudflare handles TLS termination.

## Curl Examples

```bash
# Health check
curl http://localhost:3000/health
# {"status":"ok","database":"connected","uptimeSeconds":42}

# List tokens
curl http://localhost:3000/tokens
# ["So11111111111111111111111111111111111111112","DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263"]

# Playable tracks (24 buckets, metadata only)
curl http://localhost:3000/tracks/So11111111111111111111111111111111111111112
# [{"tokenMint":"So1...","hourStartUTC":"2026-02-26T00:00:00.000Z","trackVersion":"2","pointCount":1000,"trackHash":"abc..."}]

# Latest playable track
curl http://localhost:3000/tracks/So11111111111111111111111111111111111111112/latest
# {"hourStartUTC":"2026-02-26T23:00:00.000Z","trackHash":"abc..."}

# Single track with blob
curl http://localhost:3000/tracks/So11111111111111111111111111111111111111112/2026-02-26T18:00:00.000Z
# {"tokenMint":"So1...","hourStartUTC":"...","trackVersion":"2","pointCount":1000,"trackHash":"abc...","normalizedPointsBlobBase64":"AQID..."}
```

## Architecture

```
backend-v2/
  src/
    server.ts           # Fastify entry point + global error handler
    db/
      connection.ts     # pg Pool (statement_timeout 10s)
    routes/
      health.ts         # GET /health
      tokens.ts         # GET /tokens
      tracks.ts         # GET /tracks/*
    types/
      track.types.ts    # Response types
```

## Security

- Read-only: GET endpoints only
- Global error handler: DB/internal errors never leak to clients
- Input validation: tokenMint (Solana base58) and hourStartUTC (hour-aligned ISO 8601)
- Rate limited: 100 req/min per IP (`/health` excluded)
- CORS: origin `*`, methods `GET` only
- No env vars exposed in responses
- Parameterized SQL queries only
- `statement_timeout`: 10 seconds
- Graceful shutdown on SIGINT/SIGTERM
- Unhandled rejection safety net

## Cache Headers

| Endpoint | Cache-Control |
|----------|---------------|
| `/tokens` | `public, max-age=300` |
| `/tracks/:tokenMint` | `public, max-age=300` |
| `/tracks/:tokenMint/latest` | `public, max-age=300` |
| `/tracks/:tokenMint/:hourStartUTC` | `public, max-age=3600` |

## Known Minor Issues

The following are low-priority items that do not affect correctness or security:

- **Rate-limit `/health` allowList is URL-exact**: `_req.url === '/health'` includes the query string, so `GET /health?foo=bar` would not be excluded. Not a practical concern since health probes don't use query strings.
- **Pool error handler uses `console.error`**: The `pool.on('error')` callback in `connection.ts` writes to `console.error` rather than the Fastify structured logger. Pool-level errors are rare and still captured by PM2 logs.
- **`declaration: true` in `tsconfig.json`**: Emits unnecessary `.d.ts` files since this is not a library. Harmless but adds build noise.
- **`sourceMap: true` in `tsconfig.json`**: Emits `.js.map` files in `dist/`. Not served to clients, but could be removed for production builds.
- **`GET /tracks/:tokenMint` has no explicit LIMIT**: Naturally bounded by the retention window (~24 rows), but a hard `LIMIT 48` would be an extra safety net.
