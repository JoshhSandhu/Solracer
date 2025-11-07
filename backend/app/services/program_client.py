"""
Program client for building Solana program instructions from IDL.

This service loads the program IDL and provides methods to build
instructions for the Solracer program (create_race, join_race, etc.).
"""

import os
import json
from pathlib import Path
from typing import Dict, Any, Optional, List
from solders.pubkey import Pubkey
from solders.instruction import Instruction, AccountMeta
from anchorpy import Program, Idl
from anchorpy.provider import Provider
from anchorpy.coder.instruction import InstructionCoder
import logging

logger = logging.getLogger(__name__)


class ProgramClient:
    """
    Client for building Solana program instructions from IDL.
    
    Loads the program IDL and provides methods to build instructions
    for all program operations (create_race, join_race, submit_result, etc.).
    """
    
    def __init__(self, idl_path: Optional[str] = None, program_id: Optional[str] = None):
        """
        Initialize program client with IDL and program ID.
        
        Args:
            idl_path: Path to IDL JSON file (defaults to app/idl/solracer_program.json)
            program_id: Program ID as string (defaults to SOLANA_PROGRAM_ID env var)
        """
        #get paths from environment or use defaults
        if idl_path is None:
            backend_dir = Path(__file__).parent.parent.parent
            idl_path = backend_dir / "app" / "idl" / "solracer_program.json"
        
        if program_id is None:
            program_id = os.getenv("SOLANA_PROGRAM_ID")
            if not program_id:
                raise ValueError("SOLANA_PROGRAM_ID environment variable is not set")
        
        self.program_id = Pubkey.from_string(program_id)
        self.idl_path = Path(idl_path)
        
        #load IDL (for reference, but we build instructions manually)
        if not self.idl_path.exists():
            raise FileNotFoundError(f"IDL file not found: {self.idl_path}")
        
        with open(self.idl_path, "r") as f:
            self.idl_dict = json.load(f)
        
        #try to parse IDL, but don't fail if it doesn't work
        #we build instructions manually anyway
        try:
            with open(self.idl_path, "r") as f:
                idl_json_str = f.read()
            self.idl = Idl.from_json(idl_json_str)
            self.instruction_coder = InstructionCoder(self.idl)
        except Exception as e:
            logger.warning(f"Could not parse IDL with anchorpy: {e}. Using manual instruction building.")
            self.idl = None
            self.instruction_coder = None
        
        logger.info(f"Initialized ProgramClient: {self.program_id} (IDL: {self.idl_path})")
    
    def build_create_race_instruction(
        self,
        race_pda: Pubkey,
        player1: Pubkey,
        race_id: str,
        token_mint: Pubkey,
        entry_fee_sol: int,
        system_program: Pubkey
    ) -> Instruction:
        """
        Build create_race instruction.
        
        Args:
            race_pda: Race PDA account (will be created)
            player1: Player 1 wallet (signer, payer)
            race_id: Race ID string
            token_mint: Token mint address
            entry_fee_sol: Entry fee in lamports
            system_program: System program ID
        
        Returns:
            Instruction for create_race
        """
        #build instruction data
        #instruction discriminator: [233, 107, 148, 159, 241, 155, 226, 54]
        discriminator = bytes([233, 107, 148, 159, 241, 155, 226, 54])
        
        #encode arguments: race_id (String), token_mint (Pubkey), entry_fee_sol (u64)
        #for now, we'll use a simple encoding approach
        #in production, use proper Anchor serialization
        race_id_bytes = race_id.encode('utf-8')
        race_id_len = len(race_id_bytes).to_bytes(4, byteorder='little')
        token_mint_bytes = bytes(token_mint)
        entry_fee_bytes = entry_fee_sol.to_bytes(8, byteorder='little')
        
        data = discriminator + race_id_len + race_id_bytes + token_mint_bytes + entry_fee_bytes
        
        #build accounts
        accounts = [
            AccountMeta(pubkey=race_pda, is_signer=False, is_writable=True),
            AccountMeta(pubkey=player1, is_signer=True, is_writable=True),
            AccountMeta(pubkey=system_program, is_signer=False, is_writable=False),
        ]
        
        return Instruction(
            program_id=self.program_id,
            data=data,
            accounts=accounts
        )
    
    def build_join_race_instruction(
        self,
        race_pda: Pubkey,
        player2: Pubkey,
        system_program: Pubkey
    ) -> Instruction:
        """
        Build join_race instruction.
        
        Args:
            race_pda: Race PDA account
            player2: Player 2 wallet (signer)
            system_program: System program ID
        
        Returns:
            Instruction for join_race
        """
        #instruction discriminator: [108, 6, 61, 18, 1, 218, 235, 234]
        discriminator = bytes([108, 6, 61, 18, 1, 218, 235, 234])
        
        accounts = [
            AccountMeta(pubkey=race_pda, is_signer=False, is_writable=True),
            AccountMeta(pubkey=player2, is_signer=True, is_writable=True),
            AccountMeta(pubkey=system_program, is_signer=False, is_writable=False),
        ]
        
        return Instruction(
            program_id=self.program_id,
            data=discriminator,
            accounts=accounts
        )
    
    def build_submit_result_instruction(
        self,
        race_pda: Pubkey,
        player: Pubkey,
        finish_time_ms: int,
        coins_collected: int,
        input_hash: bytes
    ) -> Instruction:
        """
        Build submit_result instruction.
        
        Args:
            race_pda: Race PDA account
            player: Player wallet (signer)
            finish_time_ms: Finish time in milliseconds
            coins_collected: Coins collected
            input_hash: SHA256 hash of input trace (32 bytes)
        
        Returns:
            Instruction for submit_result
        """
        #instruction discriminator: [175, 175, 109, 31, 13, 152, 155, 237]
        discriminator = bytes([175, 175, 109, 31, 13, 152, 155, 237])
        
        #encode arguments: finish_time_ms (u64), coins_collected (u64), input_hash ([u8; 32])
        finish_time_bytes = finish_time_ms.to_bytes(8, byteorder='little')
        coins_bytes = coins_collected.to_bytes(8, byteorder='little')
        
        if len(input_hash) != 32:
            raise ValueError("input_hash must be exactly 32 bytes")
        
        data = discriminator + finish_time_bytes + coins_bytes + input_hash
        
        accounts = [
            AccountMeta(pubkey=race_pda, is_signer=False, is_writable=True),
            AccountMeta(pubkey=player, is_signer=True, is_writable=False),
        ]
        
        return Instruction(
            program_id=self.program_id,
            data=data,
            accounts=accounts
        )
    
    def build_settle_race_instruction(
        self,
        race_pda: Pubkey
    ) -> Instruction:
        """
        Build settle_race instruction.
        
        Args:
            race_pda: Race PDA account
        
        Returns:
            Instruction for settle_race
        """
        #instruction discriminator: [110, 39, 61, 90, 218, 81, 69, 42]
        discriminator = bytes([110, 39, 61, 90, 218, 81, 69, 42])
        
        accounts = [
            AccountMeta(pubkey=race_pda, is_signer=False, is_writable=True),
        ]
        
        return Instruction(
            program_id=self.program_id,
            data=discriminator,
            accounts=accounts
        )
    
    def build_claim_prize_instruction(
        self,
        race_pda: Pubkey,
        winner: Pubkey
    ) -> Instruction:
        """
        Build claim_prize instruction.
        
        Args:
            race_pda: Race PDA account
            winner: Winner wallet (signer)
        
        Returns:
            Instruction for claim_prize
        """
        #instruction discriminator: [157, 233, 139, 121, 246, 62, 234, 235]
        discriminator = bytes([157, 233, 139, 121, 246, 62, 234, 235])
        
        accounts = [
            AccountMeta(pubkey=race_pda, is_signer=False, is_writable=True),
            AccountMeta(pubkey=winner, is_signer=True, is_writable=True),
        ]
        
        return Instruction(
            program_id=self.program_id,
            data=discriminator,
            accounts=accounts
        )


#global program client instance
_program_client: Optional[ProgramClient] = None


def get_program_client() -> ProgramClient:
    """
    get or create the global program client instance
    
    Returns:
        ProgramClient instance
    """
    global _program_client
    
    if _program_client is None:
        _program_client = ProgramClient()
    
    return _program_client

