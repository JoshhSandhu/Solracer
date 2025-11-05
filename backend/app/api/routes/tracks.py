"""
Track endpoints.

Provides track data for race generation.
"""

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
import random
import math
from typing import List
from app.database import get_db
from app.schemas import TrackResponse, TrackSample

router = APIRouter()


@router.get("/track", response_model=TrackResponse)
async def get_track(
    token_mint: str = Query(..., description="Solana token mint address"),
    seed: int = Query(None, description="Seed for deterministic track generation (optional)"),
    db: Session = Depends(get_db)
):
    """
    Get track data for a specific token.
    
    Returns normalized track samples (0-1 range) for track generation.
    Initially returns mock data, later will fetch from chart APIs (Birdeye/Dexscreener).
    
    Args:
        token_mint: Solana token mint address
        seed: Optional seed for deterministic generation (if not provided, random seed is used)
    
    Returns:
        TrackResponse with token info, seed, and normalized samples
    """
    # TODO: Fetch real chart data from Birdeye/Dexscreener API
    # TODO: Normalize chart data to 0-1 range
    # TODO: Cache chart data in database
    
    # For now, generate mock track data
    if seed is None:
        seed = random.randint(0, 1000000)
    
    # Use seed for deterministic generation
    random.seed(seed)
    
    # Generate mock track samples (sine wave with noise)
    num_samples = 1000
    samples: List[TrackSample] = []
    
    for i in range(num_samples):
        x = i / (num_samples - 1)  # Normalized X (0-1)
        
        # Generate Y using sine wave with noise (normalized to 0-1)
        base_y = 0.5 + 0.3 * math.sin(x * math.pi * 4)  # Base sine wave
        noise = random.uniform(-0.1, 0.1)  # Random noise
        y = max(0.0, min(1.0, base_y + noise))  # Clamp to 0-1
        
        samples.append(TrackSample(x=x, y=y))
    
    # Get token symbol (mock for now)
    token_symbols = {
        "So11111111111111111111111111111111111111112": "SOL",
        "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263": "BONK",
        "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy": "META"
    }
    
    token_symbol = token_symbols.get(token_mint, "UNKNOWN")
    
    return TrackResponse(
        token_mint=token_mint,
        token_symbol=token_symbol,
        seed=seed,
        samples=samples,
        point_count=len(samples)
    )

