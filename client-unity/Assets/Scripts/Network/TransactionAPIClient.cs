using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using Newtonsoft.Json;
using Solracer.Config;

namespace Solracer.Network
{
    /// <summary>
    /// API client for Solana transaction endpoints
    /// Handles building and submitting Solana transactions for on-chain race operations
    /// </summary>
    public class TransactionAPIClient : MonoBehaviour
    {
        private static TransactionAPIClient instance;
        public static TransactionAPIClient Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("TransactionAPIClient");
                    instance = go.AddComponent<TransactionAPIClient>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("API Configuration")]
        [Tooltip("API base URL (overridden by APIConfig if not set manually)")]
        [SerializeField] private string apiBaseUrl = "";
        private const string API_PREFIX = "/api/v1";

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                
                // Initialize API URL from config
                if (string.IsNullOrEmpty(apiBaseUrl))
                {
                    apiBaseUrl = APIConfig.GetApiBaseUrl();
                }
                Debug.Log($"[TransactionAPIClient] Initialized with API URL: {apiBaseUrl}");
            }
        }

        /// <summary>
        /// Build a Solana transaction for signing
        /// </summary>
        public async Task<BuildTransactionResponse> BuildTransactionAsync(BuildTransactionRequest request)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/transactions/build";
                string jsonData = JsonConvert.SerializeObject(request);

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        BuildTransactionResponse response = JsonConvert.DeserializeObject<BuildTransactionResponse>(responseText);
                        Debug.Log($"[TransactionAPIClient] Transaction built successfully. Type: {response.instruction_type}, Race ID: {response.race_id}");
                        return response;
                    }
                    else
                    {
                        string errorMessage = $"Failed to build transaction: {webRequest.error}";
                        if (webRequest.downloadHandler != null)
                        {
                            errorMessage += $"\nResponse: {webRequest.downloadHandler.text}";
                        }
                        Debug.LogError($"[TransactionAPIClient] {errorMessage}");
                        throw new Exception(errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TransactionAPIClient] Error building transaction: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Submit a signed Solana transaction to the network
        /// </summary>
        public async Task<SubmitTransactionResponse> SubmitTransactionAsync(SubmitTransactionRequest request)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/transactions/submit";
                string jsonData = JsonConvert.SerializeObject(request);

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        SubmitTransactionResponse response = JsonConvert.DeserializeObject<SubmitTransactionResponse>(responseText);
                        Debug.Log($"[TransactionAPIClient] Transaction submitted successfully. Signature: {response.transaction_signature}, Confirmed: {response.confirmed}");
                        return response;
                    }
                    else
                    {
                        string errorMessage = $"Failed to submit transaction: {webRequest.error}";
                        if (webRequest.downloadHandler != null)
                        {
                            errorMessage += $"\nResponse: {webRequest.downloadHandler.text}";
                        }
                        Debug.LogError($"[TransactionAPIClient] {errorMessage}");
                        throw new Exception(errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TransactionAPIClient] Error submitting transaction: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Set the API base URL (updates both local and config)
        /// </summary>
        public void SetApiBaseUrl(string url)
        {
            apiBaseUrl = url;
            APIConfig.SetApiBaseUrl(url); // Also update config for other clients
            Debug.Log($"[TransactionAPIClient] API base URL set to: {apiBaseUrl}");
        }
    }

    #region Request/Response Models

    /// <summary>
    /// Request model for building a Solana transaction.
    /// </summary>
    [Serializable]
    public class BuildTransactionRequest
    {
        public string instruction_type;  // "create_race", "join_race", "submit_result", "claim_prize"
        public string wallet_address;
        public string token_mint;        // for create_race
        public float entry_fee_sol;      // for create_race
        public string race_id;           // for join_race, submit_result, claim_prize
        public int finish_time_ms;       // for submit_result
        public int coins_collected;      // for submit_result
        public string input_hash;        // for submit_result (64 hex characters)
    }

    /// <summary>
    /// Response model for building a Solana transaction.
    /// </summary>
    [Serializable]
    public class BuildTransactionResponse
    {
        public string transaction_bytes;  // Base64-encoded transaction
        public string instruction_type;
        public string race_id;
        public string race_pda;
        public string recent_blockhash;
    }

    /// <summary>
    /// Request model for submitting a signed Solana transaction.
    /// </summary>
    [Serializable]
    public class SubmitTransactionRequest
    {
        public string signed_transaction_bytes;  // Base64-encoded signed transaction
        public string instruction_type;
        public string race_id;  // Optional
        // Optional fields for submit_result instruction
        public string wallet_address;  // Wallet address (for submit_result)
        public int? finish_time_ms;  // Finish time in milliseconds (for submit_result)
        public int? coins_collected;  // Coins collected (for submit_result)
        public string input_hash;  // Input hash for replay verification (for submit_result)
    }

    /// <summary>
    /// Response model for submitting a Solana transaction.
    /// </summary>
    [Serializable]
    public class SubmitTransactionResponse
    {
        public string transaction_signature;
        public string instruction_type;
        public string race_id;
        public bool confirmed;
    }

    #endregion
}

