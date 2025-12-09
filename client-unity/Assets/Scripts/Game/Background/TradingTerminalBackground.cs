using UnityEngine;
using TMPro;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Main controller for the Trading Terminal Background system.
    /// Creates and manages all background layers in the correct rendering order.
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
        
        [Tooltip("(Legacy - unused) Candles now spawn at track points")]
        [SerializeField, Range(5, 50)] private int ghostCandleCount = 25;
        
        [Tooltip("Bullish color (near resistance/high price) - GREEN")]
        [SerializeField] private Color ghostCandleGreen = new Color(0.1f, 0.6f, 0.3f, 1f);
        
        [Tooltip("Bearish color (near support/low price) - RED")]
        [SerializeField] private Color ghostCandleRed = new Color(0.6f, 0.15f, 0.15f, 1f);
        
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

        // State
        private Vector3 lastCameraPosition;
        private bool isInitialized = false;

        private void Awake()
        {
            // Find camera if not assigned
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            // Find player if not assigned
            if (playerTransform == null)
            {
                GameObject atv = GameObject.Find("ATV");
                if (atv != null)
                {
                    playerTransform = atv.transform;
                }
            }

            // Find track generator if not assigned
            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
            }
        }

        private async void Start()
        {
            // Wait a frame for track to generate
            await System.Threading.Tasks.Task.Delay(100);
            
            Initialize();
        }

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
            sr.sprite = CreateSolidSprite(backgroundColor);
            sr.sortingOrder = -1000;
            
            // Make it huge to cover any camera movement
            backgroundPanel.transform.localScale = new Vector3(1000, 500, 1);
        }

        /// <summary>
        /// Creates the ghost candle layer (Layer 1)
        /// Candles are now positioned at track data points with color based on S/R proximity
        /// </summary>
        private void CreateGhostCandleLayer()
        {
            GameObject candleLayerObj = new GameObject("GhostCandleLayer");
            candleLayerObj.transform.SetParent(transform);
            candleLayerObj.transform.localPosition = new Vector3(0, 0, 0); // Same Z as track - static in world

            ghostCandleLayer = candleLayerObj.AddComponent<GhostCandleLayer>();
            ghostCandleLayer.Initialize(
                ghostCandleCount,
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
        /// Creates a simple solid color sprite
        /// </summary>
        private Sprite CreateSolidSprite(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        /// <summary>
        /// Refreshes all background layers (useful after settings change)
        /// </summary>
        public void RefreshBackground()
        {
            // Clean up existing
            if (backgroundPanel != null) Destroy(backgroundPanel);
            if (parallaxGrid != null) Destroy(parallaxGrid.gameObject);
            if (ghostCandleLayer != null) Destroy(ghostCandleLayer.gameObject);
            if (srManager != null) Destroy(srManager.gameObject);

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
