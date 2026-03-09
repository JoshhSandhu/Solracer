// ---------------------------------------------------------------------------
// Tests  Deterministic Hour Rounding
// ---------------------------------------------------------------------------
// Verifies architecture invariant: floorToHour() must produce identical
// results regardless of input precision, ensuring consistent bucket IDs
// across different servers and timezones.
// ---------------------------------------------------------------------------

import { describe, it, expect } from 'vitest';
import { floorToHour, currentHourUTC } from '../src/utils/time';

describe('floorToHour()', () => {
  it('floors a mid-hour timestamp to the start of that hour', () => {
    const input = new Date('2026-02-24T10:37:11.482Z');
    const result = floorToHour(input);

    expect(result.toISOString()).toBe('2026-02-24T10:00:00.000Z');
  });

  it('is idempotent  flooring an already-floored time returns the same value', () => {
    const input = new Date('2026-02-24T10:00:00.000Z');
    const result = floorToHour(input);

    expect(result.toISOString()).toBe(input.toISOString());
    expect(floorToHour(result).toISOString()).toBe(input.toISOString());
  });

  it('handles the last second of the hour', () => {
    const input = new Date('2026-02-24T10:59:59.999Z');
    const result = floorToHour(input);

    expect(result.toISOString()).toBe('2026-02-24T10:00:00.000Z');
  });

  it('handles midnight correctly', () => {
    const input = new Date('2026-02-24T00:15:00.000Z');
    const result = floorToHour(input);

    expect(result.toISOString()).toBe('2026-02-24T00:00:00.000Z');
  });

  it('handles the last hour of the day', () => {
    const input = new Date('2026-02-24T23:45:30.000Z');
    const result = floorToHour(input);

    expect(result.toISOString()).toBe('2026-02-24T23:00:00.000Z');
  });

  it('does not mutate the original Date object', () => {
    const input = new Date('2026-02-24T10:37:11.482Z');
    const originalTime = input.getTime();
    floorToHour(input);

    expect(input.getTime()).toBe(originalTime);
  });

  it('produces deterministic output for the same input across calls', () => {
    const input = new Date('2026-02-24T15:22:33.456Z');
    const a = floorToHour(input);
    const b = floorToHour(input);
    const c = floorToHour(new Date(input.getTime()));

    expect(a.toISOString()).toBe(b.toISOString());
    expect(a.toISOString()).toBe(c.toISOString());
  });

  it('produces different bucket IDs for different hours', () => {
    const h1 = floorToHour(new Date('2026-02-24T10:30:00.000Z'));
    const h2 = floorToHour(new Date('2026-02-24T11:30:00.000Z'));

    expect(h1.toISOString()).not.toBe(h2.toISOString());
  });
});

describe('currentHourUTC()', () => {
  it('returns a Date with zero minutes, seconds, and milliseconds', () => {
    const result = currentHourUTC();

    expect(result.getUTCMinutes()).toBe(0);
    expect(result.getUTCSeconds()).toBe(0);
    expect(result.getUTCMilliseconds()).toBe(0);
  });
});
