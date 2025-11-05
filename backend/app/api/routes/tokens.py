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
    
    Returns all tokens available for racing.
    Initially returns mock data, later will query database.
    """
    # TODO: Query database once tokens are seeded
    # For now, return mock data matching the game's token selection
    mock_tokens = [
        {
            "mint_address": "So11111111111111111111111111111111111111112",  # SOL
            "symbol": "SOL",
            "name": "Solana",
            "decimals": 9,
            "logo_url": None
        },
        {
            "mint_address": "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",  # BONK
            "symbol": "BONK",
            "name": "Bonk",
            "decimals": 5,
            "logo_url": None
        },
        {
            "mint_address": "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy",  # META
            "symbol": "META",
            "name": "Meta",
            "decimals": 9,
            "logo_url": None
        }
    ]
    
    return mock_tokens


@router.get("/tokens/{mint_address}", response_model=TokenResponse)
async def get_token(mint_address: str, db: Session = Depends(get_db)):
    """
    Get specific token information by mint address.
    
    Returns token details including symbol, name, and decimals.
    """
    # TODO: Query database once tokens are seeded
    # For now, return mock data or 404
    mock_tokens = {
        "So11111111111111111111111111111111111111112": {
            "mint_address": "So11111111111111111111111111111111111111112",
            "symbol": "SOL",
            "name": "Solana",
            "decimals": 9,
            "logo_url": None
        },
        "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263": {
            "mint_address": "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",
            "symbol": "BONK",
            "name": "Bonk",
            "decimals": 5,
            "logo_url": None
        },
        "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy": {
            "mint_address": "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy",
            "symbol": "META",
            "name": "Meta",
            "decimals": 9,
            "logo_url": None
        }
    }
    
    if mint_address not in mock_tokens:
        raise HTTPException(status_code=404, detail="Token not found")
    
    return mock_tokens[mint_address]

