# Solracer Backend

> FastAPI backend for Solracer game with PostgreSQL database, Solana program integration, and Jupiter swap service for token prizes.

**Tech Stack:** FastAPI, Python, PostgreSQL (Supabase), Solana Web3.py, Jupiter Swap API

**Note:** Backend runs on any platform, but serves Android Unity client (Seeker device).

## Features

- RESTful API for race management and token data
- Real-time chart data fetching from Birdeye API
- Solana program integration for on-chain race escrow
- Automatic token prize swaps via Jupiter aggregator
- Deterministic replay verification for anti-cheat
- Race matching and result validation

## Screenshots

[Screenshots or demo GIF - optional if already in root README]

---

## Quick Start

### Prerequisites

- Python 3.11+
- PostgreSQL database (Supabase recommended)
- Solana CLI tools (for program deployment)
- Node.js (for Anchor/TypeScript tooling)

### Installation

```bash
# Install dependencies
pip install -r requirements.txt
```

### Running the Server

```bash
# Set up environment variables (see Configuration section)
# Run database migrations
alembic upgrade head

# Seed tokens database
python scripts/seed_tokens.py

# Start server
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

**Access API docs:**
- Swagger UI: http://localhost:8000/docs
- ReDoc: http://localhost:8000/redoc

---

## Configuration

### Environment Variables

Create a `.env` file in the `backend/` directory:

```bash
# Database Configuration
DATABASE_URL=postgresql://postgres:password@db.project.supabase.co:5432/postgres

# Solana Configuration
SOLANA_CLUSTER=devnet
SOLANA_RPC_ENDPOINT=https://api.devnet.solana.com
SOLANA_PROGRAM_ID=BW9EBdw58SZzzYY3rczk6qGeRUf21ZyPJyd6QKs4GbtM

# API Keys (Optional - for higher rate limits)
BIRDEYE_API_KEY=your_key_here

# Swap Agent (for Jupiter swaps)
SWAP_AGENT_PRIVATE_KEY=your_private_key_base58
```

| Variable | Description | Example |
|----------|-------------|---------|
| `DATABASE_URL` | PostgreSQL connection string | `postgresql://user:pass@host:5432/db` |
| `SOLANA_CLUSTER` | Solana network | `devnet` or `mainnet-beta` |
| `SOLANA_RPC_ENDPOINT` | Solana RPC endpoint | `https://api.devnet.solana.com` |
| `SOLANA_PROGRAM_ID` | Deployed program ID | `BW9EBdw58SZzzYY3rczk6qGeRUf21ZyPJyd6QKs4GbtM` |
| `BIRDEYE_API_KEY` | Birdeye API key (optional) | `your_key_here` |
| `SWAP_AGENT_PRIVATE_KEY` | Private key for swap agent (base58) | `your_private_key` |

### Critical Setup: Database Configuration

**Why?** The backend requires PostgreSQL for race state management, token data, and result storage.

**Steps:**
1. Create a Supabase project (recommended) or local PostgreSQL database
2. Get connection string from Supabase: Project Settings → Database
3. Set `DATABASE_URL` in `.env` file
4. Run migrations: `alembic upgrade head`
5. Seed tokens: `python scripts/seed_tokens.py`

---

## Project Structure

```
backend/
├── app/
│   ├── main.py                    # FastAPI app entry point
│   ├── database.py                # Database connection & session management
│   ├── models.py                  # SQLAlchemy database models
│   ├── schemas.py                 # Pydantic request/response schemas
│   ├── api/
│   │   └── routes/                # API endpoint routes ⭐
│   │       ├── tokens.py          # Token endpoints
│   │       ├── tracks.py          # Track/chart data endpoints
│   │       ├── races.py           # Race management endpoints
│   │       ├── solana_transactions.py  # Transaction building/submission
│   │       └── payouts.py         # Payout status endpoints
│   ├── services/                  # Business logic services ⭐
│   │   ├── chart_data.py          # Chart data fetching & normalization
│   │   ├── solana_client.py       # Solana RPC client
│   │   ├── program_client.py      # Solana program interaction
│   │   ├── transaction_builder.py # Transaction building
│   │   ├── transaction_submitter.py # Transaction submission
│   │   ├── jupiter_swap.py        # Jupiter swap integration
│   │   ├── payout_handler.py      # Payout processing
│   │   └── onchain_sync.py        # On-chain state synchronization
│   └── idl/
│       └── solracer_program.json   # Solana program IDL
├── alembic/                       # Database migrations
│   ├── versions/                  # Migration files
│   └── env.py                     # Alembic configuration
├── scripts/                       # Utility scripts
│   ├── seed_tokens.py             # Database seeding
│   └── test_*.py                  # Test scripts
└── requirements.txt               # Python dependencies
```

