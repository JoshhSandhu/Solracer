using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Solracer.Config;

namespace Solracer.Network
{
    /// <summary>
    /// API client for race management endpoints (lobby system)
    /// </summary>
    public class RaceAPIClient : MonoBehaviour
    {
        private static RaceAPIClient instance;
        public static RaceAPIClient Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("RaceAPIClient");
                    instance = go.AddComponent<RaceAPIClient>();
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
                Debug.Log($"[RaceAPIClient] Initialized with API URL: {apiBaseUrl}");
            }
        }

        /// <summary>
        /// Create a new race (public or private)
        /// </summary>
        public async Task<RaceResponse> CreateRaceAsync(CreateRaceRequest request)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/create";
                string jsonBody = JsonConvert.SerializeObject(request);

                // Debug: Log the actual URL being used
                Debug.Log($"[RaceAPIClient] Creating race - Full URL: {url}");
                Debug.Log($"[RaceAPIClient] API Base URL: {apiBaseUrl}");
                Debug.Log($"[RaceAPIClient] Is Local URL: {APIConfig.IsLocalUrl(apiBaseUrl)}");

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        RaceResponse response = JsonConvert.DeserializeObject<RaceResponse>(responseText);
                        Debug.Log($"[RaceAPIClient] Race created: {response.race_id}, Private: {response.is_private}, Code: {response.join_code}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error creating race: {webRequest.error}");
                        Debug.LogError($"[RaceAPIClient] Request URL was: {url}");
                        Debug.LogError($"[RaceAPIClient] Response code: {webRequest.responseCode}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[RaceAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception creating race: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Join a race by race_id (public races)
        /// </summary>
        public async Task<RaceResponse> JoinRaceByIdAsync(string raceId, string walletAddress)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/{raceId}/join";
                var request = new JoinRaceByIdRequest { wallet_address = walletAddress };
                string jsonBody = JsonConvert.SerializeObject(request);

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        RaceResponse response = JsonConvert.DeserializeObject<RaceResponse>(responseText);
                        Debug.Log($"[RaceAPIClient] Joined race: {response.race_id}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error joining race: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[RaceAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception joining race: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Join a race by join code (private races)
        /// </summary>
        public async Task<RaceResponse> JoinRaceByCodeAsync(string joinCode, string walletAddress)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/join-by-code";
                var request = new JoinRaceByCodeRequest { join_code = joinCode, wallet_address = walletAddress };
                string jsonBody = JsonConvert.SerializeObject(request);

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        RaceResponse response = JsonConvert.DeserializeObject<RaceResponse>(responseText);
                        Debug.Log($"[RaceAPIClient] Joined race by code: {response.race_id}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error joining race by code: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[RaceAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception joining race by code: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get list of available public races
        /// </summary>
        public async Task<List<PublicRaceListItem>> GetPublicRacesAsync(string tokenMint = null, float? entryFee = null)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/public";
                bool hasParams = false;
                
                if (!string.IsNullOrEmpty(tokenMint))
                {
                    url += $"?token_mint={UnityWebRequest.EscapeURL(tokenMint)}";
                    hasParams = true;
                }
                
                if (entryFee.HasValue)
                {
                    url += hasParams ? "&" : "?";
                    url += $"entry_fee={entryFee.Value}";
                }

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        List<PublicRaceListItem> races = JsonConvert.DeserializeObject<List<PublicRaceListItem>>(responseText);
                        Debug.Log($"[RaceAPIClient] Fetched {races?.Count ?? 0} public races");
                        return races ?? new List<PublicRaceListItem>();
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error fetching public races: {webRequest.error}");
                        return new List<PublicRaceListItem>();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception fetching public races: {ex.Message}");
                return new List<PublicRaceListItem>();
            }
        }

        /// <summary>
        /// Get race status
        /// </summary>
        public async Task<RaceStatusResponse> GetRaceStatusAsync(string raceId)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/{raceId}/status";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        RaceStatusResponse response = JsonConvert.DeserializeObject<RaceStatusResponse>(responseText);
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error fetching race status: {webRequest.error}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception fetching race status: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mark player as ready
        /// </summary>
        public async Task<bool> MarkPlayerReadyAsync(string raceId, string walletAddress)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/{raceId}/ready";
                var request = new MarkReadyRequest { wallet_address = walletAddress };
                string jsonBody = JsonConvert.SerializeObject(request);

                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[RaceAPIClient] Player marked as ready");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error marking player ready: {webRequest.error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception marking player ready: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cancel a race
        /// </summary>
        public async Task<bool> CancelRaceAsync(string raceId, string walletAddress)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/races/{raceId}?wallet_address={UnityWebRequest.EscapeURL(walletAddress)}";

                using (UnityWebRequest webRequest = UnityWebRequest.Delete(url))
                {
                    // Bypass certificate validation for local development URLs
                    if (APIConfig.IsLocalUrl(apiBaseUrl))
                    {
                        webRequest.certificateHandler = new CertificateHandlerBypass();
                        Debug.Log($"[RaceAPIClient] Certificate bypass enabled for local URL: {apiBaseUrl}");
                    }
                    else
                    {
                        Debug.Log($"[RaceAPIClient] Using standard SSL validation for URL: {apiBaseUrl}");
                    }

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[RaceAPIClient] Race cancelled");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[RaceAPIClient] Error cancelling race: {webRequest.error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RaceAPIClient] Exception cancelling race: {ex.Message}");
                return false;
            }
        }
    }

    #region Request/Response Models

    [Serializable]
    public class CreateRaceRequest
    {
        public string token_mint;
        public string wallet_address;
        public float entry_fee_sol;
        public bool is_private;
    }

    [Serializable]
    public class JoinRaceByIdRequest
    {
        public string wallet_address;
    }

    [Serializable]
    public class JoinRaceByCodeRequest
    {
        public string join_code;
        public string wallet_address;
    }

    [Serializable]
    public class MarkReadyRequest
    {
        public string wallet_address;
    }

    [Serializable]
    public class RaceResponse
    {
        public string id;
        public string race_id;
        public string token_mint;
        public string token_symbol;
        public float entry_fee_sol;
        public string player1_wallet;
        public string player2_wallet;
        public string status;
        public int track_seed;
        public string created_at;
        public string solana_tx_signature;
        public bool is_private;
        public string join_code;
        public string expires_at;
        public bool player1_ready;
        public bool player2_ready;
    }

    [Serializable]
    public class PlayerResult
    {
        public string wallet_address;
        public int player_number;
        public int? finish_time_ms;
        public int? coins_collected;
        public bool? verified;
    }

    [Serializable]
    public class RaceStatusResponse
    {
        public string race_id;
        public string status;
        public string player1_wallet;
        public string player2_wallet;
        public string winner_wallet;
        public bool is_settled;
        public bool player1_ready;
        public bool player2_ready;
        public bool both_ready;
        public PlayerResult player1_result;
        public PlayerResult player2_result;
    }

    [Serializable]
    public class PublicRaceListItem
    {
        public string race_id;
        public string token_mint;
        public string token_symbol;
        public float entry_fee_sol;
        public string player1_wallet;
        public string created_at;
        public string expires_at;
    }

    #endregion
}

