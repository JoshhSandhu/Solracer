"""
Payout API endpoints.

Handles payout status queries and payout processing.
"""

from fastapi import APIRouter, Depends, HTTPException, BackgroundTasks
from sqlalchemy.orm import Session
from typing import Optional
import logging

from app.database import get_db
from app.models import Race, Payout, RaceStatus
from app.schemas import PayoutResponse, ProcessPayoutResponse
from app.services.payout_handler import get_payout_handler

router = APIRouter()
logger = logging.getLogger(__name__)


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
        race_id=race.race_id,  # Use race.race_id (string) instead of race.id (UUID)
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
    
    Note: The actual swap execution and token transfer will be handled
    by the winner signing the transaction. For automatic processing,
    a backend worker would sign and submit transactions.
    """
    # Find race
    race = db.query(Race).filter(Race.race_id == race_id).first()
    if not race:
        raise HTTPException(status_code=404, detail=f"Race {race_id} not found")
    
    # Check if race is settled
    if race.status != RaceStatus.SETTLED:
        raise HTTPException(
            status_code=400,
            detail=f"Race {race_id} is not settled. Current status: {race.status}"
        )
    
    # Get or create payout
    payout = db.query(Payout).filter(Payout.race_id == race.id).first()
    
    try:
        # Process payout
        payout_handler = get_payout_handler()
        result = await payout_handler.process_payout(db, race, payout)
        
        return ProcessPayoutResponse(**result)
        
    except Exception as e:
        logger.error(f"Error processing payout for race {race_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error processing payout: {str(e)}")


@router.post("/payouts/{race_id}/retry")
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

