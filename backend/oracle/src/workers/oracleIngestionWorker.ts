// ---------------------------------------------------------------------------
// Oracle Pipeline  Tick Ingestion Worker
// ---------------------------------------------------------------------------
// Continuous worker that:
//   1. Samples oracle prices every 2s with aligned tick times.
//   2. Stores ticks in oracle_ticks table.
//   3. Generates tracks for completed hours (DB-checked, crash-safe).
//   4. Cleans up expired data beyond the retention window.
//   5. Guards against overlapping tick cycles.
// ---------------------------------------------------------------------------

import type { Pool } from 'pg';
import type { OracleConfig } from '../config';
import {
  TICK_INTERVAL_MS,
  ONE_HOUR_MS,
  TRACK_VERSION,
  MIN_TICKS_FOR_TRACK,
  TRACK_GEN_BUFFER_MINUTES,
} from '../constants';
import { floorToHour } from '../utils/time';
import { fetchOraclePricesBatch } from '../services/oracle-fetcher';
import { generateTrackBucket } from '../services/track-generator';
import {
  storeOracleTick,
  getTicksForHour,
  storeTrackBucket,
  trackBucketExists,
  deleteExpiredData,
} from '../db/repository';
import type { OracleTickInput } from '../types/oracle.types';

// ---------------------------------------------------------------------------
// Worker State
// ---------------------------------------------------------------------------

/** Overlap guard  prevents concurrent tick cycles. */
let isRunning = false;

/** Timer handle for shutdown. */
let tickTimer: ReturnType<typeof setTimeout> | null = null;

/** Track last retention cleanup time to avoid running every tick. */
let lastCleanupTime = 0;

/** Minimum interval between cleanup attempts (successful or failed). */
const CLEANUP_RETRY_INTERVAL_MS = 5 * 60 * 1000; // 5 minutes

/** In-memory cache: last stored tick time per token (avoids DB query every tick). */
const lastTickTimeMap = new Map<string, Date>();

/**
 * In-memory cache: generated track bucket keys for the current period.
 * Key format: `${tokenMint}|${hourISO}|${version}`.
 * Cleared every hour to allow re-checking after the hour rolls.
 */
const generatedTrackCache = new Set<string>();
let generatedTrackCacheHour = 0;

// ---------------------------------------------------------------------------
// Structured Logger
// ---------------------------------------------------------------------------

