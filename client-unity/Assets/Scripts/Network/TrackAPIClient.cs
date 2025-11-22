using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace Solracer.Network
{
    /// <summary>
    /// API client for fetching track data from backend (24hr candles)
    /// </summary>
    public class TrackAPIClient : MonoBehaviour
    {
        private static TrackAPIClient instance;
        public static TrackAPIClient Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("TrackAPIClient");
                    instance = go.AddComponent<TrackAPIClient>();
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
        /// Get track data from backend API (24hr candles)
        /// </summary>
        public async Task<TrackResponse> GetTrackData(string tokenMint, int? seed = null)
        {
            try
            {
                string url = $"{apiBaseUrl}{API_PREFIX}/track?token_mint={Uri.EscapeDataString(tokenMint)}";
                if (seed.HasValue)
                {
                    url += $"&seed={seed.Value}";
                }

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
                        TrackResponse response = JsonConvert.DeserializeObject<TrackResponse>(responseText);
                        Debug.Log($"[TrackAPIClient] Track data fetched successfully. Token: {response?.token_symbol}, Points: {response?.point_count ?? 0}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[TrackAPIClient] Error fetching track data: {webRequest.error}");
                        if (webRequest.downloadHandler != null)
                        {
                            Debug.LogError($"[TrackAPIClient] Response: {webRequest.downloadHandler.text}");
                        }
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrackAPIClient] Exception fetching track data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Set the API base URL
        /// </summary>
        public void SetApiBaseUrl(string url)
        {
            apiBaseUrl = url;
            Debug.Log($"[TrackAPIClient] API base URL set to: {apiBaseUrl}");
        }
    }

    #region Response Models

    /// <summary>
    /// Response model for track data from backend
    /// </summary>
    [Serializable]
    public class TrackResponse
    {
        public string token_mint;
        public string token_symbol;
        public int seed;
        public TrackSample[] samples;
        public int point_count;
    }

    /// <summary>
    /// Track sample point (normalized 0-1 range)
    /// </summary>
    [Serializable]
    public class TrackSample
    {
        public float x;  // Index (0 to point_count-1)
        public float y;  // Normalized value (0-1)
    }

    #endregion
}

