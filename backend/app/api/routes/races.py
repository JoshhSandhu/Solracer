"""
Race management endpoints.

Handles race creation, joining, status polling, ready marking, and cancellation.
"""

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_
from typing import Optional, List
from datetime import datetime, timedelta, timezone
import hashlib
import random
import logging

from app.database import get_db
from app.models import Race, RaceResult, Token, RaceStatus, Payout
from app.schemas import (
    CreateRaceRequest,
    RaceResponse,
    RaceStatusResponse,
    RaceStatus as RaceStatusEnum,
    JoinRaceByCodeRequest,
    JoinRaceByIdRequest,
    MarkReadyRequest,
    PublicRaceListItem,
    PlayerResult
)
from app.services.program_client import get_program_client
from app.services.transaction_builder import get_transaction_builder
from app.services.transaction_submitter import get_transaction_submitter
from app.services.pda_utils import derive_race_pda_simple, get_program_id
from solders.pubkey import Pubkey

router = APIRouter()
logger = logging.getLogger(__name__)


# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------

def ensure_timezone_aware(dt: datetime) -> datetime:
    """
    Ensure a datetime is timezone-aware (UTC).
    If it's timezone-naive, assume it's UTC and add timezone info.
    """
    if dt is None:
        return None
    if dt.tzinfo is None:
        # Timezone-naive datetime - assume UTC
        return dt.replace(tzinfo=timezone.utc)
    return dt


def generate_join_code() -> str:
    """
    Generate a 6-character alphanumeric join code.
    Uses uppercase letters and numbers, case-insensitive.
    """
    import string
    characters = string.ascii_uppercase + string.digits
    # Exclude confusing characters: 0, O, I, 1
    characters = characters.replace('0', '').replace('O', '').replace('I', '').replace('1', '')
    return ''.join(random.choice(characters) for _ in range(6))


async def settle_race_onchain(db: Session, race: Race) -> bool:
    """
    Settle a race on-chain by calling the settle_race instruction.
    
    This function builds the settle_race transaction. However, since the transaction
    requires a fee payer to sign it, we need either:
    1. A backend wallet to sign and pay fees (production)
    2. Client-side signing (current approach - returns transaction for client to sign)
    
    For now, this is a placeholder. The actual on-chain settlement will happen when:
    - The winner clicks "Claim Prize" - the client will call settle_race first if needed
    - Or we implement a backend wallet to automatically settle races
    
    Args:
        db: Database session
        race: Race object to settle
        
    Returns:
        bool: True if settlement preparation was successful, False otherwise
    """
    try:
        # For now, log that the race needs on-chain settlement
        # The actual settlement will happen when the winner claims the prize
        # The claim_prize flow will first call settle_race if the race isn't settled on-chain
        
        logger.info(f"[settle_race_onchain] Race {race.race_id} marked for on-chain settlement.")
        logger.info(f"[settle_race_onchain] On-chain settlement will occur when winner claims prize.")
        
        return True
            
    except Exception as e:
        logger.error(f"[settle_race_onchain] ❌ Error preparing race {race.race_id} for on-chain settlement: {e}", exc_info=True)
        return False


def build_settle_race_transaction(race: Race, payer_wallet: str = None) -> dict:
    """
    Build settle_race transaction for client to sign.
    
    Returns the transaction bytes that need to be signed by the payer wallet.
    The settle_race instruction is permissionless, but someone needs to pay tx fees.
    
    Args:
        race: Race object to settle
        payer_wallet: Wallet address that will sign and pay for the transaction.
                     If not provided, defaults to player1_wallet.
        
    Returns:
        dict with transaction_bytes (base64) and other info
    """
    import base64
    
    program_client = get_program_client()
    transaction_builder = get_transaction_builder()
    
    # Derive race PDA
    program_id = get_program_id()
    entry_fee_lamports = round(race.entry_fee_sol * 1_000_000_000)
    
    race_pda_str, bump = derive_race_pda_simple(
        program_id,
        race.race_id,
        race.token_mint,
        entry_fee_lamports
    )
    race_pda = Pubkey.from_string(race_pda_str)
    
    # Build settle_race instruction
    instruction = program_client.build_settle_race_instruction(race_pda=race_pda)
    
    # Get recent blockhash
    recent_blockhash = transaction_builder.get_recent_blockhash()
    if not recent_blockhash:
        raise ValueError("Failed to get recent blockhash")
    
    # Use the provided payer wallet or fall back to player1
    payer_address = payer_wallet if payer_wallet else race.player1_wallet
    payer = Pubkey.from_string(payer_address)
    
    # Build transaction
    transaction = transaction_builder.build_transaction(
        instructions=[instruction],
        payer=payer,
        recent_blockhash=recent_blockhash
    )
    
    # Serialize transaction
    transaction_bytes = transaction_builder.serialize_transaction(transaction)
    transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
    
    return {
        "transaction_bytes": transaction_b64,
        "race_id": race.race_id,
        "race_pda": race_pda_str,
        "recent_blockhash": recent_blockhash
    }


