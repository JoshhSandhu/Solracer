using UnityEngine;

namespace Solracer.UI
{
    /// <summary>
    /// Centralized color scheme for Solracer UI following Solana Cyberpunk design system
    /// </summary>
    [CreateAssetMenu(fileName = "SolracerColors", menuName = "Solracer/Color Scheme")]
    public class SolracerColors : ScriptableObject
    {
        [Header("Backgrounds")]
        [Tooltip("Main app background color")]
        public Color bgDark = new Color32(11, 14, 17, 255);        // #0b0e11

        [Tooltip("Cards, lobbies, and modals background")]
        public Color surfaceDark = new Color32(30, 35, 41, 255); // #1e2329

        [Header("Brand Colors")]
        [Tooltip("Primary buttons, active tabs, accents")]
        public Color solPurple = new Color32(153, 69, 255, 255);   // #9945FF

        [Tooltip("Success states, 'GO' button, positive values")]
        public Color solGreen = new Color32(20, 241, 149, 255);   // #14F195

        [Tooltip("'Stop', 'Logout', negative values")]
        public Color dangerRed = new Color32(239, 68, 68, 255);   // #ef4444

        [Header("Text Colors")]
        [Tooltip("Default white text")]
        public Color textPrimary = new Color32(248, 250, 252, 255); // #f8fafc

        [Tooltip("Labels, muted text")]
        public Color textSecondary = new Color32(148, 163, 184, 255); // #94A3B8

        [Header("UI Element Colors")]
        [Tooltip("Card background with default opacity (85%)")]
        public Color cardBackground = new Color32(30, 35, 41, 217); // surface-dark at 85% opacity

        [Tooltip("Purple border color")]
        public Color borderPurple = new Color32(153, 69, 255, 255);

        [Tooltip("Green border color")]
        public Color borderGreen = new Color32(20, 241, 149, 255);

        /// <summary>
        /// Gets card background color with custom opacity
        /// </summary>
        public Color GetCardBackgroundWithOpacity(float opacity = 0.85f)
        {
            Color c = surfaceDark;
            c.a = opacity;
            return c;
        }

        /// <summary>
        /// Gets a color variant (darker/lighter) for hover states
        /// </summary>
        public Color GetColorVariant(Color baseColor, float brightnessMultiplier)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            v = Mathf.Clamp01(v * brightnessMultiplier);
            return Color.HSVToRGB(h, s, v);
        }
    }
}
