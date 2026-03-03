/**
 * Payout routes — provides payout status and processing endpoints
 * matching what Unity's PayoutAPIClient expects.
 *
 * GET  /payouts/:race_id                 → payout status
 * POST /payouts/:race_id/process         → process payout (stub)
 * GET  /payouts/:race_id/settle-transaction → settle TX (stub)
 * POST /payouts/:race_id/retry           → retry failed payout (stub)
 */

import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { getPayoutForRace } from "../store/memory.js";

export async function payoutRoutes(app: FastifyInstance): Promise<void> {
  // GET /payouts/:race_id — payout status
  app.get(
    "/payouts/:race_id",
    async (
      req: FastifyRequest<{ Params: { race_id: string } }>,
      reply: FastifyReply,
    ) => {
      const { race_id } = req.params;
      const payout = getPayoutForRace(race_id);

      if (!payout) {
        return reply.status(404).send({ detail: `Payout not found for race ${race_id}` });
      }

      return {
        payout_id: payout.payout_id,
        race_id: payout.race_id,
        winner_wallet: payout.winner_wallet,
        prize_amount_sol: payout.prize_amount_sol,
        token_mint: payout.token_mint,
        token_amount: null,
        swap_status: payout.swap_status,
        swap_tx_signature: null,
        transfer_tx_signature: null,
        fallback_sol_amount: null,
        fallback_tx_signature: null,
        error_message: null,
        created_at: payout.created_at,
        swap_started_at: null,
        completed_at: payout.completed_at,
      };
    },
  );

  // POST /payouts/:race_id/process — process payout (stub: marks as paid)
  app.post(
    "/payouts/:race_id/process",
    async (
      req: FastifyRequest<{ Params: { race_id: string } }>,
      reply: FastifyReply,
    ) => {
      const { race_id } = req.params;
      const payout = getPayoutForRace(race_id);

      if (!payout) {
        return reply.status(404).send({ detail: `Payout not found for race ${race_id}` });
      }

      // In a full implementation, this would build a claim_prize transaction.
      // For now, mark as paid stub.
      payout.swap_status = "paid";
      payout.completed_at = new Date().toISOString().replace("Z", "").split(".")[0];

      return {
        status: "completed",
        payout_id: payout.payout_id,
        transaction: null,
        swap_transaction: null,
        method: "claim_prize",
        amount_sol: payout.prize_amount_sol,
        amount_tokens: null,
        error: null,
      };
    },
  );

  // GET /payouts/:race_id/settle-transaction — settle TX (stub)
  app.get(
    "/payouts/:race_id/settle-transaction",
    async (
      req: FastifyRequest<{
        Params: { race_id: string };
        Querystring: { wallet_address?: string };
      }>,
      reply: FastifyReply,
    ) => {
      const { race_id } = req.params;
      const payout = getPayoutForRace(race_id);

      if (!payout) {
        return reply
          .status(400)
          .send({ detail: `Race ${race_id} does not need on-chain settlement or not found` });
      }

      // Stub — in a full implementation this would build a settle_race IX
      return reply.status(400).send({
        detail: "Settlement not needed — race already settled in memory",
      });
    },
  );

  // POST /payouts/:race_id/retry — retry failed payout (stub)
  app.post(
    "/payouts/:race_id/retry",
    async (
      req: FastifyRequest<{ Params: { race_id: string } }>,
      reply: FastifyReply,
    ) => {
      const { race_id } = req.params;
      const payout = getPayoutForRace(race_id);

      if (!payout) {
        return reply.status(404).send({ detail: `Payout not found for race ${race_id}` });
      }

      if (payout.swap_status !== "failed" && payout.swap_status !== "pending") {
        return reply
          .status(400)
          .send({ detail: `Payout cannot be retried. Current status: ${payout.swap_status}` });
      }

      payout.swap_status = "pending";

      return {
        status: "processing",
        payout_id: payout.payout_id,
        transaction: null,
        swap_transaction: null,
        method: "claim_prize",
        amount_sol: payout.prize_amount_sol,
        amount_tokens: null,
        error: null,
      };
    },
  );
}
