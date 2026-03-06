/**
 * Transaction routes  drop-in replacement for the Python
 * backend/app/api/routes/solana_transactions.py endpoints.
 *
 * POST /transactions/build   → build unsigned Solana TX (base64)
 * POST /transactions/submit  → relay signed TX to Solana RPC
 */

import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { PublicKey } from "@solana/web3.js";
import crypto from "node:crypto";
import { deriveRacePda } from "../services/pda.js";
import {
  buildCreateRaceIx,
  buildJoinRaceIx,
  buildSubmitResultIx,
  buildSettleRaceIx,
  buildClaimPrizeIx,
} from "../services/program.js";
import {
  buildTransaction,
  serializeTransaction,
  getRecentBlockhash,
} from "../services/transactionBuilder.js";
import { submitTransaction, confirmTransaction } from "../services/submitter.js";
import { setRaceMeta, getRaceMeta } from "../services/raceMetaCache.js";
import { registerRaceFromChain, submitRaceResult } from "../store/memory.js";

// ---------------------------------------------------------------------------
// Request / response shapes (matching Python schemas.py exactly)
// ---------------------------------------------------------------------------

interface BuildBody {
  instruction_type: string;
  wallet_address: string;
  race_id?: string;
  token_mint?: string;
  entry_fee_sol?: number;
  finish_time_ms?: number;
  coins_collected?: number;
  input_hash?: string;
}