---

## Key Concepts

### Race Management

Races are created or joined automatically based on token and entry fee matching. The backend manages race state (waiting, active, settled) and coordinates with the Solana program for on-chain escrow.

**Files:** [races.py](app/api/routes/races.py), [program_client.py](app/services/program_client.py)

### Chart Data Integration

Real-time token price charts are fetched from Birdeye API, normalized to 0-1 range, and cached for performance. Chart data is used to generate race tracks in Unity.

**Files:** [chart_data.py](app/services/chart_data.py), [tracks.py](app/api/routes/tracks.py)

### Transaction Building & Signing

The backend builds Solana transactions for race operations. Unity signs transactions via Solana Unity SDK, and the backend submits signed transactions to Solana.

**Files:** [transaction_builder.py](app/services/transaction_builder.py), [solana_transactions.py](app/api/routes/solana_transactions.py)

### Jupiter Swap Integration

Winners receive prizes in the race token (e.g., BONK) via Jupiter aggregator. The backend handles swap quotes, execution, and token transfer to winner's wallet.

**Files:** [jupiter_swap.py](app/services/jupiter_swap.py), [payout_handler.py](app/services/payout_handler.py)

_Note: Keep this section concise (2-3 sentences per feature). For detailed implementation explanations, see the code files directly._

---

## Common Issues

### Error: "Database connection failed"

**Solution:**
1. Verify `DATABASE_URL` is correct in `.env` file
2. Check database is accessible (Supabase project is active)
3. Ensure database exists and migrations are applied
4. Test connection: `psql $DATABASE_URL`

### Error: "Solana RPC endpoint unreachable"

**Solution:**
1. Check `SOLANA_RPC_ENDPOINT` in `.env`
2. Verify network connectivity
3. Try alternative RPC endpoint (e.g., QuickNode, Helius)
4. Check RPC rate limits if using public endpoints

### Error: "Program ID not found"

**Cause:** Solana program not deployed or wrong program ID

**Solution:**
1. Verify `SOLANA_PROGRAM_ID` matches deployed program
2. Deploy program if needed: `anchor deploy` (from `solracer-program/`)
3. Check program is deployed: `solana program show $SOLANA_PROGRAM_ID`

### Issue: Chart data not updating

**Cause:** Birdeye API rate limits or caching

**Solution:**
1. Add `BIRDEYE_API_KEY` to `.env` for higher rate limits
2. Use `force_refresh=true` query parameter to bypass cache
3. Check Birdeye API status and rate limits

---

## API Endpoints

### Authentication & Wallet
- `POST /api/v1/auth/wallet` - Verify wallet signature
- `POST /api/v1/auth/login` - User login

### Tokens
- `GET /api/v1/tokens` - Get list of curated tokens

### Tracks
- `GET /api/v1/track?token_mint=<address>&seed=<optional>&force_refresh=<optional>` - Get normalized track data

### Races
- `POST /api/v1/races/create_or_join` - Create or join race
- `POST /api/v1/races/{race_id}/submit_result` - Submit race result
- `GET /api/v1/races/{race_id}/status` - Get race status

### Transactions
- `POST /api/v1/transactions/build` - Build Solana transaction
- `POST /api/v1/transactions/submit` - Submit signed transaction

### Payouts
- `GET /api/v1/payouts/{race_id}` - Get payout status

See [Swagger UI](http://localhost:8000/docs) for complete API documentation.

---

## Database Migrations

The backend uses Alembic for database schema migrations.

**Apply migrations:**
```bash
alembic upgrade head
```

**Create new migration:**
```bash
alembic revision --autogenerate -m "Description"
```

**Check migration status:**
```bash
alembic current
```

---

## Documentation

- **[Root README](../README.md)** - App overview and screenshots
- **[Backend Documentation](documentation/README.md)** - Detailed phase documentation

---

## Resources

### Official Documentation
- [FastAPI Docs](https://fastapi.tiangolo.com/)
- [Solana Web3.py](https://michaelhly.com/solana-py/)
- [Jupiter Swap API](https://docs.jup.ag/)
- [Birdeye API](https://docs.birdeye.so/)

### Developer Tools
- [Supabase Dashboard](https://app.supabase.com/)
- [Solana Explorer](https://explorer.solana.com/)
- [Postman](https://www.postman.com/) - API testing

---

## License

MIT License - See [LICENSE](../LICENSE) for details
