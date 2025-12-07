using UnityEngine;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Helper script to quickly set up the UI Animated Background in a scene.
    /// Attach this to any GameObject and it will automatically create and configure
    /// the entire animated background system.
    /// 
    /// Usage:
    /// 1. Create an empty GameObject in your UI scene (Lobby, Login, ModeSelection, Results, TokenSelector)
    /// 2. Attach this script
    /// 3. Play - the background will be automatically created
    /// 
    /// Or use the menu: GameObject > Solracer > Create UI Animated Background
    /// </summary>
    public class UIAnimatedBackgroundSetup : MonoBehaviour
    {
        [Header("Quick Setup")]
        [Tooltip("Automatically set up background on Start")]
        [SerializeField] private bool autoSetup = true;
        
        [Header("Theme Presets")]
        [Tooltip("Background theme preset")]
        [SerializeField] private UITheme theme = UITheme.CryptoTerminal;
        
        [Header("Performance")]
        [Tooltip("Reduce effects for mobile/low-end devices")]
        [SerializeField] private bool lowEndMode = false;

        public enum UITheme
        {
            CryptoTerminal,   // Dark with green/red accents, medium scroll speed
            TradingView,      // Classic trading platform look, slower scroll
            Neon,             // Vibrant neon colors, faster scroll
            Minimal           // Subtle, less distracting, slower animations
        }

        private void Start()
        {
            if (autoSetup)
            {
                SetupBackground();
            }
        }

        /// <summary>
        /// Creates the UI Animated Background with current settings
        /// </summary>
        public void SetupBackground()
        {
            // Check if background already exists
            UIAnimatedBackground existing = FindAnyObjectByType<UIAnimatedBackground>();
            if (existing != null)
            {
                Debug.Log("[UIAnimatedBackgroundSetup] Background already exists, skipping setup");
                return;
            }
            
            // Create background container
            GameObject bgContainer = new GameObject("UIAnimatedBackground");
            UIAnimatedBackground bg = bgContainer.AddComponent<UIAnimatedBackground>();
            
            // Apply theme
            ApplyTheme(bg);
            
            Debug.Log($"[UIAnimatedBackgroundSetup] Created UI Animated Background with {theme} theme");
        }

        private void ApplyTheme(UIAnimatedBackground bg)
        {
            // Theme settings would be applied via SerializedObject in editor
            // For runtime, we rely on the default values in UIAnimatedBackground
            
            // If low-end mode, we could disable some effects
            if (lowEndMode)
            {
                // These would be applied if we had setters
                Debug.Log("[UIAnimatedBackgroundSetup] Low-end mode enabled - some effects may be reduced");
            }
            
            // Note: In a full implementation, we could use reflection or public setters
            // to apply theme-specific values like scroll speeds, colors, etc.
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Menu item to create UI Animated Background in scene
        /// </summary>
        [UnityEditor.MenuItem("GameObject/Solracer/Create UI Animated Background", false, 11)]
        static void CreateUIAnimatedBackground()
        {
            GameObject bgObj = new GameObject("UIAnimatedBackground");
            bgObj.AddComponent<UIAnimatedBackground>();
            
            UnityEditor.Selection.activeGameObject = bgObj;
            UnityEditor.Undo.RegisterCreatedObjectUndo(bgObj, "Create UI Animated Background");
            
            Debug.Log("[UIAnimatedBackgroundSetup] Created UI Animated Background via menu");
        }
        #endif
    }
}

