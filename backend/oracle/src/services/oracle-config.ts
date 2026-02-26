// ---------------------------------------------------------------------------
// Oracle Pipeline  MagicBlock ER Oracle Configuration
// ---------------------------------------------------------------------------
// Static feed registry mapping Solana token mints to Pyth Lazer feed metadata.
// Single source of truth for token-to-feed mapping.
// ---------------------------------------------------------------------------

import { PublicKey } from '@solana/web3.js';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** MagicBlock price oracle program ID (Pyth Lazer on ER). */
export const PRICE_PROGRAM_ID = new PublicKey(
  'PriCems5tHihc6UDXDjzjeawomAwBduWMGAi8ZUjppd',
);

/** MagicBlock Ephemeral Rollup RPC endpoint (configurable via env). */
export const MAGICBLOCK_RPC_URL =
  process.env['MAGICBLOCK_RPC_URL'] ?? 'https://devnet.magicblock.app';

/**
 * Byte offset where the i64 price lives inside a Pyth Lazer price account.
 * Layout is part of the MagicBlock oracle protocol.
 */
export const PYTH_LAZER_PRICE_OFFSET = 73;

// ---------------------------------------------------------------------------
// Feed Registry
// ---------------------------------------------------------------------------

export interface FeedEntry {
  /** Human-readable symbol (e.g. "SOL"). */
  symbol: string;

  /** Pyth Lazer feed ID number. */
  feedId: number;

  /** Price exponent (e.g. -8 means raw / 10^8). */
  exponent: number;

  /** Pre-derived PDA for this feed's on-chain account. */
  pda: PublicKey;
}

/**
 * Derive the PDA for a Pyth Lazer price feed account.
 * Seeds: ['price_feed', 'pyth-lazer', feedId as string]
 */
function deriveFeedPDA(feedId: number): PublicKey {
  const [pda] = PublicKey.findProgramAddressSync(
    [
      Buffer.from('price_feed'),
      Buffer.from('pyth-lazer'),
      Buffer.from(String(feedId)),
    ],
    PRICE_PROGRAM_ID,
  );
  return pda;
}

/**
 * Static registry of supported token feeds.
 * Key = Solana token mint address (base-58).
 * PDAs are derived once at import time  zero cost per tick.
 */
export const FEED_REGISTRY: Record<string, FeedEntry> = {
  // SOL/USD
  'So11111111111111111111111111111111111111112': {
    symbol: 'SOL',
    feedId: 6,
    exponent: -8,
    pda: deriveFeedPDA(6),
  },
  // BONK/USD
  'DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263': {
    symbol: 'BONK',
    feedId: 9,
    exponent: -10,
    pda: deriveFeedPDA(9),
  },
  // JUP/USD
  'JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN': {
    symbol: 'JUP',
    feedId: 92,
    exponent: -8,
    pda: deriveFeedPDA(92),
  },
};