interface SubmitBody {
  signed_transaction_bytes: string;
  instruction_type: string;
  race_id?: string;
  token_mint?: string;
  entry_fee_sol?: number;
  wallet_address?: string;
  finish_time_ms?: number;
  coins_collected?: number;
  input_hash?: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function generateRaceId(
  tokenMint: string,
  entryFee: number,
  player1: string,
): string {
  const timestamp = process.hrtime.bigint().toString();
  const seed = `${tokenMint}_${entryFee}_${player1}_${timestamp}`;
  return crypto.createHash("sha256").update(seed).digest("hex").slice(0, 32);
}

function resolveRaceMeta(
  raceId: string,
  body: BuildBody,
): { tokenMint: string; entryFeeSol: number } | null {
  const cached = getRaceMeta(raceId);
  if (cached) return { tokenMint: cached.token_mint, entryFeeSol: cached.entry_fee_sol };

  if (body.token_mint && body.entry_fee_sol) {
    setRaceMeta(raceId, { token_mint: body.token_mint, entry_fee_sol: body.entry_fee_sol });
    return { tokenMint: body.token_mint, entryFeeSol: body.entry_fee_sol };
  }

  return null;
}

const VALID_TYPES = ["create_race", "join_race", "submit_result", "settle_race", "claim_prize"];

// ---------------------------------------------------------------------------
// Routes
// ---------------------------------------------------------------------------

export async function transactionRoutes(app: FastifyInstance): Promise<void> {
  // POST /transactions/build --------------------------------------------------
  app.post(
    "/transactions/build",
    async (
      req: FastifyRequest<{ Body: BuildBody }>,
      reply: FastifyReply,
    ) => {
      const body = req.body;
      const {
        instruction_type,
        wallet_address,
      } = body;

      if (!VALID_TYPES.includes(instruction_type)) {
        return reply.status(400).send({
          detail: `Unknown instruction_type: ${instruction_type}. Valid types: ${VALID_TYPES.join(", ")}`,
        });
      }

      try {
        let walletPubkey: PublicKey;
        try {
          walletPubkey = new PublicKey(wallet_address);
        } catch {
          return reply
            .status(400)
            .send({ detail: "Invalid wallet_address format" });
        }

        // --- create_race ---------------------------------------------------
        if (instruction_type === "create_race") {
          const { token_mint, entry_fee_sol } = body;

          if (!token_mint || !entry_fee_sol) {
            return reply.status(400).send({
              detail: "token_mint and entry_fee_sol required for create_race",
            });
          }

          const raceId = generateRaceId(token_mint, entry_fee_sol, wallet_address);
          const tokenMintPk = new PublicKey(token_mint);
          const lamports = BigInt(Math.round(entry_fee_sol * 1_000_000_000));

          const [racePda] = deriveRacePda(raceId, tokenMintPk, lamports);
          const ix = buildCreateRaceIx(racePda, walletPubkey, raceId, tokenMintPk, lamports);

          const recentBlockhash = await getRecentBlockhash();
          const tx = await buildTransaction([ix], walletPubkey, recentBlockhash);
          const txBase64 = serializeTransaction(tx).toString("base64");

          setRaceMeta(raceId, { token_mint, entry_fee_sol });

          app.log.info(`[TXBUILD] create_race raceId=${raceId} pda=${racePda.toBase58()}`);

          return {
            transaction_bytes: txBase64,
            instruction_type: "create_race",
            race_id: raceId,
            race_pda: racePda.toBase58(),
            recent_blockhash: recentBlockhash,
          };
        }

        // --- join_race -----------------------------------------------------
        if (instruction_type === "join_race") {
          const { race_id } = body;
          if (!race_id) {
            return reply.status(400).send({ detail: "race_id required for join_race" });
          }

          const meta = resolveRaceMeta(race_id, body);
          if (!meta) {
            return reply.status(404).send({ detail: "Race not found. Provide token_mint and entry_fee_sol or create the race first." });
          }

          const tokenMintPk = new PublicKey(meta.tokenMint);
          const lamports = BigInt(Math.round(meta.entryFeeSol * 1_000_000_000));
          const [racePda] = deriveRacePda(race_id, tokenMintPk, lamports);

          const ix = buildJoinRaceIx(racePda, walletPubkey);

          const recentBlockhash = await getRecentBlockhash();
          const tx = await buildTransaction([ix], walletPubkey, recentBlockhash);
          const txBase64 = serializeTransaction(tx).toString("base64");

          app.log.info(`[TXBUILD] join_race raceId=${race_id} pda=${racePda.toBase58()}`);

          return {
            transaction_bytes: txBase64,
            instruction_type: "join_race",
            race_id,
            race_pda: racePda.toBase58(),
            recent_blockhash: recentBlockhash,
          };
        }

        // --- submit_result -------------------------------------------------
        if (instruction_type === "submit_result") {
          const { race_id, finish_time_ms, input_hash } = body;

          if (!race_id || finish_time_ms == null || !input_hash) {
            return reply.status(400).send({
              detail: "race_id, finish_time_ms, and input_hash required for submit_result",
            });
          }

          const meta = resolveRaceMeta(race_id, body);
          if (!meta) {
            return reply.status(404).send({ detail: "Race not found. Provide token_mint and entry_fee_sol or create the race first." });
          }

          const tokenMintPk = new PublicKey(meta.tokenMint);
          const lamports = BigInt(Math.round(meta.entryFeeSol * 1_000_000_000));
          const [racePda] = deriveRacePda(race_id, tokenMintPk, lamports);

          const inputHashBytes = Buffer.from(input_hash, "hex");
          if (inputHashBytes.length !== 32) {
            return reply
              .status(400)
              .send({ detail: "input_hash must be 32 bytes (64 hex characters)" });
          }

          const ix = buildSubmitResultIx(
            racePda,
            walletPubkey,
            BigInt(finish_time_ms),
            BigInt(body.coins_collected ?? 0),
            inputHashBytes,
          );

          const recentBlockhash = await getRecentBlockhash();
          const tx = await buildTransaction([ix], walletPubkey, recentBlockhash);
          const txBase64 = serializeTransaction(tx).toString("base64");

          app.log.info(`[TXBUILD] submit_result raceId=${race_id} pda=${racePda.toBase58()}`);

          return {
            transaction_bytes: txBase64,
            instruction_type: "submit_result",
            race_id,
            race_pda: racePda.toBase58(),
            recent_blockhash: recentBlockhash,
          };
        }

        // --- settle_race ---------------------------------------------------
        if (instruction_type === "settle_race") {
          const { race_id } = body;
          if (!race_id) {
            return reply.status(400).send({ detail: "race_id required for settle_race" });
          }

          const meta = resolveRaceMeta(race_id, body);
          if (!meta) {
            return reply.status(404).send({ detail: "Race not found. Provide token_mint and entry_fee_sol or create the race first." });
          }

          const tokenMintPk = new PublicKey(meta.tokenMint);
          const lamports = BigInt(Math.round(meta.entryFeeSol * 1_000_000_000));
          const [racePda] = deriveRacePda(race_id, tokenMintPk, lamports);

          const ix = buildSettleRaceIx(racePda);

          const recentBlockhash = await getRecentBlockhash();
          const tx = await buildTransaction([ix], walletPubkey, recentBlockhash);
          const txBase64 = serializeTransaction(tx).toString("base64");

          app.log.info(`[TXBUILD] settle_race raceId=${race_id} pda=${racePda.toBase58()}`);

          return {
            transaction_bytes: txBase64,
            instruction_type: "settle_race",
            race_id,
            race_pda: racePda.toBase58(),
            recent_blockhash: recentBlockhash,
          };
        }

        // --- claim_prize ---------------------------------------------------
        if (instruction_type === "claim_prize") {
          const { race_id } = body;
          if (!race_id) {
            return reply.status(400).send({ detail: "race_id required for claim_prize" });
          }

          const meta = resolveRaceMeta(race_id, body);
          if (!meta) {
            return reply.status(404).send({ detail: "Race not found. Provide token_mint and entry_fee_sol or create the race first." });
          }

          const tokenMintPk = new PublicKey(meta.tokenMint);
          const lamports = BigInt(Math.round(meta.entryFeeSol * 1_000_000_000));
          const [racePda] = deriveRacePda(race_id, tokenMintPk, lamports);

          const ix = buildClaimPrizeIx(racePda, walletPubkey);

          const recentBlockhash = await getRecentBlockhash();
          const tx = await buildTransaction([ix], walletPubkey, recentBlockhash);
          const txBase64 = serializeTransaction(tx).toString("base64");

          app.log.info(`[TXBUILD] claim_prize raceId=${race_id} pda=${racePda.toBase58()}`);

          return {
            transaction_bytes: txBase64,
            instruction_type: "claim_prize",
            race_id,
            race_pda: racePda.toBase58(),
            recent_blockhash: recentBlockhash,
          };
        }
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        app.log.error(`[TXBUILD] Error: ${msg}`);
        return reply
          .status(500)
          .send({ detail: `Error building transaction: ${msg}` });
      }
    },
  );

  // POST /transactions/submit -------------------------------------------------
  app.post(
    "/transactions/submit",
    async (
      req: FastifyRequest<{ Body: SubmitBody }>,
      reply: FastifyReply,
    ) => {
      const { signed_transaction_bytes, instruction_type, race_id } = req.body;

      if (!signed_transaction_bytes) {
        return reply
          .status(400)
          .send({ detail: "signed_transaction_bytes is required" });
      }

      try {
        let txBytes: Buffer;
        try {
          txBytes = Buffer.from(signed_transaction_bytes, "base64");
        } catch {
          return reply.status(400).send({ detail: "Invalid base64 encoding" });
        }

        if (txBytes.length === 0) {
          return reply
            .status(400)
            .send({ detail: "Transaction bytes are empty" });
        }

        if (txBytes.length === 64) {
          return reply.status(400).send({
            detail:
              "Received signature (64 bytes) instead of signed transaction. The client must sign the full transaction.",
          });
        }

        if (txBytes.length < 100) {
          app.log.warn(
            `[TXSUBMIT] Transaction bytes unusually small (${txBytes.length} bytes)`,
          );
        }

        app.log.info(
          `[TXSUBMIT] Submitting ${txBytes.length} bytes, type=${instruction_type}, race=${race_id ?? "none"}`,
        );

        const signature = await submitTransaction(txBytes);
        const confirmed = await confirmTransaction(signature, 10);

        app.log.info(
          `[TXSUBMIT] signature=${signature} confirmed=${confirmed}`,
        );

        // After a confirmed create_race, register in the lobby store
        if (confirmed && instruction_type === "create_race" && race_id) {
          const meta = getRaceMeta(race_id);
          if (meta) {
            const regResult = registerRaceFromChain(
              race_id,
              meta.token_mint,
              req.body.wallet_address ?? "",
              meta.entry_fee_sol,
              signature,
            );
            app.log.info(`[TXSUBMIT] Registered race ${race_id} in lobby store`);
          }
        }

        // After a confirmed submit_result, record in the memory store
        if (confirmed && instruction_type === "submit_result" && race_id) {
          const result = submitRaceResult(
            race_id,
            req.body.wallet_address ?? "",
            req.body.finish_time_ms ?? 0,
            req.body.coins_collected ?? 0,
            signature,
          );
          if ("message" in result) {
            app.log.info(`[TXSUBMIT] ${result.message} settled=${result.settled}`);
          } else {
            app.log.warn(`[TXSUBMIT] Result submission issue: ${result.error}`);
          }
        }

        return {
          transaction_signature: signature,
          instruction_type: instruction_type ?? "",
          race_id: race_id ?? null,
          confirmed,
        };
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        app.log.error(`[TXSUBMIT] Error: ${msg}`);
        return reply
          .status(500)
          .send({ detail: `Error submitting transaction: ${msg}` });
      }
    },
  );
}
