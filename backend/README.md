# Solracer Backend

FastAPI backend for Solracer game with PostgreSQL/Supabase database.

## Quick Start

1. **Install dependencies**:
   ```bash
   pip install -r requirements.txt
   ```

2. **Set up environment variables**:
   ```bash
   # Create .env file with DATABASE_URL
   # DATABASE_URL=postgresql://postgres:password@db.project.supabase.co:5432/postgres
   # BIRDEYE_API_KEY=your_key_here  # Optional, for higher rate limits
   ```

3. **Apply database migrations** (optional, for new setups):
   ```bash
   alembic upgrade head
   ```

4. **Seed tokens database**:
   ```bash
   python scripts/seed_tokens.py
   ```

5. **Run the server**:
   ```bash
   uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
   ```

6. **Access API docs**:
   - Swagger UI: http://localhost:8000/docs
   - ReDoc: http://localhost:8000/redoc


## Project Structure

```
backend/
├── app/
│   ├── main.py           # FastAPI app entry point
│   ├── database.py       # Database connection & session management
│   ├── models.py         # SQLAlchemy database models
│   ├── schemas.py        # Pydantic request/response schemas
│   ├── services/         # Business logic services
│   │   └── chart_data.py # Chart data fetching & normalization
│   └── api/
│       └── routes/       # API endpoint routes
│           ├── tokens.py # Token endpoints
│           ├── tracks.py # Track endpoints
│           └── races.py  # Race management endpoints
├── alembic/              # Database migrations
│   ├── versions/        # Migration files
│   └── env.py           # Alembic configuration
├── scripts/
│   ├── seed_tokens.py   # Database seeding script
│   └── test_races.ps1   # Race endpoint test script
└── requirements.txt     # Python dependencies
```

## API Endpoints

### GET `/api/v1/tokens`
Get list of curated tokens from database.

### GET `/api/v1/track?token_mint=<address>&seed=<optional>&force_refresh=<optional>`
Get normalized track data for a token. Fetches real chart data from Birdeye API.

**Query Parameters**:
- `token_mint` (required): Solana token mint address
- `seed` (optional): Seed for deterministic generation
- `force_refresh` (optional): Force refresh chart data (default: false)

### POST `/api/v1/races/create_or_join`
Create a new race or join an existing waiting race. Automatic matchmaking based on token and entry fee.

### POST `/api/v1/races/{race_id}/submit_result`
Submit race result with input trace for verification.

### GET `/api/v1/races/{race_id}/status`
Get current race status (waiting, active, settled) and winner information.

## Database Migrations

The backend uses Alembic for database schema migrations.

**Apply migrations**:
```bash
alembic upgrade head
```

**Create new migration**:
```bash
alembic revision --autogenerate -m "Description"
```

**Check migration status**:
```bash
alembic current
```

See `documentation/PHASE3_4_DATABASE_OPTIMIZATION.md` for detailed migration guide.

## Database Setup

The backend uses PostgreSQL (via Supabase or local PostgreSQL).

1. **Create Supabase project** (recommended):
   - Go to https://supabase.com
   - Create new project
   - Get connection string from Project Settings → Database

2. **Set DATABASE_URL** in `.env`:
   ```
   DATABASE_URL=postgresql://postgres:password@db.project.supabase.co:5432/postgres
   ```

3. **Apply database migrations** (optional, for new setups):
   ```bash
   alembic upgrade head
   ```

4. **Seed tokens database**:
   ```bash
   python scripts/seed_tokens.py
   ```

5. **Run the server**:
   ```bash
   uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
   ```

6. **Access API docs**:
   - Swagger UI: http://localhost:8000/docs
   - ReDoc: http://localhost:8000/redoc




