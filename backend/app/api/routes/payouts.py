"""
Payout API endpoints.

Handles payout status queries and payout processing.
"""

from fastapi import APIRouter, Depends, HTTPException, BackgroundTasks
from sqlalchemy.orm import Session
from typing import Optional
import logging

from app.database import get_db
from app.models import Race, Payout, RaceStatus, RaceResult
from app.schemas import PayoutResponse, ProcessPayoutResponse
from app.services.payout_handler import get_payout_handler

router = APIRouter()
logger = logging.getLogger(__name__)


def check_race_needs_onchain_settlement(db: Session, race: Race) -> bool:
    """
    Check if a race needs on-chain settlement.
    
    This checks if:
    1. The race is marked as SETTLED in the database
    2. Both results have been submitted
    
    Returns:
        bool: True if race needs on-chain settlement transaction
    """
    if race.status != RaceStatus.SETTLED:
        return False
    
    # Check if both results are in
    results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
    return len(results) == 2


@router.get("/payouts/{race_id}", response_model=PayoutResponse)
async def get_payout_status(
    race_id: str,
    db: Session = Depends(get_db)
):
    """
    Get payout status for a race.
    
    Returns payout information including swap status, transaction signatures, etc.
    """
    # Find race by race_id
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail=f"Race {race_id} not found")
    
    # Find payout
    payout = db.query(Payout).filter(Payout.race_id == race.id).first()
    if not payout:
        raise HTTPException(status_code=404, detail=f"Payout not found for race {race_id}")
    
    # Convert model to response format (UUIDs to strings)
    return PayoutResponse(
        payout_id=str(payout.id),
        race_id=race.race_id,
        winner_wallet=payout.winner_wallet,
        prize_amount_sol=payout.prize_amount_sol,
        token_mint=payout.token_mint,
        token_amount=payout.token_amount,
        swap_status=payout.swap_status,
        swap_tx_signature=payout.swap_tx_signature,
        transfer_tx_signature=payout.transfer_tx_signature,
        fallback_sol_amount=payout.fallback_sol_amount,
        fallback_tx_signature=payout.fallback_tx_signature,
        error_message=payout.error_message,
        created_at=payout.created_at,
        swap_started_at=payout.swap_started_at,
        completed_at=payout.completed_at
    )


@router.get("/payouts/{race_id}/settle-transaction")
async def get_settle_transaction(
    race_id: str,
    wallet_address: str = None,
    db: Session = Depends(get_db)
):
    """
    Get the settle_race transaction for client to sign and submit.
    
    This endpoint returns the transaction bytes that settle the race on-chain.
    The client should:
    1. Call this endpoint to get the settle_race transaction (pass wallet_address as query param)
    2. Sign and submit the transaction
    3. Then call /payouts/{race_id}/process to claim the prize
    
    Args:
        wallet_address: The wallet address that will sign and pay for the transaction.
                       If not provided, defaults to winner's wallet.
    """
    # Find race
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail=f"Race {race_id} not found")
    
    # Check if race is settled in database
    if race.status != RaceStatus.SETTLED:
        raise HTTPException(
            status_code=400,
            detail=f"Race {race_id} is not settled. Current status: {race.status}"
        )
    
    # Check if we need on-chain settlement
    if not check_race_needs_onchain_settlement(db, race):
        raise HTTPException(
            status_code=400,
            detail=f"Race {race_id} does not need on-chain settlement"
        )
    
    # If no wallet_address provided, use the winner's wallet
    if not wallet_address:
        if race.winner_wallet:
            wallet_address = race.winner_wallet
        else:
            # Fallback to player1 if no winner set yet
            wallet_address = race.player1_wallet
    
    try:
        # Import here to avoid circular imports
        from app.api.routes.races import build_settle_race_transaction
        
        result = build_settle_race_transaction(race, payer_wallet=wallet_address)
        return {
            "message": "Settle race transaction ready for signing",
            "transaction_bytes": result["transaction_bytes"],
            "race_id": result["race_id"],
            "race_pda": result["race_pda"],
            "recent_blockhash": result["recent_blockhash"]
        }
    except Exception as e:
        logger.error(f"Error building settle transaction for race {race_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error building settle transaction: {str(e)}")


