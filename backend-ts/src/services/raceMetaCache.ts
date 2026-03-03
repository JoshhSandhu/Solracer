/**
 * Lightweight in-process cache that stores race metadata needed for PDA
 * derivation in transaction endpoints (token_mint, entry_fee_sol).
 *
 * Populated when a create_race transaction is built; consumed by
 * join_race / submit_result / claim_prize builds.
 */

interface RaceMeta {
  token_mint: string;
  entry_fee_sol: number;
}

const cache = new Map<string, RaceMeta>();

export function setRaceMeta(raceId: string, meta: RaceMeta): void {
  cache.set(raceId, meta);
}

export function getRaceMeta(raceId: string): RaceMeta | undefined {
  return cache.get(raceId);
}
