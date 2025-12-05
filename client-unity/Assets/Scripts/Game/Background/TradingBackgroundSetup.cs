using UnityEngine;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Helper script to quickly set up the Trading Terminal Background in a scene.
    /// Attach this to any GameObject and it will automatically create and configure
    /// the entire background system.
    /// 
    /// Usage:
    /// 1. Create an empty GameObject in your Race scene
    /// 2. Attach this script
    /// 3. Play - the background will be automatically created
    /// 
    /// Or use the menu: GameObject > Solracer > Create Trading Background
    /// </summary>
    public class TradingBackgroundSetup : MonoBehaviour
    {
        [Header("Quick Setup")]
        [Tooltip("Automatically set up background on Start")]
        [SerializeField] private bool autoSetup = true;
        
        [Header("Theme Presets")]
        [SerializeField] private BackgroundTheme theme = BackgroundTheme.CryptoTerminal;
        
        [Header("Performance")]
        [Tooltip("Reduce effects for mobile/low-end devices")]
        [SerializeField] private bool lowEndMode = false;

        public enum BackgroundTheme
        {
            CryptoTerminal,   // Dark with green/red accents
            TradingView,      // Classic trading platform look
            Neon,             // Vibrant neon colors
            Minimal           // Subtle, less distracting
        }

        private void Start()
        {
            if (autoSetup)
            {
                SetupBackground();
            }
        }

        /// <summary>
        /// Creates the Trading Terminal Background with current settings
        /// </summary>
        public void SetupBackground()
        {
            // Check if background already exists
            TradingTerminalBackground existing = FindAnyObjectByType<TradingTerminalBackground>();
            if (existing != null)
            {
                Debug.Log("[TradingBackgroundSetup] Background already exists, skipping setup");
                return;
            }
            
            // Create background container
            GameObject bgContainer = new GameObject("TradingTerminalBackground");
            TradingTerminalBackground bg = bgContainer.AddComponent<TradingTerminalBackground>();
            
            // Apply theme
            ApplyTheme(bg);
            
            Debug.Log($"[TradingBackgroundSetup] Created Trading Terminal Background with {theme} theme");
        }

        private void ApplyTheme(TradingTerminalBackground bg)
        {
            // Theme settings would be applied via SerializedObject in editor
            // For runtime, we rely on the default values in TradingTerminalBackground
            
            // If low-end mode, we could disable some effects
            if (lowEndMode)
            {
                // These would be applied if we had setters
                Debug.Log("[TradingBackgroundSetup] Low-end mode enabled - some effects may be reduced");
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Menu item to create Trading Background in scene
        /// </summary>
        [UnityEditor.MenuItem("GameObject/Solracer/Create Trading Background", false, 10)]
        static void CreateTradingBackground()
        {
            GameObject bgObj = new GameObject("TradingTerminalBackground");
            bgObj.AddComponent<TradingTerminalBackground>();
            
            UnityEditor.Selection.activeGameObject = bgObj;
            UnityEditor.Undo.RegisterCreatedObjectUndo(bgObj, "Create Trading Background");
            
            Debug.Log("[TradingBackgroundSetup] Created Trading Terminal Background via menu");
        }
        #endif
    }
}
