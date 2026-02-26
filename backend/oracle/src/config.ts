// ---------------------------------------------------------------------------
// Oracle Pipeline  Runtime Configuration
// ---------------------------------------------------------------------------

import { FEED_REGISTRY } from './services/oracle-config';

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

  for (const token of supportedTokens) {
    if (!FEED_REGISTRY[token]) {
      throw new Error(
        `Token "${token}" in ORACLE_SUPPORTED_TOKENS has no entry in FEED_REGISTRY. ` +
        `Available: ${Object.keys(FEED_REGISTRY).join(', ')}`,
      );
    }
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

  return {
    databaseUrl,
    supportedTokens,
    retentionHours,
    trackPointCount,
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
