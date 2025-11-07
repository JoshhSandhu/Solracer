"""
Transaction submitter for sending signed transactions to Solana.

This service handles transaction submission, confirmation, and error handling.
"""

from typing import Optional, Dict, Any
from solders.transaction import Transaction
from solders.rpc.responses import SendTransactionResp
from solana.rpc.api import Client
from solana.rpc.commitment import Confirmed, Finalized
from solana.rpc.types import TxOpts
import os
import logging
import time

from app.services.solana_client import get_solana_client

logger = logging.getLogger(__name__)


class TransactionSubmitter:
    """
    Service for submitting signed transactions to Solana.
    
    Handles transaction submission, confirmation, and error handling.
    """
    
    def __init__(self, rpc_url: Optional[str] = None):
        """
        Initialize transaction submitter.
        
        Args:
            rpc_url: Solana RPC endpoint URL (optional)
        """
        self.solana_client = get_solana_client()
        self.rpc_url = rpc_url or os.getenv("SOLANA_RPC_URL", "https://api.devnet.solana.com")
        self.client = Client(self.rpc_url)
    
    def submit_transaction(
        self,
        transaction: Transaction,
        skip_preflight: bool = False,
        max_retries: int = 3
    ) -> Optional[str]:
        """
        Submit a signed transaction to the Solana network.
        
        Args:
            transaction: Signed transaction object
            skip_preflight: Skip preflight checks (faster but less safe)
            max_retries: Maximum number of retry attempts
        
        Returns:
            Transaction signature as string, or None on error
        """
        transaction_bytes = bytes(transaction)
        
        for attempt in range(max_retries):
            try:
                opts = TxOpts(
                    skip_preflight=skip_preflight,
                    preflight_commitment=Confirmed,
                    max_retries=0  #we handle retries ourselves
                )
                
                response = self.client.send_transaction(transaction_bytes, opts=opts)
                
                if response.value:
                    signature = str(response.value)
                    logger.info(f"Transaction submitted: {signature}")
                    return signature
                else:
                    logger.warning(f"Transaction submission failed (attempt {attempt + 1}/{max_retries})")
                    if attempt < max_retries - 1:
                        time.sleep(1)  #wait before retry
                    
            except Exception as e:
                logger.error(f"Error submitting transaction (attempt {attempt + 1}/{max_retries}): {e}")
                if attempt < max_retries - 1:
                    time.sleep(1)  #wait before retry
        
        return None
    
    def submit_transaction_bytes(
        self,
        transaction_bytes: bytes,
        skip_preflight: bool = False,
        max_retries: int = 3
    ) -> Optional[str]:
        """
        Submit a signed transaction from bytes.
        
        Args:
            transaction_bytes: Serialized signed transaction bytes
            skip_preflight: Skip preflight checks
            max_retries: Maximum number of retry attempts
        
        Returns:
            Transaction signature as string, or None on error
        """
        for attempt in range(max_retries):
            try:
                opts = TxOpts(
                    skip_preflight=skip_preflight,
                    preflight_commitment=Confirmed,
                    max_retries=0
                )
                
                response = self.client.send_transaction(transaction_bytes, opts=opts)
                
                if response.value:
                    signature = str(response.value)
                    logger.info(f"Transaction submitted: {signature}")
                    return signature
                else:
                    logger.warning(f"Transaction submission failed (attempt {attempt + 1}/{max_retries})")
                    if attempt < max_retries - 1:
                        time.sleep(1)
                    
            except Exception as e:
                logger.error(f"Error submitting transaction (attempt {attempt + 1}/{max_retries}): {e}")
                if attempt < max_retries - 1:
                    time.sleep(1)
        
        return None
    
    def confirm_transaction(
        self,
        signature: str,
        commitment: str = "confirmed",
        timeout: int = 30
    ) -> bool:
        """
        Confirm a transaction by signature.
        
        Args:
            signature: Transaction signature
            commitment: Commitment level ("confirmed" or "finalized")
            timeout: Timeout in seconds
        
        Returns:
            True if confirmed, False otherwise
        """
        try:
            commitment_level = Confirmed if commitment == "confirmed" else Finalized
            
            #poll for confirmation
            start_time = time.time()
            while time.time() - start_time < timeout:
                response = self.client.confirm_transaction(signature, commitment_level)
                
                if response.value and len(response.value) > 0:
                    confirmation_status = response.value[0].confirmation_status
                    if confirmation_status is not None:
                        logger.info(f"Transaction confirmed: {signature} (status: {confirmation_status})")
                        return True
                
                time.sleep(1)  #wait 1 second before next check
            
            logger.warning(f"Transaction confirmation timeout: {signature}")
            return False
            
        except Exception as e:
            logger.error(f"Error confirming transaction {signature}: {e}")
            return False
    
    def get_transaction_status(self, signature: str) -> Optional[Dict[str, Any]]:
        """
        Get transaction status by signature.
        
        Args:
            signature: Transaction signature
        
        Returns:
            Transaction status dictionary or None
        """
        try:
            from solders.signature import Signature
            sig = Signature.from_string(signature)
            response = self.client.get_transaction(sig, commitment=Confirmed)
            
            if response.value is None:
                return None
            
            return {
                "slot": response.value.slot,
                "block_time": response.value.block_time,
                "err": response.value.transaction.meta.err if response.value.transaction.meta else None,
                "confirmation_status": "confirmed" if response.value else None,
            }
        except Exception as e:
            logger.error(f"Error getting transaction status {signature}: {e}")
            return None


#global transaction submitter instance
_transaction_submitter: Optional[TransactionSubmitter] = None


def get_transaction_submitter() -> TransactionSubmitter:
    """
    get or create the global transaction submitter instance
    
    Returns:
        TransactionSubmitter instance
    """
    global _transaction_submitter
    
    if _transaction_submitter is None:
        _transaction_submitter = TransactionSubmitter()
    
    return _transaction_submitter

