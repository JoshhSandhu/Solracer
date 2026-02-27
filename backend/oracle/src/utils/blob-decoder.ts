// ---------------------------------------------------------------------------
// Oracle Pipeline — Blob Decoder Utility
// ---------------------------------------------------------------------------
// Converts quantized Int16LE blobs back to normalized float arrays.
// Used by backfill scripts, debugging, and backend-v2.
// ---------------------------------------------------------------------------

/**
 * Decode a quantized Int16LE blob into normalized floats (0..1).
 *
 * Each value was encoded as `Math.round(float * 32767)` during quantization.
 * This reverses that: `value / 32767`.
 *
 * @param blob       Raw BYTEA buffer from `normalized_points_blob`
 * @param pointCount Number of points (blob.length must be pointCount * 2)
 * @returns          Float array with values in [0, 1]
 */
export function decodeBlobToFloats(blob: Buffer, pointCount: number): number[] {
    if (pointCount <= 0) return [];

    const expectedBytes = pointCount * 2;
    if (blob.length < expectedBytes) {
        throw new Error(
            `Blob too short: expected ${expectedBytes} bytes for ${pointCount} points, got ${blob.length}`,
        );
    }

    const floats = new Array<number>(pointCount);
    for (let i = 0; i < pointCount; i++) {
        floats[i] = blob.readInt16LE(i * 2) / 32767;
    }

    return floats;
}