@router.post("/payouts/{race_id}/process", response_model=ProcessPayoutResponse)
async def process_payout(
    race_id: str,
    background_tasks: BackgroundTasks,
    db: Session = Depends(get_db)
):
    """
    Process payout for a race (swap + transfer or direct SOL transfer).
    
    This endpoint:
    - Creates payout record if it doesn't exist
    - Executes swap if token is not SOL
    - Prepares transaction for signing
    - Returns transaction bytes for winner to sign
    
    For SOL tokens: Returns claim_prize transaction
    For other tokens: Returns Jupiter swap transaction
    
    IMPORTANT: The race must be settled on-chain before claiming prize.
    If you get "InstructionFallbackNotFound" error, call /payouts/{race_id}/settle-transaction first.
    """
    # Find race
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail=f"Race {race_id} not found")
    
    # Check if race is settled in database
    if race.status != RaceStatus.SETTLED:
        raise HTTPException(
            status_code=400,
            detail=f"Race {race_id} is not settled. Current status: {race.status}"
        )
    
    # Check if race needs on-chain settlement
    if check_race_needs_onchain_settlement(db, race):
        logger.warning(f"Race {race_id} is settled in DB but may not be settled on-chain. "
                      f"Client should call /payouts/{race_id}/settle-transaction first.")
        # Don't fail - let the client try, but log a warning
    
    # Get or create payout
    payout = db.query(Payout).filter(Payout.race_id == race.id).first()
    
    try:
        # Process payout
        payout_handler = get_payout_handler()
        result = await payout_handler.process_payout(db, race, payout)
        
        return ProcessPayoutResponse(**result)
        
    except Exception as e:
        error_msg = str(e)
        # Check if it's the InstructionFallbackNotFound error
        if "InstructionFallbackNotFound" in error_msg or "0x65" in error_msg:
            logger.error(f"Race {race_id} may not be settled on-chain. "
                        f"Error: {error_msg}. "
                        f"Client should call /payouts/{race_id}/settle-transaction first.")
            raise HTTPException(
                status_code=400,
                detail=f"Race {race_id} needs to be settled on-chain before claiming prize. "
                       f"Please call GET /payouts/{race_id}/settle-transaction first, "
                       f"sign and submit that transaction, then retry claiming the prize."
            )
        logger.error(f"Error processing payout for race {race_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error processing payout: {str(e)}")


@router.post("/payouts/{race_id}/retry", response_model=ProcessPayoutResponse)
async def retry_payout(
    race_id: str,
    db: Session = Depends(get_db)
):
    """
    Retry a failed payout.
    
    Useful for retrying payouts that failed due to network issues or other errors.
    """
    # Find race
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail=f"Race {race_id} not found")
    
    # Find payout
    payout = db.query(Payout).filter(Payout.race_id == race.id).first()
    if not payout:
        raise HTTPException(status_code=404, detail=f"Payout not found for race {race_id}")
    
    # Check if payout can be retried
    if payout.swap_status.value not in ["failed", "pending"]:
        raise HTTPException(
            status_code=400,
            detail=f"Payout cannot be retried. Current status: {payout.swap_status.value}"
        )
    
    try:
        # Reset payout status and retry
        payout.swap_status = "pending"
        payout.error_message = None
        db.commit()
        
        # Process payout
        payout_handler = get_payout_handler()
        result = await payout_handler.process_payout(db, race, payout)
        
        return ProcessPayoutResponse(**result)
        
    except Exception as e:
        logger.error(f"Error retrying payout for race {race_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error retrying payout: {str(e)}")
