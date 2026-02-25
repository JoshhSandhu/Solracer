// ---------------------------------------------------------------------------
// Oracle Pipeline  Ingestion Worker
// ---------------------------------------------------------------------------
// Hourly cron worker that:
//   1. Fetches oracle prices for each configured token.
//   2. Stores them as hourly buckets.
//   3. Runs catch-up on startup to backfill gaps (capped).
//   4. Cleans up expired data beyond the retention window.
//   5. Guards against overlapping cycles.
// ---------------------------------------------------------------------------

import * as cron from 'node-cron';
import type { Pool } from 'pg';
import type { OracleConfig } from '../config';
import { ONE_HOUR_MS } from '../constants';
import { floorToHour, currentHourUTC } from '../utils/time';
import { fetchOraclePrice } from '../services/oracle-fetcher';
import {
  storeOraclePoint,
  getLatestOracleHour,
  deleteExpiredData,
} from '../db/repository';
import type { OraclePointInput } from '../types/oracle.types';

// ---------------------------------------------------------------------------
// Worker State
// ---------------------------------------------------------------------------

/** Overlap guard  prevents concurrent cron cycles. */
let isRunning = false;

// ---------------------------------------------------------------------------
// Structured Logger
// ---------------------------------------------------------------------------

interface LogEntry {
  ts: string;
  level: 'info' | 'warn' | 'error';
  msg: string;
  tokenMint?: string;
  hourStart?: string;
  price?: number;
  backfilled?: number;
  deletedPoints?: number;
  deletedBuckets?: number;
  error?: string;
}

function log(entry: LogEntry): void {
  const prefix = `[oracle/worker]`;
  const meta = Object.entries(entry)
    .filter(([k]) => !['ts', 'level', 'msg'].includes(k))
    .map(([k, v]) => `${k}=${v}`)
    .join(' ');

  const line = `${prefix} ${entry.ts} ${entry.level.toUpperCase()} ${entry.msg}${meta ? ' ' + meta : ''}`;

  if (entry.level === 'error') {
    console.error(line);
  } else if (entry.level === 'warn') {
    console.warn(line);
  } else {
    console.log(line);
  }
}