interface LogEntry {
  ts: string;
  level: 'info' | 'warn' | 'error';
  msg: string;
  tokenMint?: string;
  tickTime?: string;
  hourStart?: string;
  price?: number;
  tickCount?: number;
  deletedTicks?: number;
  deletedBuckets?: number;
  gapMs?: number;
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
// Aligned Tick Time
// ---------------------------------------------------------------------------

/**
 * Compute the current aligned tick time.
 * Always a multiple of TICK_INTERVAL_MS.
 */
function alignedTickTime(): Date {
  return new Date(Math.floor(Date.now() / TICK_INTERVAL_MS) * TICK_INTERVAL_MS);
}

/**
 * Compute ms until the next tick boundary.
 */
function msUntilNextTick(): number {
  return TICK_INTERVAL_MS - (Date.now() % TICK_INTERVAL_MS);
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Start the tick ingestion worker.
 *
 * - Runs a continuous aligned-interval loop (every 2s).
 * - Each tick: fetch prices, store ticks, check for track generation.
 * - Track generation is DB-checked (crash-safe).
 */
export function startTickWorker(pool: Pool, config: OracleConfig): void {
  log({
    ts: now(),
    level: 'info',
    msg: `Starting tick worker. tokens=${config.supportedTokens.length} retention=${config.retentionHours}h interval=${TICK_INTERVAL_MS}ms`,
  });

  scheduleNextTick(pool, config);
}

/**
 * Stop the tick worker (for graceful shutdown).
 */
export function stopTickWorker(): void {
  if (tickTimer) {
    clearTimeout(tickTimer);
    tickTimer = null;
  }
}

// ---------------------------------------------------------------------------
// Tick Loop
// ---------------------------------------------------------------------------

function scheduleNextTick(pool: Pool, config: OracleConfig): void {
  const delay = msUntilNextTick();
  tickTimer = setTimeout(() => {
    runTickCycle(pool, config)
      .catch((err) => {
        log({
          ts: now(),
          level: 'error',
          msg: 'Tick cycle failed',
          error: String(err),
        });
      })
      .finally(() => {
        scheduleNextTick(pool, config);
      });
  }, delay);
}

// ---------------------------------------------------------------------------
// Core Tick Cycle
// ---------------------------------------------------------------------------

async function runTickCycle(pool: Pool, config: OracleConfig): Promise<void> {
  // Overlap guard
  if (isRunning) {
    log({ ts: now(), level: 'warn', msg: 'Skipping tick  previous cycle still running' });
    return;
  }

  isRunning = true;
  const tickTime = alignedTickTime();

  try {
    // 1. Batch fetch all oracle prices (single RPC call)
    try {
      await ingestTicksBatch(pool, config.supportedTokens, tickTime);
    } catch (err) {
      log({
        ts: now(),
        level: 'error',
        msg: 'Batch tick ingestion failed',
        tickTime: tickTime.toISOString(),
        error: String(err),
      });
    }

    // 2. Check for track generation (after hour close buffer)
    const currentMinute = new Date().getUTCMinutes();
    if (currentMinute >= TRACK_GEN_BUFFER_MINUTES) {
      await checkAndGenerateTracks(pool, config);
    }

    // 3. Periodic retention cleanup (once per hour; backoff to 5min on failure)
    const nowMs = Date.now();
    if (nowMs - lastCleanupTime >= ONE_HOUR_MS) {
      try {
        const { deletedTicks, deletedBuckets } = await deleteExpiredData(
          pool,
          config.retentionHours,
        );

        if (deletedTicks > 0 || deletedBuckets > 0) {
          log({
            ts: now(),
            level: 'info',
            msg: 'Retention cleanup complete',
            deletedTicks,
            deletedBuckets,
          });
        }
        lastCleanupTime = nowMs;
      } catch (err) {
        lastCleanupTime = nowMs - ONE_HOUR_MS + CLEANUP_RETRY_INTERVAL_MS;
        log({
          ts: now(),
          level: 'error',
          msg: 'Retention cleanup failed, retry in 5m',
          error: String(err),
        });
      }
    }
  } finally {
    isRunning = false;
  }
}

// ---------------------------------------------------------------------------
// Batch Tick Ingestion (Single RPC Call)
// ---------------------------------------------------------------------------

async function ingestTicksBatch(
  pool: Pool,
  tokenMints: string[],
  tickTime: Date,
): Promise<void> {
  // Gap detection (from in-memory cache)
  for (const tokenMint of tokenMints) {
    const lastKnown = lastTickTimeMap.get(tokenMint);
    if (lastKnown) {
      const gapMs = tickTime.getTime() - lastKnown.getTime();
      if (gapMs > TICK_INTERVAL_MS * 5) {
        log({
          ts: now(),
          level: 'warn',
          msg: 'Tick gap detected',
          tokenMint,
          gapMs,
        });
      }
    }
  }

  // Single batched RPC call for all tokens
  const priceMap = await fetchOraclePricesBatch(tokenMints);

  // Store each successfully fetched price as a tick
  for (const [tokenMint, priceData] of priceMap) {
    try {
      const tick: OracleTickInput = {
        token_mint: priceData.token_mint,
        tick_time: tickTime,
        oracle_price: priceData.price,
        publish_time: priceData.publish_time,
        source_slot: priceData.source_slot,
      };

      await storeOracleTick(pool, tick);
      lastTickTimeMap.set(tokenMint, tickTime);
    } catch (err) {
      log({
        ts: now(),
        level: 'error',
        msg: 'Failed to store tick',
        tokenMint,
        error: String(err),
      });
    }
  }
}

// ---------------------------------------------------------------------------
// Track Generation (DB-Checked, Crash-Safe)
// ---------------------------------------------------------------------------

async function checkAndGenerateTracks(
  pool: Pool,
  config: OracleConfig,
): Promise<void> {
  const currentHour = floorToHour(new Date());
  const currentHourMs = currentHour.getTime();

  if (generatedTrackCacheHour !== currentHourMs) {
    generatedTrackCache.clear();
    generatedTrackCacheHour = currentHourMs;
  }

  for (let offset = 1; offset <= 2; offset++) {
    const targetHour = new Date(currentHourMs - offset * ONE_HOUR_MS);

    for (const tokenMint of config.supportedTokens) {
      const cacheKey = `${tokenMint}|${targetHour.toISOString()}|${TRACK_VERSION}`;

      if (generatedTrackCache.has(cacheKey)) continue;

      try {
        const exists = await trackBucketExists(pool, tokenMint, targetHour, TRACK_VERSION);
        if (exists) {
          generatedTrackCache.add(cacheKey);
          continue;
        }

        const ticks = await getTicksForHour(pool, tokenMint, targetHour);

        if (ticks.length < MIN_TICKS_FOR_TRACK) {
          log({
            ts: now(),
            level: 'warn',
            msg: `Skipping track gen  insufficient ticks (${ticks.length}/${MIN_TICKS_FOR_TRACK})`,
            tokenMint,
            hourStart: targetHour.toISOString(),
            tickCount: ticks.length,
          });
          continue;
        }

        const bucket = await generateTrackBucket(
          tokenMint,
          targetHour,
          ticks,
          config.trackPointCount,
        );

        await storeTrackBucket(pool, bucket);
        generatedTrackCache.add(cacheKey);

        log({
          ts: now(),
          level: 'info',
          msg: 'Generated track bucket',
          tokenMint,
          hourStart: targetHour.toISOString(),
          tickCount: ticks.length,
        });
      } catch (err) {
        log({
          ts: now(),
          level: 'error',
          msg: 'Track generation failed',
          tokenMint,
          hourStart: targetHour.toISOString(),
          error: String(err),
        });
      }
    }
  }
}
