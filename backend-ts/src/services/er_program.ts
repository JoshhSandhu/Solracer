/**
 * Instruction builders for the solracer-er MagicBlock Ephemeral Rollup program.
 * Program ID: 3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG
 *
 * These two instructions are bundled into the same transaction as create_race / join_race
 * so the player only ever sees ONE wallet popup.
 */

import { PublicKey, TransactionInstruction, SystemProgram } from "@solana/web3.js";
import {
  delegationRecordPdaFromDelegatedAccount,
  delegationMetadataPdaFromDelegatedAccount,
  delegateBufferPdaFromDelegatedAccountAndOwnerProgram,
  DELEGATION_PROGRAM_ID,
} from "@magicblock-labs/ephemeral-rollups-sdk";
import { ER_PROGRAM_ID, raceIdHash } from "./pda.js";

// Anchor discriminators: SHA-256("global:<name>")[0..8]
const ER_DISC = {
  // init_position_pda
  init_position_pda:     Buffer.from([156, 180,   7,  67, 110,   6,  42,  76]),
  // delegate_position_pda
  delegate_position_pda: Buffer.from([ 96, 130, 182, 118, 206, 106, 123,  30]),
} as const;

/**
 * Build the init_position_pda instruction.
 * Creates the PlayerPosition PDA on base devnet.
 * Must be called before delegate_position_pda.
 *
 * @param positionPda  - Derived with derivePlayerPositionPda(raceId, playerWallet)
 * @param playerWallet - The signing wallet (pays rent)
 * @param raceId       - Race ID string (SHA-256 hashed internally)
 * @param sessionKey   - The ephemeral session key that will sign ER updates
 */
export function buildInitPositionPdaIx(
  positionPda: PublicKey,
  playerWallet: PublicKey,
  raceId: string,
  sessionKey: PublicKey,
): TransactionInstruction {
  const hash = raceIdHash(raceId); // [u8; 32]

  const data = Buffer.concat([
    ER_DISC.init_position_pda,
    hash,                    // race_id_hash: [u8; 32]
    sessionKey.toBuffer(),   // session_key:  Pubkey [u8; 32]
  ]);

  return new TransactionInstruction({
    programId: ER_PROGRAM_ID,
    keys: [
      { pubkey: positionPda,             isSigner: false, isWritable: true  },
      { pubkey: playerWallet,            isSigner: true,  isWritable: true  },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data,
  });
}

/**
 * Build the delegate_position_pda instruction.
 * Delegates the PlayerPosition PDA to the MagicBlock Ephemeral Rollup.
 * After this, update_position calls go to the ER RPC, not base devnet.
 *
 * @param positionPda  - The PlayerPosition PDA (must be initialised first)
 * @param playerWallet - The signing wallet
 */
export function buildDelegatePositionPdaIx(
  positionPda: PublicKey,
  playerWallet: PublicKey,
): TransactionInstruction {
  const buffer            = delegateBufferPdaFromDelegatedAccountAndOwnerProgram(positionPda, ER_PROGRAM_ID);
  const delegationRecord   = delegationRecordPdaFromDelegatedAccount(positionPda);
  const delegationMetadata = delegationMetadataPdaFromDelegatedAccount(positionPda);
  const delegationProgram  = new PublicKey(DELEGATION_PROGRAM_ID);

  return new TransactionInstruction({
    programId: ER_PROGRAM_ID,
    keys: [
      { pubkey: playerWallet,       isSigner: true,  isWritable: true  },
      { pubkey: positionPda,        isSigner: false, isWritable: true  },
      { pubkey: buffer,             isSigner: false, isWritable: true  },
      { pubkey: delegationRecord,   isSigner: false, isWritable: true  },
      { pubkey: delegationMetadata, isSigner: false, isWritable: true  },
      { pubkey: delegationProgram,  isSigner: false, isWritable: false },
      { pubkey: ER_PROGRAM_ID,      isSigner: false, isWritable: false },
      { pubkey: SystemProgram.programId, isSigner: false, isWritable: false },
    ],
    data: ER_DISC.delegate_position_pda,
  });
}
