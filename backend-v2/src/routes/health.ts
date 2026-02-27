// ---------------------------------------------------------------------------
// Backend-v2  GET /health
// ---------------------------------------------------------------------------

import type { FastifyInstance } from 'fastify';
import type { Pool } from 'pg';
import type { HealthResponse } from '../types/track.types';

const startTime = Date.now();

export async function healthRoutes(
  fastify: FastifyInstance,
  opts: { pool: Pool },
): Promise<void> {
  const { pool } = opts;

  fastify.get('/health', async (_request, reply) => {
    const uptimeSeconds = Math.floor((Date.now() - startTime) / 1000);

    try {
      await pool.query('SELECT 1');
      const response: HealthResponse = {
        status: 'ok',
        database: 'connected',
        uptimeSeconds,
      };
      return reply.send(response);
    } catch (err) {
      const response: HealthResponse = {
        status: 'error',
        database: 'disconnected',
        uptimeSeconds,
      };
      return reply.status(503).send(response);
    }
  });
}
