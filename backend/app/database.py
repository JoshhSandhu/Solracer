"""
Database connection and session management.

This module handles SQLAlchemy database setup, connection pooling,
and session management for production use.
"""

from sqlalchemy import create_engine
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import NullPool
import os
from typing import Generator
from dotenv import load_dotenv
from pathlib import Path

# Load environment variables from .env file
# Get the backend directory (parent of app/)
backend_dir = Path(__file__).parent.parent
load_dotenv(dotenv_path=backend_dir / ".env")

# Get database URL from environment variable
DATABASE_URL = os.getenv("DATABASE_URL")

if not DATABASE_URL:
    raise ValueError("DATABASE_URL environment variable is not set")

# Create SQLAlchemy engine
# For production: Use connection pooling
# For Supabase: Connection string handles pooling automatically
engine = create_engine(
    DATABASE_URL,
    # Connection pool settings for production
    pool_size=10,  # Number of connections to maintain
    max_overflow=20,  # Additional connections beyond pool_size
    pool_pre_ping=True,  # Verify connections before using them
    pool_recycle=3600,  # Recycle connections after 1 hour
    echo=False,  # Set to True for SQL query logging (debug only)
)

# Create session factory
SessionLocal = sessionmaker(
    autocommit=False,
    autoflush=False,
    bind=engine
)

# Base class for declarative models
Base = declarative_base()


def get_db() -> Generator:
    """
    Dependency function for FastAPI routes.
    Yields a database session and ensures it's closed after use.
    
    Usage in FastAPI route:
        @app.get("/items")
        def get_items(db: Session = Depends(get_db)):
            return db.query(Item).all()
    """
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()

