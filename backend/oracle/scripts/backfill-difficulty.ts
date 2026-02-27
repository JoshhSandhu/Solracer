// ---------------------------------------------------------------------------
// Backfill Script — Track Difficulty Classification
// ---------------------------------------------------------------------------
// Idempotent: only updates rows WHERE difficulty IS NULL or invalid.
// Batched in groups of 50.
// Safe to run multiple times.
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
    id: number;
    normalized_points_blob: Buffer;
    point_count: number;
    difficulty: number | null;
}

async function main(): Promise<void> {
    const pool = new Pool({ connectionString: DATABASE_URL });

    try {
        // Select rows that need backfill: NULL difficulty or invalid values
        const { rows } = await pool.query<TrackRow>(`
      SELECT id, normalized_points_blob, point_count, difficulty
      FROM track_buckets
      WHERE difficulty IS NULL
         OR difficulty NOT IN (0, 1, 2)
      ORDER BY id ASC
    `);

        console.log(`\n=== Difficulty Backfill ===`);
        console.log(`Found ${rows.length} rows to backfill\n`);

        if (rows.length === 0) {
            console.log('Nothing to do — all rows already classified.');
            return;
        }

        // Counters for summary
        const counts = { [DIFFICULTY_EASY]: 0, [DIFFICULTY_MEDIUM]: 0, [DIFFICULTY_HARD]: 0 };
        let errors = 0;

        // Process in batches
        for (let batchStart = 0; batchStart < rows.length; batchStart += BATCH_SIZE) {
            const batch = rows.slice(batchStart, batchStart + BATCH_SIZE);

            // Build batch UPDATE using a transaction
            const client = await pool.connect();
            try {
                await client.query('BEGIN');

                for (const row of batch) {
                    try {
                        const floats = decodeBlobToFloats(row.normalized_points_blob, row.point_count);
                        const difficulty = classifyDifficulty(floats);

                        await client.query(
                            'UPDATE track_buckets SET difficulty = $1 WHERE id = $2',
                            [difficulty, row.id],
                        );

                        counts[difficulty]++;
                    } catch (err) {
                        console.error(`Error processing row ${row.id}:`, err);
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

            const processed = Math.min(batchStart + BATCH_SIZE, rows.length);
            console.log(`  Batch ${Math.floor(batchStart / BATCH_SIZE) + 1}: processed ${processed}/${rows.length}`);
        }

        // Summary
        console.log(`\n=== Backfill Complete ===`);
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
