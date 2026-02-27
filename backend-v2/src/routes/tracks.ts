// ---------------------------------------------------------------------------
// Backend-v2  Track Routes
// ---------------------------------------------------------------------------
// GET /tracks/:tokenMint          → 24 playable track buckets (metadata only)
// GET /tracks/:tokenMint/latest   → most recent playable track
// GET /tracks/:tokenMint/:hourStartUTC → single track with base64 blob
// ---------------------------------------------------------------------------

import type { FastifyInstance } from 'fastify';
import type { Pool } from 'pg';
import type { TrackMetadata, TrackDetail, LatestTrack } from '../types/track.types';

const SOLANA_ADDRESS_RE = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;

interface TrackRouteOpts {
  pool: Pool;
  retentionHours: number;
  trackVersion: string;
}

export async function trackRoutes(
  fastify: FastifyInstance,
  opts: TrackRouteOpts,
): Promise<void> {
  const { pool, retentionHours, trackVersion } = opts;

  // ─── GET /tracks/:tokenMint ────────────────────────────────────────────
  // Returns 24 playable track buckets (metadata only, no blob).
  // SQL copied verbatim from oracle-v2 repository.ts getPlayableTrackBuckets().
  // ───────────────────────────────────────────────────────────────────────

  fastify.get<{ Params: { tokenMint: string } }>(
    '/tracks/:tokenMint',
    async (request, reply) => {
      const { tokenMint } = request.params;

      if (!SOLANA_ADDRESS_RE.test(tokenMint)) {
        return reply.status(400).send({ error: 'Invalid tokenMint. Must be a valid Solana address.' });
      }

      const sql = `
        WITH valid_window AS (
          SELECT token_mint, track_hour_start_utc, track_version, point_count, track_hash
          FROM track_buckets
          WHERE token_mint = $1
            AND track_hour_start_utc > now() - ($2::int * interval '1 hour')
            AND track_version = $3
        ),
        bounds AS (
          SELECT
            MIN(track_hour_start_utc) AS oldest,
            MAX(track_hour_start_utc) AS newest
          FROM valid_window
        )
        SELECT w.token_mint,
               w.track_hour_start_utc,
               w.track_version,
               w.point_count,
               w.track_hash
        FROM valid_window w, bounds b
        WHERE w.track_hour_start_utc > b.oldest
          AND w.track_hour_start_utc < b.newest
        ORDER BY w.track_hour_start_utc ASC
      `;

      const result = await pool.query(sql, [tokenMint, retentionHours, trackVersion]);

      const tracks: TrackMetadata[] = result.rows.map((row) => ({
        tokenMint: row.token_mint as string,
        hourStartUTC: (row.track_hour_start_utc as Date).toISOString(),
        trackVersion: row.track_version as string,
        pointCount: row.point_count as number,
        trackHash: row.track_hash as string,
      }));

      reply.header('Cache-Control', 'public, max-age=300');
      return reply.send(tracks);
    },
  );

  // ─── GET /tracks/:tokenMint/latest ─────────────────────────────────────
  // Returns most recent playable track (not the absolute newest  the
  // second-newest, since the newest hour is excluded from playable set).
  // ───────────────────────────────────────────────────────────────────────

  fastify.get<{ Params: { tokenMint: string } }>(
    '/tracks/:tokenMint/latest',
    async (request, reply) => {
      const { tokenMint } = request.params;

      if (!SOLANA_ADDRESS_RE.test(tokenMint)) {
        return reply.status(400).send({ error: 'Invalid tokenMint. Must be a valid Solana address.' });
      }

      const sql = `
        WITH valid_window AS (
          SELECT track_hour_start_utc, track_hash
          FROM track_buckets
          WHERE token_mint = $1
            AND track_hour_start_utc > now() - ($2::int * interval '1 hour')
            AND track_version = $3
        ),
        bounds AS (
          SELECT
            MIN(track_hour_start_utc) AS oldest,
            MAX(track_hour_start_utc) AS newest
          FROM valid_window
        )
        SELECT w.track_hour_start_utc,
               w.track_hash
        FROM valid_window w, bounds b
        WHERE w.track_hour_start_utc > b.oldest
          AND w.track_hour_start_utc < b.newest
        ORDER BY w.track_hour_start_utc DESC
        LIMIT 1
      `;

      const result = await pool.query(sql, [tokenMint, retentionHours, trackVersion]);

      if (result.rows.length === 0) {
        return reply.status(404).send({ error: 'No playable tracks found' });
      }

      const row = result.rows[0];
      const latest: LatestTrack = {
        hourStartUTC: (row.track_hour_start_utc as Date).toISOString(),
        trackHash: row.track_hash as string,
      };

      reply.header('Cache-Control', 'public, max-age=300');
      return reply.send(latest);
    },
  );

  // ─── GET /tracks/:tokenMint/:hourStartUTC ──────────────────────────────
  // Returns a single track with base64-encoded blob.
  // ───────────────────────────────────────────────────────────────────────

  fastify.get<{ Params: { tokenMint: string; hourStartUTC: string } }>(
    '/tracks/:tokenMint/:hourStartUTC',
    async (request, reply) => {
      const { tokenMint, hourStartUTC } = request.params;

      if (!SOLANA_ADDRESS_RE.test(tokenMint)) {
        return reply.status(400).send({ error: 'Invalid tokenMint. Must be a valid Solana address.' });
      }

      const parsed = new Date(hourStartUTC);
      if (
        isNaN(parsed.getTime()) ||
        parsed.toISOString() !== hourStartUTC ||
        parsed.getUTCMinutes() !== 0 ||
        parsed.getUTCSeconds() !== 0 ||
        parsed.getUTCMilliseconds() !== 0
      ) {
        return reply.status(400).send({
          error: 'Invalid hourStartUTC. Must be hour-aligned: YYYY-MM-DDTHH:00:00.000Z',
        });
      }

      const sql = `
        SELECT token_mint,
               track_hour_start_utc,
               track_version,
               point_count,
               track_hash,
               normalized_points_blob
        FROM track_buckets
        WHERE token_mint = $1
          AND track_hour_start_utc = $2
          AND track_version = $3
      `;

      const result = await pool.query(sql, [tokenMint, hourStartUTC, trackVersion]);

      if (result.rows.length === 0) {
        return reply.status(404).send({ error: 'Track not found' });
      }

      const row = result.rows[0];
      const blob = row.normalized_points_blob as Buffer;
      const pointCount = row.point_count as number;

      // Validate blob integrity: each point is int16 (2 bytes)
      if (blob.length !== pointCount * 2) {
        request.log.error(
          {
            tokenMint,
            hourStartUTC,
            expectedBytes: pointCount * 2,
            actualBytes: blob.length,
          },
          'Blob length mismatch  data corruption detected',
        );
        return reply.status(500).send({ error: 'Track data integrity error' });
      }

      const track: TrackDetail = {
        tokenMint: row.token_mint as string,
        hourStartUTC: (row.track_hour_start_utc as Date).toISOString(),
        trackVersion: row.track_version as string,
        pointCount,
        trackHash: row.track_hash as string,
        normalizedPointsBlobBase64: blob.toString('base64'),
      };

      reply.header('Cache-Control', 'public, max-age=3600');
      return reply.send(track);
    },
  );
}
