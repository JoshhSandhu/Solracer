// ---------------------------------------------------------------------------
// Oracle Pipeline  Entry Point & Barrel Exports
// ---------------------------------------------------------------------------
// Public API surface for the oracle module.
// No HTTP routes  internal functions only.
// ---------------------------------------------------------------------------

// Types
export type {
  OracleTick,
  OracleTickInput,
  TrackBucket,
  TrackBucketInput,
  NormalizationMeta,
  OraclePriceData,
  PlayableTrackBucket,
} from './types/oracle.types';

// Config
export { loadConfig } from './config';
export type { OracleConfig } from './config';

// Constants
export { TRACK_VERSION, TICK_INTERVAL_MS, MAX_DELTA_PER_STEP, MIN_TICKS_FOR_TRACK, TRACK_GEN_BUFFER_MINUTES, ONE_HOUR_MS } from './constants';

// Utilities
export { floorToHour, currentHourUTC } from './utils/time';

// Database
export { getPool, closePool } from './db/connection';
export {
  storeOracleTick,
  getTicksForHour,
  getLatestTickTime,
  storeTrackBucket,
  trackBucketExists,
  getPlayableTrackBuckets,
  deleteExpiredData,
} from './db/repository';

// Services
export { fetchOraclePrice, fetchOraclePricesBatch } from './services/oracle-fetcher';
export { generateTrackBucket } from './services/track-generator';
export { FEED_REGISTRY, MAGICBLOCK_RPC_URL, PYTH_LAZER_PRICE_OFFSET } from './services/oracle-config';
export type { FeedEntry } from './services/oracle-config';

// Workers
export { startTickWorker, stopTickWorker } from './workers/oracleIngestionWorker';

// ---------------------------------------------------------------------------
// Bootstrap (run directly with ts-node or node)
// ---------------------------------------------------------------------------

async function main(): Promise<void> {
  const { loadConfig: load } = await import('./config');
  const { getPool: pool } = await import('./db/connection');
  const { startTickWorker: start } = await import(
    './workers/oracleIngestionWorker'
  );
  const { fetchOraclePrice } = await import('./services/oracle-fetcher');

  console.log('[oracle] Initialising oracle pipeline (tick mode)...');

  const config = load();
  const dbPool = pool(config.databaseUrl);

  // Startup health checks — fail fast on misconfiguration
  console.log('[oracle] Running startup health checks...');

  try {
    await dbPool.query('SELECT 1');
    console.log('[oracle] ✓ Database reachable');
  } catch (err) {
    console.error('[oracle] ✗ Database unreachable:', err);
    process.exit(1);
  }

  try {
    await fetchOraclePrice(config.supportedTokens[0]);
    console.log('[oracle] ✓ MagicBlock RPC reachable');
  } catch (err) {
    console.error('[oracle] ✗ MagicBlock RPC unreachable:', err);
    process.exit(1);
  }

  start(dbPool, config);

  console.log('[oracle] Pipeline running (2s tick interval). Press Ctrl+C to stop.');

  // Graceful shutdown
  const shutdown = async (): Promise<void> => {
    console.log('\n[oracle] Shutting down...');
    const { stopTickWorker: stop } = await import('./workers/oracleIngestionWorker');
    const { closePool: close } = await import('./db/connection');
    stop();
    await close();
    process.exit(0);
  };

  process.on('SIGINT', () => void shutdown());
  process.on('SIGTERM', () => void shutdown());
}

// Auto-run if executed directly (not imported as a library)
const isDirectRun =
  require.main === module ||
  process.argv[1]?.endsWith('index.ts') ||
  process.argv[1]?.endsWith('index.js');

if (isDirectRun) {
  main().catch((err) => {
    console.error('[oracle] Fatal error:', err);
    process.exit(1);
  });
}
