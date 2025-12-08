"""
Race management endpoints.

Handles race creation, joining, result submission, and status polling.
"""

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
from sqlalchemy import and_, or_
from typing import Optional, List
from datetime import datetime, timedelta, timezone
import hashlib
import json
import random
import logging

from app.database import get_db
from app.models import Race, RaceResult, Token, RaceStatus, Payout
from app.schemas import (
    CreateRaceRequest,
    SubmitResultRequest,
    RaceResponse,
    RaceStatusResponse,
    RaceStatus as RaceStatusEnum,
    JoinRaceByCodeRequest,
    JoinRaceByIdRequest,
    MarkReadyRequest,
    PublicRaceListItem,
    PlayerResult
)
from app.services.chart_data import chart_data_service
from app.services.program_client import get_program_client
from app.services.transaction_builder import get_transaction_builder
from app.services.transaction_submitter import get_transaction_submitter
from app.services.pda_utils import derive_race_pda_simple, get_program_id
from solders.pubkey import Pubkey

router = APIRouter()
logger = logging.getLogger(__name__)


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
        
        # Mark in database that on-chain settlement is pending
        # (Optional: add a field to track this)
        
        # For production, you would:
        # 1. Have a backend wallet with SOL for transaction fees
        # 2. Sign the settle_race transaction with the backend wallet
        # 3. Submit it to the network
        #
        # Example production code:
        # from solders.keypair import Keypair
        # backend_wallet = Keypair.from_bytes(os.getenv("BACKEND_WALLET_SECRET_KEY"))
        # transaction.sign([backend_wallet])
        # signature = transaction_submitter.submit_transaction(transaction)
        
        return True
            
    except Exception as e:
        logger.error(f"[settle_race_onchain] ❌ Error preparing race {race.race_id} for on-chain settlement: {e}", exc_info=True)
        return False


