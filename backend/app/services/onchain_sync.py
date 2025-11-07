"""
On-chain state synchronization service.

This service syncs on-chain Solana account data with the off-chain database,
ensuring the database reflects the current state of on-chain race accounts.
"""

from typing import Optional, Dict, Any
from solders.pubkey import Pubkey
from sqlalchemy.orm import Session
import logging

from app.models import Race, RaceStatus
from app.services.solana_client import get_solana_client
from app.services.pda_utils import derive_race_pda_simple, get_program_id

logger = logging.getLogger(__name__)


class OnChainSync:
    """
    Service for syncing on-chain Solana account data with the database.
    
    Fetches race account data from Solana and updates the database
    to reflect the current on-chain state.
    """
    
    def __init__(self):
        """Initialize on-chain sync service."""
        self.solana_client = get_solana_client()
        self.program_id = get_program_id()
    
    def sync_race_account(
        self,
        db: Session,
        race_id: str,
        token_mint: str,
        entry_fee_sol: int
    ) -> Optional[Dict[str, Any]]:
        """
        Sync a race account from on-chain to database.
        
        Args:
            db: Database session
            race_id: Race ID string
            token_mint: Token mint address
            entry_fee_sol: Entry fee in lamports
        
        Returns:
            Race account data dictionary or None
        """
        try:
            # Derive race PDA
            race_pda_str, bump = derive_race_pda_simple(
                self.program_id,
                race_id,
                token_mint,
                entry_fee_sol
            )
            
            race_pda = Pubkey.from_string(race_pda_str)
            
            # Get account info from Solana
            account_info = self.solana_client.get_account_info(race_pda)
            
            if account_info is None:
                logger.warning(f"Race account not found on-chain: {race_pda_str}")
                return None
            
            #parse account data
            #for now, we'll return the raw account info
            #in production, deserialize the account data using anchor account decoder
            
            return {
                "pda": race_pda_str,
                "lamports": account_info["lamports"],
                "owner": account_info["owner"],
                "data": account_info["data"],
            }
            
        except Exception as e:
            logger.error(f"Error syncing race account {race_id}: {e}")
            return None
    
    def update_race_from_onchain(
        self,
        db: Session,
        race: Race
    ) -> bool:
        """
        Update a race record in the database from on-chain data.
        
        Args:
            db: Database session
            race: Race database record
        
        Returns:
            True if updated, False otherwise
        """
        try:
            # Sync account data
            account_data = self.sync_race_account(
                db,
                race.race_id,
                race.token_mint,
                int(race.entry_fee_sol * 1_000_000_000)  # Convert SOL to lamports
            )
            
            if account_data is None:
                return False
            
            #update race record with on-chain data
            #this is a simplified version
            #in production, deserialize the account data and update all fields
            
            #for now, we'll just mark that we've synced
            #the actual account data parsing would require anchor account decoder
            
            logger.info(f"Synced race {race.race_id} from on-chain")
            return True
            
        except Exception as e:
            logger.error(f"Error updating race {race.race_id} from on-chain: {e}")
            return False
    
    def sync_all_active_races(self, db: Session) -> int:
        """
        Sync all active races from on-chain to database.
        
        Args:
            db: Database session
        
        Returns:
            Number of races synced
        """
        try:
            #get all active races
            active_races = db.query(Race).filter(
                Race.status.in_([RaceStatus.WAITING, RaceStatus.ACTIVE, RaceStatus.SETTLED])
            ).all()
            
            synced_count = 0
            for race in active_races:
                if self.update_race_from_onchain(db, race):
                    synced_count += 1
            
            logger.info(f"Synced {synced_count}/{len(active_races)} active races from on-chain")
            return synced_count
            
        except Exception as e:
            logger.error(f"Error syncing all active races: {e}")
            return 0


#global on-chain sync instance
_onchain_sync: Optional[OnChainSync] = None


def get_onchain_sync() -> OnChainSync:
    """
    get or create the global on-chain sync instance
    
    Returns:
        OnChainSync instance
    """
    global _onchain_sync
    
    if _onchain_sync is None:
        _onchain_sync = OnChainSync()
    
    return _onchain_sync

