// ---------------------------------------------------------------------------
// Oracle Pipeline  Entry Point & Barrel Exports
// ---------------------------------------------------------------------------
// Public API surface for the oracle module.
// No HTTP routes  internal functions only.
// ---------------------------------------------------------------------------

// Types
export type {
  OracleHourlyPoint,
  OraclePointInput,
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
export { TRACK_VERSION, DEFAULT_SLOPE_CLAMP_DEGREES, ONE_HOUR_MS } from './constants';

// Utilities
export { floorToHour, currentHourUTC } from './utils/time';

// Database
export { getPool, closePool } from './db/connection';
export {
  storeOraclePoint,
  getOraclePointsForHour,
  getLatestOracleHour,
  storeTrackBucket,
  getPlayableTrackBuckets,
  deleteExpiredData,
} from './db/repository';

// Services
export { fetchOraclePrice } from './services/oracle-fetcher';
export { generateTrackBucket } from './services/track-generator';

// Workers
export { startIngestionWorker, computeCatchUpStartHour } from './workers/oracleIngestionWorker';

// ---------------------------------------------------------------------------
// Bootstrap (run directly with ts-node or node)
// ---------------------------------------------------------------------------

async function main(): Promise<void> {
  const { loadConfig: load } = await import('./config');
  const { getPool: pool } = await import('./db/connection');
  const { startIngestionWorker: start } = await import(
    './workers/oracleIngestionWorker'
  );

  console.log('[oracle] Initialising oracle pipeline...');

  const config = load();
  const dbPool = pool(config.databaseUrl);

  start(dbPool, config);

  console.log('[oracle] Pipeline running. Press Ctrl+C to stop.');

  // Graceful shutdown
  const shutdown = async (): Promise<void> => {
    console.log('\n[oracle] Shutting down...');
    const { closePool: close } = await import('./db/connection');
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
