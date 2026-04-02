// ---------------------------------------------------------------------------
// Oracle Pipeline Persistent Track Cache
// ---------------------------------------------------------------------------
// Saves/loads the generatedTrackCache Set to/from a JSON file so that
// restarts don't trigger thousands of redundant trackBucketExists() SELECTs
// against Supabase.
// ---------------------------------------------------------------------------

import { readFileSync, writeFileSync, existsSync } from 'fs';

/**
 * Load track cache entries from a JSON file on disk.
 * Returns an empty Set if file doesn't exist or is corrupt.
 */
export function loadTrackCache(filePath: string): Set<string> {
  try {
    if (!existsSync(filePath)) {
      return new Set();
    }

    const raw = readFileSync(filePath, 'utf-8');
    const parsed = JSON.parse(raw);

    if (!Array.isArray(parsed)) {
      console.warn('[oracle/track-cache] Cache file is not an array, ignoring.');
      return new Set();
    }

    console.log(`[oracle/track-cache] Loaded ${parsed.length} cached entries from ${filePath}`);
    return new Set(parsed.filter((v: unknown) => typeof v === 'string'));
  } catch (err) {
    console.warn(`[oracle/track-cache] Failed to load cache from ${filePath}:`, err);
    return new Set();
  }
}

/**
 * Save track cache entries to a JSON file on disk.
 * Writes atomically-ish (overwrites in place).
 */
export function saveTrackCache(filePath: string, cache: Set<string>): void {
  try {
    const data = JSON.stringify([...cache]);
    writeFileSync(filePath, data, 'utf-8');
  } catch (err) {
    console.warn(`[oracle/track-cache] Failed to save cache to ${filePath}:`, err);
  }
}
