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
    /// Backend-v2 API client for fetching oracle track data.
    /// Uses APIConfig.GetTrackApiBaseUrl() for the base URL.
    /// Includes in-memory session cache for track details.
    /// </summary>
    public class TrackAPIClientV2 : MonoBehaviour
    {
        private static TrackAPIClientV2 instance;
        public static TrackAPIClientV2 Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("TrackAPIClientV2");
                    instance = go.AddComponent<TrackAPIClientV2>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        private string apiBaseUrl;

        /// <summary>
        /// In-memory session cache for track detail responses.
        /// Key: "tokenMint:hourStartUTC"
        /// </summary>
        private static readonly Dictionary<string, TrackDetailResponse> trackCache = new Dictionary<string, TrackDetailResponse>();

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
                apiBaseUrl = APIConfig.GetTrackApiBaseUrl();
                Debug.Log($"[TrackAPIClientV2] Initialized with URL: {apiBaseUrl}");
            }
        }

        private const int REQUEST_TIMEOUT_SECONDS = 10;

        /// <summary>
        /// Fetch latest track metadata for a token.
        /// GET /tracks/:tokenMint/latest
        /// </summary>
        public async Task<LatestTrackResponse> GetLatestTrackAsync(string tokenMint)
        {
            try
            {
                string url = $"{apiBaseUrl}/tracks/{Uri.EscapeDataString(tokenMint)}/latest";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
                    webRequest.timeout = REQUEST_TIMEOUT_SECONDS;
                    if (APIConfig.IsLocalUrl(url))
                        webRequest.certificateHandler = new CertificateHandlerBypass();

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        LatestTrackResponse response = JsonConvert.DeserializeObject<LatestTrackResponse>(responseText);
                        Debug.Log($"[TrackAPIClientV2] Latest track: token={response?.tokenMint}, hour={response?.hourStartUTC}, points={response?.pointCount}");
                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[TrackAPIClientV2] Failed to fetch latest track: {webRequest.error} (HTTP {webRequest.responseCode})");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrackAPIClientV2] Exception fetching latest track: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetch full track detail including blob.
        /// GET /tracks/:tokenMint/:hourStartUTC
        /// Returns cached response if available for the same (token, hour) pair.
        /// </summary>
        public async Task<TrackDetailResponse> GetTrackDetailAsync(string tokenMint, string hourStartUTC)
        {
            // Check cache first
            string cacheKey = $"{tokenMint}:{hourStartUTC}";
            if (trackCache.TryGetValue(cacheKey, out TrackDetailResponse cached))
            {
                Debug.Log($"[TrackAPIClientV2] Using cached track: {cacheKey}");
                return cached;
            }

            try
            {
                string url = $"{apiBaseUrl}/tracks/{Uri.EscapeDataString(tokenMint)}/{Uri.EscapeDataString(hourStartUTC)}";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
                    webRequest.timeout = REQUEST_TIMEOUT_SECONDS;
                    if (APIConfig.IsLocalUrl(url))
                        webRequest.certificateHandler = new CertificateHandlerBypass();

                    var operation = webRequest.SendWebRequest();

                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string responseText = webRequest.downloadHandler.text;
                        TrackDetailResponse response = JsonConvert.DeserializeObject<TrackDetailResponse>(responseText);
                        Debug.Log($"[TrackAPIClientV2] Track detail: token={response?.tokenMint}, hour={response?.hourStartUTC}, points={response?.pointCount}");

                        // Store in cache
                        if (response != null)
                        {
                            trackCache[cacheKey] = response;
                        }

                        return response;
                    }
                    else
                    {
                        Debug.LogError($"[TrackAPIClientV2] Failed to fetch track detail: {webRequest.error} (HTTP {webRequest.responseCode})");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrackAPIClientV2] Exception fetching track detail: {ex.Message}");
                return null;
            }
        }
    }
}

