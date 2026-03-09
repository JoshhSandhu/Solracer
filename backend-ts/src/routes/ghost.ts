/**
 * Ghost relay routes  Phase 2a ER simulator.
 *
 * POST /ghost/update        → accept a position update from a race participant
 * GET  /ghost/:race_id      → return all live ghost states for the race
 *
 * These endpoints simulate the interface Unity will later have against ER:
 *   POST /ghost/update  ↔  update_position Anchor instruction → ER
 *   GET  /ghost/:id     ↔  getAccountInfo(race_player_pda) → ER RPC
 */

import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { updateGhost, getGhostsForRace } from "../store/ghost.js";
import { getRaceRaw } from "../store/memory.js";

// ---------------------------------------------------------------------------
// Request shapes
// ---------------------------------------------------------------------------

interface UpdateBody {
  race_id: string;
  wallet_address: string;
  x: number;
  y: number;
  speed: number;
  checkpoint_index: number;
  seq: number;
}

// ---------------------------------------------------------------------------
// Routes
// ---------------------------------------------------------------------------

export async function ghostRoutes(app: FastifyInstance): Promise<void> {
  // POST /ghost/update -------------------------------------------------------
  app.post(
    "/ghost/update",
    async (
      req: FastifyRequest<{ Body: UpdateBody }>,
      reply: FastifyReply,
    ) => {
      const { race_id, wallet_address, x, y, speed, checkpoint_index, seq } = req.body;

      // --- Input validation -------------------------------------------------
      if (!race_id || !wallet_address) {
        return reply.status(400).send({ detail: "race_id and wallet_address are required" });
      }
      if (typeof x !== "number" || typeof y !== "number" || typeof speed !== "number") {
        return reply.status(400).send({ detail: "x, y, speed must be numbers" });
      }
      if (typeof checkpoint_index !== "number" || typeof seq !== "number") {
        return reply.status(400).send({ detail: "checkpoint_index and seq must be numbers" });
      }

      // --- Wallet must be a participant in this race ------------------------
      const race = getRaceRaw(race_id);
      if (!race) {
        return reply.status(404).send({ detail: `Race ${race_id} not found` });
      }
      const isParticipant =
        race.player1_wallet === wallet_address ||
        race.player2_wallet === wallet_address;
      if (!isParticipant) {
        return reply
          .status(403)
          .send({ detail: "wallet_address is not a participant in this race" });
      }

      // --- Attempt ghost update (throttle + seq enforced inside store) ------
      const result = updateGhost(race_id, wallet_address, x, y, speed, checkpoint_index, seq);

      if (!result.accepted) {
        // Not an error  just a no-op. Return 200 so Unity doesn't retry
        return { status: "ignored", reason: result.reason };
      }

      return { status: "ok" };
    },
  );

  // GET /ghost/:race_id -------------------------------------------------------
  app.get(
    "/ghost/:race_id",
    async (
      req: FastifyRequest<{ Params: { race_id: string } }>,
      reply: FastifyReply,
    ) => {
      const { race_id } = req.params;

      const race = getRaceRaw(race_id);
      if (!race) {
        return reply.status(404).send({ detail: `Race ${race_id} not found` });
      }

      const states = getGhostsForRace(race_id);

      return {
        race_id,
        players: states.map((s) => ({
          wallet: s.wallet,
          x: s.x,
          y: s.y,
          speed: s.speed,
          checkpoint_index: s.checkpoint_index,
          updated_at: s.updated_at,
        })),
      };
    },
  );
}
