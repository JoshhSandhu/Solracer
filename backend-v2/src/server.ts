// ---------------------------------------------------------------------------
// Backend-v2  Fastify Server Entry Point
// ---------------------------------------------------------------------------
// Minimal read-only API for SolRacer track buckets.
// Reads from the same Supabase/PostgreSQL database as the oracle pipeline.
// ---------------------------------------------------------------------------

import Fastify from 'fastify';
import cors from '@fastify/cors';
import rateLimit from '@fastify/rate-limit';
import { getPool, closePool } from './db/connection';
import { healthRoutes } from './routes/health';
import { tokenRoutes } from './routes/tokens';
import { trackRoutes } from './routes/tracks';

// ─── Environment ────────────────────────────────────────────────────────────

const DATABASE_URL = requireEnv('DATABASE_URL');
const TRACK_VERSION = process.env['TRACK_VERSION'] ?? '2';
const RETENTION_HOURS = parseInt(process.env['RETENTION_HOURS'] ?? '26', 10);
const PORT = parseInt(process.env['PORT'] ?? '3000', 10);

if (isNaN(RETENTION_HOURS) || RETENTION_HOURS <= 0) {
  throw new Error('RETENTION_HOURS must be a positive integer.');
}

if (isNaN(PORT) || PORT <= 0) {
  throw new Error('PORT must be a positive integer.');
}

// ─── Server ─────────────────────────────────────────────────────────────────

async function main(): Promise<void> {
  const fastify = Fastify({ logger: true });

  // ── CORS ──────────────────────────────────────────────────────────────
  await fastify.register(cors, {
    origin: '*',
    methods: ['GET'],
  });

  // ── Rate Limiting ─────────────────────────────────────────────────────
  await fastify.register(rateLimit, {
    max: 100,
    timeWindow: '1 minute',
    allowList: (_req, _key) => {
      // Skip rate limiting for health checks
      return _req.url === '/health';
    },
  });

  // ── Database ──────────────────────────────────────────────────────────
  const pool = getPool(DATABASE_URL);

  // ── Global Error Handler ─────────────────────────────────────────────
  fastify.setErrorHandler((error, request, reply) => {
    request.log.error(error);
    const err = error as { statusCode?: number; message?: string };
    const statusCode = err.statusCode ?? 500;
    reply.status(statusCode).send({
      error: statusCode < 500 ? err.message : 'Internal server error',
    });
  });

  // ── Routes ────────────────────────────────────────────────────────────
  await fastify.register(healthRoutes, { pool });
  await fastify.register(tokenRoutes, { pool });
  await fastify.register(trackRoutes, { pool, retentionHours: RETENTION_HOURS, trackVersion: TRACK_VERSION });

  // ── Start ─────────────────────────────────────────────────────────────
  await fastify.listen({ port: PORT, host: '0.0.0.0' });

  // ── Graceful Shutdown ─────────────────────────────────────────────────
  const shutdown = async (signal: string) => {
    fastify.log.info(`Received ${signal}, shutting down...`);
    await fastify.close();
    await closePool();
    process.exit(0);
  };

  process.on('SIGINT', () => shutdown('SIGINT'));
  process.on('SIGTERM', () => shutdown('SIGTERM'));
}

// ─── Helpers ────────────────────────────────────────────────────────────────

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Required environment variable ${name} is not set.`);
  }
  return value;
}

// ─── Run ────────────────────────────────────────────────────────────────────

process.on('unhandledRejection', (reason) => {
  console.error('Unhandled rejection:', reason);
  process.exit(1);
});

main().catch((err) => {
  console.error('Fatal error starting server:', err);
  process.exit(1);
});
