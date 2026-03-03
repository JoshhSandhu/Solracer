/**
 * Transaction submission & confirmation — mirrors backend/app/services/transaction_submitter.py.
 */

import { getConnection } from "./solana.js";

export async function submitTransaction(
  signedTxBytes: Buffer,
  maxRetries = 3,
): Promise<string> {
  const connection = getConnection();

  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      const signature = await connection.sendRawTransaction(signedTxBytes, {
        skipPreflight: false,
        preflightCommitment: "confirmed",
        maxRetries: 0,
      });

      console.log(`[TXSUBMIT] signature=${signature}`);
      return signature;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error(
        `[TXSUBMIT] Error (attempt ${attempt + 1}/${maxRetries}): ${msg}`,
      );

      if (/deserialize|failed to fill/i.test(msg)) break;

      if (attempt < maxRetries - 1) {
        await new Promise((r) => setTimeout(r, 1_000));
      }
    }
  }

  throw new Error("Failed to submit transaction after retries");
}

export async function confirmTransaction(
  signature: string,
  timeoutSeconds = 30,
): Promise<boolean> {
  const connection = getConnection();
  try {
    const { blockhash, lastValidBlockHeight } =
      await connection.getLatestBlockhash("confirmed");

    const result = await connection.confirmTransaction(
      { signature, blockhash, lastValidBlockHeight },
      "confirmed",
    );

    return !result.value.err;
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : String(err);
    console.error(`[TXSUBMIT] Confirmation error: ${msg}`);
    return false;
  }
}
