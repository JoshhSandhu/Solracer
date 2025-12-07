"""
Pydantic schemas for request/response validation.

These schemas define the structure of API requests and responses,
providing automatic validation and serialization.
"""

from pydantic import BaseModel, Field
from typing import Optional, List, Any, Dict
from datetime import datetime
from enum import Enum


class TokenResponse(BaseModel):
    """Response schema for token information."""
    mint_address: str
    symbol: str
    name: str
    decimals: int
    logo_url: Optional[str] = None

    class Config:
        from_attributes = True


class TrackSample(BaseModel):
    """Single track sample point."""
    x: float = Field(..., ge=0.0, le=1.0, description="Normalized X position (0-1)")
    y: float = Field(..., ge=0.0, le=1.0, description="Normalized Y position (0-1)")


class TrackResponse(BaseModel):
    """Response schema for track data."""
    token_mint: str
    token_symbol: str
    seed: int = Field(..., description="Seed for deterministic track generation")
    samples: List[TrackSample] = Field(..., description="Normalized track samples")
    point_count: int = Field(..., description="Number of samples in track")


class RaceStatus(str, Enum):
    """Race status enumeration."""
    WAITING = "waiting"
    ACTIVE = "active"
    SETTLED = "settled"
    CANCELLED = "cancelled"


class RaceResponse(BaseModel):
    """Response schema for race information."""
    id: str
    race_id: str
    token_mint: str
    token_symbol: str
    entry_fee_sol: float
    player1_wallet: str
    player2_wallet: Optional[str] = None
    status: RaceStatus
    track_seed: int
    created_at: datetime
    solana_tx_signature: Optional[str] = None
    is_private: bool = False
    join_code: Optional[str] = None
    expires_at: Optional[datetime] = None
    player1_ready: bool = False
    player2_ready: bool = False

    class Config:
        from_attributes = True


class CreateRaceRequest(BaseModel):
    """Request schema for creating a race."""
    token_mint: str = Field(..., description="Solana token mint address")
    wallet_address: str = Field(..., description="Player wallet address")
    entry_fee_sol: float = Field(..., ge=0.005, le=0.02, description="Entry fee in SOL (0.005-0.02)")
    is_private: bool = Field(default=False, description="Whether race is private (requires join code)")


class JoinRaceByCodeRequest(BaseModel):
    """Request schema for joining a race by code."""
    join_code: str = Field(..., min_length=6, max_length=6, description="6-character join code")
    wallet_address: str = Field(..., description="Player wallet address")


class JoinRaceByIdRequest(BaseModel):
    """Request schema for joining a race by ID."""
    wallet_address: str = Field(..., description="Player wallet address")


class MarkReadyRequest(BaseModel):
    """Request schema for marking player as ready."""
    wallet_address: str = Field(..., description="Player wallet address")


class PublicRaceListItem(BaseModel):
    """Response schema for public race list item."""
    race_id: str
    token_mint: str
    token_symbol: str
    entry_fee_sol: float
    player1_wallet: str
    created_at: datetime
    expires_at: Optional[datetime] = None

    class Config:
        from_attributes = True


class SubmitResultRequest(BaseModel):
    """Request schema for submitting race result."""
    wallet_address: str = Field(..., description="Player wallet address")
    finish_time_ms: int = Field(..., ge=0, description="Race completion time in milliseconds")
    coins_collected: int = Field(default=0, ge=0, description="Number of coins collected")
    input_hash: str = Field(..., description="SHA256 hash of input trace")
    input_trace: Optional[List[dict]] = Field(None, description="Input trace for verification")


class PlayerResult(BaseModel):
    """Player result information."""
    wallet_address: str
    player_number: int
    finish_time_ms: Optional[int] = None
    coins_collected: Optional[int] = None
    verified: Optional[bool] = None

    class Config:
        from_attributes = True


class RaceStatusResponse(BaseModel):
    """Response schema for race status."""
    race_id: str
    status: RaceStatus
    player1_wallet: str
    player2_wallet: Optional[str] = None
    winner_wallet: Optional[str] = None
    is_settled: bool
    player1_ready: bool = False
    player2_ready: bool = False
    both_ready: bool = False
    player1_result: Optional[PlayerResult] = None
    player2_result: Optional[PlayerResult] = None

    class Config:
        from_attributes = True


