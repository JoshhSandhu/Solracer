import { Transaction } from "@solana/web3.js";
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
      // Surface ALL error details for debugging
      const logs = (err as any)?.logs ?? (err as any)?.data?.logs;
      const txErr = (err as any)?.transactionError ?? (err as any)?.data?.err;
      console.error(
        `[TXSUBMIT] Error (attempt ${attempt + 1}/${maxRetries}): ${msg}`,
      );
      if (txErr) {
        console.error(`[TXSUBMIT] Transaction error: ${JSON.stringify(txErr)}`);
      }
      if (logs && (logs as string[]).length > 0) {
        console.error(`[TXSUBMIT] Program logs:\n${(logs as string[]).join('\n')}`);
      }
      // Dump full error for hard-to-diagnose issues
      try {
        const errKeys = Object.getOwnPropertyNames(err);
        const errDump: Record<string, unknown> = {};
        for (const k of errKeys) {
          if (k !== 'stack') errDump[k] = (err as any)[k];
        }
        console.error(`[TXSUBMIT] Full error dump: ${JSON.stringify(errDump)}`);
      } catch { /* ignore serialization errors */ }

      if (/deserialize|failed to fill/i.test(msg)) break;

      if (attempt < maxRetries - 1) {
        await new Promise((r) => setTimeout(r, 1_000));
      }
    }
  }

  // Before giving up, simulate to get detailed error
  try {
    const tx = Transaction.from(signedTxBytes);
    const sim = await connection.simulateTransaction(tx);
    console.error(`[TXSUBMIT] Final simulation result: ${JSON.stringify(sim.value.err)}`);
    if (sim.value.logs) {
      console.error(`[TXSUBMIT] Final sim logs:\n${sim.value.logs.join('\n')}`);
    }
  } catch (simErr) {
    console.error(`[TXSUBMIT] Could not run final simulation: ${simErr}`);
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
