import { v4 as uuidv4 } from "uuid";
import crypto from "node:crypto";
import type {
  Race,
  RaceStatus,
  Token,
  RaceResponse,
  RaceStatusResponse,
  PublicRaceListItem,
  ReadyResponse,
  CancelResponse,
  PlayerResult,
} from "../types.js";

// ---------------------------------------------------------------------------
// Result & Payout types (in-memory only)
// ---------------------------------------------------------------------------

export interface StoredResult {
  wallet_address: string;
  player_number: 1 | 2;
  finish_time_ms: number;
  coins_collected: number;
  tx_signature: string;
}

export interface StoredPayout {
  payout_id: string;
  race_id: string;
  winner_wallet: string;
  prize_amount_sol: number;
  token_mint: string;
  swap_status: "pending" | "paid" | "failed";
  created_at: string;
  completed_at: string | null;
}

// ---------------------------------------------------------------------------
// Hardcoded tokens (same as Python backend seed data)
// ---------------------------------------------------------------------------

const TOKENS: Token[] = [
  {
    mint_address: "So11111111111111111111111111111111111111112",
    symbol: "SOL",
    name: "Solana",
    decimals: 9,
  },
  {
    mint_address: "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263",
    symbol: "BONK",
    name: "Bonk",
    decimals: 5,
  },
  {
    mint_address: "JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN",
    symbol: "JUP",
    name: "Jupiter",
    decimals: 6,
  },
];

// ---------------------------------------------------------------------------
// Helper functions (match Python backend exactly)
// ---------------------------------------------------------------------------

function generateRaceId(
  tokenMint: string,
  entryFee: number,
  player1: string
): string {
  const timestamp = process.hrtime.bigint().toString();
  const seed = `${tokenMint}_${entryFee}_${player1}_${timestamp}`;
  return crypto.createHash("sha256").update(seed).digest("hex").slice(0, 32);
}

function generateJoinCode(): string {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // excludes 0, O, I, 1
  let code = "";
  for (let i = 0; i < 6; i++) {
    code += chars[Math.floor(Math.random() * chars.length)];
  }
  return code;
}

/** Format: YYYY-MM-DDTHH:MM:SS no milliseconds, no timezone. */
function formatTimestamp(d: Date): string {
  return d.toISOString().replace("Z", "").split(".")[0];
}

function nowISO(): string {
  return formatTimestamp(new Date());
}

function isoAddMinutes(minutes: number): string {
  return formatTimestamp(new Date(Date.now() + minutes * 60_000));
}

