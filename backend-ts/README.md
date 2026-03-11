# backend-ts

TypeScript/Fastify backend for Solracer gameplay APIs.

This service currently handles:
- race lobby endpoints (`/api/v1/races/*`)
- transaction build/submit endpoints (`/api/v1/transactions/*`)
- payout endpoints (`/api/v1/payouts/*`)
- ghost relay endpoints (`/api/v1/ghost/*`)

## Status

`backend-ts` is the active backend path for the current Unity client flow, but it is not a full replacement for the legacy Python backend yet.

Current limitations:
- In-memory state only (`src/store/memory.ts`), no database persistence.
- Payout processing endpoints are still stubs (`src/routes/payouts.ts`), including missing Jupiter swap transaction generation.
- Token list is hardcoded (`SOL`, `BONK`, `JUP`) in `src/store/memory.ts`.

## API Surface

Base prefix defaults to `/api/v1`.

### Health
- `GET /`
- `GET /health`

### Races
- `POST /races/create`
- `POST /races/:race_id/join`
- `POST /races/join-by-code`
- `GET /races/public`
- `GET /races/:race_id/status`
- `POST /races/:race_id/ready`
- `DELETE /races/:race_id?wallet_address=...`

### Transactions
- `POST /transactions/build`
- `POST /transactions/submit`

`build` supports: `create_race`, `join_race`, `submit_result`, `settle_race`, `claim_prize`.

### Payouts
- `GET /payouts/:race_id`
- `POST /payouts/:race_id/process`
- `GET /payouts/:race_id/settle-transaction`
- `POST /payouts/:race_id/retry`

### Ghost Relay
- `POST /ghost/update`
- `GET /ghost/:race_id`

## Environment Variables

Used directly by current source code:

- `PORT` (default `8001`)
- `HOST` (default `0.0.0.0`)
- `API_V1_PREFIX` (default `/api/v1`)
- `CORS_ORIGIN` (default `true`)
- `USE_HTTPS` (`true`/`false`)
- `CERTS_DIR` (default `../certs`)
- `CERT_PFX_PASSWORD`
- `LOG_LEVEL` (default `info`)
- `NODE_ENV`
- `SOLANA_RPC_URL` (default `https://api.devnet.solana.com`)
- `SOLANA_COMMITMENT` (default `confirmed`)
- `SOLANA_PROGRAM_ID` (defaults to program ID in `src/services/pda.ts`)

## Local Development

```bash
cd backend-ts
pnpm install
pnpm dev
```

Build and run:

```bash
pnpm build
pnpm start
```

Typecheck:

```bash
pnpm typecheck
```

## Docker

```bash
cd backend-ts
docker compose up --build
```

`docker-compose.yml` maps container port `8001` to host `8001`.

## Related Components

- Main race escrow program: [`../solracer-program`](../solracer-program)
- ER ghost program: [`../solracer-er`](../solracer-er)
- Unity client: [`../client-unity`](../client-unity)
- Legacy backend reference: [`../backend`](../backend)

