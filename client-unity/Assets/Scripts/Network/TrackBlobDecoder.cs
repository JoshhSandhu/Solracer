using System;
using UnityEngine;

namespace Solracer.Network
{
    /// <summary>
    /// Decodes Base64-encoded track blob from Backend-v2 into normalized float array
    /// Blob format: each point = 2 bytes Int16LE, range 0–32767
    /// Normalized float = value / 32767f, clamped to [0, 1]
    /// </summary>
    public static class TrackBlobDecoder
    {
        /// <summary>
        /// Decode a Base64-encoded blob into normalized float array
        /// Returns null on failure
        /// </summary>
        /// <param name="base64">Base64-encoded blob string</param>
        /// <param name="pointCount">Expected number of points</param>
        public static float[] DecodeBase64Blob(string base64, int pointCount)
        {
            if (string.IsNullOrEmpty(base64))
            {
                Debug.LogError("[TrackBlobDecoder] Base64 string is null or empty");
                return null;
            }

            if (pointCount < 2)
            {
                Debug.LogError($"[TrackBlobDecoder] Invalid pointCount: {pointCount}");
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                Debug.LogError($"[TrackBlobDecoder] Invalid Base64: {ex.Message}");
                return null;
            }

            // Validate blob size: each point = 2 bytes (Int16)
            int expectedBytes = pointCount * 2;
            if (bytes.Length != expectedBytes)
            {
                Debug.LogError($"[TrackBlobDecoder] Blob size mismatch: expected {expectedBytes} bytes, got {bytes.Length}");
                return null;
            }

            float[] result = new float[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                short value = BitConverter.ToInt16(bytes, i * 2);
                float normalized = value / 32767f;
                result[i] = Mathf.Clamp01(normalized);
            }

            return result;
        }
    }
}
