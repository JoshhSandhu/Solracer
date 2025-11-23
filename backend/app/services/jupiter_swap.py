"""
Jupiter swap service for token swaps.

This service integrates with Jupiter API to swap SOL to SPL tokens.
"""

import os
import httpx
from typing import Optional, Dict, Any
from solders.pubkey import Pubkey
import logging
import json

logger = logging.getLogger(__name__)

# Jupiter API endpoints
JUPITER_QUOTE_API = "https://quote-api.jup.ag/v6/quote"
JUPITER_SWAP_API = "https://quote-api.jup.ag/v6/swap"

# SOL mint address (wrapped SOL)
SOL_MINT = "So11111111111111111111111111111111111111112"


class JupiterSwapService:
    """
    Service for executing token swaps via Jupiter aggregator.
    
    Handles quote fetching and swap execution.
    """
    
    def __init__(self, api_base_url: Optional[str] = None):
        """
        Initialize Jupiter swap service.
        
        Args:
            api_base_url: Jupiter API base URL (defaults to v6)
        """
        self.quote_api = api_base_url or JUPITER_QUOTE_API
        self.swap_api = api_base_url or JUPITER_SWAP_API
        self.client = httpx.AsyncClient(timeout=30.0)
    
    async def get_swap_quote(
        self,
        input_mint: str,
        output_mint: str,
        amount: int,
        slippage_bps: int = 50
    ) -> Optional[Dict[str, Any]]:
        """
        Get a swap quote from Jupiter.
        
        Args:
            input_mint: Input token mint address (SOL for our use case)
            output_mint: Output token mint address
            amount: Amount in lamports (for SOL) or smallest unit
            slippage_bps: Slippage in basis points (50 = 0.5%, 100 = 1%)
        
        Returns:
            Quote dictionary with route, output amount, etc., or None on error
        """
        try:
            params = {
                "inputMint": input_mint,
                "outputMint": output_mint,
                "amount": str(amount),
                "slippageBps": slippage_bps,
                "onlyDirectRoutes": "false",
                "asLegacyTransaction": "false"
            }
            
            logger.info(f"Fetching Jupiter quote: {input_mint} -> {output_mint}, amount: {amount}")
            
            response = await self.client.get(self.quote_api, params=params)
            response.raise_for_status()
            
            quote = response.json()
            
            logger.info(f"Jupiter quote received: {quote.get('outAmount', 'N/A')} output tokens")
            
            return quote
            
        except httpx.HTTPStatusError as e:
            logger.error(f"Jupiter API error: {e.response.status_code} - {e.response.text}")
            return None
        except Exception as e:
            logger.error(f"Error fetching Jupiter quote: {e}")
            return None
    
    async def get_swap_transaction(
        self,
        quote: Dict[str, Any],
        user_public_key: str,
        wrap_unwrap_sol: bool = True
    ) -> Optional[Dict[str, Any]]:
        """
        Get swap transaction from Jupiter.
        
        Args:
            quote: Quote response from get_swap_quote
            user_public_key: User's wallet public key (base58)
            wrap_unwrap_sol: Whether to wrap/unwrap SOL automatically
        
        Returns:
            Swap transaction dictionary with transaction bytes, or None on error
        """
        try:
            payload = {
                "quoteResponse": quote,
                "userPublicKey": user_public_key,
                "wrapUnwrapSOL": wrap_unwrap_sol,
                "dynamicComputeUnitLimit": True,
                "prioritizationFeeLamports": "auto"
            }
            
            logger.info(f"Requesting swap transaction for user: {user_public_key}")
            
            response = await self.client.post(
                self.swap_api,
                json=payload,
                headers={"Content-Type": "application/json"}
            )
            response.raise_for_status()
            
            swap_response = response.json()
            
            logger.info("Swap transaction received from Jupiter")
            
            return swap_response
            
        except httpx.HTTPStatusError as e:
            logger.error(f"Jupiter swap API error: {e.response.status_code} - {e.response.text}")
            return None
        except Exception as e:
            logger.error(f"Error getting swap transaction: {e}")
            return None
    
    async def execute_swap(
        self,
        input_mint: str,
        output_mint: str,
        amount: int,
        user_public_key: str,
        slippage_bps: int = 50
    ) -> Optional[Dict[str, Any]]:
        """
        Get a complete swap transaction (quote + swap).
        
        Args:
            input_mint: Input token mint (SOL)
            output_mint: Output token mint
            amount: Amount in lamports
            user_public_key: User's wallet public key
            slippage_bps: Slippage in basis points
        
        Returns:
            Swap transaction dictionary, or None on error
        """
        # Get quote
        quote = await self.get_swap_quote(input_mint, output_mint, amount, slippage_bps)
        
        if not quote:
            logger.error("Failed to get swap quote")
            return None
        
        # Get swap transaction
        swap_tx = await self.get_swap_transaction(quote, user_public_key)
        
        if not swap_tx:
            logger.error("Failed to get swap transaction")
            return None
        
        return {
            "quote": quote,
            "swap_transaction": swap_tx,
            "input_mint": input_mint,
            "output_mint": output_mint,
            "input_amount": amount,
            "output_amount": quote.get("outAmount", "0")
        }
    
    async def close(self):
        """Close the HTTP client."""
        await self.client.aclose()


# Global Jupiter swap service instance
_jupiter_swap_service: Optional[JupiterSwapService] = None


def get_jupiter_swap_service() -> JupiterSwapService:
    """
    Get or create the global Jupiter swap service instance.
    
    Returns:
        JupiterSwapService instance
    """
    global _jupiter_swap_service
    
    if _jupiter_swap_service is None:
        api_url = os.getenv("JUPITER_API_URL")
        _jupiter_swap_service = JupiterSwapService(api_base_url=api_url)
    
    return _jupiter_swap_service

