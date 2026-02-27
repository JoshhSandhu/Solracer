// ---------------------------------------------------------------------------
// Oracle Pipeline PostgreSQL Connection Pool
// ---------------------------------------------------------------------------

import { Pool, type PoolConfig } from 'pg';

let pool: Pool | null = null;

/**
 * Initialise (or return existing) connection pool.
 *
 * @param databaseUrl  PostgreSQL connection string
 *                     (e.g. `postgresql://user:pass@host:5432/db`)
 */
export function getPool(databaseUrl: string): Pool {
  if (!pool) {
    const config: PoolConfig = {
      connectionString: databaseUrl,
      max: 10,
      idleTimeoutMillis: 30_000,
      connectionTimeoutMillis: 5_000,
      statement_timeout: 10_000,
    };
    pool = new Pool(config);

    pool.on('error', (err) => {
      console.error('[oracle/db] Unexpected pool error:', err.message);
    });
  }
  return pool;
}

/**
 * Gracefully shut down the pool.
 */
export async function closePool(): Promise<void> {
  if (pool) {
    await pool.end();
    pool = null;
  }
}