# Transaction-related schemas
class BuildTransactionRequest(BaseModel):
    """Request schema for building a transaction."""
    instruction_type: str = Field(..., description="Instruction type: create_race, join_race, submit_result, claim_prize")
    race_id: Optional[str] = Field(None, description="Race ID (required for join_race, submit_result, claim_prize)")
    wallet_address: str = Field(..., description="Wallet address (signer)")
    token_mint: Optional[str] = Field(None, description="Token mint (required for create_race)")
    entry_fee_sol: Optional[float] = Field(None, description="Entry fee in SOL (required for create_race)")
    finish_time_ms: Optional[int] = Field(None, description="Finish time in ms (required for submit_result)")
    coins_collected: Optional[int] = Field(None, description="Coins collected (required for submit_result)")
    input_hash: Optional[str] = Field(None, description="Input hash (required for submit_result)")


class BuildTransactionResponse(BaseModel):
    """Response schema for built transaction."""
    transaction_bytes: str = Field(..., description="Base64-encoded transaction bytes for signing")
    instruction_type: str = Field(..., description="Instruction type")
    race_id: Optional[str] = Field(None, description="Race ID")
    race_pda: Optional[str] = Field(None, description="Race PDA address")
    recent_blockhash: str = Field(..., description="Recent blockhash used in transaction")


class SubmitTransactionRequest(BaseModel):
    """Request schema for submitting a signed transaction."""
    signed_transaction_bytes: str = Field(..., description="Base64-encoded signed transaction bytes")
    instruction_type: str = Field(..., description="Instruction type")
    race_id: Optional[str] = Field(None, description="Race ID (for tracking)")
    # Optional fields for create_race instruction
    token_mint: Optional[str] = Field(None, description="Token mint address (for create_race)")
    entry_fee_sol: Optional[float] = Field(None, description="Entry fee in SOL (for create_race)")
    # Optional fields for submit_result instruction
    wallet_address: Optional[str] = Field(None, description="Wallet address (for submit_result)")
    finish_time_ms: Optional[int] = Field(None, description="Finish time in milliseconds (for submit_result)")
    coins_collected: Optional[int] = Field(None, description="Coins collected (for submit_result)")
    input_hash: Optional[str] = Field(None, description="Input hash for replay verification (for submit_result)")


class SubmitTransactionResponse(BaseModel):
    """Response schema for transaction submission."""
    transaction_signature: str = Field(..., description="Transaction signature")
    instruction_type: str = Field(..., description="Instruction type")
    race_id: Optional[str] = Field(None, description="Race ID")
    confirmed: bool = Field(default=False, description="Whether transaction is confirmed")


class SettleRaceRequest(BaseModel):
    """Request schema for settling a race."""
    race_id: str = Field(..., description="Race ID")


class ClaimPrizeRequest(BaseModel):
    """Request schema for claiming prize."""
    race_id: str = Field(..., description="Race ID")
    wallet_address: str = Field(..., description="Winner wallet address")


# Payout-related schemas
class PayoutStatusEnum(str, Enum):
    """Payout status enumeration."""
    PENDING = "pending"
    SWAPPING = "swapping"
    PAID = "paid"
    FALLBACK_SOL = "fallback_sol"
    FAILED = "failed"


class PayoutResponse(BaseModel):
    """Response schema for payout information."""
    payout_id: str
    race_id: str
    winner_wallet: str
    prize_amount_sol: float
    token_mint: str
    token_amount: Optional[float] = None
    swap_status: PayoutStatusEnum
    swap_tx_signature: Optional[str] = None
    transfer_tx_signature: Optional[str] = None
    fallback_sol_amount: Optional[float] = None
    fallback_tx_signature: Optional[str] = None
    error_message: Optional[str] = None
    created_at: datetime
    swap_started_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None

    class Config:
        from_attributes = True


class ProcessPayoutResponse(BaseModel):
    """
    Response schema for processing payout.
    
    Returns transaction bytes for the client to sign and submit.
    """
    status: str = Field(..., description="Status: ready_for_signing, processing, completed, failed")
    payout_id: str = Field(..., description="Payout ID")
    transaction: Optional[str] = Field(None, description="Base64-encoded claim_prize transaction (for SOL)")
    swap_transaction: Optional[str] = Field(None, description="Base64-encoded Jupiter swap transaction (for non-SOL tokens)")
    method: str = Field(..., description="Method: claim_prize, jupiter_swap, fallback_sol")
    amount_sol: Optional[float] = Field(None, description="Prize amount in SOL")
    amount_tokens: Optional[float] = Field(None, description="Expected token amount after swap")
    error: Optional[str] = Field(None, description="Error message if any")
