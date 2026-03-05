/**
 * Ghost position store — Phase 2a ER simulator.
 *
 * Stores the last known position of each player keyed by (race_id + wallet).
 * Enforces:
 *   - 50ms minimum interval between updates (throttle)
 *   - Sequence numbers to reject out-of-order packets
 *   - 30s TTL expiry per entry
 *   - Background cleanup every 10s
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface GhostState {
  race_id: string;
  wallet: string;
  x: number;
  y: number;
  speed: number;
  checkpoint_index: number;
  seq: number;
  updated_at: number; // Date.now() ms
}

export interface GhostUpdateResult {
  accepted: boolean;
  reason?: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const THROTTLE_MS = 50;
const TTL_MS = 30_000;
const CLEANUP_INTERVAL_MS = 10_000;

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

function makeKey(race_id: string, wallet: string): string {
  return `${race_id}::${wallet}`;
}

const ghostMap = new Map<string, GhostState>();

// ---------------------------------------------------------------------------
// Background cleanup
// ---------------------------------------------------------------------------

let _cleanupTimer: ReturnType<typeof setInterval> | null = null;

export function startGhostCleanup(): void {
  if (_cleanupTimer) return;
  _cleanupTimer = setInterval(() => {
    const now = Date.now();
    for (const [key, state] of ghostMap) {
      if (now - state.updated_at > TTL_MS) {
        ghostMap.delete(key);
      }
    }
  }, CLEANUP_INTERVAL_MS);
  if (_cleanupTimer.unref) _cleanupTimer.unref();
}

export function stopGhostCleanup(): void {
  if (_cleanupTimer) {
    clearInterval(_cleanupTimer);
    _cleanupTimer = null;
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Attempt to record a ghost position update.
 * Enforces throttle (50ms) and sequence ordering.
 */
export function updateGhost(
  race_id: string,
  wallet: string,
  x: number,
  y: number,
  speed: number,
  checkpoint_index: number,
  seq: number,
): GhostUpdateResult {
  const key = makeKey(race_id, wallet);
  const existing = ghostMap.get(key);
  const now = Date.now();

  if (existing) {
    // Throttle: ignore if too soon
    if (now - existing.updated_at < THROTTLE_MS) {
      return { accepted: false, reason: "throttled" };
    }
    // Sequence: ignore stale packets
    if (seq <= existing.seq) {
      return { accepted: false, reason: "out_of_order" };
    }
  }

  ghostMap.set(key, { race_id, wallet, x, y, speed, checkpoint_index, seq, updated_at: now });
  return { accepted: true };
}

/**
 * Get all live ghost states for a race.
 * Filters out entries that have exceeded TTL.
 */
export function getGhostsForRace(race_id: string): GhostState[] {
  const now = Date.now();
  const results: GhostState[] = [];

  for (const state of ghostMap.values()) {
    if (state.race_id === race_id && now - state.updated_at <= TTL_MS) {
      results.push(state);
    }
  }

  return results;
}
