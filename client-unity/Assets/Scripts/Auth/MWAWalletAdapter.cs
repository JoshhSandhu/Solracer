using System;
using System.Threading.Tasks;
using UnityEngine;
using Solana.Unity.SDK;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

namespace Solracer.Auth
{
    /// <summary>
    /// Adapter for Mobile Wallet Adapter (MWA) integration using Solana Unity SDK.
    /// Connects to Mock MWA Wallet (Seeker) on physical Android devices.
    /// </summary>
    public class MWAWalletAdapter : MonoBehaviour
    {
        private static MWAWalletAdapter instance;
        public static MWAWalletAdapter Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("MWAWalletAdapter");
                    instance = go.AddComponent<MWAWalletAdapter>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("MWA Configuration")]
        [SerializeField] private string appIdentityName = "Solracer";
        [SerializeField] private string appIdentityUri = "https://solracer.com";
        [SerializeField] private string appIconUri = "/favicon.ico";
        [SerializeField] private bool keepConnectionAlive = true;
        
        [Header("RPC Configuration")]
        [SerializeField] private RpcCluster rpcCluster = RpcCluster.DevNet;
        [SerializeField] private string customRpcUri = "";

        // Internal wallet adapter from Solana Unity SDK
        private SolanaWalletAdapter _walletAdapter;
        private Account _connectedAccount;
        private bool _isConnected = false;

        public bool IsConnected => _isConnected;
        public string WalletAddress => _connectedAccount?.PublicKey?.ToString() ?? "";
        public Account ConnectedAccount => _connectedAccount;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Initialize the MWA wallet adapter
        /// </summary>
        private void InitializeWalletAdapter()
        {
            if (_walletAdapter != null) return;

            Debug.Log("[MWAWalletAdapter] Initializing Solana Mobile Wallet Adapter...");

            var options = new SolanaWalletAdapterOptions
            {
                solanaMobileWalletAdapterOptions = new SolanaMobileWalletAdapterOptions
                {
                    identityUri = appIdentityUri,
                    iconUri = appIconUri,
                    name = appIdentityName,
                    keepConnectionAlive = keepConnectionAlive
                }
            };

            string rpcUri = string.IsNullOrEmpty(customRpcUri) ? null : customRpcUri;
            
            _walletAdapter = new SolanaWalletAdapter(
                options,
                rpcCluster,
                rpcUri,
                null,
                false
            );

            Debug.Log($"[MWAWalletAdapter] Initialized with cluster: {rpcCluster}");
        }