def build_settle_race_transaction(race: Race) -> dict:
    """
    Build settle_race transaction for client to sign.
    
    Returns the transaction bytes that need to be signed by any wallet
    (the settle_race instruction is permissionless).
    
    Args:
        race: Race object to settle
        
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
    
    # Use player1 as the payer (the client will replace this with their wallet)
    payer = Pubkey.from_string(race.player1_wallet)
    
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
    Generate a deterministic race ID based on race parameters.
    
    NOTE: Race ID must be ≤ 32 bytes for Solana PDA seed compatibility.
    We use a 32-character hex hash (32 bytes when encoded as UTF-8).
    """
    # Create a deterministic seed from race parameters
    seed_string = f"{token_mint}_{entry_fee}_{player1}"
    race_id_hash = hashlib.sha256(seed_string.encode()).hexdigest()[:32]
    # Use just the hash - no prefix to keep it ≤ 32 bytes for PDA seeds
    return race_id_hash


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
                existing_race_by_id.started_at = datetime.now(timezone.utc)
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
        existing_race.started_at = datetime.now(timezone.utc)
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
    
    logger.info(f"[submit_result] Result submitted for race {race_id}, player {player_number}, wallet {request.wallet_address}, time: {request.finish_time_ms}ms")
    
    # Trigger replay verification (async, non-blocking)
    # For now, we'll verify synchronously, but this could be moved to a background task
    await verify_replay(db, race, result)
    
    # Refresh race to get latest state
    db.refresh(race)
    
    # Check if both results are submitted
    results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
    logger.info(f"[submit_result] Race {race_id} has {len(results)} result(s). Current status: {race.status}")
    
    if len(results) == 2:
        # Both players submitted, determine winner
        winner_result = min(results, key=lambda r: r.finish_time_ms)
        race.status = RaceStatus.SETTLED
        race.settled_at = datetime.now(timezone.utc)
        db.commit()
        db.refresh(race)
        
        logger.info(f"[submit_result] Race {race_id} SETTLED. Winner: {winner_result.wallet_address} (time: {winner_result.finish_time_ms}ms)")
        
        # Settle the race on-chain (send settle_race transaction)
        try:
            settle_success = await settle_race_onchain(db, race)
            if settle_success:
                logger.info(f"[submit_result] ✅ Race {race_id} settled on-chain successfully!")
            else:
                logger.warning(f"[submit_result] ⚠ Failed to settle race {race_id} on-chain. Will retry on payout.")
        except Exception as e:
            logger.error(f"[submit_result] Error settling race {race_id} on-chain: {e}", exc_info=True)
            # Don't fail - the race is settled in DB, on-chain can be retried
        
        # Auto-create payout record when race is settled
        try:
            from app.services.payout_handler import get_payout_handler
            payout_handler = get_payout_handler()
            payout_handler.create_payout_record(
                db=db,
                race=race,
                winner_wallet=winner_result.wallet_address,
                winner_result=winner_result
            )
            logger.info(f"[submit_result] Auto-created payout for race {race_id}, winner: {winner_result.wallet_address}")
        except Exception as e:
            logger.error(f"[submit_result] Error auto-creating payout for race {race_id}: {e}", exc_info=True)
            # Don't fail the request if payout creation fails - it can be created later
    
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
            result.verified_at = datetime.now(timezone.utc)
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
        if existing_race.status == RaceStatus.WAITING:
            # Return existing waiting race
            race_dict = {
                "id": str(existing_race.id),
                "race_id": existing_race.race_id,
                "token_mint": existing_race.token_mint,
                "token_symbol": existing_race.token_symbol,
                "entry_fee_sol": existing_race.entry_fee_sol,
                "player1_wallet": existing_race.player1_wallet,
                "player2_wallet": existing_race.player2_wallet,
                "status": existing_race.status.value,
                "track_seed": existing_race.track_seed,
                "created_at": existing_race.created_at,
                "solana_tx_signature": existing_race.solana_tx_signature,
                "is_private": existing_race.is_private,
                "join_code": existing_race.join_code,
                "expires_at": existing_race.expires_at,
                "player1_ready": existing_race.player1_ready,
                "player2_ready": existing_race.player2_ready
            }
            return RaceResponse(**race_dict)
        else:
            # Race exists but is not WAITING - return it anyway (might be ACTIVE, SETTLED, etc.)
            race_dict = {
                "id": str(existing_race.id),
                "race_id": existing_race.race_id,
                "token_mint": existing_race.token_mint,
                "token_symbol": existing_race.token_symbol,
                "entry_fee_sol": existing_race.entry_fee_sol,
                "player1_wallet": existing_race.player1_wallet,
                "player2_wallet": existing_race.player2_wallet,
                "status": existing_race.status.value,
                "track_seed": existing_race.track_seed,
                "created_at": existing_race.created_at,
                "solana_tx_signature": existing_race.solana_tx_signature,
                "is_private": existing_race.is_private,
                "join_code": existing_race.join_code,
                "expires_at": existing_race.expires_at,
                "player1_ready": existing_race.player1_ready,
                "player2_ready": existing_race.player2_ready
            }
            return RaceResponse(**race_dict)
    
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
    
    race_dict = {
        "id": str(new_race.id),
        "race_id": new_race.race_id,
        "token_mint": new_race.token_mint,
        "token_symbol": new_race.token_symbol,
        "entry_fee_sol": new_race.entry_fee_sol,
        "player1_wallet": new_race.player1_wallet,
        "player2_wallet": new_race.player2_wallet,
        "status": new_race.status.value,
        "track_seed": new_race.track_seed,
        "created_at": new_race.created_at,
        "solana_tx_signature": new_race.solana_tx_signature,
        "is_private": new_race.is_private,
        "join_code": new_race.join_code,
        "expires_at": new_race.expires_at,
        "player1_ready": new_race.player1_ready,
        "player2_ready": new_race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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


    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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
        "solana_tx_signature": race.solana_tx_signature,
        "is_private": race.is_private,
        "join_code": race.join_code,
        "expires_at": race.expires_at,
        "player1_ready": race.player1_ready,
        "player2_ready": race.player2_ready
    }
    
    return RaceResponse(**race_dict)


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

