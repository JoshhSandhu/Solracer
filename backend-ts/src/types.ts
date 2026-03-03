// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export type RaceStatus = "waiting" | "active" | "settled" | "cancelled";

// ---------------------------------------------------------------------------
// Stored Race (internal, superset of all response shapes)
// ---------------------------------------------------------------------------

export interface Race {
  /** Internal UUID string, e.g. "bad0aa56-bd2e-4518-be83-2cc41efe1be1" */
  id: string;
  /** 32-char hex PDA seed, e.g. "19d686c6f6bb50d45218865e45329344" */
  race_id: string;
  /** Solana token mint address */
  token_mint: string;
  /** Token symbol, e.g. "SOL" */
  token_symbol: string;
  /** Entry fee in SOL */
  entry_fee_sol: number;
  /** Player 1 wallet address */
  player1_wallet: string;
  /** Player 2 wallet address  null until joined */
  player2_wallet: string | null;
  /** Current race state */
  status: RaceStatus;
  /** Track generation seed (0–999999) */
  track_seed: number;
  /** ISO 8601 creation timestamp */
  created_at: string;
  /** Solana transaction signature  null until settled */
  solana_tx_signature: string | null;
  /** Whether this is a private (join-code) race */
  is_private: boolean;
  /** 6-char join code for private races  null for public */
  join_code: string | null;
  /** ISO 8601 expiration timestamp  null if unset */
  expires_at: string | null;
  /** Player 1 ready status */
  player1_ready: boolean;
  /** Player 2 ready status */
  player2_ready: boolean;
}

// ---------------------------------------------------------------------------
// Request schemas
// ---------------------------------------------------------------------------

/** POST /api/v1/races/create */
export interface CreateRaceRequest {
  token_mint: string;
  wallet_address: string;
  entry_fee_sol: number;
  is_private?: boolean;
}

/** POST /api/v1/races/{race_id}/join */
export interface JoinRaceByIdRequest {
  wallet_address: string;
}

/** POST /api/v1/races/join-by-code */
export interface JoinRaceByCodeRequest {
  join_code: string;
  wallet_address: string;
}

/** POST /api/v1/races/{race_id}/ready */
export interface MarkReadyRequest {
  wallet_address: string;
}

// ---------------------------------------------------------------------------
// Response schemas  exact shapes from golden JSON
// ---------------------------------------------------------------------------

/**
 * Response for: POST /create, POST /join, POST /join-by-code
 * Matches golden/create.json, golden/join.json, golden/join_by_code.json
 */
export interface RaceResponse {
  id: string;
  race_id: string;
  token_mint: string;
  token_symbol: string;
  entry_fee_sol: number;
  player1_wallet: string;
  player2_wallet: string | null;
  status: RaceStatus;
  track_seed: number;
  created_at: string;
  solana_tx_signature: string | null;
  is_private: boolean;
  join_code: string | null;
  expires_at: string | null;
  player1_ready: boolean;
  player2_ready: boolean;
}

/** Player result  nested in RaceStatusResponse */
export interface PlayerResult {
  wallet_address: string;
  player_number: 1 | 2;
  finish_time_ms: number | null;
  coins_collected: number | null;
  verified: boolean | null;
}

/**
 * Response for: GET /races/{race_id}/status
 * Matches golden/status_waiting.json, golden/status_active.json
 */
export interface RaceStatusResponse {
  race_id: string;
  status: RaceStatus;
  player1_wallet: string;
  player2_wallet: string | null;
  winner_wallet: string | null;
  is_settled: boolean;
  player1_ready: boolean;
  player2_ready: boolean;
  both_ready: boolean;
  player1_result: PlayerResult | null;
  player2_result: PlayerResult | null;
}

/**
 * Item in: GET /races/public
 * Matches golden/public.json array items
 */
export interface PublicRaceListItem {
  race_id: string;
  token_mint: string;
  token_symbol: string;
  entry_fee_sol: number;
  player1_wallet: string;
  created_at: string;
  expires_at: string | null;
}

/**
 * Response for: POST /races/{race_id}/ready
 * Matches golden/ready.json
 */
export interface ReadyResponse {
  message: string;
  race_id: string;
  player1_ready: boolean;
  player2_ready: boolean;
  both_ready: boolean;
}

/**
 * Response for: DELETE /races/{race_id}
 * Matches golden/cancel.json
 */
export interface CancelResponse {
  message: string;
  race_id: string;
}

/**
 * Error response shape (FastAPI standard)
 */
export interface ErrorResponse {
  detail: string;
}

// ---------------------------------------------------------------------------
// Token (internal  for race creation validation)
// ---------------------------------------------------------------------------

export interface Token {
  mint_address: string;
  symbol: string;
  name: string;
  decimals: number;
}
