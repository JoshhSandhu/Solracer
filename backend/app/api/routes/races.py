"""
Race management endpoints.

Handles race creation, joining, result submission, and status polling.
"""

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_
from typing import Optional
from datetime import datetime, timedelta
import hashlib
import json
import random

from app.database import get_db
from app.models import Race, RaceResult, Token, RaceStatus
from app.schemas import (
    CreateRaceRequest,
    SubmitResultRequest,
    RaceResponse,
    RaceStatusResponse,
    RaceStatus as RaceStatusEnum
)
from app.services.chart_data import chart_data_service

router = APIRouter()


def generate_race_id(token_mint: str, entry_fee: float, player1: str) -> str:
    """
    Generate a deterministic race ID based on race parameters.
    
    In production, this would be the Solana PDA address from the on-chain program.
    For now, we use a hash-based approach for consistency.
    """
    # Create a deterministic seed from race parameters
    seed_string = f"{token_mint}_{entry_fee}_{player1}"
    race_id_hash = hashlib.sha256(seed_string.encode()).hexdigest()[:32]
    return f"race_{race_id_hash}"


def find_or_create_race(
    db: Session,
    token_mint: str,
    entry_fee_sol: float,
    wallet_address: str
) -> Race:
    """
    Find an existing waiting race or create a new one.
    
    Matchmaking logic:
    - Look for races with same token_mint and entry_fee_sol
    - Status must be WAITING
    - Player1 must not be the same wallet (can't race yourself)
    - If found, join it (set player2_wallet, status=ACTIVE)
    - If not found, create new race (status=WAITING)
    """
    # Generate race ID first (deterministic)
    race_id = generate_race_id(token_mint, entry_fee_sol, wallet_address)
    
    # Check if race with this ID already exists
    existing_race_by_id = db.query(Race).filter(Race.race_id == race_id).first()
    
    if existing_race_by_id:
        # Race with this ID already exists
        if existing_race_by_id.status == RaceStatus.WAITING:
            # Can join if it's waiting and we're not player1
            if existing_race_by_id.player1_wallet != wallet_address:
                existing_race_by_id.player2_wallet = wallet_address
                existing_race_by_id.status = RaceStatus.ACTIVE
                existing_race_by_id.started_at = datetime.now()
                db.commit()
                db.refresh(existing_race_by_id)
                return existing_race_by_id
            else:
                # Same player trying to create again - return existing race
                return existing_race_by_id
        else:
            # Race exists but not waiting - return it (might be active/settled)
            return existing_race_by_id
    
    # Look for existing waiting race with matching parameters (different race_id)
    existing_race = db.query(Race).filter(
        and_(
            Race.token_mint == token_mint,
            Race.entry_fee_sol == entry_fee_sol,
            Race.status == RaceStatus.WAITING,
            Race.player1_wallet != wallet_address  # Can't join your own race
        )
    ).first()
    
    if existing_race:
        # Join existing race
        existing_race.player2_wallet = wallet_address
        existing_race.status = RaceStatus.ACTIVE
        existing_race.started_at = datetime.now()
        db.commit()
        db.refresh(existing_race)
        return existing_race
    
    # Create new race
    # Get token info
    token = db.query(Token).filter(Token.mint_address == token_mint).first()
    if not token:
        raise HTTPException(status_code=404, detail=f"Token {token_mint} not found")
    
    # Generate track seed (deterministic based on race_id)
    track_seed = hash(race_id) % 1000000
    
    # Get track data for caching (skip for now, will be fetched when needed)
    # Track data can be fetched on-demand when race starts
    track_data = None
    
    # Create race
    new_race = Race(
        race_id=race_id,
        token_mint=token_mint,
        token_symbol=token.symbol,
        entry_fee_sol=entry_fee_sol,
        player1_wallet=wallet_address,
        status=RaceStatus.WAITING,
        track_seed=track_seed,
        track_data=track_data
    )
    
    db.add(new_race)
    db.commit()
    db.refresh(new_race)
    
    return new_race


@router.post("/races/create_or_join", response_model=RaceResponse)
async def create_or_join_race(
    request: CreateRaceRequest,
    db: Session = Depends(get_db)
):
    """
    Create a new race or join an existing waiting race.
    
    Matchmaking logic:
    - Searches for races with same token_mint and entry_fee_sol
    - If found, joins it (becomes player2, race status becomes ACTIVE)
    - If not found, creates new race (status WAITING)
    
    Returns race information including race_id and track_seed.
    """
    race = find_or_create_race(
        db=db,
        token_mint=request.token_mint,
        entry_fee_sol=request.entry_fee_sol,
        wallet_address=request.wallet_address
    )
    
    # Convert UUID to string for response
    race_dict = {
        "id": str(race.id),
        "race_id": race.race_id,
        "token_mint": race.token_mint,
        "token_symbol": race.token_symbol,
        "entry_fee_sol": race.entry_fee_sol,
        "player1_wallet": race.player1_wallet,
        "player2_wallet": race.player2_wallet,
        "status": race.status.value,
        "track_seed": race.track_seed,
        "created_at": race.created_at,
        "solana_tx_signature": race.solana_tx_signature
    }
    
    return RaceResponse(**race_dict)


