"""Add optimized indexes for common query patterns

Revision ID: d4b00ed3cf8f
Revises: add_optimized_indexes
Create Date: 2025-11-06 21:29:44.387253

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = 'd4b00ed3cf8f'
down_revision: Union[str, None] = '99e8ea701ddd'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    """
    Add optimized indexes for common query patterns.
    
    Indexes added:
    1. Composite index on races (token_mint, entry_fee_sol, status) - for matchmaking
    2. Composite index on race_results (race_id, wallet_address) - for duplicate check
    3. Index on race_results (submitted_at) - for time-based queries
    4. Index on races (created_at) - for cleanup queries
    5. Indexes on player wallets for finding races by player
    """
    
    # Composite index for race matchmaking (most common query)
    op.create_index(
        'ix_races_token_entry_status',
        'races',
        ['token_mint', 'entry_fee_sol', 'status'],
        unique=False
    )
    
    # Composite index for checking duplicate result submissions
    op.create_index(
        'ix_race_results_race_wallet',
        'race_results',
        ['race_id', 'wallet_address'],
        unique=False
    )
    
    # Index for time-based queries (cleanup, analytics)
    op.create_index(
        'ix_race_results_submitted_at',
        'race_results',
        ['submitted_at'],
        unique=False
    )
    
    # Index for race cleanup queries
    op.create_index(
        'ix_races_created_at',
        'races',
        ['created_at'],
        unique=False
    )
    
    # Indexes for finding races by player wallet
    op.create_index(
        'ix_races_player1_wallet',
        'races',
        ['player1_wallet'],
        unique=False
    )
    
    op.create_index(
        'ix_races_player2_wallet',
        'races',
        ['player2_wallet'],
        unique=False
    )


def downgrade() -> None:
    """Remove optimized indexes."""
    op.drop_index('ix_races_player2_wallet', table_name='races')
    op.drop_index('ix_races_player1_wallet', table_name='races')
    op.drop_index('ix_races_created_at', table_name='races')
    op.drop_index('ix_race_results_submitted_at', table_name='race_results')
    op.drop_index('ix_race_results_race_wallet', table_name='race_results')
    op.drop_index('ix_races_token_entry_status', table_name='races')
