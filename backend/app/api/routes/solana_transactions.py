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

from app.database import get_db
from app.models import Race
from app.schemas import (
    BuildTransactionRequest,
    BuildTransactionResponse,
    SubmitTransactionRequest,
    SubmitTransactionResponse,
    SettleRaceRequest,
    ClaimPrizeRequest
)
from app.services.program_client import get_program_client
from app.services.transaction_builder import get_transaction_builder
from app.services.transaction_submitter import get_transaction_submitter
from app.services.pda_utils import derive_race_pda_simple, get_program_id
import logging

logger = logging.getLogger(__name__)

router = APIRouter()


def generate_race_id(token_mint: str, entry_fee: float, player1: str) -> str:
    """Generate deterministic race ID (same as in races.py)."""
    seed_string = f"{token_mint}_{entry_fee}_{player1}"
    race_id_hash = hashlib.sha256(seed_string.encode()).hexdigest()[:32]
    return f"race_{race_id_hash}"


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
            
            #get race from database
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if not race:
                raise HTTPException(status_code=404, detail="Race not found")
            
            #derive race PDA
            program_id = get_program_id()
            entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            #convert input_hash from hex string to bytes
            input_hash_bytes = bytes.fromhex(request.input_hash)
            if len(input_hash_bytes) != 32:
                raise HTTPException(status_code=400, detail="input_hash must be 32 bytes (64 hex characters)")
            
            #build instruction
            instruction = program_client.build_submit_result_instruction(
                race_pda=race_pda,
                player=wallet_pubkey,
                finish_time_ms=request.finish_time_ms,
                coins_collected=request.coins_collected or 0,
                input_hash=input_hash_bytes
            )
            
            #build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            #serialize transaction
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
            
            #get race from database
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if not race:
                raise HTTPException(status_code=404, detail="Race not found")
            
            #derive race PDA
            program_id = get_program_id()
            entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            
            #build instruction
            instruction = program_client.build_claim_prize_instruction(
                race_pda=race_pda,
                winner=wallet_pubkey
            )
            
            #build transaction
            recent_blockhash = transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
            
            transaction = transaction_builder.build_transaction(
                instructions=[instruction],
                payer=wallet_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            #serialize transaction
            transaction_bytes = transaction_builder.serialize_transaction(transaction)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            return BuildTransactionResponse(
                transaction_bytes=transaction_b64,
                instruction_type="claim_prize",
                race_id=race.race_id,
                race_pda=race_pda_str,
                recent_blockhash=recent_blockhash
            )
        
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
        #decode transaction bytes
        transaction_bytes = base64.b64decode(request.signed_transaction_bytes)
        
        #submit transaction
        signature = transaction_submitter.submit_transaction_bytes(transaction_bytes)
        
        if not signature:
            raise HTTPException(status_code=500, detail="Failed to submit transaction")
        
        #update race record with transaction signature if race_id provided
        if request.race_id:
            race = db.query(Race).filter(Race.race_id == request.race_id).first()
            if race:
                if request.instruction_type == "create_race":
                    race.solana_tx_signature = signature
                db.commit()
        
        #confirm transaction (async, non-blocking)
        #in production, this could be done in a background task
        confirmed = transaction_submitter.confirm_transaction(signature, timeout=10)
        
        return SubmitTransactionResponse(
            transaction_signature=signature,
            instruction_type=request.instruction_type,
            race_id=request.race_id,
            confirmed=confirmed
        )
    
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
    transaction_submitter = get_transaction_submitter()
    
    try:
        #get race from database
        race = db.query(Race).filter(Race.race_id == race_id).first()
        if not race:
            raise HTTPException(status_code=404, detail="Race not found")
        
        #derive race PDA
        program_id = get_program_id()
        entry_fee_lamports = int(race.entry_fee_sol * 1_000_000_000)
        
        race_pda_str, bump = derive_race_pda_simple(
            program_id,
            race.race_id,
            race.token_mint,
            entry_fee_lamports
        )
        race_pda = Pubkey.from_string(race_pda_str)
        
        #build settle_race instruction
        instruction = program_client.build_settle_race_instruction(race_pda=race_pda)
        
        #build transaction (no signer needed - anyone can call settle_race)
        #but we need a payer for fees - use backend wallet if available
        #in production, backend wallet would sign this
        
        #get recent blockhash
        recent_blockhash = transaction_builder.get_recent_blockhash()
        if not recent_blockhash:
            raise HTTPException(status_code=500, detail="Failed to get recent blockhash")
        
        #return instruction for client to sign
        #in production, backend wallet would sign this automatically
        transaction = transaction_builder.build_transaction(
            instructions=[instruction],
            payer=race_pda,  #this won't work - need actual payer
            recent_blockhash=recent_blockhash
        )
        
        #serialize and return for signing
        transaction_bytes = transaction_builder.serialize_transaction(transaction)
        transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
        
        return {
            "message": "Settle race transaction built. Sign and submit via /transactions/submit",
            "transaction_bytes": transaction_b64,
            "race_id": race_id,
            "race_pda": race_pda_str
        }
    
    except Exception as e:
        logger.error(f"Error settling race: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Error settling race: {str(e)}")

