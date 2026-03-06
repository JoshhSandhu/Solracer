using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Solracer.Config;

namespace Solracer.Network
{
    /// <summary>
    /// Manages the on-chain lifecycle for the ghost position PDA:
    ///   1. Generate an ephemeral session keypair
    ///   2. init_position_pda on base devnet
    ///   3. delegate_position_pda to MagicBlock ER
    ///   4. Wait for ER sync
    ///   5. Return session keypair for ErGhostRelay
    ///
    /// Also handles close_position_pda at race end to reclaim rent.
    /// </summary>
    public static class ErLifecycleManager
    {
        // MagicBlock delegation program on devnet
        private const string DELEGATION_PROGRAM_ID = "DELeGGvXpWV2fqJUhqcF5ZSYMS4JTLjteaAMARRSaeSS";

        /// <summary>
        /// Result of a successful init + delegate flow.
        /// </summary>
        public class InitResult
        {
            public byte[] SessionPrivateKey;  // 64-byte Ed25519 expanded key
            public byte[] SessionPublicKey;   // 32-byte public key
            public string PositionPdaBase58;
        }

        /// <summary>
        /// Initialize and delegate a PlayerPosition PDA for this race.
        /// Must be called BEFORE starting the ErGhostRelay.
        ///
        /// Flow:
        ///   1. Generate session keypair
        ///   2. Send init_position_pda to base devnet (signed by player wallet)
        ///   3. Send delegate_position_pda to base devnet (signed by player wallet)
        ///   4. Wait 3s for ER sync
        /// </summary>
        /// <param name="raceId">Human-readable race ID</param>
        /// <param name="playerWalletBase58">Player's wallet pubkey</param>
        /// <param name="signAndSendCallback">
        ///   Callback that takes a base64-encoded transaction, signs it with the player's
        ///   wallet, and returns the transaction signature. This bridges to the existing
        ///   wallet auth flow (Privy or MWA).
        /// </param>
        public static async Task<InitResult> InitAndDelegateAsync(
            string raceId,
            string playerWalletBase58,
            Func<string, Task<string>> signAndSendCallback)
        {
            Debug.Log($"[ErLifecycleManager] Starting init+delegate for race={raceId}");

            // 1. Generate ephemeral session keypair
            byte[] seed = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(seed);

            // Ed25519 keypair from seed using Chaos.NaCl
            Chaos.NaCl.Ed25519.KeyPairFromSeed(out byte[] sessionPublic, out byte[] sessionPrivate, seed);

            Debug.Log($"[ErLifecycleManager] Session key: {ErGhostRelay.Base58Encode(sessionPublic)}");

            // 2. Compute race hash and derive PDA
            using var sha = SHA256.Create();
            byte[] raceHash = sha.ComputeHash(Encoding.UTF8.GetBytes(raceId));
            byte[] playerWalletBytes = ErGhostRelay.Base58Decode(playerWalletBase58);
            byte[] programIdBytes = ErGhostRelay.Base58Decode(ErGhostRelay.PROGRAM_ID);

            // Derive PDA (we need the bump too for delegate)
            string pdaBase58 = null;
            for (int bump = 255; bump >= 0; bump--)
            {
                byte[] candidate = ComputePdaHash(
                    new[] {
                        Encoding.UTF8.GetBytes("position"),
                        raceHash,
                        playerWalletBytes,
                        new[] { (byte)bump }
                    },
                    programIdBytes
                );
                if (candidate != null)
                {
                    pdaBase58 = ErGhostRelay.Base58Encode(candidate);
                    break;
                }
            }

            if (pdaBase58 == null)
                throw new Exception("[ErLifecycleManager] Failed to derive PDA");

            Debug.Log($"[ErLifecycleManager] PDA: {pdaBase58}");

            // 3. Build and send init_position_pda transaction
            //    This requires the player's wallet to sign (it's the payer).
            //    We build the instruction data and transaction, then pass to the
            //    signAndSendCallback which bridges to Privy/MWA signing.

            // For now, log that the init flow needs to be triggered externally.
            // The full transaction building for init_position_pda and delegate_position_pda
            // requires more complex account resolution (system program, delegation PDAs).
            // This will be connected to the wallet signing flow in the next iteration.

            Debug.Log("[ErLifecycleManager] Init+delegate flow prepared. " +
                      "Connect to wallet signing in next iteration.");

            // TODO: Build init_position_pda transaction
            //   instruction data = discriminator(SHA256("global:init_position_pda")[0..8])
            //                    + raceHash (32 bytes)
            //                    + sessionPublicKey (32 bytes)
            //   accounts = [position PDA (init), player (signer, mut), systemProgram]

            // TODO: Build delegate_position_pda transaction
            //   accounts = [player (signer, mut), position (mut), buffer, delegationRecord,
            //               delegationMetadata, delegationProgram, program, systemProgram]

            // For now, return the session keypair so ErGhostRelay can be tested
            // if the init+delegate is done externally via TypeScript
            return new InitResult
            {
                SessionPrivateKey = sessionPrivate,
                SessionPublicKey  = sessionPublic,
                PositionPdaBase58 = pdaBase58
            };
        }

        /// <summary>
        /// Close the position PDA and reclaim rent after race ends.
        /// </summary>
        public static async Task ClosePositionPdaAsync(
            string raceId,
            string playerWalletBase58,
            Func<string, Task<string>> signAndSendCallback)
        {
            // TODO: Build close_position_pda transaction and sign via callback
            Debug.Log("[ErLifecycleManager] ClosePositionPda — to be connected to wallet signing");
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static byte[] ComputePdaHash(byte[][] seeds, byte[] programId)
        {
            using var sha = SHA256.Create();
            var buffer = new List<byte>();
            foreach (var seed in seeds)
                buffer.AddRange(seed);
            buffer.AddRange(programId);
            buffer.AddRange(Encoding.UTF8.GetBytes("ProgramDerivedAddress"));
            return sha.ComputeHash(buffer.ToArray());
        }
    }
}
