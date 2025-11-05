# Solracer Backend

FastAPI backend for Solracer game with PostgreSQL/Supabase database.

## Quick Start

1. **Install dependencies**:
   ```bash
   pip install -r requirements.txt
   ```

2. **Set up environment variables**:
   ```bash
   cp .env.example .env
   # Edit .env and add your DATABASE_URL
   ```

3. **Run the server**:
   ```bash
   uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
   ```

4. **Access API docs**:
   - Swagger UI: http://localhost:8000/docs
   - ReDoc: http://localhost:8000/redoc

## Documentation

See [PHASE3_BACKEND_SETUP.md](./PHASE3_BACKEND_SETUP.md) for detailed setup instructions and explanations.

## Project Structure

```
backend/
├── app/
│   ├── main.py           # FastAPI app entry point
│   ├── database.py       # Database connection & session management
│   ├── models.py         # SQLAlchemy database models
│   ├── schemas.py        # Pydantic request/response schemas
│   └── api/
│       └── routes/       # API endpoint routes
├── requirements.txt      # Python dependencies
├── .env.example          # Environment variables template
└── PHASE3_BACKEND_SETUP.md  # Detailed setup guide
```

## API Endpoints

### GET `/api/v1/tokens`
Get list of curated tokens.

### GET `/api/v1/track?token_mint=<address>&seed=<optional>`
Get track data for a token.

## Database Setup

The backend uses PostgreSQL (via Supabase or local PostgreSQL).

See [PHASE3_BACKEND_SETUP.md](./PHASE3_BACKEND_SETUP.md) for database setup instructions.

