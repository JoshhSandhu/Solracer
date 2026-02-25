// ---------------------------------------------------------------------------
// Oracle Pipeline  Runtime Configuration
// ---------------------------------------------------------------------------

export interface OracleConfig {
  /** PostgreSQL connection string. */
  databaseUrl: string;

  /**
   * Comma-separated list of token mint addresses to ingest.
   * Example: "So11...1112,DezX...cPu8"
   */
  supportedTokens: string[];

  /** Rolling window retention in hours (default 26). */
  retentionHours: number;

  /**
   * Fixed number of points per normalized track.
   * Normalization must produce exactly this many Y-values.
   */
  trackPointCount: number;

  /**
   * Minute past the hour at which the cron worker fires.
   * Gives oracle feeds time to publish before we sample.
   * Default: 5 (i.e. XX:05).
   */
  oraclePollMinute: number;

  /**
   * Maximum number of hours to backfill during startup catch-up.
   * Prevents runaway loops if the server was down for weeks.
   * Default: 48.
   */
  maxCatchUpHours: number;
}

/**
 * Load configuration from environment variables.
 * Throws on missing required variables.
 */
export function loadConfig(): OracleConfig {
  const databaseUrl = requireEnv('DATABASE_URL');

  const tokensRaw = process.env['ORACLE_SUPPORTED_TOKENS'] ?? '';
  const supportedTokens = tokensRaw
    .split(',')
    .map((t) => t.trim())
    .filter((t) => t.length > 0);

  if (supportedTokens.length === 0) {
    throw new Error(
      'ORACLE_SUPPORTED_TOKENS must be set to a comma-separated list of token mints.',
    );
  }

  const retentionHours = parseInt(
    process.env['RETENTION_HOURS'] ?? '26',
    10,
  );

  if (isNaN(retentionHours) || retentionHours <= 0) {
    throw new Error('RETENTION_HOURS must be a positive integer.');
  }

  const trackPointCount = parseInt(
    process.env['TRACK_POINT_COUNT'] ?? '1000',
    10,
  );

  if (isNaN(trackPointCount) || trackPointCount <= 0) {
    throw new Error('TRACK_POINT_COUNT must be a positive integer.');
  }

  const oraclePollMinute = parseInt(
    process.env['ORACLE_POLL_MINUTE'] ?? '5',
    10,
  );

  if (isNaN(oraclePollMinute) || oraclePollMinute < 0 || oraclePollMinute > 59) {
    throw new Error('ORACLE_POLL_MINUTE must be an integer 0–59.');
  }

  const maxCatchUpHours = parseInt(
    process.env['MAX_CATCHUP_HOURS'] ?? '48',
    10,
  );

  if (isNaN(maxCatchUpHours) || maxCatchUpHours <= 0) {
    throw new Error('MAX_CATCHUP_HOURS must be a positive integer.');
  }

  return {
    databaseUrl,
    supportedTokens,
    retentionHours,
    trackPointCount,
    oraclePollMinute,
    maxCatchUpHours,
  };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Required environment variable ${name} is not set.`);
  }
  return value;
}
