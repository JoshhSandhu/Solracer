"""
Transaction builder for creating Solana transactions.

This service builds Solana transactions from instructions, handles
blockhash fetching, and prepares transactions for signing.
"""

from typing import List, Optional
from solders.transaction import Transaction
from solders.instruction import Instruction
from solders.message import Message
from solders.hash import Hash
from solders.pubkey import Pubkey
from solders.rpc.responses import GetLatestBlockhashResp
from solana.rpc.api import Client
import os
import logging

from app.services.solana_client import get_solana_client

logger = logging.getLogger(__name__)


class TransactionBuilder:
    """
    Service for building Solana transactions.
    
    Handles transaction construction, blockhash fetching, and
    transaction serialization for signing.
    """
    
    def __init__(self, rpc_url: Optional[str] = None):
        """
        Initialize transaction builder.
        
        Args:
            rpc_url: Solana RPC endpoint URL (optional)
        """
        self.solana_client = get_solana_client()
        self.rpc_url = rpc_url or os.getenv("SOLANA_RPC_URL", "https://api.devnet.solana.com")
        self.client = Client(self.rpc_url)
    
    def build_transaction(
        self,
        instructions: List[Instruction],
        payer: Pubkey,
        recent_blockhash: Optional[str] = None
    ) -> Transaction:
        """
        Build a Solana transaction from instructions.
        
        Args:
            instructions: List of instructions to include
            payer: Payer account (fee payer)
            recent_blockhash: Recent blockhash (fetched if not provided)
        
        Returns:
            Transaction object ready for signing
        """
        # Get recent blockhash if not provided
        if recent_blockhash is None:
            blockhash_resp = self.client.get_latest_blockhash()
            if blockhash_resp.value is None:
                raise ValueError("Failed to get recent blockhash")
            recent_blockhash = str(blockhash_resp.value.blockhash)
        
        #create message
        message = Message.new_with_blockhash(
            instructions,
            payer,
            Hash.from_string(recent_blockhash)
        )
        
        #create transaction
        transaction = Transaction.new_unsigned(message)
        
        return transaction
    
    def serialize_transaction(self, transaction: Transaction) -> bytes:
        """
        Serialize a transaction to bytes for signing.
        
        Args:
            transaction: Transaction object
        
        Returns:
            Serialized transaction bytes
        """
        return bytes(transaction)
    
    def deserialize_transaction(self, transaction_bytes: bytes) -> Transaction:
        """
        Deserialize transaction bytes to Transaction object.
        
        Args:
            transaction_bytes: Serialized transaction bytes
        
        Returns:
            Transaction object
        """
        return Transaction.from_bytes(transaction_bytes)
    
    def get_recent_blockhash(self) -> Optional[str]:
        """
        Get the latest blockhash for transaction building.
        
        Returns:
            Recent blockhash as string, or None on error
        """
        try:
            response = self.client.get_latest_blockhash()
            if response.value is None:
                return None
            return str(response.value.blockhash)
        except Exception as e:
            logger.error(f"Error getting recent blockhash: {e}")
            return None


#global transaction builder instance
_transaction_builder: Optional[TransactionBuilder] = None


def get_transaction_builder() -> TransactionBuilder:
    """
    Get or create the global transaction builder instance.
    
    Returns:
        TransactionBuilder instance
    """
    global _transaction_builder
    
    if _transaction_builder is None:
        _transaction_builder = TransactionBuilder()
    
    return _transaction_builder