function now(): string {
  return new Date().toISOString();
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Start the ingestion cron worker.
 *
 * - Immediately runs startup catch-up to backfill missed hours (capped).
 * - Then schedules hourly execution at the configured poll minute.
 */
export function startIngestionWorker(pool: Pool, config: OracleConfig): void {
  log({
    ts: now(),
    level: 'info',
    msg: `Starting ingestion worker. tokens=${config.supportedTokens.length} retention=${config.retentionHours}h pollMinute=:${String(config.oraclePollMinute).padStart(2, '0')} maxCatchUp=${config.maxCatchUpHours}h`,
  });

  // Run catch-up immediately on startup
  runStartupCatchUp(pool, config).catch((err) => {
    log({
      ts: now(),
      level: 'error',
      msg: 'Startup catch-up failed',
      error: String(err),
    });
  });

  // Schedule hourly at the configured minute
  const cronExpr = `${config.oraclePollMinute} * * * *`;
  cron.schedule(cronExpr, () => {
    runIngestionCycle(pool, config).catch((err) => {
      log({
        ts: now(),
        level: 'error',
        msg: 'Ingestion cycle failed',
        error: String(err),
      });
    });
  });

  log({
    ts: now(),
    level: 'info',
    msg: `Cron scheduled: "${cronExpr}" (every hour at :${String(config.oraclePollMinute).padStart(2, '0')})`,
  });
}

// ---------------------------------------------------------------------------
// Core Ingestion Cycle
// ---------------------------------------------------------------------------

/**
 * Single ingestion cycle: fetch → store → cleanup for all tokens.
 * Guarded by `isRunning` flag to prevent overlap.
 */
async function runIngestionCycle(
  pool: Pool,
  config: OracleConfig,
): Promise<void> {
  // --- Overlap guard ---
  if (isRunning) {
    log({
      ts: now(),
      level: 'warn',
      msg: 'Skipping cycle  previous cycle still running',
    });
    return;
  }

  isRunning = true;
  const hourStart = currentHourUTC();

  try {
    log({
      ts: now(),
      level: 'info',
      msg: 'Ingestion cycle start',
      hourStart: hourStart.toISOString(),
    });

    for (const tokenMint of config.supportedTokens) {
      try {
        await ingestTokenHour(pool, tokenMint, hourStart);
      } catch (err) {
        log({
          ts: now(),
          level: 'error',
          msg: 'Token ingestion failed',
          tokenMint,
          hourStart: hourStart.toISOString(),
          error: String(err),
        });
      }
    }

    // Cleanup expired data
    try {
      const { deletedPoints, deletedBuckets } = await deleteExpiredData(
        pool,
        config.retentionHours,
      );

      if (deletedPoints > 0 || deletedBuckets > 0) {
        log({
          ts: now(),
          level: 'info',
          msg: 'Retention cleanup complete',
          deletedPoints,
          deletedBuckets,
        });
      }
    } catch (err) {
      log({
        ts: now(),
        level: 'error',
        msg: 'Retention cleanup failed',
        error: String(err),
      });
    }

    log({ ts: now(), level: 'info', msg: 'Ingestion cycle complete' });
  } finally {
    isRunning = false;
  }
}

// ---------------------------------------------------------------------------
// Per-Token Ingestion
// ---------------------------------------------------------------------------

/**
 * Fetch and store a single oracle price for a token + hour.
 */
async function ingestTokenHour(
  pool: Pool,
  tokenMint: string,
  hourStart: Date,
): Promise<void> {
  const priceData = await fetchOraclePrice(tokenMint);

  const point: OraclePointInput = {
    token_mint: priceData.token_mint,
    hour_start_utc: hourStart,
    oracle_price: priceData.price,
    publish_time: priceData.publish_time,
    source_slot: priceData.source_slot,
  };

  await storeOraclePoint(pool, point);

  log({
    ts: now(),
    level: 'info',
    msg: 'Stored oracle point',
    tokenMint,
    hourStart: hourStart.toISOString(),
    price: priceData.price,
  });
}

// ---------------------------------------------------------------------------
// Startup Catch-Up
// ---------------------------------------------------------------------------

/**
 * Pure function: compute the starting hour for catch-up backfill.
 *
 * Exported for testability. No side effects  just math.
 *
 * Rules:
 *   - If `latestHour` is null → start from `currentHour - retentionHours`.
 *   - Otherwise start from `latestHour + 1h`.
 *   - If the resulting gap exceeds `maxCatchUpHours` → clamp to
 *     `currentHour - retentionHours` to prevent runaway loops.
 */
export function computeCatchUpStartHour(
  latestHour: Date | null,
  currentHour: Date,
  retentionHours: number,
  maxCatchUpHours: number,
): Date {
  let startHour: Date;

  if (latestHour === null) {
    startHour = floorToHour(
      new Date(currentHour.getTime() - retentionHours * ONE_HOUR_MS),
    );
  } else {
    startHour = new Date(latestHour.getTime() + ONE_HOUR_MS);
  }

  const gapHours = Math.floor(
    (currentHour.getTime() - startHour.getTime()) / ONE_HOUR_MS,
  );

  if (gapHours > maxCatchUpHours) {
    startHour = floorToHour(
      new Date(currentHour.getTime() - retentionHours * ONE_HOUR_MS),
    );
  }

  return startHour;
}

/**
 * On startup, detect gaps and backfill missed hours (with safety cap).
 *
 * For each token:
 *   1. Get the latest stored hour.
 *   2. Compute missing hours up to current hour.
 *   3. Cap backfill to `maxCatchUpHours` to prevent runaway loops.
 *   4. Ingest each missing hour.
 *
 * If no data exists at all, backfills from `now - retentionHours`.
 */
async function runStartupCatchUp(
  pool: Pool,
  config: OracleConfig,
): Promise<void> {
  log({ ts: now(), level: 'info', msg: 'Running startup catch-up...' });

  const currentHour = currentHourUTC();

  for (const tokenMint of config.supportedTokens) {
    try {
      const latestHour = await getLatestOracleHour(pool, tokenMint);

      const startHour = computeCatchUpStartHour(
        latestHour,
        currentHour,
        config.retentionHours,
        config.maxCatchUpHours,
      );

      if (latestHour !== null) {
        const gapHours = Math.floor(
          (currentHour.getTime() - (latestHour.getTime() + ONE_HOUR_MS)) / ONE_HOUR_MS,
        );
        if (gapHours > config.maxCatchUpHours) {
          log({
            ts: now(),
            level: 'warn',
            msg: `Gap of ${gapHours}h exceeds MAX_CATCHUP_HOURS=${config.maxCatchUpHours}. Clamped start to now - ${config.retentionHours}h`,
            tokenMint,
          });
        }
      } else {
        log({
          ts: now(),
          level: 'info',
          msg: 'No existing data  starting fresh backfill',
          tokenMint,
          hourStart: startHour.toISOString(),
        });
      }

      // Walk forward hour by hour until we reach current hour
      let current = new Date(startHour.getTime());
      let backfilled = 0;

      while (current.getTime() <= currentHour.getTime()) {
        await ingestTokenHour(pool, tokenMint, current);
        backfilled++;
        current = new Date(current.getTime() + ONE_HOUR_MS);
      }

      log({
        ts: now(),
        level: 'info',
        msg: backfilled > 0
          ? `Catch-up complete`
          : `Already up to date`,
        tokenMint,
        backfilled,
      });
    } catch (err) {
      log({
        ts: now(),
        level: 'error',
        msg: 'Catch-up failed for token',
        tokenMint,
        error: String(err),
      });
    }
  }

  log({ ts: now(), level: 'info', msg: 'Startup catch-up complete.' });
}
