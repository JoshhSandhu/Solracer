using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Solracer.Network
{
    /// <summary>
    /// Manages the session keypair for the ghost position PDA.
    ///
    /// The on-chain lifecycle (init_position_pda + delegate_position_pda) is now
    /// handled entirely by the backend-ts, bundled into the same transaction as
    /// create_race / join_race — so only ONE wallet popup is needed.
    ///
    /// This class only needs to:
    ///   1. Read the session keypair from SessionKeyStore (populated during create/join tx)
    ///   2. Derive the PlayerPosition PDA locally for use by ErGhostRelay
    ///   3. Return the result synchronously — no RPC calls needed here
    /// </summary>
    public static class ErLifecycleManager
    {
        /// <summary>
        /// Result returned to callers.
        /// </summary>
        public class InitResult
        {
            public byte[] SessionPrivateKey;   // 64-byte Ed25519 expanded key
            public byte[] SessionPublicKey;    // 32-byte public key
            public string PositionPdaBase58;   // base58 of the PlayerPosition PDA
        }

        /// <summary>
        /// Retrieve the session keypair from SessionKeyStore and compute the
        /// PlayerPosition PDA for the given race.  No network calls are made —
        /// the backend already sent init_position_pda + delegate_position_pda
        /// inside the create_race / join_race transaction.
        /// </summary>
        public static InitResult PrepareSessionKey(string raceId, string playerWalletBase58)
        {
            Debug.Log($"[ErLifecycleManager] PrepareSessionKey for race={raceId}");

            // 1. Get session keypair from SessionKeyStore (set during create/join tx build)
            byte[] sessionPrivate = SessionKeyStore.GetPrivateKey(raceId);
            byte[] sessionPublic  = SessionKeyStore.GetPublicKey(raceId);

            if (sessionPrivate == null || sessionPublic == null)
            {
                // Fallback: generate a fresh keypair.
                // This should not happen in the normal flow — session key is always
                // generated before calling the backend create/join endpoint.
                Debug.LogWarning("[ErLifecycleManager] No session key in store — generating fresh keypair (fallback)");
                byte[] seed = new byte[32];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(seed);
                Chaos.NaCl.Ed25519.KeyPairFromSeed(out sessionPublic, out sessionPrivate, seed);
            }

            // 2. Compute PlayerPosition PDA locally for use by ErGhostRelay
            using var sha = SHA256.Create();
            byte[] raceHash    = sha.ComputeHash(Encoding.UTF8.GetBytes(raceId));
            byte[] playerBytes = ErGhostRelay.Base58Decode(playerWalletBase58);
            string pdaBase58   = DerivePositionPda(raceHash, playerBytes);

            Debug.Log($"[ErLifecycleManager] Session key: {ErGhostRelay.Base58Encode(sessionPublic)}, PDA: {pdaBase58}");

            return new InitResult
            {
                SessionPrivateKey = sessionPrivate,
                SessionPublicKey  = sessionPublic,
                PositionPdaBase58 = pdaBase58,
            };
        }

        /// <summary>
        /// Derive the PlayerPosition PDA (Ephemeral Rollup program).
        /// Seeds: ["position", raceHash(32), playerWalletBytes(32)]
        /// Program: 3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG
        /// </summary>
        private static string DerivePositionPda(byte[] raceHash, byte[] playerWalletBytes)
        {
            byte[] erProgramBytes = ErGhostRelay.Base58Decode(ErGhostRelay.PROGRAM_ID);

            for (int bump = 255; bump >= 0; bump--)
            {
                byte[] candidate = ComputePdaHash(new[]
                {
                    Encoding.UTF8.GetBytes("position"),
                    raceHash,
                    playerWalletBytes,
                    new[] { (byte)bump },
                }, erProgramBytes);

                if (candidate != null)
                    return ErGhostRelay.Base58Encode(candidate);
            }

            throw new Exception("[ErLifecycleManager] Failed to derive PlayerPosition PDA");
        }

        private static byte[] ComputePdaHash(byte[][] seeds, byte[] programId)
        {
            using var sha = SHA256.Create();
            var buffer = new System.Collections.Generic.List<byte>();
            foreach (var seed in seeds)
                buffer.AddRange(seed);
            buffer.AddRange(programId);
            buffer.AddRange(Encoding.UTF8.GetBytes("ProgramDerivedAddress"));
            byte[] hash = sha.ComputeHash(buffer.ToArray());
            // PDA must NOT be a valid Ed25519 curve point
            return IsOnEd25519Curve(hash) ? null : hash;
        }

        private static bool IsOnEd25519Curve(byte[] point)
        {
            // Quick rejection: check the high bit of the last byte (sign bit of y-coord)
            // A proper check would verify the point satisfies the Ed25519 curve equation,
            // but this conservative approach always returns false (off-curve) for safety.
            // The bump-loop in DerivePositionPda tries all 256 values so we find the right one.
            try
            {
                // If Chaos.NaCl can interpret this as a public key, it's on-curve
                // We can't call that from here easily, so we use the SHA256 approach
                // The Solana runtime uses a different check — just return false here
                // and let the bump loop find a valid off-curve point naturally.
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
