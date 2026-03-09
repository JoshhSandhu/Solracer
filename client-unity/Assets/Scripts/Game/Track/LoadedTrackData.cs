namespace Solracer.Game
{
    /// <summary>
    /// Represents the full context of a loaded track.
    /// Populated by TrackLoader after fetching and decoding track data.
    /// </summary>
    public class LoadedTrackData
    {
        /// <summary>
        /// Normalized height values (0..1) for the track.
        /// </summary>
        public float[] NormalizedHeights;

        /// <summary>
        /// Track hash for deterministic verification. Null for mock tracks.
        /// </summary>
        public string TrackHash;

        /// <summary>
        /// Hour-aligned UTC timestamp of the track bucket. Null for mock tracks.
        /// </summary>
        public string HourStartUTC;

        /// <summary>
        /// Token mint address used to generate this track. Null for mock tracks.
        /// </summary>
        public string TokenMint;

        /// <summary>
        /// Number of data points in the track.
        /// </summary>
        public int PointCount;

        /// <summary>
        /// Track difficulty level from the oracle.
        /// </summary>
        public int Difficulty;

        /// <summary>
        /// True if this track was loaded from mock/fallback data.
        /// </summary>
        public bool IsMockData;
    }
}