function isExpired(isoStr: string | null): boolean {
  if (!isoStr) return false;
  const t = new Date(isoStr + "Z").getTime();
  return Date.now() > t;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

/** Internal race record timestamps stored as ISO strings without TZ */
const races = new Map<string, Race>();
const raceResults = new Map<string, StoredResult[]>();
const payouts = new Map<string, StoredPayout>();

// ---------------------------------------------------------------------------
// Background Cleanup Interval
// ---------------------------------------------------------------------------

let _cleanupTimer: ReturnType<typeof setInterval> | null = null;

/**
 * Start a background interval that cleans up expired races.
 * Prevents unbounded memory growth when the server is idle (no incoming requests).
 * @param intervalMs Cleanup frequency in ms (default 60_000)
 */
export function startCleanupInterval(intervalMs = 60_000): void {
  if (_cleanupTimer) return;
  _cleanupTimer = setInterval(() => {
    cleanupExpiredRaces();
  }, intervalMs);
  // Don't keep the Node process alive just for cleanup
  if (_cleanupTimer.unref) _cleanupTimer.unref();
}

/** Stop the background cleanup interval (for graceful shutdown). */
export function stopCleanupInterval(): void {
  if (_cleanupTimer) {
    clearInterval(_cleanupTimer);
    _cleanupTimer = null;
  }
}

function toRaceResponse(r: Race): RaceResponse {
  return {
    id: r.id,
    race_id: r.race_id,
    token_mint: r.token_mint,
    token_symbol: r.token_symbol,
    entry_fee_sol: r.entry_fee_sol,
    player1_wallet: r.player1_wallet,
    player2_wallet: r.player2_wallet,
    status: r.status,
    track_seed: r.track_seed,
    created_at: r.created_at,
    solana_tx_signature: r.solana_tx_signature,
    is_private: r.is_private,
    join_code: r.join_code,
    expires_at: r.expires_at,
    player1_ready: r.player1_ready,
    player2_ready: r.player2_ready,
  };
}

/** Cancel expired + hard-delete >10min old races (same as Python) */
function cleanupExpiredRaces(): void {
  const now = Date.now();
  const tenMinMs = 10 * 60_000;

  for (const [id, race] of races) {
    // Hard-delete any race older than 10 minutes
    const createdMs = new Date(race.created_at + "Z").getTime();
    if (now - createdMs > tenMinMs) {
      races.delete(id);
      continue;
    }

    // Cancel expired waiting races
    if (race.status === "waiting" && race.expires_at && isExpired(race.expires_at)) {
      race.status = "cancelled";
    }
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export function getToken(mintAddress: string): Token | undefined {
  return TOKENS.find((t) => t.mint_address === mintAddress);
}

export function createRace(
  tokenMint: string,
  walletAddress: string,
  entryFeeSol: number,
  isPrivate: boolean
): RaceResponse | { error: string; status: number } {
  cleanupExpiredRaces();

  const token = getToken(tokenMint);
  if (!token) {
    return { error: `Token ${tokenMint} not found`, status: 404 };
  }

  // Validate entry fee (0.005 – 0.02)
  if (entryFeeSol < 0.005 || entryFeeSol > 0.02) {
    return { error: "Entry fee must be between 0.005 and 0.02 SOL", status: 422 };
  }

  const raceId = generateRaceId(tokenMint, entryFeeSol, walletAddress);

  // Generate join code for private races
  let joinCode: string | null = null;
  if (isPrivate) {
    for (let attempt = 0; attempt < 10; attempt++) {
      const code = generateJoinCode();
      const exists = [...races.values()].some((r) => r.join_code === code);
      if (!exists) {
        joinCode = code;
        break;
      }
    }
    if (!joinCode) {
      return { error: "Failed to generate unique join code", status: 500 };
    }
  }

  const expirationMinutes = isPrivate ? 10 : 5;
  const trackSeed =
    Math.abs(
      raceId.split("").reduce((h, c) => (h * 31 + c.charCodeAt(0)) | 0, 0)
    ) % 1_000_000;

  const race: Race = {
    id: uuidv4(),
    race_id: raceId,
    token_mint: tokenMint,
    token_symbol: token.symbol,
    entry_fee_sol: entryFeeSol,
    player1_wallet: walletAddress,
    player2_wallet: null,
    status: "waiting",
    track_seed: trackSeed,
    created_at: nowISO(),
    solana_tx_signature: null,
    is_private: isPrivate,
    join_code: joinCode,
    expires_at: isoAddMinutes(expirationMinutes),
    player1_ready: false,
    player2_ready: false,
  };

  races.set(raceId, race);
  return toRaceResponse(race);
}

export function joinRaceById(
  raceId: string,
  walletAddress: string
): RaceResponse | { error: string; status: number } {
  cleanupExpiredRaces();

  const race = races.get(raceId);
  if (!race) return { error: "Race not found", status: 404 };
  if (race.is_private)
    return { error: "Cannot join private race by ID. Use join-by-code endpoint.", status: 400 };
  if (race.status !== "waiting")
    return { error: `Race is not waiting for players. Status: ${race.status}`, status: 400 };
  if (race.player1_wallet === walletAddress)
    return { error: "Cannot join your own race", status: 400 };
  if (race.player2_wallet !== null)
    return { error: "Race is already full", status: 400 };

  race.player2_wallet = walletAddress;
  race.status = "active";
  return toRaceResponse(race);
}

export function joinRaceByCode(
  joinCodeRaw: string,
  walletAddress: string
): RaceResponse | { error: string; status: number } {
  cleanupExpiredRaces();

  const joinCode = joinCodeRaw.toUpperCase().trim();
  const race = [...races.values()].find((r) => r.join_code === joinCode);
  if (!race) return { error: "Invalid join code", status: 404 };
  if (race.status !== "waiting")
    return { error: `Race is not waiting for players. Status: ${race.status}`, status: 400 };
  if (race.player1_wallet === walletAddress)
    return { error: "Cannot join your own race", status: 400 };
  if (race.player2_wallet !== null)
    return { error: "Race is already full", status: 400 };

  // Check code expiration
  if (race.expires_at && isExpired(race.expires_at)) {
    race.status = "cancelled";
    return { error: "Join code has expired", status: 400 };
  }

  race.player2_wallet = walletAddress;
  race.status = "active";
  return toRaceResponse(race);
}

export function getRaceStatus(
  raceId: string
): RaceStatusResponse | { error: string; status: number } {
  cleanupExpiredRaces();

  const race = races.get(raceId);
  if (!race) return { error: "Race not found", status: 404 };

  const results = raceResults.get(raceId) ?? [];
  const p1 = results.find((r) => r.player_number === 1) ?? null;
  const p2 = results.find((r) => r.player_number === 2) ?? null;

  const payout = payouts.get(raceId);
  const winnerWallet = payout?.winner_wallet ?? null;

  return {
    race_id: race.race_id,
    status: race.status,
    player1_wallet: race.player1_wallet,
    player2_wallet: race.player2_wallet,
    winner_wallet: winnerWallet,
    is_settled: race.status === "settled",
    player1_ready: race.player1_ready,
    player2_ready: race.player2_ready,
    both_ready:
      race.player1_ready &&
      race.player2_ready &&
      race.player2_wallet !== null,
    player1_result: p1
      ? {
        wallet_address: p1.wallet_address,
        player_number: 1,
        finish_time_ms: p1.finish_time_ms,
        coins_collected: p1.coins_collected,
        verified: true,
      }
      : null,
    player2_result: p2
      ? {
        wallet_address: p2.wallet_address,
        player_number: 2,
        finish_time_ms: p2.finish_time_ms,
        coins_collected: p2.coins_collected,
        verified: true,
      }
      : null,
  };
}

// ---------------------------------------------------------------------------
// Result submission & settlement
// ---------------------------------------------------------------------------

export function submitRaceResult(
  raceId: string,
  walletAddress: string,
  finishTimeMs: number,
  coinsCollected: number,
  txSignature: string,
): { message: string; settled: boolean } | { error: string; status: number } {
  const race = races.get(raceId);
  if (!race) return { error: "Race not found", status: 404 };
  if (race.status !== "active" && race.status !== "settled")
    return { error: `Race status is ${race.status}, cannot submit result`, status: 400 };

  // Determine player number
  let playerNumber: 1 | 2;
  if (walletAddress === race.player1_wallet) playerNumber = 1;
  else if (walletAddress === race.player2_wallet) playerNumber = 2;
  else return { error: "Wallet is not a participant in this race", status: 403 };

  // Initialize results array
  if (!raceResults.has(raceId)) raceResults.set(raceId, []);
  const results = raceResults.get(raceId)!;

  // Idempotent: don't duplicate
  if (results.some((r) => r.player_number === playerNumber)) {
    const settled = race.status === "settled";
    return { message: `Result already submitted for player ${playerNumber}`, settled };
  }

  results.push({
    wallet_address: walletAddress,
    player_number: playerNumber,
    finish_time_ms: finishTimeMs,
    coins_collected: coinsCollected,
    tx_signature: txSignature,
  });

  // Auto-settle when both results are in
  if (results.length === 2) {
    race.status = "settled";

    // Determine winner: lowest finish time wins
    const sorted = [...results].sort((a, b) => a.finish_time_ms - b.finish_time_ms);
    const winner = sorted[0];

    // Create payout record
    const prizeAmountSol = race.entry_fee_sol * 2 * 0.95; // 5% platform fee
    payouts.set(raceId, {
      payout_id: crypto.randomUUID(),
      race_id: raceId,
      winner_wallet: winner.wallet_address,
      prize_amount_sol: prizeAmountSol,
      token_mint: race.token_mint,
      swap_status: "pending",
      created_at: nowISO(),
      completed_at: null,
    });

    return { message: "Both results submitted. Race settled.", settled: true };
  }

  return { message: "Result submitted. Waiting for opponent.", settled: false };
}

// ---------------------------------------------------------------------------
// Payout queries
// ---------------------------------------------------------------------------

export function getPayoutForRace(raceId: string): StoredPayout | undefined {
  return payouts.get(raceId);
}

export function markReady(
  raceId: string,
  walletAddress: string
): ReadyResponse | { error: string; status: number } {
  const race = races.get(raceId);
  if (!race) return { error: "Race not found", status: 404 };
  if (race.status !== "active")
    return { error: `Race is not active. Status: ${race.status}`, status: 400 };

  if (walletAddress === race.player1_wallet) {
    race.player1_ready = true;
  } else if (walletAddress === race.player2_wallet) {
    race.player2_ready = true;
  } else {
    return {
      error: "Wallet address does not match any player in this race",
      status: 403,
    };
  }

  return {
    message: "Player marked as ready",
    race_id: raceId,
    player1_ready: race.player1_ready,
    player2_ready: race.player2_ready,
    both_ready: race.player1_ready && race.player2_ready,
  };
}

export function cancelRace(
  raceId: string,
  walletAddress: string
): CancelResponse | { error: string; status: number } {
  const race = races.get(raceId);
  if (!race) return { error: "Race not found", status: 404 };
  if (race.player1_wallet !== walletAddress)
    return { error: "Only the race creator can cancel the race", status: 403 };
  if (race.status !== "waiting")
    return { error: `Cannot cancel race with status ${race.status}`, status: 400 };

  race.status = "cancelled";
  return {
    message: "Race cancelled successfully",
    race_id: raceId,
  };
}

/**
 * Register a race in the in-memory lobby store after a confirmed on-chain
 * create_race transaction.  Bridges the Solana TX flow (raceMetaCache)
 * with the lobby endpoints (/races/public, /races/:id/status, etc.).
 */
export function registerRaceFromChain(
  raceId: string,
  tokenMint: string,
  walletAddress: string,
  entryFeeSol: number,
  txSignature: string,
  isPrivate = false,
): RaceResponse | { error: string; status: number } {
  // Idempotent  don't duplicate if already registered
  if (races.has(raceId)) {
    return toRaceResponse(races.get(raceId)!);
  }

  const token = getToken(tokenMint);
  const tokenSymbol = token ? token.symbol : "UNKNOWN";

  let joinCode: string | null = null;
  if (isPrivate) {
    for (let attempt = 0; attempt < 10; attempt++) {
      const code = generateJoinCode();
      const exists = [...races.values()].some((r) => r.join_code === code);
      if (!exists) { joinCode = code; break; }
    }
    if (!joinCode) {
      return { error: "Failed to generate unique join code", status: 500 };
    }
  }

  const expirationMinutes = isPrivate ? 10 : 5;
  const trackSeed =
    Math.abs(
      raceId.split("").reduce((h, c) => (h * 31 + c.charCodeAt(0)) | 0, 0)
    ) % 1_000_000;

  const race: Race = {
    id: raceId,
    race_id: raceId,
    token_mint: tokenMint,
    token_symbol: tokenSymbol,
    entry_fee_sol: entryFeeSol,
    player1_wallet: walletAddress,
    player2_wallet: null,
    status: "waiting",
    track_seed: trackSeed,
    created_at: nowISO(),
    solana_tx_signature: txSignature,
    is_private: isPrivate,
    join_code: joinCode,
    expires_at: isoAddMinutes(expirationMinutes),
    player1_ready: false,
    player2_ready: false,
  };

  races.set(raceId, race);
  return toRaceResponse(race);
}

/** Returns the raw internal Race record (not the response DTO). Used by ghost relay for participant validation. */
export function getRaceRaw(raceId: string): Race | undefined {
  return races.get(raceId);
}

export function listPublicRaces(
  tokenMint?: string,
  entryFee?: number
): PublicRaceListItem[] {
  cleanupExpiredRaces();

  let list = [...races.values()].filter(
    (r) =>
      !r.is_private &&
      r.status === "waiting" &&
      r.player2_wallet === null
  );

  if (tokenMint) list = list.filter((r) => r.token_mint === tokenMint);
  if (entryFee !== undefined) list = list.filter((r) => r.entry_fee_sol === entryFee);

  // Sort by created_at desc, limit 50
  list.sort((a, b) => b.created_at.localeCompare(a.created_at));
  list = list.slice(0, 50);

  return list.map((r) => ({
    race_id: r.race_id,
    token_mint: r.token_mint,
    token_symbol: r.token_symbol,
    entry_fee_sol: r.entry_fee_sol,
    player1_wallet: r.player1_wallet,
    created_at: r.created_at,
    expires_at: r.expires_at,
  }));
}
