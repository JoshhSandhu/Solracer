using UnityEngine;

namespace Solracer.UI.Toast
{
    /// <summary>
    /// Serializable style configuration for a toast type.
    /// Editable from Inspector on ToastManager.
    /// </summary>
    [System.Serializable]
    public class ToastStyle
    {
        [Tooltip("Background color for this toast type")]
        public Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        [Tooltip("Title text color")]
        public Color titleColor = Color.white;

        [Tooltip("Message text color")]
        public Color messageColor = new Color(0.85f, 0.85f, 0.85f, 1f);

        [Tooltip("Icon tint color")]
        public Color iconTint = Color.white;

        [Tooltip("Default display duration in seconds")]
        public float defaultDuration = 3f;
    }
}
