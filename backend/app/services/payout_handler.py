"""
Payout handler service for processing race payouts.

This service orchestrates the payout flow:
1. Check if token is SOL (skip swap) or other token (swap via Jupiter)
2. Execute swap if needed
3. Transfer tokens/SOL to winner
4. Handle fallback to SOL if swap fails
"""

import os
from typing import Optional, Dict, Any
from datetime import datetime
from sqlalchemy.orm import Session
from solders.pubkey import Pubkey
from solders.instruction import Instruction
import logging
import base64

from app.models import Race, RaceResult, Payout, PayoutStatus, RaceStatus
from app.services.jupiter_swap import get_jupiter_swap_service, SOL_MINT
from app.services.token_accounts import get_or_create_ata, is_sol_mint
from app.services.transaction_builder import get_transaction_builder
from app.services.transaction_submitter import get_transaction_submitter
from app.services.program_client import get_program_client
from app.services.pda_utils import derive_race_pda_simple, get_program_id
from app.services.solana_client import get_solana_client

logger = logging.getLogger(__name__)


class PayoutHandler:
    """
    Service for handling race payouts.
    
    Orchestrates swap execution, token transfers, and fallback logic.
    """
    
    def __init__(self):
        """Initialize payout handler."""
        self.jupiter_swap = get_jupiter_swap_service()
        self.transaction_builder = get_transaction_builder()
        self.transaction_submitter = get_transaction_submitter()
        self.program_client = get_program_client()
        self.solana_client = get_solana_client()
    
    def create_payout_record(
        self,
        db: Session,
        race: Race,
        winner_wallet: str,
        winner_result: RaceResult
    ) -> Payout:
        """
        Create a payout record in the database.
        
        Args:
            db: Database session
            race: Race record
            winner_wallet: Winner's wallet address
            winner_result: Winner's race result record
        
        Returns:
            Created Payout record
        """
        # Check if payout already exists (idempotent)
        existing_payout = db.query(Payout).filter(Payout.race_id == race.id).first()
        if existing_payout:
            logger.info(f"Payout already exists for race {race.race_id}")
            return existing_payout
        
        # Calculate prize amount (2x entry fee)
        prize_amount_sol = race.entry_fee_sol * 2
        
        # Create payout record
        payout = Payout(
            race_id=race.id,
            winner_wallet=winner_wallet,
            winner_result_id=winner_result.id,
            prize_amount_sol=prize_amount_sol,
            token_mint=race.token_mint,
            swap_status=PayoutStatus.PENDING
        )
        
        db.add(payout)
        db.commit()
        db.refresh(payout)
        
        logger.info(f"Created payout record for race {race.race_id}, winner: {winner_wallet}")
        
        return payout
    
    async def process_payout(
        self,
        db: Session,
        race: Race,
        payout: Optional[Payout] = None
    ) -> Dict[str, Any]:
        """
        Process a race payout (swap + transfer or direct SOL transfer).
        
        Args:
            db: Database session
            race: Race record
            payout: Existing payout record (created if None)
        
        Returns:
            Dictionary with payout status and transaction signatures
        """
        # Get or create payout record
        if payout is None:
            # Get winner from race
            if race.status != RaceStatus.SETTLED:
                raise ValueError(f"Race {race.race_id} is not settled")
            
            # Get winner result
            winner_result = db.query(RaceResult).filter(
                RaceResult.race_id == race.id
            ).order_by(
                RaceResult.finish_time_ms.asc()
            ).first()
            
            if not winner_result:
                raise ValueError(f"No winner result found for race {race.race_id}")
            
            # Determine winner wallet (simplified - in production, get from on-chain)
            winner_wallet = winner_result.wallet_address
            
            payout = self.create_payout_record(db, race, winner_wallet, winner_result)
        
        # Update status to SWAPPING
        payout.swap_status = PayoutStatus.SWAPPING
        payout.swap_started_at = datetime.now()
        db.commit()
        
        try:
            # Check if token is SOL
            if is_sol_mint(race.token_mint):
                # Skip swap, transfer SOL directly
                logger.info(f"Token is SOL, skipping swap for race {race.race_id}")
                return await self._transfer_sol_directly(db, race, payout)
            else:
                # Swap SOL → Token, then transfer
                logger.info(f"Swapping SOL to {race.token_mint} for race {race.race_id}")
                return await self._swap_and_transfer(db, race, payout)
                
        except Exception as e:
            logger.error(f"Error processing payout for race {race.race_id}: {e}")
            payout.swap_status = PayoutStatus.FAILED
            payout.error_message = str(e)
            db.commit()
            raise
    
    async def _transfer_sol_directly(
        self,
        db: Session,
        race: Race,
        payout: Payout
    ) -> Dict[str, Any]:
        """
        Transfer SOL directly to winner (no swap needed).
        
        Uses claim_prize instruction from the Solana program.
        """
        try:
            # Build claim_prize instruction
            program_id = get_program_id()
            entry_fee_lamports = round(race.entry_fee_sol * 1_000_000_000)
            
            race_pda_str, bump = derive_race_pda_simple(
                program_id,
                race.race_id,
                race.token_mint,
                entry_fee_lamports
            )
            race_pda = Pubkey.from_string(race_pda_str)
            winner_pubkey = Pubkey.from_string(payout.winner_wallet)
            
            # Build claim_prize instruction
            instruction = self.program_client.build_claim_prize_instruction(
                race_pda=race_pda,
                winner=winner_pubkey
            )
            
            # Build transaction
            recent_blockhash = self.transaction_builder.get_recent_blockhash()
            if not recent_blockhash:
                raise ValueError("Failed to get recent blockhash")
            
            transaction = self.transaction_builder.build_transaction(
                instructions=[instruction],
                payer=winner_pubkey,
                recent_blockhash=recent_blockhash
            )
            
            # Serialize transaction for signing
            transaction_bytes = self.transaction_builder.serialize_transaction(transaction)
            
            # Return transaction for signing (winner signs it)
            transaction_b64 = base64.b64encode(transaction_bytes).decode('utf-8')
            
            # Note: Don't set PAID status yet - wait for transaction confirmation
            # Status remains SWAPPING until the signed transaction is submitted
            payout.fallback_sol_amount = payout.prize_amount_sol
            db.commit()
            
            logger.info(f"SOL transfer transaction prepared for race {race.race_id}")
            
            return {
                "status": "ready_for_signing",
                "transaction": transaction_b64,
                "swap_transaction": None,  # No swap needed for SOL
                "payout_id": str(payout.id),
                "amount_sol": payout.prize_amount_sol,
                "amount_tokens": None,
                "method": "claim_prize",
                "error": None
            }
            
        except Exception as e:
            logger.error(f"Error transferring SOL directly: {e}")
            payout.swap_status = PayoutStatus.FAILED
            payout.error_message = str(e)
            db.commit()
            raise
    
    async def _swap_and_transfer(
        self,
        db: Session,
        race: Race,
        payout: Payout
    ) -> Dict[str, Any]:
        """
        Swap SOL to token and transfer to winner.
        
        This is a simplified version - in production, the backend would:
        1. Execute swap using backend wallet (swap agent)
        2. Transfer tokens to winner's ATA
        """
        try:
            # Get swap quote and transaction
            prize_amount_lamports = int(payout.prize_amount_sol * 1_000_000_000)
            
            swap_result = await self.jupiter_swap.execute_swap(
                input_mint=SOL_MINT,
                output_mint=race.token_mint,
                amount=prize_amount_lamports,
                user_public_key=payout.winner_wallet,
                slippage_bps=50  # 0.5% slippage
            )
            
            if not swap_result:
                # Swap failed, fallback to SOL
                logger.warning(f"Swap failed for race {race.race_id}, falling back to SOL")
                return await self._fallback_to_sol(db, race, payout, "Swap quote/transaction failed")
            
            # Extract swap transaction
            swap_tx = swap_result.get("swap_transaction", {})
            swap_transaction_bytes = swap_tx.get("swapTransaction")
            
            if not swap_transaction_bytes:
                # Fallback to SOL
                logger.warning(f"No swap transaction received, falling back to SOL")
                return await self._fallback_to_sol(db, race, payout, "No swap transaction in response")
            
            # Update payout with swap info
            payout.swap_tx_signature = None  # Will be set after submission
            output_amount = swap_result.get("quote", {}).get("outAmount", "0")
            
            # Calculate token amount (considering decimals)
            # This is simplified - in production, fetch token decimals
            token_amount = float(output_amount) / (10 ** 9)  # Assume 9 decimals
            payout.token_amount = token_amount
            
            db.commit()
            
            logger.info(f"Swap transaction prepared for race {race.race_id}")
            
            return {
                "status": "ready_for_signing",
                "transaction": None,  # claim_prize transaction not used for swap
                "swap_transaction": swap_transaction_bytes,
                "payout_id": str(payout.id),
                "amount_sol": payout.prize_amount_sol,
                "amount_tokens": token_amount,
                "method": "jupiter_swap",
                "error": None
            }
            
        except Exception as e:
            logger.error(f"Error in swap and transfer: {e}")
            return await self._fallback_to_sol(db, race, payout, str(e))
    
    async def _fallback_to_sol(
        self,
        db: Session,
        race: Race,
        payout: Payout,
        error_message: str
    ) -> Dict[str, Any]:
        """
        Fallback to SOL transfer if swap fails.
        
        Uses claim_prize instruction.
        """
        logger.info(f"Falling back to SOL transfer for race {race.race_id}: {error_message}")
        
        try:
            # Use claim_prize instruction (same as _transfer_sol_directly)
            result = await self._transfer_sol_directly(db, race, payout)
            
            # Update payout status to fallback
            payout.swap_status = PayoutStatus.FALLBACK_SOL
            payout.error_message = error_message
            payout.fallback_sol_amount = payout.prize_amount_sol
            db.commit()
            
            # Update result fields for fallback
            result["method"] = "fallback_sol"
            result["error"] = error_message
            
            return result
            
        except Exception as e:
            logger.error(f"Error in fallback SOL transfer: {e}")
            payout.swap_status = PayoutStatus.FAILED
            payout.error_message = f"Fallback failed: {str(e)}"
            db.commit()
            raise


# Global payout handler instance
_payout_handler: Optional[PayoutHandler] = None


def get_payout_handler() -> PayoutHandler:
    """
    Get or create the global payout handler instance.
    
    Returns:
        PayoutHandler instance
    """
    global _payout_handler
    
    if _payout_handler is None:
        _payout_handler = PayoutHandler()
    
    return _payout_handler

