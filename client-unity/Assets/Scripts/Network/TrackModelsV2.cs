using System;
using Newtonsoft.Json;

namespace Solracer.Network
{
    /// <summary>
    /// Response from GET /tracks/:tokenMint/latest
    /// Returns metadata for the most recent playable track
    /// </summary>
    [Serializable]
    public class LatestTrackResponse
    {
        [JsonProperty("tokenMint")]
        public string tokenMint;

        [JsonProperty("hourStartUTC")]
        public string hourStartUTC;

        [JsonProperty("trackVersion")]
        public string trackVersion;

        [JsonProperty("pointCount")]
        public int pointCount;

        [JsonProperty("trackHash")]
        public string trackHash;

        [JsonProperty("difficulty")]
        public int difficulty;
    }

    /// <summary>
    /// Response from GET /tracks/:tokenMint/:hourStartUTC
    /// Returns full track detail including the normalized points blob
    /// </summary>
    [Serializable]
    public class TrackDetailResponse
    {
        [JsonProperty("tokenMint")]
        public string tokenMint;

        [JsonProperty("hourStartUTC")]
        public string hourStartUTC;

        [JsonProperty("trackVersion")]
        public string trackVersion;

        [JsonProperty("pointCount")]
        public int pointCount;

        [JsonProperty("trackHash")]
        public string trackHash;

        [JsonProperty("difficulty")]
        public int difficulty;

        [JsonProperty("normalizedPointsBlobBase64")]
        public string normalizedPointsBlobBase64;
    }
}
