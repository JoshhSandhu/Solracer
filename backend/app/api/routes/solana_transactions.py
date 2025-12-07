"""
Solana transaction endpoints for Phase 6 (Legacy: Phase 4.3).

Handles transaction building and submission for on-chain race operations.
"""

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from solders.pubkey import Pubkey
from solders.system_program import ID as SYSTEM_PROGRAM_ID
import base64
import hashlib
import random
from datetime import datetime, timezone, timedelta

from app.database import get_db
from app.models import Race, RaceResult, RaceStatus, Payout, Token
from app.schemas import (
    BuildTransactionRequest,
    BuildTransactionResponse,
    SubmitTransactionRequest,
    SubmitTransactionResponse,
    SettleRaceRequest,
    ClaimPrizeRequest
)
from sqlalchemy import and_
import json
from app.services.program_client import get_program_client
from app.services.transaction_builder import get_transaction_builder
from app.services.transaction_submitter import get_transaction_submitter
from app.services.pda_utils import derive_race_pda_simple, get_program_id
from app.services.solana_client import get_solana_client
import logging

logger = logging.getLogger(__name__)

router = APIRouter()


def generate_race_id(token_mint: str, entry_fee: float, player1: str) -> str:
    """
    Generate deterministic race ID (same as in races.py).
    
    NOTE: Race ID must be ≤ 32 bytes for Solana PDA seed compatibility.
    We use a 32-character hex hash (32 bytes when encoded as UTF-8).
    """
    seed_string = f"{token_mint}_{entry_fee}_{player1}"
    race_id_hash = hashlib.sha256(seed_string.encode()).hexdigest()[:32]
    # Use just the hash - no prefix to keep it ≤ 32 bytes for PDA seeds
    return race_id_hash


async def handle_submit_result(
    db,
    race: Race,
    wallet_address: str,
    finish_time_ms: int,
    coins_collected: int,
    input_hash: str,
    tx_signature: str
):
    """
    Handle submit_result instruction by storing result in database
    and settling the race if both players have finished.
    """
    # Validate required fields
    if not wallet_address:
        logger.error(f"[handle_submit_result] Missing wallet_address for race {race.race_id}")
        return
    
    if finish_time_ms is None:
        logger.error(f"[handle_submit_result] Missing finish_time_ms for race {race.race_id}")
        return
    
    # Check if result already exists (idempotent)
    existing_result = db.query(RaceResult).filter(
        and_(
            RaceResult.race_id == race.id,
            RaceResult.wallet_address == wallet_address
        )
    ).first()
    
    if existing_result:
        logger.info(f"[handle_submit_result] Result already exists for race {race.race_id}, wallet {wallet_address}")
        return
    
    # Determine player number
    if wallet_address == race.player1_wallet:
        player_number = 1
    elif wallet_address == race.player2_wallet:
        player_number = 2
    else:
        logger.error(f"[handle_submit_result] Wallet {wallet_address} not in race {race.race_id}")
        return
    
    # Create race result
    result = RaceResult(
        race_id=race.id,
        wallet_address=wallet_address,
        player_number=player_number,
        finish_time_ms=finish_time_ms,
        coins_collected=coins_collected or 0,
        input_hash=input_hash or "",
        verified=False
    )
    
    db.add(result)
    db.commit()
    db.refresh(result)
    
    logger.info(f"[handle_submit_result] Result stored for race {race.race_id}, player {player_number}, time: {finish_time_ms}ms")
    
    # Check if both results are submitted
    results = db.query(RaceResult).filter(RaceResult.race_id == race.id).all()
    logger.info(f"[handle_submit_result] Race {race.race_id} has {len(results)} result(s)")
    
    if len(results) == 2 and race.status != RaceStatus.SETTLED:
        # Both players submitted, determine winner and settle race
        winner_result = min(results, key=lambda r: r.finish_time_ms)
        race.status = RaceStatus.SETTLED
        race.settled_at = datetime.now(timezone.utc)
        db.commit()
        db.refresh(race)
        
        logger.info(f"[handle_submit_result] Race {race.race_id} SETTLED! Winner: {winner_result.wallet_address} (time: {winner_result.finish_time_ms}ms)")
        
        # Auto-create payout record
        try:
            from app.services.payout_handler import get_payout_handler
            payout_handler = get_payout_handler()
            payout_handler.create_payout_record(
                db=db,
                race=race,
                winner_wallet=winner_result.wallet_address,
                winner_result=winner_result
            )
            logger.info(f"[handle_submit_result] ✅ Payout created for race {race.race_id}, winner: {winner_result.wallet_address}")
        except Exception as e:
            logger.error(f"[handle_submit_result] Error creating payout for race {race.race_id}: {e}", exc_info=True)


