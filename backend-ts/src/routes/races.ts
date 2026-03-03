/**
 * Race routes 7 endpoints matching frozen Python backend exactly.
 */

import type { FastifyInstance } from "fastify";
import {
  createRace,
  joinRaceById,
  joinRaceByCode,
  getRaceStatus,
  markReady,
  cancelRace,
  listPublicRaces,
} from "../store/memory.js";
import type {
  CreateRaceRequest,
  JoinRaceByIdRequest,
  JoinRaceByCodeRequest,
  MarkReadyRequest,
} from "../types.js";

function isError(
  result: unknown
): result is { error: string; status: number } {
  return (
    typeof result === "object" &&
    result !== null &&
    "error" in result &&
    "status" in result
  );
}

export async function raceRoutes(app: FastifyInstance): Promise<void> {
  // POST /races/create
  app.post<{ Body: CreateRaceRequest }>("/races/create", async (req, reply) => {
    const { token_mint, wallet_address, entry_fee_sol, is_private } = req.body;

    if (!token_mint || !wallet_address || entry_fee_sol === undefined) {
      return reply.status(422).send({
        detail: [{ msg: "Field required", type: "missing" }],
      });
    }

    const result = createRace(
      token_mint,
      wallet_address,
      entry_fee_sol,
      is_private ?? false
    );

    if (isError(result)) {
      return reply.status(result.status).send({ detail: result.error });
    }
    return reply.status(200).send(result);
  });

  // POST /races/:race_id/join
  app.post<{ Params: { race_id: string }; Body: JoinRaceByIdRequest }>(
    "/races/:race_id/join",
    async (req, reply) => {
      if (!req.body?.wallet_address) {
        return reply.status(422).send({
          detail: [{ msg: "Field required", type: "missing", loc: "wallet_address" }],
        });
      }
      const result = joinRaceById(req.params.race_id, req.body.wallet_address);
      if (isError(result)) {
        return reply.status(result.status).send({ detail: result.error });
      }
      return reply.status(200).send(result);
    }
  );

  // POST /races/join-by-code
  app.post<{ Body: JoinRaceByCodeRequest }>(
    "/races/join-by-code",
    async (req, reply) => {
      const { join_code, wallet_address } = req.body;

      if (!join_code || join_code.length !== 6) {
        return reply.status(422).send({
          detail: [{ msg: "join_code must be exactly 6 characters", type: "value_error" }],
        });
      }

      const result = joinRaceByCode(join_code, wallet_address);
      if (isError(result)) {
        return reply.status(result.status).send({ detail: result.error });
      }
      return reply.status(200).send(result);
    }
  );

  // GET /races/public
  app.get<{
    Querystring: { token_mint?: string; entry_fee?: string };
  }>("/races/public", async (req, reply) => {
    const tokenMint = req.query.token_mint;
    const entryFee = req.query.entry_fee
      ? parseFloat(req.query.entry_fee)
      : undefined;

    const result = listPublicRaces(tokenMint, entryFee);
    return reply.status(200).send(result);
  });

  // GET /races/:race_id/status
  app.get<{ Params: { race_id: string } }>(
    "/races/:race_id/status",
    async (req, reply) => {
      const result = getRaceStatus(req.params.race_id);
      if (isError(result)) {
        return reply.status(result.status).send({ detail: result.error });
      }
      return reply.status(200).send(result);
    }
  );

  // POST /races/:race_id/ready
  app.post<{ Params: { race_id: string }; Body: MarkReadyRequest }>(
    "/races/:race_id/ready",
    async (req, reply) => {
      const result = markReady(req.params.race_id, req.body.wallet_address);
      if (isError(result)) {
        return reply.status(result.status).send({ detail: result.error });
      }
      return reply.status(200).send(result);
    }
  );

  // DELETE /races/:race_id
  app.delete<{
    Params: { race_id: string };
    Querystring: { wallet_address?: string };
  }>("/races/:race_id", async (req, reply) => {
    if (!req.query.wallet_address) {
      return reply.status(422).send({
        detail: [{ msg: "Field required", type: "missing", loc: "wallet_address" }],
      });
    }
    const result = cancelRace(
      req.params.race_id,
      req.query.wallet_address
    );
    if (isError(result)) {
      return reply.status(result.status).send({ detail: result.error });
    }
    return reply.status(200).send(result);
  });
}
