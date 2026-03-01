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
    ///   - Retry once on failure (2s delay)
    ///   - Fall back to TrackDataProvider on any failure
    ///   - Set RaceData commitment fields only in competitive mode
    ///   - Expose CurrentTrack (LoadedTrackData) for consumers
    ///   - Call TrackGenerator.SetTrackData() + GenerateTrackFromData()
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TrackLoader : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to the TrackGenerator in the scene")]
        [SerializeField] private TrackGenerator trackGenerator;

        /// <summary>
        /// Full context of the currently loaded track.
        /// </summary>
        public LoadedTrackData CurrentTrack { get; private set; }

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
                    LoadMockTrack();
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
        /// into TrackGenerator. Includes offline detection and retry logic.
        /// </summary>
        private async Task LoadTrackAsync()
        {
            string modeName = GameModeData.IsCompetitive ? "Competitive" : "Practice";

            // Phase 4: Offline detection (non-blocking optimization)
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.Log($"[TrackLoader] No internet detected, using fallback track ({modeName})");
                LoadMockTrack();
                return;
            }

            float[] trackData = null;
            TrackDetailResponse trackDetail = null;

            // Always try Backend-v2 for real oracle track data
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);

            if (!string.IsNullOrEmpty(tokenMint))
            {
                // First attempt
                (trackData, trackDetail) = await FetchFromBackendV2Async(tokenMint);

                // Phase 4: Retry once after 2s on failure
                if (trackData == null || trackData.Length == 0)
                {
                    Debug.LogWarning($"[TrackLoader] Backend request failed, retrying... ({modeName})");
                    await Task.Delay(2000);
                    (trackData, trackDetail) = await FetchFromBackendV2Async(tokenMint);

                    if (trackData == null || trackData.Length == 0)
                    {
                        Debug.LogWarning($"[TrackLoader] Retry failed, using fallback track ({modeName})");
                    }
                }
            }

            // Phase 4: Blob validation after decode
            if (trackData != null && trackData.Length < 2)
            {
                Debug.LogWarning("[TrackLoader] Invalid blob data (less than 2 points), using fallback");
                trackData = null;
                trackDetail = null;
            }

            // Fallback to mock data on any failure
            if (trackData == null || trackData.Length == 0)
            {
                LoadMockTrack();
                Debug.Log($"[TrackLoader] Backend-v2 unavailable, using mock track ({modeName})");
                return;
            }

            // Build LoadedTrackData for oracle track
            CurrentTrack = new LoadedTrackData
            {
                NormalizedHeights = trackData,
                TrackHash = trackDetail?.trackHash,
                HourStartUTC = trackDetail?.hourStartUTC,
                TokenMint = tokenMint,
                PointCount = trackData.Length,
                Difficulty = trackDetail?.difficulty ?? 0,
                IsMockData = false
            };

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
        /// Loads mock track data as fallback. Sets CurrentTrack with IsMockData = true.
        /// Always produces a playable track.
        /// </summary>
        private void LoadMockTrack()
        {
            float[] mockData = TrackDataProvider.GetMockTrackData();

            CurrentTrack = new LoadedTrackData
            {
                NormalizedHeights = mockData,
                TrackHash = null,
                HourStartUTC = null,
                TokenMint = null,
                PointCount = mockData.Length,
                Difficulty = 0,
                IsMockData = true
            };

            // Clear RaceData commitment
            RaceData.TrackHash = null;
            RaceData.TrackHourStartUTC = null;
            RaceData.TrackTokenMint = null;

            trackGenerator.SetTrackData(mockData);
            trackGenerator.GenerateTrackFromData(mockData);
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

            // Phase 4: Validate decoded data
            if (heights.Length != detail.pointCount)
            {
                Debug.LogError($"[TrackLoader] Decoded point count mismatch: expected {detail.pointCount}, got {heights.Length}");
                return (null, null);
            }

            if (detail.pointCount < 2)
            {
                Debug.LogError($"[TrackLoader] Invalid blob data: pointCount={detail.pointCount}");
                return (null, null);
            }

            Debug.Log($"[TrackLoader] Loaded oracle track ({modeName}): Token={detail.tokenMint}, Points={detail.pointCount}, Difficulty={detail.difficulty}, Hour={detail.hourStartUTC}");
            return (heights, detail);
        }
    }
}
