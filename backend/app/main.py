"""
FastAPI application entry point.

This is the main application file that sets up the FastAPI app,
configures middleware, includes routers, and handles startup/shutdown events.
"""

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
import os
from contextlib import asynccontextmanager
from dotenv import load_dotenv
from pathlib import Path

from app.api.routes import tokens, tracks, races, solana_transactions, payouts
from app.database import engine, Base

# Load environment variables from .env file
# Get the backend directory (parent of app/)
backend_dir = Path(__file__).parent.parent
load_dotenv(dotenv_path=backend_dir / ".env")

# Get configuration from environment
API_V1_PREFIX = os.getenv("API_V1_PREFIX", "/api/v1")
DEBUG = os.getenv("DEBUG", "False").lower() == "true"
PROJECT_NAME = os.getenv("PROJECT_NAME", "Solracer Backend")


@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Lifespan context manager for startup and shutdown events.
    
    - Startup: Create database tables (if they don't exist)
    - Shutdown: Clean up resources
    """
    # Startup: Create database tables (test connection)
    print("Starting Solracer Backend...")
    try:
        print(f"Testing database connection...")
        # Try to connect and create tables
        Base.metadata.create_all(bind=engine)
        print("✓ Database connection successful. Tables created/verified.")
    except Exception as e:
        print(f"⚠ Warning: Database connection failed: {e}")
        print("⚠ Backend will continue, but database-dependent endpoints may not work.")
        print("⚠ For Phase 6 testing, transaction endpoints (/transactions/build, /transactions/submit)")
        print("⚠ and track endpoint (/track) should still work without database.")
        print("⚠ To fix: Update DATABASE_URL in backend/.env file with valid connection string.")
    
    yield
    
    # Shutdown: Clean up (if needed)
    print("Shutting down Solracer Backend...")


# Create FastAPI application
app = FastAPI(
    title=PROJECT_NAME,
    description="Backend API for Solracer - A fast-paced line-riding game on Solana",
    version="1.0.0",
    debug=DEBUG,
    lifespan=lifespan
)

# Configure CORS
# In production, restrict ALLOWED_ORIGINS to your Unity builds and web frontend
ALLOWED_ORIGINS = os.getenv("ALLOWED_ORIGINS", "http://localhost:3000,http://localhost:8080").split(",")

app.add_middleware(
    CORSMiddleware,
    allow_origins=ALLOWED_ORIGINS,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# Include API routers
app.include_router(tokens.router, prefix=API_V1_PREFIX, tags=["tokens"])
app.include_router(tracks.router, prefix=API_V1_PREFIX, tags=["tracks"])
app.include_router(races.router, prefix=API_V1_PREFIX, tags=["races"])
app.include_router(solana_transactions.router, prefix=API_V1_PREFIX, tags=["solana-transactions"])
app.include_router(payouts.router, prefix=API_V1_PREFIX, tags=["payouts"])


@app.get("/")
async def root():
    """Root endpoint - API health check."""
    return {
        "message": "Solracer Backend API",
        "version": "1.0.0",
        "status": "healthy",
        "docs": "/docs"
    }


@app.get("/health")
async def health_check():
    """Health check endpoint for monitoring."""
    return {"status": "healthy"}


@app.exception_handler(Exception)
async def global_exception_handler(request, exc):
    """Global exception handler for unhandled errors."""
    return JSONResponse(
        status_code=500,
        content={
            "error": "Internal server error",
            "message": str(exc) if DEBUG else "An error occurred"
        }
    )


if __name__ == "__main__":
    import uvicorn
    
    # Run with uvicorn (for development)
    # In production, use: uvicorn app.main:app --host 0.0.0.0 --port 8000
    uvicorn.run(
        "app.main:app",
        host="0.0.0.0",
        port=8000,
        reload=DEBUG
    )

