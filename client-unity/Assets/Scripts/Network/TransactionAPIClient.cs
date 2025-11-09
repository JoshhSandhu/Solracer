using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using Newtonsoft.Json;

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
        [SerializeField] private string apiBaseUrl = "http://localhost:8000";
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
        /// set the API base URL
        /// </summary>
        public void SetApiBaseUrl(string url)
        {
            apiBaseUrl = url;
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
        public string token_mint;        // Required for create_race
        public float entry_fee_sol;      // Required for create_race
        public string race_id;           // Required for join_race, submit_result, claim_prize
        public int finish_time_ms;       // Required for submit_result
        public int coins_collected;      // Required for submit_result
        public string input_hash;        // Required for submit_result (64 hex characters)
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

