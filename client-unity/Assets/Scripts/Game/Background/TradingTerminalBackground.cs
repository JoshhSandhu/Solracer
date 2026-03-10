using System;
using UnityEngine;
using TMPro;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Main controller for the Trading Terminal Background system.
    /// Creates and manages all background layers in the correct rendering order.
    /// 
    /// Initialization is event-driven: subscribes to TrackGenerator.OnTrackDataReady
    /// so layers are created only after track data is guaranteed available.
    /// If track data is regenerated, layers rebuild automatically.
    /// 
    /// Layer Order (back to front):
    /// - Layer 0: Solid Dark Background (#09090B)
    /// - Layer 1: Ghost Candles (5% opacity, 5% parallax)
    /// - Layer 2: Parallax Grid (15% opacity, 15% parallax)
    /// - Layer 3: Support/Resistance Lines (fixed in world space)
    /// - Layer 4: Gameplay (Track + Car) - Not managed here
    /// - Layer 5: UI - Not managed here
    /// </summary>
    public class TradingTerminalBackground : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Camera to follow for parallax effect")]
        [SerializeField] private Camera mainCamera;
        
        [Tooltip("Transform to track for parallax (usually the ATV/player)")]
        [SerializeField] private Transform playerTransform;
        
        [Tooltip("Track generator for height reference")]
        [SerializeField] private TrackGenerator trackGenerator;

        [Header("Background Color")]
        [Tooltip("Solid background color (Layer 0)")]
        [SerializeField] private Color backgroundColor = new Color(0.035f, 0.035f, 0.043f, 1f); // #09090B

        [Header("Grid Settings (Layer 2)")]
        [Tooltip("Enable parallax grid")]
        [SerializeField] private bool enableGrid = true;
        
        [Tooltip("Grid parallax speed (0.1 = 10% of camera movement)")]
        [SerializeField, Range(0.05f, 0.3f)] private float gridParallaxSpeed = 0.15f;
        
        [Tooltip("Grid line color - increase alpha for more visibility")]
        [SerializeField] private Color gridColor = new Color(0.25f, 0.25f, 0.32f, 0.35f); // Brighter, more visible
        
        [Tooltip("Grid cell size in world units")]
        [SerializeField] private float gridCellSize = 3f; // Smaller cells = more lines visible
        
        [Tooltip("Grid line width")]
        [SerializeField, Range(0.01f, 0.2f)] private float gridLineWidth = 0.04f; // Thicker for visibility

        [Header("Ghost Candles (Layer 1) - Track-Based")]
        [Tooltip("Enable ghost candles at track data points")]
        [SerializeField] private bool enableGhostCandles = true;
        
        [Tooltip("Bullish color (near resistance/high price) - Crypto Neon GREEN")]
        [SerializeField] private Color ghostCandleGreen = new Color(0f, 0.9f, 0.46f, 1f); // #00E676, Binance green
        
        [Tooltip("Bearish color (near support/low price) - Crypto Vivid RED")]
        [SerializeField] private Color ghostCandleRed = new Color(1f, 0.32f, 0.32f, 1f); // #FF5252, TradingView red
        
        [Tooltip("Ghost candle opacity - subtle background effect")]
        [SerializeField, Range(0.02f, 1f)] private float ghostCandleOpacity = 0.06f;

        [Header("Support/Resistance Lines (Layer 3)")]
        [Tooltip("Enable support/resistance lines")]
        [SerializeField] private bool enableSupportResistance = true;
        
        [Tooltip("Resistance line color (above player)")]
        [SerializeField] private Color resistanceColor = new Color(0.2f, 0.9f, 0.4f, 0.7f); // Neon green
        
        [Tooltip("Support line color (below player)")]
        [SerializeField] private Color supportColor = new Color(0.9f, 0.2f, 0.2f, 0.6f); // Neon red
        
        [Tooltip("Number of resistance levels")]
        [SerializeField, Range(1, 5)] private int resistanceLevelCount = 3;
        
        [Tooltip("Number of support levels")]
        [SerializeField, Range(1, 5)] private int supportLevelCount = 2;
        
        [Tooltip("Height spacing between levels")]
        [SerializeField] private float levelSpacing = 15f;
        
        [Tooltip("Base height for level calculations (usually track average height)")]
        [SerializeField] private float baseLevelHeight = 0f;
        
        [Tooltip("Dash pattern length")]
        [SerializeField] private float dashLength = 2f;
        
        [Tooltip("Gap between dashes")]
        [SerializeField] private float dashGap = 1f;
        
        [Tooltip("S/R line width")]
        [SerializeField, Range(0.02f, 0.15f)] private float srLineWidth = 0.05f;

        [Header("Labels")]
        [Tooltip("Show price labels on S/R lines")]
        [SerializeField] private bool showLabels = true;
        
        [Tooltip("Label font")]
        [SerializeField] private TMP_FontAsset labelFont;
        
        [Tooltip("Label font size")]
        [SerializeField] private float labelFontSize = 2f;

        // Child objects
        private GameObject backgroundPanel;
        private ParallaxGrid parallaxGrid;
        private GhostCandleLayer ghostCandleLayer;
        private SupportResistanceManager srManager;

        // Cached runtime assets for cleanup
        private Texture2D _bgPanelTex;
        private Sprite _bgPanelSprite;

        // State
        private Vector3 lastCameraPosition;
        private bool isInitialized = false;

        private void Awake()
        {
            // Cache references once, avoid per-frame lookups
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (playerTransform == null)
            {
                GameObject atv = GameObject.Find("ATV");
                if (atv != null)
                {
                    playerTransform = atv.transform;
                }
            }

            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
            }
        }

        private void OnEnable()
        {
            SubscribeToTrackGenerator();
        }

        private void OnDisable()
        {
            UnsubscribeFromTrackGenerator();
        }

        private void OnDestroy()
        {
            UnsubscribeFromTrackGenerator();
            DestroyRuntimeAssets();
        }

        /// <summary>
        /// Subscribes to TrackGenerator.OnTrackDataReady.
        /// Includes missed-event guard: if track data is already available, initializes immediately.
        /// Also includes fallback: if trackGenerator is null, attempts resolution once.
        /// </summary>
        private void SubscribeToTrackGenerator()
        {
            // Fallback resolve if still null (handles odd load-order scenes)
            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
            }

            if (trackGenerator == null)
            {
                Debug.LogWarning("[TradingTerminalBackground] TrackGenerator not found, background cannot initialize");
                return;
            }

            trackGenerator.OnTrackDataReady += OnTrackDataReady;

            // Missed-event guard: if track data is already available, initialize now
            if (trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                Initialize();
            }
        }

        private void UnsubscribeFromTrackGenerator()
        {
            if (trackGenerator != null)
            {
                trackGenerator.OnTrackDataReady -= OnTrackDataReady;
            }
        }

        /// <summary>
        /// Callback when track data becomes ready (or is regenerated).
        /// On first call, initializes. On subsequent calls, rebuilds layers.
        /// </summary>
        private void OnTrackDataReady(Vector2[] trackPoints)
        {
            if (!isInitialized)
            {
                Initialize();
            }
            else
            {
                // Track data was regenerated after init, rebuild layers
                Debug.Log("[TradingTerminalBackground] Track data regenerated, rebuilding background layers");
                RefreshBackground();
            }
        }

        /// <summary>
        /// Idempotent initialization, safe to call from both event callback and missed-event guard.
        /// </summary>
        private void Initialize()
        {
            if (isInitialized) return;

            // Set camera background color
            if (mainCamera != null)
            {
                mainCamera.backgroundColor = backgroundColor;
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
            }

            // Create background layers
            CreateBackgroundPanel();
            
            if (enableGhostCandles)
            {
                CreateGhostCandleLayer();
            }
            
            if (enableGrid)
            {
                CreateParallaxGrid();
            }
            
            if (enableSupportResistance)
            {
                CreateSupportResistanceLines();
            }

            if (mainCamera != null)
            {
                lastCameraPosition = mainCamera.transform.position;
            }

            isInitialized = true;
            Debug.Log("[TradingTerminalBackground] Initialized all background layers");
        }

        private void LateUpdate()
        {
            if (!isInitialized || mainCamera == null) return;

            Vector3 cameraPosition = mainCamera.transform.position;
            Vector3 cameraDelta = cameraPosition - lastCameraPosition;

            // Update parallax grid (moves slower than camera for depth effect)
            if (parallaxGrid != null)
            {
                parallaxGrid.UpdateParallax(cameraDelta, gridParallaxSpeed);
            }

            // Ghost candles are now STATIC at track points - no parallax update needed
            // They move with the world like the track itself

            // Update S/R line visibility based on camera position
            if (srManager != null)
            {
                srManager.UpdateVisibility(cameraPosition);
            }

            lastCameraPosition = cameraPosition;
        }

        /// <summary>
        /// Creates the solid background panel (Layer 0)
        /// </summary>
        private void CreateBackgroundPanel()
        {
            backgroundPanel = new GameObject("BackgroundPanel");
            backgroundPanel.transform.SetParent(transform);
            backgroundPanel.transform.localPosition = new Vector3(495, 0, 100); // Far back

            // Create a large quad for background
            SpriteRenderer sr = backgroundPanel.AddComponent<SpriteRenderer>();
            _bgPanelSprite = CreateSolidSprite(backgroundColor);
            sr.sprite = _bgPanelSprite;
            sr.sortingOrder = -1000;
            
            // Make it huge to cover any camera movement
            backgroundPanel.transform.localScale = new Vector3(1000, 500, 1);
        }

        /// <summary>
        /// Creates the ghost candle layer (Layer 1)
        /// Candles are positioned at track data points with color based on S/R proximity
        /// </summary>
        private void CreateGhostCandleLayer()
        {
            GameObject candleLayerObj = new GameObject("GhostCandleLayer");
            candleLayerObj.transform.SetParent(transform);
            candleLayerObj.transform.localPosition = new Vector3(0, 0, 0); // Same Z as track - static in world

            ghostCandleLayer = candleLayerObj.AddComponent<GhostCandleLayer>();
            ghostCandleLayer.Initialize(
                ghostCandleGreen,
                ghostCandleRed,
                ghostCandleOpacity,
                mainCamera,
                trackGenerator
            );
            
            // Pass S/R levels to candle layer for color calculation
            if (trackGenerator != null && trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                float minY = float.MaxValue;
                float maxY = float.MinValue;
                foreach (var point in trackGenerator.TrackPoints)
                {
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                }
                
                // Support is below min, resistance is above max
                float support = minY - (supportLevelCount * levelSpacing * 0.5f);
                float resistance = maxY + (resistanceLevelCount * levelSpacing * 0.5f);
                
                ghostCandleLayer.SetSupportResistanceLevels(support, resistance);
            }
        }

        /// <summary>
        /// Creates the parallax grid (Layer 2)
        /// </summary>
        private void CreateParallaxGrid()
        {
            GameObject gridObj = new GameObject("ParallaxGrid");
            gridObj.transform.SetParent(transform);
            gridObj.transform.localPosition = new Vector3(495, 0, 80); // Behind S/R lines

            parallaxGrid = gridObj.AddComponent<ParallaxGrid>();
            parallaxGrid.Initialize(
                gridColor,
                gridCellSize,
                gridLineWidth,
                mainCamera
            );
        }

        /// <summary>
        /// Creates support/resistance lines (Layer 3)
        /// </summary>
        private void CreateSupportResistanceLines()
        {
            GameObject srObj = new GameObject("SupportResistanceLines");
            srObj.transform.SetParent(transform);
            srObj.transform.localPosition = new Vector3(1000, 0, 50); // In front of grid, behind gameplay

            srManager = srObj.AddComponent<SupportResistanceManager>();
            
            // Calculate base height from track if available
            float baseHeight = baseLevelHeight;
            if (trackGenerator != null && trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                // Use average track height as base
                float sum = 0f;
                foreach (var point in trackGenerator.TrackPoints)
                {
                    sum += point.y;
                }
                baseHeight = sum / trackGenerator.TrackPoints.Length;
            }

            srManager.Initialize(
                resistanceColor,
                supportColor,
                resistanceLevelCount,
                supportLevelCount,
                levelSpacing,
                baseHeight,
                dashLength,
                dashGap,
                srLineWidth,
                showLabels,
                labelFont,
                labelFontSize,
                mainCamera,
                trackGenerator
            );
        }

        /// <summary>
        /// Creates a simple solid color sprite (instance-owned)
        /// </summary>
        private Sprite CreateSolidSprite(Color color)
        {
            _bgPanelTex = new Texture2D(1, 1);
            _bgPanelTex.SetPixel(0, 0, color);
            _bgPanelTex.Apply();
            return Sprite.Create(_bgPanelTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        /// <summary>
        /// Destroys runtime-created texture/sprite assets to prevent leaks.
        /// Called by OnDestroy and RefreshBackground.
        /// </summary>
        private void DestroyRuntimeAssets()
        {
            if (_bgPanelSprite != null) { Destroy(_bgPanelSprite); _bgPanelSprite = null; }
            if (_bgPanelTex != null) { Destroy(_bgPanelTex); _bgPanelTex = null; }
        }

        /// <summary>
        /// Refreshes all background layers (useful after settings change or track reload).
        /// Hard-resets all references and cached assets before re-initialization.
        /// </summary>
        public void RefreshBackground()
        {
            // Destroy child objects
            if (backgroundPanel != null) Destroy(backgroundPanel);
            if (parallaxGrid != null) Destroy(parallaxGrid.gameObject);
            if (ghostCandleLayer != null) Destroy(ghostCandleLayer.gameObject);
            if (srManager != null) Destroy(srManager.gameObject);

            // Clear references to avoid stale pointers before end-of-frame destruction
            backgroundPanel = null;
            parallaxGrid = null;
            ghostCandleLayer = null;
            srManager = null;

            // Destroy cached runtime assets
            DestroyRuntimeAssets();

            isInitialized = false;
            Initialize();
        }

        /// <summary>
        /// Updates the base height for S/R levels dynamically
        /// </summary>
        public void UpdateBaseLevelHeight(float newBaseHeight)
        {
            baseLevelHeight = newBaseHeight;
            if (srManager != null)
            {
                srManager.UpdateBaseHeight(newBaseHeight);
            }
        }
    }
}
