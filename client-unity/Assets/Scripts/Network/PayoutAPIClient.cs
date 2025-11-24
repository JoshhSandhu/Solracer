using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

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
        /// Get payout status for a race
        /// </summary>
        public async Task<PayoutStatusResponse> GetPayoutStatus(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/payouts/{Uri.EscapeDataString(raceId)}";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
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
                        Debug.LogError($"[PayoutAPIClient] Error fetching payout status: {webRequest.error}");
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
}

