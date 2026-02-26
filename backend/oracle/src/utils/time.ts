// ---------------------------------------------------------------------------
// Oracle Pipeline  Utility Functions
// ---------------------------------------------------------------------------

/**
 * Floor a Date to the start of its UTC hour.
 *
 * Deterministic: always produces the same result for the same input
 * regardless of local timezone or server clock skew.
 *
 * Example:
 *   2026-02-24T10:37:11.482Z  →  2026-02-24T10:00:00.000Z
 */
export function floorToHour(date: Date): Date {
  const d = new Date(date.getTime());
  d.setUTCMinutes(0, 0, 0);
  return d;
}

/**
 * Get the current UTC time floored to the hour.
 */
export function currentHourUTC(): Date {
  return floorToHour(new Date());
}
