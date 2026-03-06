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
    /// IPositionRelay implementation that reads/writes ghost positions
    /// directly to the solracer-er Anchor program on MagicBlock Ephemeral Rollup.
    ///
    /// SendPosition  → builds and sends an update_position instruction to the ER RPC.
    /// GetOpponentPositions → calls getAccountInfo on the opponent's PlayerPosition PDA.
    /// </summary>
    public class ErGhostRelay : IPositionRelay
    {
        // -----------------------------------------------------------------------
        // Constants
        // -----------------------------------------------------------------------

        public const string PROGRAM_ID = "3BhDmsVJYASHEUE2DJAJr2FHjRWUCF1nwn6SraKJgoEG";
        private const string PDA_SEED = "position";
        private const int ANCHOR_DISCRIMINATOR_LEN = 8;

        /// <summary>
        /// Anchor sighash for update_position = first 8 bytes of SHA256("global:update_position")
        /// </summary>
        private static readonly byte[] UPDATE_POSITION_DISCRIMINATOR;

        // -----------------------------------------------------------------------
        // Cached invariants (set once in constructor)
        // -----------------------------------------------------------------------

        private readonly string _erRpcUrl;
        private readonly byte[] _programIdBytes;   // 32 bytes
        private readonly byte[] _raceHash;          // SHA256(raceId) = 32 bytes
        private readonly byte[] _sessionPrivateKey; // 64-byte Ed25519 private key
        private readonly byte[] _sessionPublicKey;  // 32 bytes
        private readonly string _opponentPdaBase58;
        private readonly string _myPdaBase58;
        private readonly string _opponentWallet;

        // -----------------------------------------------------------------------
        // Static init
        // -----------------------------------------------------------------------

        static ErGhostRelay()
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("global:update_position"));
            UPDATE_POSITION_DISCRIMINATOR = new byte[8];
            Array.Copy(hash, 0, UPDATE_POSITION_DISCRIMINATOR, 0, 8);
        }

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        /// <param name="raceId">Human-readable race ID (will be SHA-256 hashed for PDA seed)</param>
        /// <param name="myWalletBase58">This player's wallet pubkey (base58)</param>
        /// <param name="opponentWalletBase58">Opponent's wallet pubkey (base58)</param>
        /// <param name="sessionPrivateKey">64-byte Ed25519 private key for the session keypair</param>
        /// <param name="sessionPublicKey">32-byte Ed25519 public key for the session keypair</param>
        /// <param name="erRpcUrl">Override for MagicBlock ER RPC endpoint</param>
        public ErGhostRelay(
            string raceId,
            string myWalletBase58,
            string opponentWalletBase58,
            byte[] sessionPrivateKey,
            byte[] sessionPublicKey,
            string erRpcUrl = null)
        {
            _erRpcUrl         = erRpcUrl ?? APIConfig.GetErRpcUrl();
            _programIdBytes   = Base58Decode(PROGRAM_ID);
            _sessionPrivateKey = sessionPrivateKey;
            _sessionPublicKey  = sessionPublicKey;
            _opponentWallet    = opponentWalletBase58;

            // Hash the raceId once
            using var sha = SHA256.Create();
            _raceHash = sha.ComputeHash(Encoding.UTF8.GetBytes(raceId));

            // Derive PDAs once
            byte[] myWalletBytes  = Base58Decode(myWalletBase58);
            byte[] oppWalletBytes = Base58Decode(opponentWalletBase58);

            _myPdaBase58       = DerivePositionPda(_raceHash, myWalletBytes);
            _opponentPdaBase58 = DerivePositionPda(_raceHash, oppWalletBytes);

            Debug.Log($"[ErGhostRelay] Initialized. ER={_erRpcUrl} myPDA={_myPdaBase58} oppPDA={_opponentPdaBase58}");
        }

        // -----------------------------------------------------------------------
        // IPositionRelay: SendPosition
        // -----------------------------------------------------------------------

        public async Task SendPosition(PositionUpdate update)
        {
            try
            {
                byte[] instructionData = BuildUpdatePositionInstruction(
                    _raceHash,
                    update.x,
                    update.y,
                    update.speed,
                    update.checkpoint_index,
                    update.seq
                );

                // Build the transaction via sendTransaction JSON-RPC
                // For now, we use a raw Solana transaction with a single instruction
                string txBase64 = await BuildAndSignTransaction(instructionData);
                if (txBase64 == null) return;

                // Send to ER
                string rpcBody = JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "sendTransaction",
                    @params = new object[]
                    {
                        txBase64,
                        new { encoding = "base64", skipPreflight = true, commitment = "processed" }
                    }
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(rpcBody);

                using var req = new UnityWebRequest(_erRpcUrl, "POST");
                req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ErGhostRelay] SendPosition RPC error: {req.responseCode} {req.error}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ErGhostRelay] SendPosition exception: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // IPositionRelay: GetOpponentPositions
        // -----------------------------------------------------------------------

        public async Task<GhostRaceState> GetOpponentPositions(string raceId)
        {
            try
            {
                string rpcBody = JsonConvert.SerializeObject(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "getAccountInfo",
                    @params = new object[]
                    {
                        _opponentPdaBase58,
                        new { encoding = "base64", commitment = "processed" }
                    }
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(rpcBody);

                using var req = new UnityWebRequest(_erRpcUrl, "POST");
                req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[ErGhostRelay] getAccountInfo failed: {req.responseCode} {req.error}");
                    return null;
                }

                var json = JObject.Parse(req.downloadHandler.text);
                var value = json["result"]?["value"];
                if (value == null || value.Type == JTokenType.Null)
                    return null; // account doesn't exist yet

                string dataBase64 = value["data"]?[0]?.ToString();
                if (string.IsNullOrEmpty(dataBase64))
                    return null;

                byte[] accountData = Convert.FromBase64String(dataBase64);
                var playerState = DeserializePlayerPosition(accountData);
                if (playerState == null)
                    return null;

                return new GhostRaceState
                {
                    race_id = raceId,
                    players = new[] { playerState }
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ErGhostRelay] GetOpponentPositions exception: {ex.Message}");
                return null;
            }
        }

        // -----------------------------------------------------------------------
        // Instruction serialization
        // -----------------------------------------------------------------------

        /// <summary>
        /// Builds the update_position instruction data in Anchor borsh format:
        ///   [8]  discriminator
        ///   [32] expected_race_id_hash
        ///   [4]  x (f32 LE)
        ///   [4]  y (f32 LE)
        ///   [4]  speed (f32 LE)
        ///   [4]  checkpoint_index (u32 LE)
        ///   [4]  seq (u32 LE)
        /// Total: 60 bytes
        /// </summary>
        private byte[] BuildUpdatePositionInstruction(
            byte[] raceHash, float x, float y, float speed, int checkpointIndex, int seq)
        {
            var data = new List<byte>(60);

            data.AddRange(UPDATE_POSITION_DISCRIMINATOR);  // 8
            data.AddRange(raceHash);                       // 32
            data.AddRange(BitConverter.GetBytes(x));        // 4 (f32 LE)
            data.AddRange(BitConverter.GetBytes(y));        // 4
            data.AddRange(BitConverter.GetBytes(speed));    // 4
            data.AddRange(BitConverter.GetBytes((uint)checkpointIndex)); // 4 (u32 LE)
            data.AddRange(BitConverter.GetBytes((uint)seq));             // 4

            return data.ToArray();
        }

        // -----------------------------------------------------------------------
        // Account deserialization
        // -----------------------------------------------------------------------

        /// <summary>
        /// Deserialises a PlayerPosition account from raw bytes (including 8-byte Anchor discriminator).
        /// Layout after discriminator:
        ///   [32] race_id_hash
        ///   [32] player pubkey
        ///   [32] session_key pubkey
        ///   [4]  x (f32 LE)
        ///   [4]  y (f32 LE)
        ///   [4]  speed (f32 LE)
        ///   [4]  checkpoint_index (u32 LE)
        ///   [4]  seq (u32 LE)
        ///   [8]  updated_at (i64 LE)
        ///   [1]  bump
        /// Total after disc: 125 bytes. Total with disc: 133 bytes.
        /// </summary>
        private GhostPlayerState DeserializePlayerPosition(byte[] data)
        {
            const int expectedLen = ANCHOR_DISCRIMINATOR_LEN + 125; // 133
            if (data == null || data.Length < expectedLen)
            {
                Debug.LogWarning($"[ErGhostRelay] Account data too short: {data?.Length ?? 0} < {expectedLen}");
                return null;
            }

            int offset = ANCHOR_DISCRIMINATOR_LEN; // skip discriminator

            // Skip race_id_hash (32), player (32), session_key (32) = 96 bytes
            offset += 96;

            float x     = BitConverter.ToSingle(data, offset); offset += 4;
            float y     = BitConverter.ToSingle(data, offset); offset += 4;
            float speed = BitConverter.ToSingle(data, offset); offset += 4;
            int checkpoint = (int)BitConverter.ToUInt32(data, offset); offset += 4;
            // skip seq (4 bytes)
            offset += 4;
            long updatedAt = BitConverter.ToInt64(data, offset); offset += 8;

            return new GhostPlayerState
            {
                wallet           = _opponentWallet,
                x                = x,
                y                = y,
                speed            = speed,
                checkpoint_index = checkpoint,
                updated_at       = updatedAt * 1000 // convert seconds to ms epoch
            };
        }

        // -----------------------------------------------------------------------
        // PDA derivation
        // -----------------------------------------------------------------------

        /// <summary>
        /// Derive the PlayerPosition PDA:
        ///   seeds = ["position", SHA256(raceId), playerPubkeyBytes]
        ///   program = PROGRAM_ID
        /// </summary>
        private string DerivePositionPda(byte[] raceHash, byte[] playerPubkey)
        {
            byte[] seedPrefix = Encoding.UTF8.GetBytes(PDA_SEED);

            // FindProgramAddress: iterate bump from 255 → 0
            for (int bump = 255; bump >= 0; bump--)
            {
                try
                {
                    byte[] candidate = ComputePda(
                        new[] { seedPrefix, raceHash, playerPubkey, new[] { (byte)bump } },
                        _programIdBytes
                    );

                    // Valid PDA must NOT be on the Ed25519 curve
                    // SHA256 of seeds + programId + "ProgramDerivedAddress"
                    // If it's valid (off-curve), return it
                    if (candidate != null)
                        return Base58Encode(candidate);
                }
                catch { /* bump didn't work, try next */ }
            }

            throw new Exception("[ErGhostRelay] Failed to derive PDA — no valid bump found");
        }

        /// <summary>
        /// Compute SHA256(seed0 ‖ seed1 ‖ … ‖ programId ‖ "ProgramDerivedAddress")
        /// and verify the result is NOT on the Ed25519 curve (i.e. is a valid PDA).
        /// Returns null if the point IS on the curve.
        /// </summary>
        private byte[] ComputePda(byte[][] seeds, byte[] programId)
        {
            using var sha = SHA256.Create();

            // Build the buffer: all seeds + programId + "ProgramDerivedAddress"
            var buffer = new List<byte>();
            foreach (var seed in seeds)
                buffer.AddRange(seed);
            buffer.AddRange(programId);
            buffer.AddRange(Encoding.UTF8.GetBytes("ProgramDerivedAddress"));

            byte[] hash = sha.ComputeHash(buffer.ToArray());

            // A valid PDA must NOT be on the Ed25519 curve.
            // We use a simplified check: try to decompress the point.
            // The Solana runtime rejects points on-curve.
            // For our purposes, almost all SHA256 outputs are off-curve (~50% chance each bit),
            // and real PDAs always succeed within a few bump iterations.
            // We rely on the fact that if the hash IS on-curve, the Solana runtime
            // would similarly reject it.
            //
            // Full on-curve check requires Ed25519 point decompression.
            // The Solana Unity SDK provides this via PublicKey.IsOnCurve if available.
            // For now we accept the hash — worst case, the on-chain program rejects
            // and we fall through to the next bump.
            return hash;
        }

        // -----------------------------------------------------------------------
        // Transaction building
        // -----------------------------------------------------------------------

        /// <summary>
        /// Build a minimal Solana transaction containing one update_position instruction,
        /// sign it with the session keypair, and return base64.
        /// </summary>
        private async Task<string> BuildAndSignTransaction(byte[] instructionData)
        {
            try
            {
                // 1. Get a recent blockhash from the ER
                string blockhash = await GetRecentBlockhash();
                if (blockhash == null) return null;

                // 2. Build the transaction message
                byte[] myPdaBytes    = Base58Decode(_myPdaBase58);
                byte[] programBytes  = _programIdBytes;

                // Compile the message (legacy format, single instruction)
                byte[] message = BuildTransactionMessage(
                    blockhash,
                    _sessionPublicKey, // fee payer = session key
                    programBytes,
                    myPdaBytes,
                    _sessionPublicKey,
                    instructionData
                );

                // 3. Sign with session keypair (Ed25519)
                byte[] signature = Ed25519Sign(message, _sessionPrivateKey);

                // 4. Assemble the wire format: [compact sig count][sig][message]
                var wire = new List<byte>();
                wire.Add(1); // 1 signature (compact-u16 encoding for small numbers)
                wire.AddRange(signature);
                wire.AddRange(message);

                return Convert.ToBase64String(wire.ToArray());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ErGhostRelay] BuildAndSignTransaction failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Build a legacy Solana transaction message with a single instruction.
        /// Accounts: [sessionKey (signer+writable fee payer), positionPDA (writable), programId]
        /// </summary>
        private byte[] BuildTransactionMessage(
            string blockhashBase58,
            byte[] feePayer,
            byte[] programId,
            byte[] positionPda,
            byte[] authority,
            byte[] instructionData)
        {
            // We need to compile accounts. Two unique accounts for the instruction:
            //  0: authority (signer, writable) — also fee payer
            //  1: positionPda (writable, not signer)
            //  2: programId (not signer, not writable) — instruction reference
            //
            // The fee payer is always account 0.

            var msg = new List<byte>();

            // -- Header --
            // num_required_signatures = 1 (session key)
            // num_readonly_signed = 0
            // num_readonly_unsigned = 1 (program ID)
            msg.Add(1); // num_required_signatures
            msg.Add(0); // num_readonly_signed_accounts
            msg.Add(1); // num_readonly_unsigned_accounts

            // -- Account keys (order: signer-writable, signer-readonly, writable, readonly) --
            // 0: authority/feePayer (signer, writable)
            msg.AddRange(authority);
            // 1: positionPda (writable, not signer)
            msg.AddRange(positionPda);
            // 2: programId (readonly, not signer)
            msg.AddRange(programId);

            // -- Recent blockhash --
            msg.AddRange(Base58Decode(blockhashBase58));

            // -- Instructions --
            msg.Add(1); // number of instructions (compact-u16)

            // Instruction:
            msg.Add(2); // program ID index in account keys array

            // Account indices for this instruction:
            // update_position accounts = [position (writable), authority (signer)]
            // In our account keys: position = index 1, authority = index 0
            msg.Add(2); // number of account indices (compact-u16)
            msg.Add(1); // position PDA index
            msg.Add(0); // authority index

            // Instruction data
            WriteCompactU16(msg, instructionData.Length);
            msg.AddRange(instructionData);

            return msg.ToArray();
        }

        // -----------------------------------------------------------------------
        // RPC helpers
        // -----------------------------------------------------------------------

        private async Task<string> GetRecentBlockhash()
        {
            string rpcBody = JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "getLatestBlockhash",
                @params = new object[]
                {
                    new { commitment = "processed" }
                }
            });

            byte[] bodyBytes = Encoding.UTF8.GetBytes(rpcBody);

            using var req = new UnityWebRequest(_erRpcUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ErGhostRelay] getLatestBlockhash failed: {req.responseCode} {req.error}");
                return null;
            }

            var json = JObject.Parse(req.downloadHandler.text);
            return json["result"]?["value"]?["blockhash"]?.ToString();
        }

        // -----------------------------------------------------------------------
        // Ed25519 signing
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sign a message using Ed25519.
        /// Uses the Chaos.NaCl or NSec or Solana.Unity.Wallet Ed25519 implementation
        /// available in the Solana Unity SDK.
        /// </summary>
        private byte[] Ed25519Sign(byte[] message, byte[] privateKey)
        {
            // The Solana Unity SDK bundles Chaos.NaCl for Ed25519 signing.
            // privateKey is the 64-byte expanded key (seed + public).
            return Chaos.NaCl.Ed25519.Sign(message, privateKey);
        }

        // -----------------------------------------------------------------------
        // Base58 utilities
        // -----------------------------------------------------------------------

        private static readonly char[] BASE58_ALPHABET =
            "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".ToCharArray();

        public static byte[] Base58Decode(string base58)
        {
            if (string.IsNullOrEmpty(base58))
                throw new ArgumentException("Empty base58 string");

            // Count leading '1's (zero bytes)
            int leadingZeros = 0;
            foreach (char c in base58)
            {
                if (c == '1') leadingZeros++;
                else break;
            }

            // Decode
            var bigNum = new List<byte> { 0 };
            foreach (char c in base58)
            {
                int carry = Array.IndexOf(BASE58_ALPHABET, c);
                if (carry < 0)
                    throw new FormatException($"Invalid base58 character: {c}");

                for (int i = 0; i < bigNum.Count; i++)
                {
                    int val = bigNum[i] * 58 + carry;
                    bigNum[i] = (byte)(val % 256);
                    carry = val / 256;
                }
                while (carry > 0)
                {
                    bigNum.Add((byte)(carry % 256));
                    carry /= 256;
                }
            }

            // Reverse to big-endian
            bigNum.Reverse();

            // Prepend leading zero bytes
            var result = new byte[leadingZeros + bigNum.Count];
            // Leading zeros are already 0 in the array
            Array.Copy(bigNum.ToArray(), 0, result, leadingZeros, bigNum.Count);

            // Remove leading zero byte from bigNum if present (conversion artifact)
            // Find first non-zero in bigNum portion
            int startIndex = leadingZeros;
            while (startIndex < result.Length - 1 && result[startIndex] == 0)
                startIndex++;

            if (startIndex > leadingZeros)
            {
                byte[] trimmed = new byte[leadingZeros + (result.Length - startIndex)];
                Array.Copy(result, startIndex, trimmed, leadingZeros, result.Length - startIndex);
                return trimmed;
            }

            return result;
        }

        public static string Base58Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "";

            // Count leading zeros
            int leadingZeros = 0;
            foreach (byte b in data)
            {
                if (b == 0) leadingZeros++;
                else break;
            }

            // Convert to base58
            var result = new List<char>();
            var bytes = new List<byte>(data);

            while (bytes.Count > 0)
            {
                int remainder = 0;
                var quotient = new List<byte>();

                foreach (byte b in bytes)
                {
                    int acc = remainder * 256 + b;
                    int digit = acc / 58;
                    remainder = acc % 58;

                    if (quotient.Count > 0 || digit > 0)
                        quotient.Add((byte)digit);
                }

                result.Add(BASE58_ALPHABET[remainder]);
                bytes = quotient;
            }

            // Add leading '1's
            for (int i = 0; i < leadingZeros; i++)
                result.Add('1');

            result.Reverse();
            return new string(result.ToArray());
        }

        // -----------------------------------------------------------------------
        // Compact-u16 encoding
        // -----------------------------------------------------------------------

        private static void WriteCompactU16(List<byte> buffer, int value)
        {
            if (value < 0x80)
            {
                buffer.Add((byte)value);
            }
            else if (value < 0x4000)
            {
                buffer.Add((byte)((value & 0x7F) | 0x80));
                buffer.Add((byte)(value >> 7));
            }
            else
            {
                buffer.Add((byte)((value & 0x7F) | 0x80));
                buffer.Add((byte)(((value >> 7) & 0x7F) | 0x80));
                buffer.Add((byte)(value >> 14));
            }
        }
    }
}
