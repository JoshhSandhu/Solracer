// ---------------------------------------------------------------------------
// Tests  Catch-Up Safety Cap
// ---------------------------------------------------------------------------
// Verifies architecture invariant: startup backfill must never exceed
// MAX_CATCHUP_HOURS to prevent runaway loops after long outages.
// Pure logic  no DB required.
// ---------------------------------------------------------------------------

import { describe, it, expect } from 'vitest';
import { computeCatchUpStartHour } from '../src/workers/oracleIngestionWorker';
import { floorToHour } from '../src/utils/time';

const ONE_HOUR_MS = 60 * 60 * 1000;

// Helper: create a floored Date N hours before `current`
function hoursBefore(current: Date, n: number): Date {
  return floorToHour(new Date(current.getTime() - n * ONE_HOUR_MS));
}

describe('computeCatchUpStartHour()', () => {
  const currentHour = new Date('2026-02-24T12:00:00.000Z');
  const retentionHours = 26;
  const maxCatchUpHours = 48;

  it('with no prior data (null), starts from currentHour - retentionHours', () => {
    const result = computeCatchUpStartHour(
      null,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    const expected = hoursBefore(currentHour, retentionHours);
    expect(result.toISOString()).toBe(expected.toISOString());
  });

  it('with recent data (2h gap), starts from latestHour + 1h', () => {
    const latestHour = hoursBefore(currentHour, 2);
    const result = computeCatchUpStartHour(
      latestHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    const expected = new Date(latestHour.getTime() + ONE_HOUR_MS);
    expect(result.toISOString()).toBe(expected.toISOString());
  });

  it('with a small gap (5h), does NOT clamp', () => {
    const latestHour = hoursBefore(currentHour, 5);
    const result = computeCatchUpStartHour(
      latestHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    const expected = new Date(latestHour.getTime() + ONE_HOUR_MS);
    expect(result.toISOString()).toBe(expected.toISOString());
  });

  it('with a gap exactly at MAX_CATCHUP_HOURS, does NOT clamp', () => {
    // latestHour is exactly maxCatchUpHours+1 ago, so gap = maxCatchUpHours
    const latestHour = hoursBefore(currentHour, maxCatchUpHours + 1);
    const result = computeCatchUpStartHour(
      latestHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    // startHour = latestHour + 1h, gap = maxCatchUpHours, NOT > maxCatchUpHours
    const expected = new Date(latestHour.getTime() + ONE_HOUR_MS);
    expect(result.toISOString()).toBe(expected.toISOString());
  });

  it('with a gap exceeding MAX_CATCHUP_HOURS, CLAMPS to retentionHours', () => {
    // Server down for 3 weeks = 504 hours
    const latestHour = hoursBefore(currentHour, 504);
    const result = computeCatchUpStartHour(
      latestHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    // Clamped: should start from currentHour - retentionHours
    const expected = hoursBefore(currentHour, retentionHours);
    expect(result.toISOString()).toBe(expected.toISOString());
  });

  it('clamped catch-up produces at most retentionHours worth of backfill', () => {
    const latestHour = hoursBefore(currentHour, 1000);
    const result = computeCatchUpStartHour(
      latestHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    const hoursToBackfill = Math.floor(
      (currentHour.getTime() - result.getTime()) / ONE_HOUR_MS,
    );

    expect(hoursToBackfill).toBeLessThanOrEqual(retentionHours);
  });

  it('already up-to-date (latestHour === currentHour) returns next hour', () => {
    const result = computeCatchUpStartHour(
      currentHour,
      currentHour,
      retentionHours,
      maxCatchUpHours,
    );

    // startHour = currentHour + 1h, which is in the future → 0 hours to backfill
    const expected = new Date(currentHour.getTime() + ONE_HOUR_MS);
    expect(result.toISOString()).toBe(expected.toISOString());
  });
});
