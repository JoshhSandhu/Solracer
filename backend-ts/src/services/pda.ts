import { PublicKey } from "@solana/web3.js";
import crypto from "crypto";

// Updated program ID after Linux re-deploy
const DEFAULT_PROGRAM_ID = "2g9tQ4g6Qki95UBTGN4NcQ4ggpz5XRa6eQJ8MCuznr8S";

// solracer-er: the MagicBlock Ephemeral Rollup ghost position program
export const ER_PROGRAM_ID = new PublicKey("3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG");

let _programId: PublicKey | null = null;

export function getProgramId(): PublicKey {
  if (!_programId) {
    const id = process.env.SOLANA_PROGRAM_ID ?? DEFAULT_PROGRAM_ID;
    _programId = new PublicKey(id);
  }
  return _programId;
}

export function deriveRacePda(
  raceId: string,
  tokenMint: PublicKey,
  entryFeeLamports: bigint,
): [PublicKey, number] {
  const entryFeeBytes = Buffer.alloc(8);
  entryFeeBytes.writeBigUInt64LE(entryFeeLamports);

  const seeds = [
    Buffer.from("race"),
    Buffer.from(raceId, "utf-8"),
    tokenMint.toBuffer(),
    entryFeeBytes,
  ];

  return PublicKey.findProgramAddressSync(seeds, getProgramId());
}

/** SHA-256 of the raceId string, matches the on-chain race_id_hash() helper */
export function raceIdHash(raceId: string): Buffer {
  return crypto.createHash("sha256").update(raceId, "utf-8").digest();
}

/**
 * Derive the PlayerSession PDA for a given race + player wallet.
 * Seeds: ["session", SHA256(raceId), playerWallet]
 */
export function deriveSessionPda(raceId: string, playerWallet: PublicKey): PublicKey {
  const hash = raceIdHash(raceId);
  const [pda] = PublicKey.findProgramAddressSync(
    [Buffer.from("session"), hash, playerWallet.toBuffer()],
    getProgramId(),
  );
  return pda;
}

/**
 * Derive the PlayerPosition PDA for the solracer-er ghost relay program.
 * Seeds: ["position", SHA256(raceId), playerWallet]
 */
export function derivePlayerPositionPda(raceId: string, playerWallet: PublicKey): PublicKey {
  const hash = raceIdHash(raceId);
  const [pda] = PublicKey.findProgramAddressSync(
    [Buffer.from("position"), hash, playerWallet.toBuffer()],
    ER_PROGRAM_ID,
  );
  return pda;
}
