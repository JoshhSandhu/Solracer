/**
 * Instruction builders — mirrors backend/app/services/program_client.py.
 *
 * Discriminators, account order, and serialisation layout taken directly
 * from the IDL (backend/app/idl/solracer_program.json).
 */

import { PublicKey, TransactionInstruction, SystemProgram } from "@solana/web3.js";
import { getProgramId } from "./pda.js";

const DISC = {
  create_race:   Buffer.from([233, 107, 148, 159, 241, 155, 226, 54]),
  join_race:     Buffer.from([207,  91, 222,  84, 249, 246, 229, 54]),
  submit_result: Buffer.from([240,  42,  89, 180,  10, 239,   9, 214]),
  settle_race:   Buffer.from([172,  32,  72, 212, 155,  33, 161, 237]),
  claim_prize:   Buffer.from([157, 233, 139, 121, 246,  62, 234, 235]),
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
      { pubkey: player1, isSigner: true, isWritable: true },
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
      { pubkey: player2, isSigner: true, isWritable: true },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data: DISC.join_race,
  });
}

export function buildSubmitResultIx(
  racePda: PublicKey,
  player: PublicKey,
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

  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: racePda, isSigner: false, isWritable: true },
      { pubkey: player, isSigner: true, isWritable: false },
    ],
    data,
  });
}

export function buildSettleRaceIx(
  racePda: PublicKey,
): TransactionInstruction {
  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: racePda, isSigner: false, isWritable: true },
    ],
    data: DISC.settle_race,
  });
}

export function buildClaimPrizeIx(
  racePda: PublicKey,
  winner: PublicKey,
): TransactionInstruction {
  return new TransactionInstruction({
    programId: getProgramId(),
    keys: [
      { pubkey: racePda, isSigner: false, isWritable: true },
      { pubkey: winner, isSigner: true, isWritable: true },
    ],
    data: DISC.claim_prize,
  });
}
