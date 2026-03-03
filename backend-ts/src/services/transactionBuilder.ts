/**
 * Transaction building & serialisation — mirrors backend/app/services/transaction_builder.py.
 *
 * Produces the same wire-format bytes as Python's
 *   `bytes(Transaction.new_unsigned(Message.new_with_blockhash(...)))`.
 */

import { Transaction, type TransactionInstruction, type PublicKey } from "@solana/web3.js";
import { getConnection } from "./solana.js";

export async function buildTransaction(
  instructions: TransactionInstruction[],
  payer: PublicKey,
  recentBlockhash?: string,
): Promise<Transaction> {
  if (!recentBlockhash) {
    recentBlockhash = await getRecentBlockhash();
  }

  const tx = new Transaction({
    recentBlockhash,
    feePayer: payer,
  });

  for (const ix of instructions) {
    tx.add(ix);
  }

  return tx;
}

export function serializeTransaction(tx: Transaction): Buffer {
  return tx.serialize({
    requireAllSignatures: false,
    verifySignatures: false,
  });
}

export async function getRecentBlockhash(): Promise<string> {
  const connection = getConnection();
  const { blockhash } = await connection.getLatestBlockhash("confirmed");
  return blockhash;
}
