// ---------------------------------------------------------------------------
// Tests  Difficulty Classifier + Blob Decoder
// ---------------------------------------------------------------------------

import { describe, it, expect } from 'vitest';
import { classifyDifficulty } from '../src/services/track-generator';
import { decodeBlobToFloats } from '../src/utils/blob-decoder';
import {
    DIFFICULTY_EASY,
    DIFFICULTY_MEDIUM,
    DIFFICULTY_HARD,
} from '../src/constants';

// ---------------------------------------------------------------------------
// classifyDifficulty
// ---------------------------------------------------------------------------

describe('classifyDifficulty', () => {
    it('classifies a flat track as Easy', () => {
        // All values identical → verticalRange = 0, slopes = 0 → score = 0
        const flat = new Array(200).fill(0.5);
        expect(classifyDifficulty(flat)).toBe(DIFFICULTY_EASY);
    });

    it('classifies a gently sloping track as Easy', () => {
        // Linear ramp from 0.5 to 0.52 over 200 points
        const gentle = Array.from({ length: 200 }, (_, i) => 0.5 + (i / 199) * 0.02);
        expect(classifyDifficulty(gentle)).toBe(DIFFICULTY_EASY);
    });

    it('classifies medium volatility as Medium', () => {
        // Sine wave with moderate amplitude
        const medium = Array.from({ length: 200 }, (_, i) =>
            0.5 + 0.12 * Math.sin((i / 200) * Math.PI * 4),
        );
        expect(classifyDifficulty(medium)).toBe(DIFFICULTY_MEDIUM);
    });

    it('classifies high volatility as Hard', () => {
        // Rapid zigzag between 0 and 1
        const hard = Array.from({ length: 200 }, (_, i) => (i % 2 === 0 ? 0.0 : 1.0));
        expect(classifyDifficulty(hard)).toBe(DIFFICULTY_HARD);
    });

    it('returns Easy for empty array', () => {
        expect(classifyDifficulty([])).toBe(DIFFICULTY_EASY);
    });

    it('returns Easy for single point', () => {
        expect(classifyDifficulty([0.5])).toBe(DIFFICULTY_EASY);
    });

    it('never returns NaN', () => {
        const weird = [0, 0, 0, 0, 0];
        const result = classifyDifficulty(weird);
        expect(result).not.toBeNaN();
        expect(result).toBe(DIFFICULTY_EASY);
    });

    it('always returns 0, 1, or 2', () => {
        const cases = [
            new Array(100).fill(0.5),                                              // flat
            Array.from({ length: 100 }, (_, i) => 0.5 + 0.1 * Math.sin(i / 10)), // medium
            Array.from({ length: 100 }, (_, i) => i % 2),                         // hard
        ];

        for (const heights of cases) {
            const d = classifyDifficulty(heights);
            expect(d).toBeGreaterThanOrEqual(0);
            expect(d).toBeLessThanOrEqual(2);
            expect(Number.isInteger(d)).toBe(true);
        }
    });

    it('is deterministic  same input always produces same output', () => {
        const track = Array.from({ length: 500 }, (_, i) =>
            0.5 + 0.15 * Math.sin((i / 500) * Math.PI * 6),
        );
        const result1 = classifyDifficulty(track);
        const result2 = classifyDifficulty(track);
        const result3 = classifyDifficulty(track);
        expect(result1).toBe(result2);
        expect(result2).toBe(result3);
    });

    it('works with any pointCount', () => {
        for (const count of [1, 2, 10, 100, 1000, 5000]) {
            const track = Array.from({ length: count }, (_, i) => (i / Math.max(count - 1, 1)));
            const d = classifyDifficulty(track);
            expect(d).toBeGreaterThanOrEqual(0);
            expect(d).toBeLessThanOrEqual(2);
        }
    });
});

// ---------------------------------------------------------------------------
// decodeBlobToFloats
// ---------------------------------------------------------------------------

describe('decodeBlobToFloats', () => {
    it('round-trips quantized values correctly', () => {
        const original = [0.0, 0.25, 0.5, 0.75, 1.0];
        const pointCount = original.length;

        // Quantize (same logic as track-generator)
        const blob = Buffer.alloc(pointCount * 2);
        for (let i = 0; i < pointCount; i++) {
            let value = Math.round(original[i] * 32767);
            if (value > 32767) value = 32767;
            if (value < 0) value = 0;
            blob.writeInt16LE(value, i * 2);
        }

        const decoded = decodeBlobToFloats(blob, pointCount);

        expect(decoded).toHaveLength(pointCount);
        for (let i = 0; i < pointCount; i++) {
            // Allow 1-bit rounding error (1/32767 ≈ 0.00003)
            expect(decoded[i]).toBeCloseTo(original[i], 4);
        }
    });

    it('returns empty array for pointCount 0', () => {
        const blob = Buffer.alloc(0);
        expect(decodeBlobToFloats(blob, 0)).toEqual([]);
    });

    it('throws if blob is too short', () => {
        const blob = Buffer.alloc(4); // 2 points worth
        expect(() => decodeBlobToFloats(blob, 10)).toThrow('Blob too short');
    });
});
