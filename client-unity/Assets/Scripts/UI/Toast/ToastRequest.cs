namespace Solracer.UI.Toast
{
    /// <summary>
    /// Immutable data class describing a single toast notification request.
    /// </summary>
    public class ToastRequest
    {
        /// <summary>Toast severity / visual type</summary>
        public ToastType Type { get; }

        /// <summary>Short title (e.g. "Wallet Connected"). Keep under ~30 chars.</summary>
        public string Title { get; }

        /// <summary>One-line message body. Truncated to MaxMessageLength if longer.</summary>
        public string Message { get; }

        /// <summary>Display duration in seconds (0 = use default for type)</summary>
        public float Duration { get; }

        /// <summary>
        /// Key used for deduplication. Two requests with the same DedupeKey
        /// within the dedup window are treated as duplicates (new one is dropped).
        /// Null/empty = no dedup.
        /// </summary>
        public string DedupeKey { get; }

        /// <summary>Maximum message length before truncation with ellipsis.</summary>
        public const int MaxMessageLength = 120;

        public ToastRequest(ToastType type, string title, string message,
                           float duration = 0f, string dedupeKey = null)
        {
            Type = type;
            Title = title ?? "";
            Duration = duration;
            DedupeKey = dedupeKey;

            // Truncate long backend errors to keep toast readable
            if (message != null && message.Length > MaxMessageLength)
            {
                Message = message.Substring(0, MaxMessageLength - 3) + "...";
            }
            else
            {
                Message = message ?? "";
            }
        }
    }
}
