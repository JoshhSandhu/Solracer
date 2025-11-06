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
from app.models import Token
from app.services.chart_data import chart_data_service

router = APIRouter()


@router.get("/track", response_model=TrackResponse)
async def get_track(
    token_mint: str = Query(..., description="Solana token mint address"),
    seed: int = Query(None, description="Seed for deterministic track generation"),
    force_refresh: bool = Query(False, description="Force refresh chart data from API"),
    db: Session = Depends(get_db)
):
    """
    Get track data for a specific token.
    
    Returns normalized track samples (0-1 range) for track generation.
    Fetches real chart data from Birdeye API, normalizes it, and caches it in the database.
    
    Args:
        token_mint: Solana token mint address
        seed: Optional seed for deterministic generation (used for race replay verification)
        force_refresh: If True, fetch fresh chart data even if cache is valid
    
    Returns:
        TrackResponse with token info, seed, and normalized samples
    """
    # Get token from database
    token = db.query(Token).filter(Token.mint_address == token_mint).first()
    
    if not token:
        raise HTTPException(
            status_code=404,
            detail=f"Token with mint address {token_mint} not found. Please ensure token is seeded in database."
        )
    
    # Generate seed if not provided use hash of token mint + current time for uniqueness
    if seed is None:
        # Use a deterministic seed based on token mint for consistency
       
        seed = hash(token_mint) % 1000000
    
    # Fetch normalized chart data
    normalized_samples = await chart_data_service.get_chart_data_for_token(
        db=db,
        token=token,
        force_refresh=force_refresh
    )
    
    if not normalized_samples:
        # Fallback to mock data if API fetch fails
        # Use seed for deterministic generation
        random.seed(seed)
        
        num_samples = 1000
        samples: List[TrackSample] = []
        
        for i in range(num_samples):
            x = i / (num_samples - 1)  # Normalized X (0-1)
            
            # Generate Y using sine wave with noise (normalized to 0-1)
            base_y = 0.5 + 0.3 * math.sin(x * math.pi * 4)  # Base sine wave
            noise = random.uniform(-0.1, 0.1)  # Random noise
            y = max(0.0, min(1.0, base_y + noise))  # Clamp to 0-1
            
            samples.append(TrackSample(x=x, y=y))
        
        return TrackResponse(
            token_mint=token_mint,
            token_symbol=token.symbol,
            seed=seed,
            samples=samples,
            point_count=len(samples)
        )
    
    # Convert normalized samples to TrackSample objects
    samples = [
        TrackSample(x=sample["x"], y=sample["y"])
        for sample in normalized_samples
    ]
    
    return TrackResponse(
        token_mint=token_mint,
        token_symbol=token.symbol,
        seed=seed,
        samples=samples,
        point_count=len(samples)
    )