        /// <summary>
        /// Connect to MWA wallet (authorize)
        /// </summary>
        public async Task<MWAConnectionResult> ConnectAsync()
        {
            try
            {
                Debug.Log("[MWAWalletAdapter] Starting MWA connection...");

#if !UNITY_ANDROID
                Debug.LogError("[MWAWalletAdapter] MWA is only supported on Android devices");
                return new MWAConnectionResult
                {
                    Success = false,
                    ErrorMessage = "MWA is only supported on Android devices. Please build and run on an Android device."
                };
#endif

                InitializeWalletAdapter();

                // Login calls the MWA authorize flow
                _connectedAccount = await _walletAdapter.Login();

                if (_connectedAccount != null && _connectedAccount.PublicKey != null)
                {
                    _isConnected = true;
                    string walletAddress = _connectedAccount.PublicKey.ToString();
                    
                    Debug.Log($"[MWAWalletAdapter] Connected to MWA wallet: {walletAddress}");

                    return new MWAConnectionResult
                    {
                        Success = true,
                        WalletAddress = walletAddress,
                        PublicKey = _connectedAccount.PublicKey.KeyBytes
                    };
                }
                else
                {
                    Debug.LogError("[MWAWalletAdapter] Login returned null account");
                    return new MWAConnectionResult
                    {
                        Success = false,
                        ErrorMessage = "MWA authorization failed - no account returned"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MWAWalletAdapter] Connection failed: {ex.Message}");
                Debug.LogError($"[MWAWalletAdapter] Stack trace: {ex.StackTrace}");
                
                return new MWAConnectionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Sign in using MWA - same as connect for MWA (authorize flow)
        /// </summary>
        public async Task<MWAConnectionResult> SignInAsync()
        {
            // For MWA, SignIn is the same as Connect (authorize)
            return await ConnectAsync();
        }

        /// <summary>
        /// Sign a message using MWA wallet
        /// </summary>
        public async Task<MWASignResult> SignMessageAsync(byte[] message)
        {
            try
            {
                if (!_isConnected || _walletAdapter == null)
                {
                    Debug.LogError("[MWAWalletAdapter] Not connected to MWA wallet");
                    return new MWASignResult
                    {
                        Success = false,
                        ErrorMessage = "Not connected to MWA wallet"
                    };
                }

                Debug.Log($"[MWAWalletAdapter] Signing message ({message.Length} bytes)...");

                // Use SDK's SignMessage - this triggers MWA bottom sheet
                byte[] signature = await _walletAdapter.SignMessage(message);

                if (signature != null && signature.Length > 0)
                {
                    Debug.Log($"[MWAWalletAdapter] Message signed successfully ({signature.Length} bytes)");
                    
                    return new MWASignResult
                    {
                        Success = true,
                        Signature = signature
                    };
                }
                else
                {
                    Debug.LogError("[MWAWalletAdapter] SignMessage returned null or empty signature");
                    return new MWASignResult
                    {
                        Success = false,
                        ErrorMessage = "MWA signing failed - no signature returned"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MWAWalletAdapter] Sign message failed: {ex.Message}");
                return new MWASignResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Sign a Solana transaction using MWA wallet.
        /// Takes base64-encoded transaction bytes from backend, returns signed transaction base64.
        /// </summary>
        public async Task<string> SignTransactionAsync(string transactionBase64)
        {
            try
            {
                if (!_isConnected || _walletAdapter == null)
                {
                    Debug.LogError("[MWAWalletAdapter] Not connected to MWA wallet");
                    return null;
                }

                Debug.Log($"[MWAWalletAdapter] Signing transaction (base64 length: {transactionBase64.Length})...");

                // Step 1: Decode base64 to bytes
                byte[] transactionBytes = Convert.FromBase64String(transactionBase64);
                Debug.Log($"[MWAWalletAdapter] Decoded transaction: {transactionBytes.Length} bytes");

                // Step 2: Deserialize transaction
                Transaction transaction = Transaction.Deserialize(transactionBytes);
                Debug.Log($"[MWAWalletAdapter] Deserialized transaction. Instructions: {transaction.Instructions?.Count ?? 0}");

                // Step 3: Sign transaction using MWA SDK - this triggers the wallet bottom sheet
                Transaction signedTransaction = await _walletAdapter.SignTransaction(transaction);

                if (signedTransaction == null)
                {
                    Debug.LogError("[MWAWalletAdapter] SignTransaction returned null");
                    return null;
                }

                // Step 4: Serialize the signed transaction
                byte[] signedTransactionBytes = signedTransaction.Serialize();
                Debug.Log($"[MWAWalletAdapter] Serialized signed transaction: {signedTransactionBytes.Length} bytes");

                // Verify we got a full transaction, not just a signature
                if (signedTransactionBytes.Length == 64)
                {
                    Debug.LogError("[MWAWalletAdapter] ERROR: Serialized transaction is only 64 bytes - this is just a signature!");
                    return null;
                }

                // Step 5: Return base64-encoded signed transaction
                string signedTransactionBase64 = Convert.ToBase64String(signedTransactionBytes);
                Debug.Log($"[MWAWalletAdapter] Transaction signed successfully! ({signedTransactionBytes.Length} bytes)");

                return signedTransactionBase64;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MWAWalletAdapter] Transaction signing failed: {ex.Message}");
                Debug.LogError($"[MWAWalletAdapter] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Sign multiple transactions at once
        /// </summary>
        public async Task<string[]> SignAllTransactionsAsync(string[] transactionsBase64)
        {
            try
            {
                if (!_isConnected || _walletAdapter == null)
                {
                    Debug.LogError("[MWAWalletAdapter] Not connected to MWA wallet");
                    return null;
                }

                Debug.Log($"[MWAWalletAdapter] Signing {transactionsBase64.Length} transactions...");

                // Deserialize all transactions
                Transaction[] transactions = new Transaction[transactionsBase64.Length];
                for (int i = 0; i < transactionsBase64.Length; i++)
                {
                    byte[] txBytes = Convert.FromBase64String(transactionsBase64[i]);
                    transactions[i] = Transaction.Deserialize(txBytes);
                }

                // Sign all transactions - this triggers the wallet bottom sheet once
                Transaction[] signedTransactions = await _walletAdapter.SignAllTransactions(transactions);

                if (signedTransactions == null || signedTransactions.Length == 0)
                {
                    Debug.LogError("[MWAWalletAdapter] SignAllTransactions returned null or empty");
                    return null;
                }

                // Serialize all signed transactions
                string[] signedTransactionsBase64 = new string[signedTransactions.Length];
                for (int i = 0; i < signedTransactions.Length; i++)
                {
                    byte[] signedBytes = signedTransactions[i].Serialize();
                    signedTransactionsBase64[i] = Convert.ToBase64String(signedBytes);
                }

                Debug.Log($"[MWAWalletAdapter] {signedTransactions.Length} transactions signed successfully!");
                return signedTransactionsBase64;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MWAWalletAdapter] SignAllTransactions failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Disconnect from MWA wallet
        /// </summary>
        public void Disconnect()
        {
            try
            {
                Debug.Log("[MWAWalletAdapter] Disconnecting from MWA wallet...");

                if (_walletAdapter != null)
                {
                    _walletAdapter.Logout();
                }

                _connectedAccount = null;
                _isConnected = false;

                Debug.Log("[MWAWalletAdapter] Disconnected from MWA wallet");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MWAWalletAdapter] Disconnect failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Async disconnect
        /// </summary>
        public Task DisconnectAsync()
        {
            Disconnect();
            return Task.CompletedTask;
        }
    }

    #region MWA Result Types

    /// <summary>
    /// Result of MWA connection attempt
    /// </summary>
    public class MWAConnectionResult
    {
        public bool Success { get; set; }
        public string WalletAddress { get; set; }
        public byte[] PublicKey { get; set; }
        public string AuthToken { get; set; }
        public string SignedSIWSMessage { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of MWA message signing
    /// </summary>
    public class MWASignResult
    {
        public bool Success { get; set; }
        public byte[] Signature { get; set; }
        public string ErrorMessage { get; set; }
    }

    #endregion
}
