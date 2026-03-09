using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Solracer.Network
{
    /// <summary>
    /// In-memory store for the ephemeral session keypair generated before each race.
    ///
    /// Lifecycle:
    ///   1. RaceAPIClient generates keypair before calling create_race / join_race
    ///      and stores it here.
    ///   2. The backend bundles delegate_session into the same transaction.
    ///   3. After the race, OnChainRaceManager retrieves the keypair to sign
    ///      submit_result and claim_prize silently (no wallet popup).
    ///   4. Clear() is called on race reset / scene unload.
    /// </summary>
    public static class SessionKeyStore
    {
        private static byte[] _sessionPrivateKey; // 64-byte Ed25519 expanded key
        private static byte[] _sessionPublicKey;  // 32-byte Ed25519 public key
        private static string _raceId;
        private static string _playerWallet;

        // -----------------------------------------------------------------------
        // Save / Get / Clear
        // -----------------------------------------------------------------------

        /// <summary>
        /// Generate and store a fresh Ed25519 session keypair for the given race.
        /// Called by RaceAPIClient just before building the create_race / join_race tx.
        /// Returns the base58 session public key to include in the build request.
        /// </summary>
        public static string GenerateAndSave(string raceId, string playerWallet)
        {
            byte[] seed = new byte[32];
            RandomNumberGenerator.Create().GetBytes(seed);

            Chaos.NaCl.Ed25519.KeyPairFromSeed(
                out byte[] pub,
                out byte[] priv,
                seed
            );

            _sessionPublicKey  = pub;
            _sessionPrivateKey = priv;
            _raceId            = raceId;
            _playerWallet      = playerWallet;

            string pubBase58 = ErGhostRelay.Base58Encode(pub);
            Debug.Log($"[SessionKeyStore] Generated session key for race={raceId} key={pubBase58}");
            return pubBase58;
        }

        /// <summary>
        /// Returns true if a session key exists for the given raceId.
        /// </summary>
        public static bool HasSession(string raceId) =>
            _raceId == raceId && _sessionPrivateKey != null;

        /// <summary>
        /// Returns the session public key as base58, or null if none stored.
        /// </summary>
        public static string GetPublicKeyBase58(string raceId)
        {
            if (!HasSession(raceId)) return null;
            return ErGhostRelay.Base58Encode(_sessionPublicKey);
        }

        /// <summary>
        /// Returns the session private key bytes (64-byte expanded Ed25519).
        /// </summary>
        public static byte[] GetPrivateKey(string raceId)
        {
            if (!HasSession(raceId)) return null;
            return _sessionPrivateKey;
        }

        /// <summary>
        /// Returns the session public key bytes (32 bytes).
        /// </summary>
        public static byte[] GetPublicKey(string raceId)
        {
            if (!HasSession(raceId)) return null;
            return _sessionPublicKey;
        }

        /// <summary>
        /// Move the existing session key from one raceId to another WITHOUT generating
        /// a new keypair. Used to re-register a "__pending__" key under the real raceId
        /// after the create_race build response arrives.
        /// </summary>
        public static void ReRegister(string newRaceId)
        {
            if (_sessionPrivateKey == null)
            {
                Debug.LogWarning($"[SessionKeyStore] ReRegister called but no session key exists");
                return;
            }
            string oldRaceId = _raceId;
            _raceId = newRaceId;
            Debug.Log($"[SessionKeyStore] Re-registered session key from race={oldRaceId} to race={newRaceId}");
        }

        /// <summary>
        /// Clears the stored session. Call on race reset / results screen.
        /// </summary>
        public static void Clear()
        {
            _sessionPrivateKey = null;
            _sessionPublicKey  = null;
            _raceId            = null;
            _playerWallet      = null;
            Debug.Log("[SessionKeyStore] Session cleared");
        }

        // -----------------------------------------------------------------------
        // Session signing helper
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sign a Solana transaction message with the stored session key (Ed25519).
        /// Returns the 64-byte Ed25519 signature only (not sig+message).
        /// Used by OnChainRaceManager for silent submit_result / claim_prize.
        /// </summary>
        public static byte[] Sign(string raceId, byte[] message)
        {
            if (!HasSession(raceId))
                throw new InvalidOperationException($"[SessionKeyStore] No session for race={raceId}");

            // Chaos.NaCl.Ed25519.Sign returns [64-byte sig][message] (NaCl convention).
            // Solana needs only the 64-byte signature.
            byte[] signedMessage = Chaos.NaCl.Ed25519.Sign(message, _sessionPrivateKey);
            byte[] sig = new byte[64];
            Array.Copy(signedMessage, 0, sig, 0, 64);
            return sig;
        }
    }
}