def check_and_cancel_expired_races(db: Session):
    """
    Cancel races that have expired and clean up very old races.
    - Public races: cancel after 5 minutes
    - Private races: cancel after 10 minutes
    - Any race (any status): hard-delete after 10 minutes from creation
    """
    now = datetime.now(timezone.utc)
    
    # Fetch all waiting races (we'll filter in Python to handle timezone issues)
    waiting_races = db.query(Race).filter(
        and_(
            Race.status == RaceStatus.WAITING,
            Race.expires_at.isnot(None)
        )
    ).all()
    
    # Cancel expired public races (5 minutes)
    for race in waiting_races:
        if not race.is_private and race.expires_at:
            expires_at_aware = ensure_timezone_aware(race.expires_at)
            if expires_at_aware and expires_at_aware <= now:
                race.status = RaceStatus.CANCELLED
                db.commit()
    
    # Cancel expired private races (10 minutes)
    for race in waiting_races:
        if race.is_private and race.expires_at:
            expires_at_aware = ensure_timezone_aware(race.expires_at)
            if expires_at_aware and expires_at_aware <= now:
                race.status = RaceStatus.CANCELLED
                db.commit()

    # Hard-delete any races older than 10 minutes (completed or not)
    ten_minutes_ago = now - timedelta(minutes=10)
    old_races = db.query(Race).filter(Race.created_at <= ten_minutes_ago).all()

    for race in old_races:
        # Delete related results
        db.query(RaceResult).filter(RaceResult.race_id == race.id).delete()
        # Delete related payouts
        db.query(Payout).filter(Payout.race_id == race.id).delete()
        # Delete the race itself
        db.delete(race)

    if old_races:
        db.commit()


def generate_race_id(token_mint: str, entry_fee: float, player1: str) -> str:
    """
    Generate unique race ID with timestamp to prevent PDA collisions.
    
    NOTE: Race ID must be ≤ 32 bytes for Solana PDA seed compatibility.
    We use a 32-character hex hash (32 bytes when encoded as UTF-8).
    
    The timestamp ensures each race gets a unique ID even if the same
    player creates multiple races with the same token and entry fee.
    """
    import time
    timestamp = time.time_ns()  # Nanosecond precision for uniqueness
    seed_string = f"{token_mint}_{entry_fee}_{player1}_{timestamp}"
    race_id_hash = hashlib.sha256(seed_string.encode()).hexdigest()[:32]
    return race_id_hash


def _race_to_dict(race: Race) -> dict:
    """Convert a Race ORM object to a dict suitable for RaceResponse."""
    return {
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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready,
    }


# ---------------------------------------------------------------------------
# Route: POST /races/create
# ---------------------------------------------------------------------------

@router.post("/races/create", response_model=RaceResponse)
async def create_race(
    request: CreateRaceRequest,
    db: Session = Depends(get_db)
):
    """
    Create a new race explicitly (for lobby system).
    
    For public races: Auto-matchmaking will happen when another player calls join.
    For private races: Returns a join code that other players can use.
    """
    # Check and cancel expired races first
    check_and_cancel_expired_races(db)
    
    # Get token info
    token = db.query(Token).filter(Token.mint_address == request.token_mint).first()
    if not token:
        raise HTTPException(status_code=404, detail=f"Token {request.token_mint} not found")
    
    # Generate race ID
    race_id = generate_race_id(request.token_mint, request.entry_fee_sol, request.wallet_address)
    
    # Check if race with this ID already exists
    existing_race = db.query(Race).filter(Race.race_id == race_id).first()
    if existing_race:
        return RaceResponse(**_race_to_dict(existing_race))
    
    # Generate join code for private races
    join_code = None
    if request.is_private:
        # Generate unique join code
        max_attempts = 10
        for _ in range(max_attempts):
            code = generate_join_code()
            existing_code = db.query(Race).filter(Race.join_code == code).first()
            if not existing_code:
                join_code = code
                break
        
        if not join_code:
            raise HTTPException(status_code=500, detail="Failed to generate unique join code")
    
    # Set expiration time
    expiration_minutes = 10 if request.is_private else 5
    expires_at = datetime.now(timezone.utc) + timedelta(minutes=expiration_minutes)
    
    # Generate track seed
    track_seed = hash(race_id) % 1000000
    
    # Create race
    new_race = Race(
        race_id=race_id,
        token_mint=request.token_mint,
        token_symbol=token.symbol,
        entry_fee_sol=request.entry_fee_sol,
        player1_wallet=request.wallet_address,
        status=RaceStatus.WAITING,
        track_seed=track_seed,
        track_data=None,
        is_private=request.is_private,
        join_code=join_code,
        expires_at=expires_at,
        player1_ready=False,
        player2_ready=False
    )
    
    db.add(new_race)
    db.commit()
    db.refresh(new_race)
    
    return RaceResponse(**_race_to_dict(new_race))


