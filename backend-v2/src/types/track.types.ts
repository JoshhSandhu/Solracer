// ---------------------------------------------------------------------------
// Backend-v2  Response Types
// ---------------------------------------------------------------------------

/** Metadata-only track info returned by GET /tracks/:tokenMint */
export interface TrackMetadata {
  tokenMint: string;
  hourStartUTC: string;
  trackVersion: string;
  pointCount: number;
  trackHash: string;
  difficulty: number;
}

/** Full track detail returned by GET /tracks/:tokenMint/:hourStartUTC */
export interface TrackDetail extends TrackMetadata {
  normalizedPointsBlobBase64: string;
}

/** Latest playable track returned by GET /tracks/:tokenMint/latest */
export interface LatestTrack {
  hourStartUTC: string;
  trackHash: string;
  difficulty: number;
}

/** Health check response */
export interface HealthResponse {
  status: 'ok' | 'error';
  database: 'connected' | 'disconnected';
  uptimeSeconds: number;
}
