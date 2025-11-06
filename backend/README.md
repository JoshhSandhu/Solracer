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

3. **Seed tokens database**:
   ```bash
   python scripts/seed_tokens.py
   ```

4. **Run the server**:
   ```bash
   uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
   ```

5. **Access API docs**:
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
├── scripts/
│   └── seed_tokens.py   # Database seeding script
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

3. **Seed tokens**:
   ```bash
   python scripts/seed_tokens.py
   ```

## Chart Data Integration

The backend fetches real token price data from Birdeye API and normalizes it to generate race tracks.

- **API**: Birdeye (https://birdeye.so)
- **Cache Duration**: 1 hour (configurable)
- **Normalization**: Prices converted to 0-1 range