# ---------------------------------------------------------------------------
# Route: POST /races/{race_id}/join
# ---------------------------------------------------------------------------

@router.post("/races/{race_id}/join", response_model=RaceResponse)
async def join_race_by_id(
    race_id: str,
    request: JoinRaceByIdRequest,
    db: Session = Depends(get_db)
):
    """
    Join a public race by race_id.
    """
    check_and_cancel_expired_races(db)
    
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    if race.is_private:
        raise HTTPException(status_code=400, detail="Cannot join private race by ID. Use join-by-code endpoint.")
    
    if race.status != RaceStatus.WAITING:
        raise HTTPException(status_code=400, detail=f"Race is not waiting for players. Status: {race.status}")
    
    if race.player1_wallet == request.wallet_address:
        raise HTTPException(status_code=400, detail="Cannot join your own race")
    
    if race.player2_wallet is not None:
        raise HTTPException(status_code=400, detail="Race is already full")
    
    # Join the race
    race.player2_wallet = request.wallet_address
    race.status = RaceStatus.ACTIVE
    race.started_at = datetime.now(timezone.utc)
    db.commit()
    db.refresh(race)
    
    return RaceResponse(**_race_to_dict(race))


# ---------------------------------------------------------------------------
# Route: POST /races/join-by-code
# ---------------------------------------------------------------------------

@router.post("/races/join-by-code", response_model=RaceResponse)
async def join_race_by_code(
    request: JoinRaceByCodeRequest,
    db: Session = Depends(get_db)
):
    """
    Join a private race by join code.
    """
    check_and_cancel_expired_races(db)
    
    # Normalize join code (uppercase, case-insensitive)
    join_code = request.join_code.upper().strip()
    
    race = db.query(Race).filter(Race.join_code == join_code).first()
    if not race:
        raise HTTPException(status_code=404, detail="Invalid join code")
    
    if race.status != RaceStatus.WAITING:
        raise HTTPException(status_code=400, detail=f"Race is not waiting for players. Status: {race.status}")
    
    if race.player1_wallet == request.wallet_address:
        raise HTTPException(status_code=400, detail="Cannot join your own race")
    
    if race.player2_wallet is not None:
        raise HTTPException(status_code=400, detail="Race is already full")
    
    # Check if code expired (private codes expire after 5 minutes)
    if race.expires_at:
        expires_at_aware = ensure_timezone_aware(race.expires_at)
        if expires_at_aware and expires_at_aware < datetime.now(timezone.utc):
            race.status = RaceStatus.CANCELLED
            db.commit()
            raise HTTPException(status_code=400, detail="Join code has expired")
    
    # Join the race
    race.player2_wallet = request.wallet_address
    race.status = RaceStatus.ACTIVE
    race.started_at = datetime.now(timezone.utc)
    db.commit()
    db.refresh(race)
    
    return RaceResponse(**_race_to_dict(race))


# ---------------------------------------------------------------------------
# Route: GET /races/public
# ---------------------------------------------------------------------------

@router.get("/races/public", response_model=List[PublicRaceListItem])
async def list_public_races(
    token_mint: Optional[str] = Query(None, description="Filter by token mint"),
    entry_fee: Optional[float] = Query(None, description="Filter by entry fee"),
    db: Session = Depends(get_db)
):
    """
    List available public races waiting for players.
    """
    check_and_cancel_expired_races(db)
    
    query = db.query(Race).filter(
        and_(
            Race.is_private == False,
            Race.status == RaceStatus.WAITING,
            Race.player2_wallet.is_(None)
        )
    )
    
    if token_mint:
        query = query.filter(Race.token_mint == token_mint)
    
    if entry_fee is not None:
        query = query.filter(Race.entry_fee_sol == entry_fee)
    
    races = query.order_by(Race.created_at.desc()).limit(50).all()
    
    return [
        PublicRaceListItem(
            race_id=race.race_id,
            token_mint=race.token_mint,
            token_symbol=race.token_symbol,
            entry_fee_sol=race.entry_fee_sol,
            player1_wallet=race.player1_wallet,
            created_at=race.created_at,
            expires_at=race.expires_at
        )
        for race in races
    ]