@router.post("/races/{race_id}/submit_result")
async def submit_result(
    race_id: str,
    request: SubmitResultRequest,
    db: Session = Depends(get_db)
):
    """
    Submit race result with input trace for verification.
    
    Validates:
    - Race exists and is ACTIVE
    - Wallet address matches player1 or player2
    - Result hasn't been submitted already
    
    Stores result and triggers replay verification.
    """
    # Find race
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    # Validate race status
    if race.status != RaceStatus.ACTIVE:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot submit result for race with status {race.status}"
        )
    
    # Validate wallet address
    if request.wallet_address not in [race.player1_wallet, race.player2_wallet]:
        raise HTTPException(
            status_code=403,
            detail="Wallet address does not match any player in this race"
        )
    
    # Check if result already submitted
    existing_result = db.query(RaceResult).filter(
        and_(
            RaceResult.race_id == race.id,
            RaceResult.wallet_address == request.wallet_address
        )
    ).first()
    
    if existing_result:
        raise HTTPException(
            status_code=400,
            detail="Result already submitted for this race"
        )
    
    # Determine player number
    player_number = 1 if request.wallet_address == race.player1_wallet else 2
    
    # Store input trace as JSON
    input_trace_json = None
    if request.input_trace:
        input_trace_json = json.dumps(request.input_trace)
    
    # Create race result
    result = RaceResult(
        race_id=race.id,
        wallet_address=request.wallet_address,
        player_number=player_number,
        finish_time_ms=request.finish_time_ms,
        coins_collected=request.coins_collected,
        input_hash=request.input_hash,
        input_trace=input_trace_json,
        verified=False
    )
    
    db.add(result)
    db.commit()
    db.refresh(result)
    
    # Trigger replay verification (async, non-blocking)
    # For now, we'll verify synchronously, but this could be moved to a background task
    await verify_replay(db, race, result)
    
    # Check if both results are submitted
    results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
    if len(results) == 2:
        # Both players submitted, determine winner
        winner_result = min(results, key=lambda r: r.finish_time_ms)
        race.status = RaceStatus.SETTLED
        race.settled_at = datetime.now()
        db.commit()
        
        # Phase 7: Trigger payout processing (async, non-blocking)
        # Payout will be processed when winner calls /payouts/{race_id}/process
        # For automatic processing, a background worker would handle this
    
    return {
        "message": "Result submitted successfully",
        "race_id": race_id,
        "verified": result.verified
    }


async def verify_replay(db: Session, race: Race, result: RaceResult):
    """
    Verify race result by replaying input trace.
    
    This is a simplified verification. In production, you would:
    1. Load track data from race.track_data
    2. Replay input trace step-by-step
    3. Calculate expected finish time
    4. Compare with submitted finish_time_ms (±50ms tolerance)
    """
    # For now, we'll do basic validation
    # In production, implement full replay simulation
    
    if not result.input_trace:
        result.verified = False
        result.verification_tolerance_ms = None
        db.commit()
        return
    
    # Basic validation: check input_hash matches input_trace
    try:
        input_trace_data = json.loads(result.input_trace)
        trace_string = json.dumps(input_trace_data, sort_keys=True)
        calculated_hash = hashlib.sha256(trace_string.encode()).hexdigest()
        
        if calculated_hash == result.input_hash:
            # Hash matches, mark as verified (simplified - full replay would verify time)
            result.verified = True
            result.verification_tolerance_ms = 50  # Default tolerance
            result.verified_at = datetime.now()
        else:
            result.verified = False
            result.verification_tolerance_ms = None
    except Exception as e:
        result.verified = False
        result.verification_tolerance_ms = None
        print(f"Replay verification error: {e}")
    
    db.commit()


@router.get("/races/{race_id}/status", response_model=RaceStatusResponse)
async def get_race_status(
    race_id: str,
    db: Session = Depends(get_db)
):
    """
    Get current race status.
    
    Returns race information including:
    - Current status (WAITING, ACTIVE, SETTLED, CANCELLED)
    - Player wallets
    - Winner wallet (if settled)
    - Whether race is settled
    """
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    # Get winner if race is settled
    winner_wallet = None
    if race.status == RaceStatus.SETTLED:
        results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
        if len(results) == 2:
            winner_result = min(results, key=lambda r: r.finish_time_ms)
            winner_wallet = winner_result.wallet_address
    
    return RaceStatusResponse(
        race_id=race_id,
        status=RaceStatusEnum(race.status.value),
        player1_wallet=race.player1_wallet,
        player2_wallet=race.player2_wallet,
        winner_wallet=winner_wallet,
        is_settled=(race.status == RaceStatus.SETTLED)
    )

