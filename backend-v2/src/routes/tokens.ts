// ---------------------------------------------------------------------------
// Backend-v2  GET /tokens
// ---------------------------------------------------------------------------

import type { FastifyInstance } from 'fastify';
import type { Pool } from 'pg';

export async function tokenRoutes(
  fastify: FastifyInstance,
  opts: { pool: Pool },
): Promise<void> {
  const { pool } = opts;

  fastify.get('/tokens', async (_request, reply) => {
    const sql = `
      SELECT DISTINCT token_mint
      FROM track_buckets
      ORDER BY token_mint
    `;

    const result = await pool.query(sql);
    const tokens = result.rows.map((row) => row.token_mint as string);

    reply.header('Cache-Control', 'public, max-age=300');
    return reply.send(tokens);
  });
}