@router.post("/transactions/build", response_model=BuildTransactionResponse)
async def build_transaction(
    request: BuildTransactionRequest,
    db: Session = Depends(get_db)
):
    """
    Build a Solana transaction for signing.
    
    Supported instruction types:
    - create_race: Create a new race on-chain
    - join_race: Join an existing race
    - submit_result: Submit race result on-chain
    - claim_prize: Claim prize after race settlement
    
    Returns base64-encoded transaction bytes for Unity to sign.
    """
    program_client = get_program_client()
    transaction_builder = get_transaction_builder()
    
    # Validate instruction type first
    valid_instruction_types = ["create_race", "join_race", "submit_result", "claim_prize"]
    if request.instruction_type not in valid_instruction_types:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown instruction_type: {request.instruction_type}. Valid types: {', '.join(valid_instruction_types)}"
        )
    
    try:
        # Validate wallet address format first
        try:
            wallet_pubkey = Pubkey.from_string(request.wallet_address)
        except Exception as e:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid wallet_address format: {str(e)}"
            )
        
        if request.instruction_type == "create_race":
            if not request.token_mint or not request.entry_fee_sol:
                raise HTTPException(
                    status_code=400,
                    detail="token_mint and entry_fee_sol required for create_race"
                )
            
            # Generate race ID
            race_id = generate_race_id(request.token_mint, request.entry_fee_sol, request.wallet_address)
            
            # Derive race PDA
            program_id = get_program_id()
            token_mint_pubkey = Pubkey.from_string(request.token_mint)
            entry_fee_lamports = int(request.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race_id,
                request.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            # Build instruction
            instruction = program_client.build_create_race_instruction(
                race_pda=race_pda,
                player1=wallet_pubkey,
                race_id=race_id,
                token_mint=token_mint_pubkey,
                entry_fee_sol=entry_fee_lamports,
                system_program=SYSTEM_PROGRAM_ID
            )
            
            # Build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            # Serialize transaction
            transaction_bytes = transaction_builder.serialize_transaction(transaction)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            return BuildTransactionResponse(
                transaction_bytes=transaction_b64,
                instruction_type="create_race",
                race_id=race_id,
                race_pda=race_pda_str,
                recent_blockhash=recent_blockhash
            )
        
        elif request.instruction_type == "join_race":
            if not request.race_id:
                raise HTTPException(status_code=400, detail="race_id required for join_race")
            
            # Get race from database
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if not race:
                raise HTTPException(status_code=404, detail="Race not found")
            
            # Derive race PDA
            program_id = get_program_id()
            entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            # Build instruction
            instruction = program_client.build_join_race_instruction(
                race_pda=race_pda,
                player2=wallet_pubkey,
                system_program=SYSTEM_PROGRAM_ID
            )
            
            # Build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            # Serialize transaction
            transaction_bytes = transaction_builder.serialize_transaction(transaction)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            return BuildTransactionResponse(
                transaction_bytes=transaction_b64,
                instruction_type="join_race",
                race_id=race.race_id,
                race_pda=race_pda_str,
                recent_blockhash=recent_blockhash
            )
        
        elif request.instruction_type == "submit_result":
            if not request.race_id or not request.finish_time_ms or not request.input_hash:
                raise HTTPException(
                    status_code=400,
                    detail="race_id, finish_time_ms, and input_hash required for submit_result"
                )
            
            # Get race from database
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if not race:
                raise HTTPException(status_code=404, detail="Race not found")
            
            # Derive race PDA
            program_id = get_program_id()
            entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            # Verify race account exists on-chain before building submit_result
            solana_client = get_solana_client()
            account_info = solana_client.get_account_info(race_pda)
            if account_info is None:
                logger.error(f"[build_transaction] Race {race.race_id} not found on-chain at PDA {race_pda_str}")
                raise HTTPException(
                    status_code=400,
                    detail=f"Race account not found on-chain. The race must be created on-chain first. PDA: {race_pda_str}"
                )
            
            logger.info(f"[build_transaction] ✅ Race account {race_pda_str} verified on-chain for race {race.race_id}")
            
            # Convert input_hash from hex string to bytes
            input_hash_bytes = bytes.fromhex(request.input_hash)
            if len(input_hash_bytes) != 32:
                raise HTTPException(status_code=400, detail="input_hash must be 32 bytes (64 hex characters)")
            
            # Build instruction
            instruction = program_client.build_submit_result_instruction(
                race_pda=race_pda,
                player=wallet_pubkey,
                finish_time_ms=request.finish_time_ms,
                coins_collected=request.coins_collected or 0,
                input_hash=input_hash_bytes
            )
            
            # Build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            # Serialize transaction
            transaction_bytes = transaction_builder.serialize_transaction(transaction)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            return BuildTransactionResponse(
                transaction_bytes=transaction_b64,
                instruction_type="submit_result",
                race_id=race.race_id,
                race_pda=race_pda_str,
                recent_blockhash=recent_blockhash
            )
        
        elif request.instruction_type == "claim_prize":
            if not request.race_id:
                raise HTTPException(status_code=400, detail="race_id required for claim_prize")
            
            # Get race from database
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if not race:
                raise HTTPException(status_code=404, detail="Race not found")
            
            # Derive race PDA
            program_id = get_program_id()
            entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            # Verify race account exists on-chain before building claim_prize
            solana_client = get_solana_client()
            account_info = solana_client.get_account_info(race_pda)
            if account_info is None:
                logger.error(f"[build_transaction] Race {race.race_id} not found on-chain at PDA {race_pda_str}")
                raise HTTPException(
                    status_code=400,
                    detail=f"Race account not found on-chain. Cannot claim prize for off-chain race. PDA: {race_pda_str}"
                )
            
            logger.info(f"[build_transaction] ✅ Race account {race_pda_str} verified on-chain for claim_prize")
            
            # Build instruction
            instruction = program_client.build_claim_prize_instruction(
                race_pda=race_pda,
                winner=wallet_pubkey
            )
            
            # Build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            # Serialize transaction
            transaction_bytes = transaction_builder.serialize_transaction(transaction)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            return BuildTransactionResponse(
                transaction_bytes=transaction_b64,
                instruction_type="claim_prize",
                race_id=race.race_id,
                race_pda=race_pda_str,
                recent_blockhash=recent_blockhash
            )
        
    except HTTPException:
        # Re-raise HTTPExceptions (they already have the correct status code)
        raise
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Error building transaction: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Error building transaction: {str(e)}")


@router.post("/transactions/submit", response_model=SubmitTransactionResponse)
async def submit_transaction(
    request: SubmitTransactionRequest,
    db: Session = Depends(get_db)
):
    """
    Submit a signed Solana transaction.
    
    Accepts base64-encoded signed transaction bytes from Unity client
    and submits them to the Solana network.
    """
    transaction_submitter = get_transaction_submitter()
    
    try:
        # Validate and decode transaction bytes
        if not request.signed_transaction_bytes:
            raise HTTPException(status_code=400, detail="signed_transaction_bytes is required")
        
        try:
            transaction_bytes = base64.b64decode(request.signed_transaction_bytes)
        except Exception as e:
            logger.error(f"Error decoding base64 transaction bytes: {e}")
            raise HTTPException(status_code=400, detail=f"Invalid base64 encoding: {str(e)}")
        
        if not transaction_bytes or len(transaction_bytes) == 0:
            raise HTTPException(status_code=400, detail="Transaction bytes are empty")
        
        logger.info(f"[submit_transaction] Decoded transaction bytes: {len(transaction_bytes)} bytes, instruction_type: {request.instruction_type}, race_id: {request.race_id}")
        
        # Check if we received a signature (64 bytes) instead of a signed transaction
        if len(transaction_bytes) == 64:
            logger.error(f"[submit_transaction] Received 64 bytes - this appears to be a signature, not a signed transaction.")
            raise HTTPException(
                status_code=400, 
                detail="Received signature (64 bytes) instead of signed transaction. The client must sign the full transaction."
            )
        
        # A signed transaction should be at least 100 bytes
        if len(transaction_bytes) < 100:
            logger.warning(f"[submit_transaction] Transaction bytes are unusually small ({len(transaction_bytes)} bytes).")
        
        # Submit transaction
        signature = transaction_submitter.submit_transaction_bytes(transaction_bytes)
        
        if not signature:
            raise HTTPException(status_code=500, detail="Failed to submit transaction")
        
        # Update race record with transaction signature if race_id provided
        logger.info(f"[submit_transaction] Processing instruction_type={request.instruction_type}, race_id={request.race_id}")
        
        if request.race_id:
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            
            # For create_race, create the race in database if it doesn't exist
            if request.instruction_type == "create_race" and race is None:
                logger.info(f"[submit_transaction] Creating race in database: {request.race_id}")
                
                # Get token info
                token_symbol = "SOL"  # Default
                if request.token_mint:
                    token = db.query(Token).filter(Token.mint_address == request.token_mint).first()
                    if token:
                        token_symbol = token.symbol
                
                race = Race(
                    race_id=request.race_id,
                    token_mint=request.token_mint or "So11111111111111111111111111111111111111112",
                    token_symbol=token_symbol,
                    entry_fee_sol=request.entry_fee_sol or 0.005,
                    player1_wallet=request.wallet_address,
                    status=RaceStatus.WAITING,
                    is_private=False,
                    track_seed=random.randint(1, 1000000),
                    expires_at=datetime.now(timezone.utc) + timedelta(minutes=5),
                    solana_tx_signature=signature
                )
                db.add(race)
                db.commit()
                db.refresh(race)
                logger.info(f"[submit_transaction] Race created in database: {race.race_id}")
            elif race:
                if request.instruction_type == "create_race":
                    race.solana_tx_signature = signature
                    db.commit()
                
                # Handle submit_result: store result in database and check for race settlement
                elif request.instruction_type == "submit_result":
                    logger.info(f"[submit_transaction] Calling handle_submit_result for race {request.race_id}")
                    await handle_submit_result(
                        db=db,
                        race=race,
                        wallet_address=request.wallet_address,
                        finish_time_ms=request.finish_time_ms,
                        coins_collected=request.coins_collected,
                        input_hash=request.input_hash,
                        tx_signature=signature
                    )
            else:
                logger.warning(f"[submit_transaction] Race not found in database: {request.race_id}")
        else:
            logger.warning(f"[submit_transaction] No race_id provided for instruction_type={request.instruction_type}")
        
        # Confirm transaction
        confirmed = transaction_submitter.confirm_transaction(signature, timeout=10)
        
        return SubmitTransactionResponse(
            transaction_signature=signature,
            instruction_type=request.instruction_type,
            race_id=request.race_id,
            confirmed=confirmed
        )
    
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error submitting transaction: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Error submitting transaction: {str(e)}")


@router.post("/races/{race_id}/settle")
async def settle_race(
    race_id: str,
    db: Session = Depends(get_db)
):
    """
    Settle a race on-chain (determine winner).
    
    This can be called by anyone once both results are submitted.
    The Solana program will determine the winner based on finish times.
    """
    program_client = get_program_client()
    transaction_builder = get_transaction_builder()
    
    try:
        # Get race from database
        race = db.query(Race).filter(Race.race_id == race_id).first()
        if not race:
            raise HTTPException(status_code=404, detail="Race not found")
        
        # Derive race PDA
        program_id = get_program_id()
        entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
        
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
            raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
        
        # Build transaction (needs payer for fees)
        transaction = transaction_builder.build_transaction(
            instructions=[instruction],
            payer=race_pda,  # Note: this won't work - need actual payer
            recent_blockhash=recent_blockhash
        )
        
        # Serialize and return for signing
        transaction_bytes = transaction_builder.serialize_transaction(transaction)
        transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
        
        return {
            "message": "Settle race transaction built. Sign and submit via /transactions/submit",
            "transaction_bytes": transaction_b64,
            "race_id": race_id,
            "race_pda": race_pda_str
        }
    
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error settling race: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Error settling race: {str(e)}")
