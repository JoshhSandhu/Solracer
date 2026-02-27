// ---------------------------------------------------------------------------
// Backfill Script — Track Difficulty Classification
// ---------------------------------------------------------------------------
// Idempotent: only updates rows WHERE difficulty IS NULL or invalid.
// Batched: fetches and processes BATCH_SIZE rows at a time (no full table load).
// Crash-safe: partial runs leave already-updated rows intact.
//
// Usage:
//   npx tsx scripts/backfill-difficulty.ts
// ---------------------------------------------------------------------------

import 'dotenv/config';
import { Pool } from 'pg';
import { decodeBlobToFloats } from '../src/utils/blob-decoder';
import { classifyDifficulty } from '../src/services/track-generator';
import {
    DIFFICULTY_EASY,
    DIFFICULTY_MEDIUM,
    DIFFICULTY_HARD,
} from '../src/constants';

const DATABASE_URL = process.env['DATABASE_URL'];
if (!DATABASE_URL) {
    console.error('DATABASE_URL is not set');
    process.exit(1);
}

const BATCH_SIZE = 50;

interface TrackRow {
    token_mint: string;
    track_hour_start_utc: Date;
    track_version: string;
    normalized_points_blob: Buffer;
    point_count: number;
}

function rowKey(row: TrackRow): string {
    return `${row.token_mint} | ${(row.track_hour_start_utc as Date).toISOString()} | ${row.track_version}`;
}

async function main(): Promise<void> {
    const pool = new Pool({ connectionString: DATABASE_URL });

    const counts = { [DIFFICULTY_EASY]: 0, [DIFFICULTY_MEDIUM]: 0, [DIFFICULTY_HARD]: 0 };
    let totalProcessed = 0;
    let errors = 0;
    let batchNum = 0;

    console.log(`\n=== Difficulty Backfill ===\n`);

    try {
        // Batched loop: fetch BATCH_SIZE rows at a time until none remain
        while (true) {
            const { rows } = await pool.query<TrackRow>(`
                SELECT token_mint, track_hour_start_utc, track_version,
                       normalized_points_blob, point_count
                FROM track_buckets
                WHERE difficulty IS NULL
                   OR difficulty NOT IN (0, 1, 2)
                ORDER BY track_hour_start_utc ASC
                LIMIT $1
            `, [BATCH_SIZE]);

            if (rows.length === 0) break;

            batchNum++;
            const client = await pool.connect();
            try {
                await client.query('BEGIN');

                for (const row of rows) {
                    try {
                        const expectedBytes = row.point_count * 2;
                        if (!row.normalized_points_blob || row.normalized_points_blob.length < expectedBytes) {
                            console.error(`  Skipping ${rowKey(row)}: blob too short (${row.normalized_points_blob?.length ?? 0} < ${expectedBytes})`);
                            errors++;
                            continue;
                        }

                        const floats = decodeBlobToFloats(row.normalized_points_blob, row.point_count);
                        const difficulty = classifyDifficulty(floats);

                        await client.query(
                            `UPDATE track_buckets SET difficulty = $1
                             WHERE token_mint = $2
                               AND track_hour_start_utc = $3
                               AND track_version = $4`,
                            [difficulty, row.token_mint, row.track_hour_start_utc, row.track_version],
                        );

                        counts[difficulty]++;
                    } catch (err) {
                        console.error(`  Error processing ${rowKey(row)}:`, err);
                        errors++;
                    }
                }

                await client.query('COMMIT');
            } catch (err) {
                await client.query('ROLLBACK');
                throw err;
            } finally {
                client.release();
            }

            totalProcessed += rows.length;
            console.log(`  Batch ${batchNum}: processed ${totalProcessed} rows so far`);
        }

        if (totalProcessed === 0) {
            console.log('Nothing to do — all rows already classified.\n');
            return;
        }

        console.log(`\n=== Backfill Complete ===`);
        console.log(`  Total:  ${totalProcessed}`);
        console.log(`  Easy:   ${counts[DIFFICULTY_EASY]}`);
        console.log(`  Medium: ${counts[DIFFICULTY_MEDIUM]}`);
        console.log(`  Hard:   ${counts[DIFFICULTY_HARD]}`);
        if (errors > 0) {
            console.log(`  Errors: ${errors}`);
        }
        console.log();
    } finally {
        await pool.end();
    }
}

main().catch((err) => {
    console.error('Backfill failed:', err);
    process.exit(1);
});
