

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
