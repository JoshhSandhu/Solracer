"""
PDA (Program Derived Address) utilities for Solana program integration.

This module provides functions to derive PDA addresses for race accounts
using the same seeds as the Solana program.
"""

from solders.pubkey import Pubkey
from typing import Tuple
import os
import logging

logger = logging.getLogger(__name__)


def derive_race_pda(
    program_id: Pubkey,
    race_id: str,
    token_mint: Pubkey,
    entry_fee_sol: int
) -> Tuple[Pubkey, int]:
    """
    Derive the race PDA address using the same seeds as the Solana program.
    
    Seeds used (matching lib.rs):
    - b"race" (static seed)
    - race_id.as_bytes() (race ID string)
    - token_mint.as_ref() (token mint Pubkey)
    - entry_fee_sol.to_le_bytes() (entry fee in lamports, little-endian)
    
    Uses Pubkey.find_program_address() for correct PDA derivation.
    
    Args:
        program_id: The Solana program ID
        race_id: The deterministic race ID string
        token_mint: The token mint address (Pubkey)
        entry_fee_sol: Entry fee in lamports (u64)
    
    Returns:
        Tuple of (PDA Pubkey, bump seed)
    """
    # Convert inputs to bytes
    race_id_bytes = race_id.encode('utf-8')
    token_mint_bytes = bytes(token_mint)
    entry_fee_bytes = entry_fee_sol.to_bytes(8, byteorder='little')
    
    # Build seeds array (matching lib.rs exactly)
    # seeds = [b"race", race_id.as_bytes(), token_mint.as_ref(), &entry_fee_sol.to_le_bytes()]
    seeds = [
        b"race",
        race_id_bytes,
        token_mint_bytes,
        entry_fee_bytes,
    ]
    
    logger.debug(f"[derive_race_pda] Seeds: race={list(b'race')}, race_id={list(race_id_bytes)}, "
                 f"token_mint={list(token_mint_bytes)[:8]}..., entry_fee={list(entry_fee_bytes)}")
    
    # Use Pubkey.find_program_address for correct PDA derivation
    pda, bump = Pubkey.find_program_address(seeds, program_id)
    
    logger.debug(f"[derive_race_pda] Derived PDA: {pda}, bump: {bump}")
    
    return pda, bump


def derive_race_pda_simple(
    program_id_str: str,
    race_id: str,
    token_mint_str: str,
    entry_fee_sol: int
) -> Tuple[str, int]:
    """
    Simplified PDA derivation that returns string addresses.
    
    This is a wrapper around derive_race_pda that handles string inputs
    and returns string outputs for easier use in the backend.
    
    Args:
        program_id_str: The Solana program ID as a string
        race_id: The deterministic race ID string
        token_mint_str: The token mint address as a string
        entry_fee_sol: Entry fee in lamports (u64)
    
    Returns:
        Tuple of (PDA address as string, bump seed)
    
    Raises:
        ValueError: If inputs are invalid
    """
    logger.info(f"[derive_race_pda_simple] program_id={program_id_str}, race_id={race_id}, "
                f"token_mint={token_mint_str}, entry_fee={entry_fee_sol}")
    
    program_id = Pubkey.from_string(program_id_str)
    token_mint = Pubkey.from_string(token_mint_str)
    
    pda, bump = derive_race_pda(program_id, race_id, token_mint, entry_fee_sol)
    
    pda_str = str(pda)
    logger.info(f"[derive_race_pda_simple] Derived PDA: {pda_str}, bump: {bump}")
    
    return pda_str, bump


def get_program_id() -> str:
    """
    get the Solana program ID from environment variables
    
    Returns:
        program ID as a string
    """
    program_id = os.getenv("SOLANA_PROGRAM_ID")
    if not program_id:
        raise ValueError("SOLANA_PROGRAM_ID environment variable is not set")
    return program_id

