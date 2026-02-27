// ---------------------------------------------------------------------------
// Verification Script  MagicBlock ER Oracle Decode
// ---------------------------------------------------------------------------
// Fetches the SOL oracle account, decodes the price at offset 73,
// and prints raw + decoded values for manual verification.
//
// Usage:  npx tsx scripts/verify-oracle-decode.ts
// ---------------------------------------------------------------------------

import { Connection, PublicKey } from '@solana/web3.js';

const MAGICBLOCK_RPC_URL =
  process.env['MAGICBLOCK_RPC_URL'] ?? 'https://devnet.magicblock.app';

const PRICE_PROGRAM_ID = new PublicKey(
  'PriCems5tHihc6UDXDjzjeawomAwBduWMGAi8ZUjppd',
);

const PYTH_LAZER_PRICE_OFFSET = 73;

interface FeedSpec {
  symbol: string;
  feedId: number;
  exponent: number;
}

const FEEDS: FeedSpec[] = [
  { symbol: 'SOL', feedId: 6, exponent: -8 },
  { symbol: 'BONK', feedId: 9, exponent: -10 },
  { symbol: 'JUP', feedId: 92, exponent: -8 },
];

function derivePDA(feedId: number): PublicKey {
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

async function main(): Promise<void> {
  const conn = new Connection(MAGICBLOCK_RPC_URL, 'confirmed');
  console.log(`RPC: ${MAGICBLOCK_RPC_URL}\n`);

  for (const feed of FEEDS) {
    const pda = derivePDA(feed.feedId);
    console.log(`--- ${feed.symbol} ---`);
    console.log(`PDA:      ${pda.toBase58()}`);

    try {
      const resp = await conn.getAccountInfoAndContext(pda);
      const { value: info, context } = resp;

      if (!info || !info.data) {
        console.log('Status:   Account not found\n');
        continue;
      }

      const buf = Buffer.from(info.data);
      if (buf.length < PYTH_LAZER_PRICE_OFFSET + 8) {
        console.log(`Status:   Data too short (${buf.length} bytes)\n`);
        continue;
      }

      const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
      const raw = dv.getBigInt64(PYTH_LAZER_PRICE_OFFSET, true);
      const price = Number(raw) * Math.pow(10, feed.exponent);

      console.log(`Raw i64:  ${raw}`);
      console.log(`Decoded:  ${price.toFixed(Math.abs(feed.exponent))}`);
      console.log(`Slot:     ${context.slot}`);
      console.log(`Bytes:    ${buf.length}`);
    } catch (err) {
      console.log(`Error:    ${err}`);
    }
    console.log();
  }
}

main().catch((err) => {
  console.error('Fatal:', err);
  process.exit(1);
});
