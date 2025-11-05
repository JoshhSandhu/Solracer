"""
SQLAlchemy database models.

This module defines the database schema using SQLAlchemy ORM.
Each model represents a table in PostgreSQL/Supabase.
"""

from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, Text, Enum as SQLEnum
from sqlalchemy.sql import func
from sqlalchemy.dialects.postgresql import UUID
import uuid
import enum
from app.database import Base


class RaceStatus(str, enum.Enum):
    """Race status enumeration."""
    WAITING = "waiting"  # Waiting for second player
    ACTIVE = "active"  # Both players joined, race in progress
    SETTLED = "settled"  # Race completed and settled on-chain
    CANCELLED = "cancelled"  # Race cancelled (timeout, etc.)


class PayoutStatus(str, enum.Enum):
    """Payout status enumeration."""
    PENDING = "pending"  # Waiting for swap
    SWAPPING = "swapping"  # Jupiter swap in progress
    PAID = "paid"  # Token successfully sent to winner
    FALLBACK_SOL = "fallback_sol"  # Fallback to SOL payment
    FAILED = "failed"  # Payout failed


class Race(Base):
    """
    Race table: Stores competitive race information.
    
    Each race represents a match between two players on a specific token track.
    Race state is synchronized with Solana program via race_id (PDA address).
    """
    __tablename__ = "races"

    # Primary key
    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, index=True)
    
    # Solana program reference
    race_id = Column(String, unique=True, nullable=False, index=True)  # PDA address from Solana program
    solana_tx_signature = Column(String, nullable=True)  # Transaction signature for race creation
    
    # Race configuration
    token_mint = Column(String, nullable=False, index=True)  # Solana token mint address
    token_symbol = Column(String, nullable=False)  # Token symbol (e.g., "SOL", "BONK")
    entry_fee_sol = Column(Float, nullable=False)  # Entry fee in SOL (e.g., 0.01)
    
    # Players
    player1_wallet = Column(String, nullable=False)  # Wallet address of first player
    player2_wallet = Column(String, nullable=True)  # Wallet address of second player (null until joined)
    
    # Race state
    status = Column(SQLEnum(RaceStatus), nullable=False, default=RaceStatus.WAITING, index=True)
    
    # Track data (for replay verification)
    track_seed = Column(Integer, nullable=False)  # Seed for deterministic track generation
    track_data = Column(Text, nullable=True)  # JSON string of normalized track samples
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    started_at = Column(DateTime(timezone=True), nullable=True)  # When player2 joined
    settled_at = Column(DateTime(timezone=True), nullable=True)  # When race was settled on-chain
    
    # Metadata
    created_tx_signature = Column(String, nullable=True)  # Transaction signature for race creation


class RaceResult(Base):
    """
    Race results table: Stores player race results and input traces.
    
    Used for replay verification and winner determination.
    """
    __tablename__ = "race_results"

    # Primary key
    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, index=True)
    
    # Foreign key to race
    race_id = Column(UUID(as_uuid=True), nullable=False, index=True)  # References races.id
    
    # Player information
    wallet_address = Column(String, nullable=False, index=True)
    player_number = Column(Integer, nullable=False)  # 1 or 2
    
    # Race performance
    finish_time_ms = Column(Integer, nullable=False)  # Race completion time in milliseconds
    coins_collected = Column(Integer, nullable=False, default=0)  # Number of coins collected
    
    # Replay verification data
    input_hash = Column(String, nullable=False)  # SHA256 hash of input trace
    input_trace = Column(Text, nullable=True)  # JSON array of input events (for debugging/verification)
    
    # Verification status
    verified = Column(Boolean, nullable=False, default=False)  # Whether replay verification passed
    verification_tolerance_ms = Column(Integer, nullable=True)  # Time difference tolerance (±50ms)
    
    # Timestamps
    submitted_at = Column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    verified_at = Column(DateTime(timezone=True), nullable=True)


class Payout(Base):
    """
    Payout table: Tracks token swap and payment status.
    
    Records Jupiter swap transactions and fallback SOL payments.
    """
    __tablename__ = "payouts"

    # Primary key
    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, index=True)
    
    # Foreign key to race
    race_id = Column(UUID(as_uuid=True), nullable=False, unique=True, index=True)  # One payout per race
    
    # Winner information
    winner_wallet = Column(String, nullable=False, index=True)
    winner_result_id = Column(UUID(as_uuid=True), nullable=False)  # References race_results.id
    
    # Prize information
    prize_amount_sol = Column(Float, nullable=False)  # Total prize pool in SOL (2x entry fee)
    token_mint = Column(String, nullable=False)  # Token mint address for swap
    token_amount = Column(Float, nullable=True)  # Amount of tokens received from swap
    
    # Jupiter swap details
    swap_tx_signature = Column(String, nullable=True)  # Jupiter swap transaction signature
    swap_status = Column(SQLEnum(PayoutStatus), nullable=False, default=PayoutStatus.PENDING, index=True)
    
    # Transfer details
    transfer_tx_signature = Column(String, nullable=True)  # Token transfer to winner transaction signature
    
    # Fallback information
    fallback_sol_amount = Column(Float, nullable=True)  # SOL amount if swap failed
    fallback_tx_signature = Column(String, nullable=True)  # SOL transfer transaction signature
    
    # Error tracking
    error_message = Column(Text, nullable=True)  # Error message if swap/payout failed
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    swap_started_at = Column(DateTime(timezone=True), nullable=True)
    completed_at = Column(DateTime(timezone=True), nullable=True)


class Token(Base):
    """
    Token table: Stores curated token information.
    
    Initially populated from curated_tokens.json, later synced with Solana token metadata.
    """
    __tablename__ = "tokens"

    # Primary key
    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4, index=True)
    
    # Token information
    mint_address = Column(String, unique=True, nullable=False, index=True)  # Solana token mint address
    symbol = Column(String, nullable=False, index=True)  # Token symbol (e.g., "SOL", "BONK")
    name = Column(String, nullable=False)  # Token name
    
    # Metadata
    decimals = Column(Integer, nullable=False, default=9)  # Token decimals
    logo_url = Column(String, nullable=True)  # Token logo URL
    
    # Chart data (cached)
    last_chart_update = Column(DateTime(timezone=True), nullable=True)  # Last time chart data was fetched
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    updated_at = Column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now(), nullable=False)

