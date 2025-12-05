using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Solracer.Config;  // ADD THIS

namespace Solracer.Network
{
    /// <summary>
    /// API client for payout operations (checking status, processing payouts)
    /// </summary>
    public class PayoutAPIClient : MonoBehaviour
    {
        private static PayoutAPIClient instance;
        public static PayoutAPIClient Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("PayoutAPIClient");
                    instance = go.AddComponent<PayoutAPIClient>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("API Configuration")]
        [Tooltip("API base URL (overridden by APIConfig if not set manually)")]
        [SerializeField] private string apiBaseUrl = "";  // CHANGE: Remove hardcoded URL
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
                
                // ADD THIS: Initialize API URL from config
                if (string.IsNullOrEmpty(apiBaseUrl))
                {
                    apiBaseUrl = APIConfig.GetApiBaseUrl();
                }
                Debug.Log($"[PayoutAPIClient] Initialized with API URL: {apiBaseUrl}");
            }
        }

        /// <summary>
        /// Get payout status for a race
        /// </summary>
        public async Task<PayoutStatusResponse> GetPayoutStatus(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/payouts/{Uri.EscapeDataString(raceId)}";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
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
                        PayoutStatusResponse response = JsonConvert.DeserializeObject<PayoutStatusResponse>(responseText);
                        Debug.Log($"[PayoutAPIClient] Payout status fetched. Status: {response?.swap_status}, Amount: {response?.prize_amount_sol} SOL");
                        return response;
                    }
                    else
                    {
                        // Special-case 404: payout not found is expected if no winner / payout yet
                        if (webRequest.responseCode == 404)
                        {
                            Debug.Log($"[PayoutAPIClient] No payout exists yet for race {raceId} (404).");
                            return null;
                        }

                        Debug.LogError($"[PayoutAPIClient] Error fetching payout status: {webRequest.error} (HTTP {webRequest.responseCode})");
                        if (webRequest.downloadHandler != null)
                            Debug.LogError($"[PayoutAPIClient] Response: {webRequest.downloadHandler.text}");

                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PayoutAPIClient] Exception fetching payout status: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process payout for a race (returns transaction for signing)
        /// </summary>
        public async Task<ProcessPayoutResponse> ProcessPayout(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/payouts/{Uri.EscapeDataString(raceId)}/process";

                using (UnityWebRequest webRequest = UnityWebRequest.PostWwwForm(url, ""))
                {
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
                        ProcessPayoutResponse response = JsonConvert.DeserializeObject<ProcessPayoutResponse>(responseText);
                        Debug.Log($"[PayoutAPIClient] Payout processed. Status: {response?.status}, Method: {response?.method}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[PayoutAPIClient] Error processing payout: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[PayoutAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PayoutAPIClient] Exception processing payout: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the settle_race transaction for a race.
        /// This transaction settles the race on-chain before claiming prize.
        /// </summary>
        public async Task<SettleTransactionResponse> GetSettleTransaction(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/payouts/{Uri.EscapeDataString(raceId)}/settle-transaction";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
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
                        SettleTransactionResponse response = JsonConvert.DeserializeObject<SettleTransactionResponse>(responseText);
                        Debug.Log($"[PayoutAPIClient] Settle transaction retrieved for race {raceId}");
                        return response;
                    }
                    else
                    {
                        // 400 is expected if settlement is not needed
                        if (webRequest.responseCode == 400)
                        {
                            Debug.Log($"[PayoutAPIClient] Settlement not needed for race {raceId}");
                            return null;
                        }

                        Debug.LogError($"[PayoutAPIClient] Error getting settle transaction: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[PayoutAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PayoutAPIClient] Exception getting settle transaction: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Retry a failed payout
        /// </summary>
        public async Task<ProcessPayoutResponse> RetryPayout(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/payouts/{Uri.EscapeDataString(raceId)}/retry";

                using (UnityWebRequest webRequest = UnityWebRequest.PostWwwForm(url, ""))
                {
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
                        ProcessPayoutResponse response = JsonConvert.DeserializeObject<ProcessPayoutResponse>(responseText);
                        Debug.Log($"[PayoutAPIClient] Payout retry successful. Status: {response?.status}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[PayoutAPIClient] Error retrying payout: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[PayoutAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PayoutAPIClient] Exception retrying payout: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Response model for payout status
    /// </summary>
    [Serializable]
    public class PayoutStatusResponse
    {
        public string payout_id;
        public string race_id;
        public string winner_wallet;
        public float prize_amount_sol;
        public string token_mint;
        public float? token_amount;
        public string swap_status; // pending, swapping, paid, fallback_sol, failed
        public string swap_tx_signature;
        public string transfer_tx_signature;
        public float? fallback_sol_amount;
        public string fallback_tx_signature;
        public string error_message;
        public string created_at;
        public string swap_started_at;
        public string completed_at;
    }

    /// <summary>
    /// Response model for processing payout
    /// </summary>
    [Serializable]
    public class ProcessPayoutResponse
    {
        public string status; // ready_for_signing, processing, completed, failed
        public string payout_id;
        public string transaction; // Base64-encoded transaction (if ready_for_signing)
        public string swap_transaction; // Jupiter swap transaction (if using swap)
        public string method; // claim_prize, jupiter_swap, fallback_sol
        public float? amount_sol;
        public float? amount_tokens;
        public string error;
    }

    /// <summary>
    /// Response model for settle_race transaction
    /// </summary>
    [Serializable]
    public class SettleTransactionResponse
    {
        public string message;
        public string transaction_bytes; // Base64-encoded transaction for signing
        public string race_id;
        public string race_pda;
        public string recent_blockhash;
    }
}

