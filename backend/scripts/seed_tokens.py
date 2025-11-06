"""
Database seeding script.

Populates the tokens table with curated tokens for racing.
Run this script after setting up the database to seed initial token data.
"""

import sys
from pathlib import Path

# Add parent directory to path to import app modules
backend_dir = Path(__file__).parent.parent
sys.path.insert(0, str(backend_dir))

from sqlalchemy.orm import Session
from app.database import SessionLocal, engine, Base
from app.models import Token
from dotenv import load_dotenv

# Load environment variables
load_dotenv(dotenv_path=backend_dir / ".env")


# Curated tokens for racing
CURATED_TOKENS = [
    {
        "mint_address": "So11111111111111111111111111111111111111112",
        "symbol": "SOL",
        "name": "Solana",
        "decimals": 9,
        "logo_url": None
    },
    {
        "mint_address": "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",
        "symbol": "BONK",
        "name": "Bonk",
        "decimals": 5,
        "logo_url": None
    },
    {
        "mint_address": "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy",
        "symbol": "META",
        "name": "Meta",
        "decimals": 9,
        "logo_url": None
    }
]


def seed_tokens():
    """Seed tokens table with curated tokens."""
    # Create tables if they don't exist
    Base.metadata.create_all(bind=engine)
    
    db: Session = SessionLocal()
    
    try:
        print("Seeding tokens table...")
        
        for token_data in CURATED_TOKENS:
            # Check if token already exists
            existing_token = db.query(Token).filter(
                Token.mint_address == token_data["mint_address"]
            ).first()
            
            if existing_token:
                print(f"Token {token_data['symbol']} already exists, skipping...")
                continue
            
            # Create new token
            token = Token(**token_data)
            db.add(token)
            print(f"Added token: {token_data['symbol']} ({token_data['name']})")
        
        db.commit()
        print("Tokens seeded successfully!")
        
    except Exception as e:
        db.rollback()
        print(f"Error seeding tokens: {e}")
        raise
    finally:
        db.close()


if __name__ == "__main__":
    seed_tokens()

