import { PublicKey, TransactionInstruction, SystemProgram } from "@solana/web3.js";
import { getProgramId, deriveSessionPda, raceIdHash } from "./pda.js";

// Anchor sighash discriminators (first 8 bytes of SHA-256("global:<instruction>"))
const DISC = {
  create_race:      Buffer.from([233, 107, 148, 159, 241, 155, 226,  54]),
  join_race:        Buffer.from([207,  91, 222,  84, 249, 246, 229,  54]),
  delegate_session: Buffer.from([82, 83, 119, 119, 196,  219, 5, 197]), // SHA-256("global:delegate_session")[0..8]
  submit_result:    Buffer.from([240,  42,  89, 180,  10, 239,   9, 214]),
  settle_race:      Buffer.from([172,  32,  72, 212, 155,  33, 161, 237]),
  claim_prize:      Buffer.from([157, 233, 139, 121, 246,  62, 234, 235]),
} as const;

export function buildCreateRaceIx(
  racePda: PublicKey,
  player1: PublicKey,
  raceId: string,
  tokenMint: PublicKey,
  entryFeeLamports: bigint,
): TransactionInstruction {
  const raceIdBytes = Buffer.from(raceId, "utf-8");
  const raceIdLen = Buffer.alloc(4);
  raceIdLen.writeUInt32LE(raceIdBytes.length);

  const tokenMintBytes = tokenMint.toBuffer();

  const entryFeeBytes = Buffer.alloc(8);
  entryFeeBytes.writeBigUInt64LE(entryFeeLamports);

  const data = Buffer.concat([
    DISC.create_race,
    raceIdLen,
    raceIdBytes,
    tokenMintBytes,
    entryFeeBytes,
  ]);

  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: racePda, isSigner: false, isWritable: true },
      { pubkey: player1, isSigner: true,  isWritable: true },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data,
  });
}

export function buildJoinRaceIx(
  racePda: PublicKey,
  player2: PublicKey,
): TransactionInstruction {
  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: racePda, isSigner: false, isWritable: true },
      { pubkey: player2, isSigner: true,  isWritable: true },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data: DISC.join_race,
  });
}

/**
 * Build the delegate_session instruction.
 * Must be included in the SAME transaction as create_race / join_race
 * so only one wallet popup is needed.
 *
 * @param sessionPda  - Derived with deriveSessionPda(raceId, playerWallet)
 * @param playerWallet - The signing wallet
 * @param raceId       - The race ID string (will be SHA-256 hashed internally)
 * @param sessionKey   - The ephemeral Ed25519 pubkey generated in Unity
 * @param durationSecs - How long the session should last (e.g. 10800 = 3h)
 */
export function buildDelegateSessionIx(
  sessionPda: PublicKey,
  playerWallet: PublicKey,
  raceId: string,
  sessionKey: PublicKey,
  durationSecs: number = 10800,
): TransactionInstruction {
  const hash = raceIdHash(raceId);           // [u8; 32]
  const sessionKeyBuf = sessionKey.toBuffer(); // [u8; 32]
  const durationBuf = Buffer.alloc(8);
  durationBuf.writeBigInt64LE(BigInt(durationSecs));

  const data = Buffer.concat([
    DISC.delegate_session,
    hash,
    sessionKeyBuf,
    durationBuf,
  ]);

  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: sessionPda,              isSigner: false, isWritable: true },
      { pubkey: playerWallet,            isSigner: true,  isWritable: true },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data,
  });
}

/**
 * Build submit_result instruction.
 * When signing with session key, pass authority=sessionKey, sessionPda, and playerWallet.
 * When signing directly with player wallet, pass authority=playerWallet, sessionPda=null, playerWallet=playerWallet.
 */
export function buildSubmitResultIx(
  racePda: PublicKey,
  authority: PublicKey,     // session key OR player wallet
  playerWallet: PublicKey,  // always the real player wallet
  sessionPda: PublicKey | null,
  finishTimeMs: bigint,
  coinsCollected: bigint,
  inputHash: Buffer,
): TransactionInstruction {
  if (inputHash.length !== 32) {
    throw new Error("input_hash must be exactly 32 bytes");
  }

  const ftBuf = Buffer.alloc(8);
  ftBuf.writeBigUInt64LE(finishTimeMs);

  const ccBuf = Buffer.alloc(8);
  ccBuf.writeBigUInt64LE(coinsCollected);

  const data = Buffer.concat([DISC.submit_result, ftBuf, ccBuf, inputHash]);

  // Anchor account order: race, authority, session (optional), player_wallet
  const keys: { pubkey: PublicKey; isSigner: boolean; isWritable: boolean }[] = [
    { pubkey: racePda,      isSigner: false, isWritable: true  },
    { pubkey: authority,    isSigner: true,  isWritable: false },
  ];

  // Session PDA is optional — must come BEFORE player_wallet to match Anchor struct
  if (sessionPda) {
    keys.push({ pubkey: sessionPda, isSigner: false, isWritable: false });
  }
  keys.push({ pubkey: playerWallet, isSigner: false, isWritable: false });

  return new TransactionInstruction({
    programId: getProgramId(),
    keys,
    data,
  });
}

export function buildSettleRaceIx(racePda: PublicKey): TransactionInstruction {
  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [{ pubkey: racePda, isSigner: false, isWritable: true }],
    data: DISC.settle_race,
  });
}

/**
 * Build claim_prize instruction.
 * winner_wallet is always the real wallet (funds destination).
 * authority can be the session key, with sessionPda provided.
 */
export function buildClaimPrizeIx(
  racePda: PublicKey,
  authority: PublicKey,    // session key OR winner wallet
  winnerWallet: PublicKey, // always the real wallet (receives lamports)
  sessionPda: PublicKey | null,
): TransactionInstruction {
  // Anchor account order: race, authority, session (optional), winner_wallet
  const keys: { pubkey: PublicKey; isSigner: boolean; isWritable: boolean }[] = [
    { pubkey: racePda,      isSigner: false, isWritable: true  },
    { pubkey: authority,    isSigner: true,  isWritable: false },
  ];

  // Session PDA is optional — must come BEFORE winner_wallet to match Anchor struct
  if (sessionPda) {
    keys.push({ pubkey: sessionPda, isSigner: false, isWritable: false });
  }
  keys.push({ pubkey: winnerWallet, isSigner: false, isWritable: true  });

  return new TransactionInstruction({
    programId: getProgramId(),
    keys,
    data: DISC.claim_prize,
  });
}

// Re-export for convenience
export { deriveSessionPda, raceIdHash };