# ---------------------------------------------------------------------------
# Route: GET /races/{race_id}/status
# ---------------------------------------------------------------------------

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
    - Ready status for both players
    """
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    # Check and cancel expired races
    check_and_cancel_expired_races(db)
    
    # Re-query race in case it was affected by the expiration check
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found or expired")
    
    # Get winner and player results if race is settled or has results
    winner_wallet = None
    player1_result = None
    player2_result = None
    
    results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
    
    # Auto-settle race if both results exist but race is still ACTIVE
    if race.status == RaceStatus.ACTIVE and len(results) == 2:
        logger.info(f"[get_race_status] Auto-settling race {race_id} - both results present but status is ACTIVE")
        winner_result = min(results, key=lambda r: r.finish_time_ms)
        race.status = RaceStatus.SETTLED
        race.settled_at = datetime.now(timezone.utc)
        db.commit()
        db.refresh(race)
        
        # Auto-create payout if it doesn't exist
        existing_payout = db.query(Payout).filter(Payout.race_id == race.id).first()
        if not existing_payout:
            try:
                from app.services.payout_handler import get_payout_handler
                payout_handler = get_payout_handler()
                payout_handler.create_payout_record(
                    db=db,
                    race=race,
                    winner_wallet=winner_result.wallet_address,
                    winner_result=winner_result
                )
                logger.info(f"[get_race_status] Auto-created payout for race {race_id}")
            except Exception as e:
                logger.error(f"[get_race_status] Error auto-creating payout for race {race_id}: {e}", exc_info=True)
    
    if race.status == RaceStatus.SETTLED and len(results) == 2:
        winner_result = min(results, key=lambda r: r.finish_time_ms)
        winner_wallet = winner_result.wallet_address
    
    # Build player results
    for result in results:
        if result.player_number == 1:
            player1_result = PlayerResult(
                wallet_address=result.wallet_address,
                player_number=1,
                finish_time_ms=result.finish_time_ms,
                coins_collected=result.coins_collected,
                verified=result.verified
            )
        elif result.player_number == 2:
            player2_result = PlayerResult(
                wallet_address=result.wallet_address,
                player_number=2,
                finish_time_ms=result.finish_time_ms,
                coins_collected=result.coins_collected,
                verified=result.verified
            )
    
    return RaceStatusResponse(
        race_id=race_id,
        status=RaceStatusEnum(race.status.value),
        player1_wallet=race.player1_wallet,
        player2_wallet=race.player2_wallet,
        winner_wallet=winner_wallet,
        is_settled=(race.status == RaceStatus.SETTLED),
        player1_ready=race.player1_ready,
        player2_ready=race.player2_ready,
        both_ready=(race.player1_ready and race.player2_ready and race.player2_wallet is not None),
        player1_result=player1_result,
        player2_result=player2_result
    )


# ---------------------------------------------------------------------------
# Route: POST /races/{race_id}/ready
# ---------------------------------------------------------------------------

@router.post("/races/{race_id}/ready")
async def mark_player_ready(
    race_id: str,
    request: MarkReadyRequest,
    db: Session = Depends(get_db)
):
    """
    Mark a player as ready. Race can start when both players are ready.
    """
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    if race.status != RaceStatus.ACTIVE:
        raise HTTPException(status_code=400, detail=f"Race is not active. Status: {race.status}")
    
    if request.wallet_address == race.player1_wallet:
        race.player1_ready = True
    elif request.wallet_address == race.player2_wallet:
        race.player2_ready = True
    else:
        raise HTTPException(status_code=403, detail="Wallet address does not match any player in this race")
    
    db.commit()
    db.refresh(race)
    
    return {
        "message": "Player marked as ready",
        "race_id": race_id,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready,
        "both_ready": race.player1_ready and race.player2_ready
    }


# ---------------------------------------------------------------------------
# Route: DELETE /races/{race_id}
# ---------------------------------------------------------------------------

@router.delete("/races/{race_id}")
async def cancel_race(
    race_id: str,
    wallet_address: str = Query(..., description="Wallet address of player cancelling"),
    db: Session = Depends(get_db)
):
    """
    Cancel a waiting race. Only player1 can cancel.
    """
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail="Race not found")
    
    if race.player1_wallet != wallet_address:
        raise HTTPException(status_code=403, detail="Only the race creator can cancel the race")
    
    if race.status != RaceStatus.WAITING:
        raise HTTPException(status_code=400, detail=f"Cannot cancel race with status {race.status}")
    
    race.status = RaceStatus.CANCELLED
    db.commit()
    
    return {
        "message": "Race cancelled successfully",
        "race_id": race_id
    }
