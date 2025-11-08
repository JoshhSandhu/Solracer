"""
Token endpoints.

Provides token information including curated tokens list.
"""

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from typing import List
from app.database import get_db
from app.schemas import TokenResponse
from app.models import Token

router = APIRouter()


@router.get("/tokens", response_model=List[TokenResponse])
async def get_tokens(db: Session = Depends(get_db)):
    """
    Get list of curated tokens.
    
    Returns all tokens available for racing from the database.
    """
    tokens = db.query(Token).all()
    
    if not tokens:
        #return mock data if database is empty (for initial setup)
        return [
            TokenResponse(
                mint_address="So11111111111111111111111111111111111111112",
                symbol="SOL",
                name="Solana",
                decimals=9,
                logo_url=None
            ),
            TokenResponse(
                mint_address="DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",
                symbol="BONK",
                name="Bonk",
                decimals=5,
                logo_url=None
            ),
            TokenResponse(
                mint_address="METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy",
                symbol="META",
                name="Meta",
                decimals=9,
                logo_url=None
            )
        ]
    
    return [TokenResponse.model_validate(token) for token in tokens]


@router.get("/tokens/{mint_address}", response_model=TokenResponse)
async def get_token(mint_address: str, db: Session = Depends(get_db)):
    """
    Get specific token information by mint address.
    
    Returns token details including symbol, name, and decimals.
    """
    token = db.query(Token).filter(Token.mint_address == mint_address).first()
    
    if not token:
        raise HTTPException(status_code=404, detail="Token not found")
    
    return TokenResponse.model_validate(token)

