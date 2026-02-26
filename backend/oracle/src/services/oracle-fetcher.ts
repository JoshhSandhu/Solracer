// ---------------------------------------------------------------------------
// Oracle Pipeline  MagicBlock ER Oracle Fetcher (Production)
// ---------------------------------------------------------------------------
// Reads real-time Pyth Lazer price feeds from MagicBlock's Ephemeral Rollup
// via a single batched RPC call per tick cycle.
//
// Architecture:
//   - Singleton Connection (created once, reset on RPC failure)
//   - Pre-derived PDAs (from oracle-config.ts, computed at import time)
//   - getMultipleAccountsInfo() for batch reads (1 RPC call per tick)
//   - Worker owns tick_time  this module does NOT generate timestamps
// ---------------------------------------------------------------------------

import { Connection } from '@solana/web3.js';
import type { OraclePriceData } from '../types/oracle.types';
import {
  MAGICBLOCK_RPC_URL,
  PYTH_LAZER_PRICE_OFFSET,
  FEED_REGISTRY,
  type FeedEntry,
} from './oracle-config';

/** RPC call timeout in milliseconds. */
const RPC_TIMEOUT_MS = 5_000;

// ---------------------------------------------------------------------------
// Singleton Connection (reset on RPC failure)
// ---------------------------------------------------------------------------

let connection: Connection | null = null;

function getConnection(): Connection {
  if (!connection) {
    connection = new Connection(MAGICBLOCK_RPC_URL, 'confirmed');
    console.log(`[oracle/fetcher] Connected to MagicBlock ER: ${MAGICBLOCK_RPC_URL}`);
  }
  return connection;
}

function resetConnection(): void {
  connection = null;
}

// ---------------------------------------------------------------------------
// RPC Timeout Wrapper
// ---------------------------------------------------------------------------
// Known limitation: Promise.race does not cancel the underlying HTTP request.
// On timeout the in-flight RPC call completes in the background. At ~1800
// calls/hour and a 5s timeout this is acceptable — stale responses are
// discarded and the connection is reset for the next tick.
// ---------------------------------------------------------------------------

async function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return Promise.race([
    promise,
    new Promise<T>((_, reject) =>
      setTimeout(() => reject(new Error(`RPC timeout after ${ms}ms`)), ms),
    ),
  ]);
}

// ---------------------------------------------------------------------------
// Price Decoding
// ---------------------------------------------------------------------------

/**
 * Decode the i64 price from a Pyth Lazer account's raw data.
 * Returns the human-readable price after applying the exponent.
 */
function decodePrice(data: Buffer, exponent: number): number {
  if (data.length < PYTH_LAZER_PRICE_OFFSET + 8) {
    throw new Error(
      `Account data too short: ${data.length} bytes (need >= ${PYTH_LAZER_PRICE_OFFSET + 8})`,
    );
  }

  const dv = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const raw = dv.getBigInt64(PYTH_LAZER_PRICE_OFFSET, true); // i64, little-endian

  if (raw > BigInt(Number.MAX_SAFE_INTEGER) || raw < BigInt(Number.MIN_SAFE_INTEGER)) {
    throw new Error(
      `Oracle price raw value exceeds safe integer range: ${raw}`,
    );
  }

  return Number(raw) * Math.pow(10, exponent);
}

// ---------------------------------------------------------------------------
// Public API — Single Token Fetch
// ---------------------------------------------------------------------------

/**
 * Fetch the current oracle price for a single token.
 *
 * Uses getAccountInfo for individual calls.
 * For batch reads, use fetchOraclePricesBatch() instead.
 *
 * @param tokenMint  Solana token mint address (base-58)
 * @returns          Oracle price data (without tick_time  worker owns timing)
 */
export async function fetchOraclePrice(
  tokenMint: string,
): Promise<OraclePriceData> {
  const feed = FEED_REGISTRY[tokenMint];

  if (!feed) {
    throw new Error(
      `No Pyth Lazer feed configured for token mint: ${tokenMint}`,
    );
  }

  let response;
  try {
    const conn = getConnection();
    response = await withTimeout(
      conn.getAccountInfoAndContext(feed.pda),
      RPC_TIMEOUT_MS,
    );
  } catch (err) {
    resetConnection();
    throw err;
  }

  const { value: accountInfo, context } = response;

  if (!accountInfo || !accountInfo.data) {
    throw new Error(
      `Oracle account not found for ${feed.symbol} (PDA: ${feed.pda.toBase58()})`,
    );
  }

  const price = decodePrice(Buffer.from(accountInfo.data), feed.exponent);

  if (!Number.isFinite(price) || price <= 0) {
    throw new Error(
      `Invalid oracle price for ${feed.symbol}: ${price}`,
    );
  }

  return {
    token_mint: tokenMint,
    price,
    publish_time: new Date(),
    source_slot: context.slot,
  };
}

// ---------------------------------------------------------------------------
// Public API — Batch Fetch (Preferred)
// ---------------------------------------------------------------------------

/**
 * Fetch oracle prices for multiple tokens in a single RPC call.
 *
 * Uses getMultipleAccountsInfo() for efficiency:
 *   - 1 RPC call instead of N
 *   - Lower latency, lower rate-limit risk
 *
 * @param tokenMints  Array of Solana token mint addresses
 * @returns           Map of tokenMint → OraclePriceData (only successful reads)
 */
export async function fetchOraclePricesBatch(
  tokenMints: string[],
): Promise<Map<string, OraclePriceData>> {
  const results = new Map<string, OraclePriceData>();

  const feeds: { mint: string; feed: FeedEntry }[] = [];
  for (const mint of tokenMints) {
    const feed = FEED_REGISTRY[mint];
    if (!feed) {
      console.warn(`[oracle/fetcher] No feed configured for: ${mint}`);
      continue;
    }
    feeds.push({ mint, feed });
  }

  if (feeds.length === 0) return results;

  const pdas = feeds.map((f) => f.feed.pda);
  const now = new Date();

  let response;
  try {
    const conn = getConnection();
    response = await withTimeout(
      conn.getMultipleAccountsInfoAndContext(pdas),
      RPC_TIMEOUT_MS,
    );
  } catch (err) {
    resetConnection();
    throw err;
  }

  const { value: accounts, context } = response;
  const slot = context.slot;

  for (let i = 0; i < feeds.length; i++) {
    const { mint, feed } = feeds[i];
    const accountInfo = accounts[i];

    if (!accountInfo || !accountInfo.data) {
      console.warn(
        `[oracle/fetcher] Account not found for ${feed.symbol} (PDA: ${feed.pda.toBase58()})`,
      );
      continue;
    }

    try {
      const price = decodePrice(Buffer.from(accountInfo.data), feed.exponent);

      if (!Number.isFinite(price) || price <= 0) {
        console.warn(
          `[oracle/fetcher] Invalid price for ${feed.symbol}: ${price}`,
        );
        continue;
      }

      results.set(mint, {
        token_mint: mint,
        price,
        publish_time: now,
        source_slot: slot,
      });
    } catch (err) {
      console.warn(
        `[oracle/fetcher] Failed to decode ${feed.symbol}: ${err}`,
      );
    }
  }

  return results;
}
