"""
Solana RPC client for interacting with the Solana blockchain.

This service provides a connection to the Solana RPC endpoint
for querying account data, submitting transactions, and monitoring state.
"""

import os
from typing import Optional, Dict, Any
from solana.rpc.api import Client
from solana.rpc.commitment import Confirmed, Finalized
from solana.rpc.types import TxOpts
from solders.pubkey import Pubkey
from solders.rpc.responses import GetAccountInfoResp, RpcResponseContext
import logging

logger = logging.getLogger(__name__)


class SolanaClient:
    """
    Client for interacting with Solana RPC endpoints.
    
    Handles connection to Solana network (devnet/mainnet) and provides
    methods for querying account data and submitting transactions.
    """
    
    def __init__(self, rpc_url: Optional[str] = None, commitment: str = "confirmed"):
        """
        Initialize Solana RPC client.
        
        Args:
            rpc_url: Solana RPC endpoint URL (defaults to SOLANA_RPC_URL env var)
            commitment: Commitment level ("processed", "confirmed", "finalized")
        """
        self.rpc_url = rpc_url or os.getenv("SOLANA_RPC_URL", "https://api.devnet.solana.com")
        self.commitment = commitment
        self.client = Client(self.rpc_url)
        
        logger.info(f"Initialized Solana client: {self.rpc_url} (commitment: {commitment})")
    
    def get_account_info(self, pubkey: Pubkey) -> Optional[Dict[str, Any]]:
        """
        Get account information for a given public key.
        
        Args:
            pubkey: The account public key
        
        Returns:
            Account info dictionary or None if account doesn't exist
        """
        try:
            response = self.client.get_account_info(pubkey, commitment=Confirmed)
            
            if response.value is None:
                return None
            
            return {
                "lamports": response.value.lamports,
                "data": response.value.data,
                "owner": str(response.value.owner),
                "executable": response.value.executable,
                "rent_epoch": response.value.rent_epoch,
            }
        except Exception as e:
            logger.error(f"Error getting account info for {pubkey}: {e}")
            return None
    
    def get_balance(self, pubkey: Pubkey) -> int:
        """
        Get SOL balance for a given public key.
        
        Args:
            pubkey: The account public key
        
        Returns:
            Balance in lamports
        """
        try:
            response = self.client.get_balance(pubkey, commitment=Confirmed)
            return response.value
        except Exception as e:
            logger.error(f"Error getting balance for {pubkey}: {e}")
            return 0
    
    def get_latest_blockhash(self) -> Optional[str]:
        """
        Get the latest blockhash for transaction building.
        
        Returns:
            Latest blockhash as string, or None on error
        """
        try:
            response = self.client.get_latest_blockhash(commitment=Confirmed)
            return str(response.value.blockhash)
        except Exception as e:
            logger.error(f"Error getting latest blockhash: {e}")
            return None
    
    def send_transaction(self, transaction_bytes: bytes, opts: Optional[TxOpts] = None) -> Optional[str]:
        """
        Send a signed transaction to the Solana network.
        
        Args:
            transaction_bytes: Serialized transaction bytes
            opts: Transaction options (skip_preflight, etc.)
        
        Returns:
            Transaction signature as string, or None on error
        """
        try:
            if opts is None:
                opts = TxOpts(skip_preflight=False, preflight_commitment=Confirmed)
            
            response = self.client.send_transaction(transaction_bytes, opts=opts)
            return str(response.value)
        except Exception as e:
            logger.error(f"Error sending transaction: {e}")
            return None
    
    def confirm_transaction(self, signature: str, commitment: str = "confirmed") -> bool:
        """
        Confirm a transaction by signature.
        
        Args:
            signature: Transaction signature
            commitment: Commitment level for confirmation
        
        Returns:
            True if confirmed, False otherwise
        """
        try:
            # Convert commitment string to Commitment enum
            commitment_level = Confirmed if commitment == "confirmed" else Finalized
            
            response = self.client.confirm_transaction(signature, commitment_level)
            return response.value[0].confirmation_status is not None
        except Exception as e:
            logger.error(f"Error confirming transaction {signature}: {e}")
            return False
    
    def get_transaction(self, signature: str) -> Optional[Dict[str, Any]]:
        """
        Get transaction details by signature.
        
        Args:
            signature: Transaction signature
        
        Returns:
            Transaction details dictionary or None
        """
        try:
            response = self.client.get_transaction(signature, commitment=Confirmed)
            
            if response.value is None:
                return None
            
            return {
                "slot": response.value.slot,
                "block_time": response.value.block_time,
                "meta": response.value.meta.__dict__ if response.value.meta else None,
            }
        except Exception as e:
            logger.error(f"Error getting transaction {signature}: {e}")
            return None


#global Solana client instance
_solana_client: Optional[SolanaClient] = None


def get_solana_client() -> SolanaClient:
    """
    Get or create the global Solana client instance.
    
    Returns:
        SolanaClient instance
    """
    global _solana_client
    
    if _solana_client is None:
        rpc_url = os.getenv("SOLANA_RPC_URL", "https://api.devnet.solana.com")
        commitment = os.getenv("SOLANA_COMMITMENT", "confirmed")
        _solana_client = SolanaClient(rpc_url=rpc_url, commitment=commitment)
    
    return _solana_client

