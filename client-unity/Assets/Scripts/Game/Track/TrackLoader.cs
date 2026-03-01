using UnityEngine;
using System;
using System.Threading.Tasks;
using Solracer.Network;

namespace Solracer.Game
{
    /// <summary>
    /// Track data loader. Orchestrates track source selection and injects
    /// data into TrackGenerator.
    /// 
    /// Responsibilities:
    ///   - Always fetch real oracle track data from Backend-v2
    ///   - Fall back to TrackDataProvider on any failure
    ///   - Set RaceData.TrackHash/TrackHourStartUTC only in competitive mode
    ///   - Call TrackGenerator.SetTrackData() + GenerateTrackFromData()
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TrackLoader : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to the TrackGenerator in the scene")]
        [SerializeField] private TrackGenerator trackGenerator;

        /// <summary>
        /// Current track data stored for debugging.
        /// </summary>
        private float[] currentTrackData;

        private async void Start()
        {
            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();

                if (trackGenerator == null)
                {
                    Debug.LogError("[TrackLoader] TrackGenerator not found in scene  cannot load track");
                    return;
                }

                Debug.LogWarning("[TrackLoader] Auto-found TrackGenerator (assign in inspector to avoid this)");
            }

            try
            {
                await LoadTrackAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrackLoader] Fatal error during track load: {ex}");

                try
                {
                    var fallback = TrackDataProvider.GetMockTrackData();
                    trackGenerator.SetTrackData(fallback);
                    trackGenerator.GenerateTrackFromData(fallback);
                    Debug.LogWarning("[TrackLoader] Recovered with fallback mock track after fatal error");
                }
                catch (Exception fallbackEx)
                {
                    Debug.LogError($"[TrackLoader] Fallback also failed  game unplayable: {fallbackEx}");
                }
            }
        }

        /// <summary>
        /// Loads track data from the appropriate source and injects it
        /// into TrackGenerator.
        /// </summary>
        private async Task LoadTrackAsync()
        {
            string modeName = GameModeData.IsCompetitive ? "Competitive" : "Practice";
            float[] trackData = null;
            TrackDetailResponse trackDetail = null;

            // Always try Backend-v2 for real oracle track data
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);

            if (!string.IsNullOrEmpty(tokenMint))
            {
                (trackData, trackDetail) = await FetchFromBackendV2Async(tokenMint);
            }

            // Fallback to mock data on any failure
            if (trackData == null || trackData.Length == 0)
            {
                trackData = GetFallbackTrackData();
                Debug.Log($"[TrackLoader] Backend-v2 unavailable, using mock track ({modeName})");
            }

            currentTrackData = trackData;

            // Set RaceData track commitment  only in competitive mode
            if (GameModeData.IsCompetitive && trackDetail != null)
            {
                RaceData.TrackHash = trackDetail.trackHash;
                RaceData.TrackHourStartUTC = trackDetail.hourStartUTC;
                RaceData.TrackTokenMint = tokenMint;
            }
            else
            {
                RaceData.TrackHash = null;
                RaceData.TrackHourStartUTC = null;
                RaceData.TrackTokenMint = null;
            }

            // Inject into TrackGenerator
            trackGenerator.SetTrackData(trackData);
            trackGenerator.GenerateTrackFromData(trackData);
        }

        /// <summary>
        /// Fetches oracle track data from Backend-v2.
        /// Returns (float[] heights, TrackDetailResponse detail) or (null, null) on failure.
        /// </summary>
        private async Task<(float[], TrackDetailResponse)> FetchFromBackendV2Async(string tokenMint)
        {
            string modeName = GameModeData.IsCompetitive ? "Competitive" : "Practice";

            // Step 1: Get latest track metadata
            var apiClient = TrackAPIClientV2.Instance;
            if (apiClient == null)
            {
                Debug.LogError("[TrackLoader] TrackAPIClientV2 instance unavailable");
                return (null, null);
            }

            LatestTrackResponse latest = await apiClient.GetLatestTrackAsync(tokenMint);
            if (latest == null)
            {
                Debug.LogWarning("[TrackLoader] Failed to fetch latest track");
                return (null, null);
            }

            // Step 2: Get full track detail with blob
            TrackDetailResponse detail = await apiClient.GetTrackDetailAsync(tokenMint, latest.hourStartUTC);
            if (detail == null)
            {
                Debug.LogWarning("[TrackLoader] Failed to fetch track detail");
                return (null, null);
            }

            // Step 3: Decode blob
            float[] heights = TrackBlobDecoder.DecodeBase64Blob(detail.normalizedPointsBlobBase64, detail.pointCount);
            if (heights == null)
            {
                Debug.LogError("[TrackLoader] Failed to decode track blob");
                return (null, null);
            }

            Debug.Log($"[TrackLoader] Loaded oracle track ({modeName}): Token={detail.tokenMint}, Points={detail.pointCount}, Difficulty={detail.difficulty}, Hour={detail.hourStartUTC}");
            return (heights, detail);
        }

        /// <summary>
        /// Returns fallback mock track data from TrackDataProvider.
        /// </summary>
        private float[] GetFallbackTrackData()
        {
            return TrackDataProvider.GetMockTrackData();
        }
    }
}
