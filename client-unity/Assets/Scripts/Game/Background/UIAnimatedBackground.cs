using UnityEngine;
using TMPro;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Animated background for UI scenes (Lobby, Login, ModeSelection, Results, TokenSelector).
    /// Features auto-scrolling grid, S/R lines, and random graph generation.
    /// </summary>
    public class UIAnimatedBackground : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Camera to use for view calculations")]
        [SerializeField] private Camera mainCamera;

        [Header("Grid Settings")]
        [Tooltip("Enable animated parallax grid")]
        [SerializeField] private bool enableGrid = true;
        
        [Tooltip("Grid scroll speed (units per second)")]
        [SerializeField] private float gridScrollSpeed = 2f;
        
        [Tooltip("Grid line color")]
        [SerializeField] private Color gridColor = new Color(0.25f, 0.25f, 0.32f, 0.35f);
        
        [Tooltip("Grid cell size in world units")]
        [SerializeField] private float gridCellSize = 3f;
        
        [Tooltip("Grid line width")]
        [SerializeField, Range(0.01f, 0.2f)] private float gridLineWidth = 0.04f;

        [Header("S/R Lines Settings")]
        [Tooltip("Enable animated S/R lines")]
        [SerializeField] private bool enableSupportResistance = true;
        
        [Tooltip("S/R line scroll speed (units per second)")]
        [SerializeField] private float srScrollSpeed = 1.5f;
        
        [Tooltip("Resistance line color (green)")]
        [SerializeField] private Color resistanceColor = new Color(0.2f, 0.9f, 0.4f, 0.7f);
        
        [Tooltip("Support line color (red)")]
        [SerializeField] private Color supportColor = new Color(0.9f, 0.2f, 0.2f, 0.6f);
        
        [Tooltip("Number of resistance levels")]
        [SerializeField, Range(1, 5)] private int resistanceLevelCount = 3;
        
        [Tooltip("Number of support levels")]
        [SerializeField, Range(1, 5)] private int supportLevelCount = 2;
        
        [Tooltip("Height spacing between levels")]
        [SerializeField] private float levelSpacing = 15f;
        
        [Tooltip("Dash pattern length")]
        [SerializeField] private float dashLength = 2f;
        
        [Tooltip("Gap between dashes")]
        [SerializeField] private float dashGap = 1f;
        
        [Tooltip("S/R line width")]
        [SerializeField, Range(0.02f, 0.15f)] private float srLineWidth = 0.05f;
        
        [Tooltip("Show price labels on S/R lines")]
        [SerializeField] private bool showLabels = true;
        
        [Tooltip("Label font")]
        [SerializeField] private TMP_FontAsset labelFont;
        
        [Tooltip("Label font size")]
        [SerializeField] private float labelFontSize = 2f;

        [Header("Random Graph Settings")]
        [Tooltip("Enable random graph generation with candles")]
        [SerializeField] private bool enableRandomGraph = true;
        
        [Tooltip("Number of graph segments to generate")]
        [SerializeField, Range(3, 10)] private int graphSegmentCount = 5;
        
        [Tooltip("Graph scroll speed (units per second)")]
        [SerializeField] private float graphScrollSpeed = 1f;
        
        [Tooltip("Graph height range")]
        [SerializeField] private Vector2 graphHeightRange = new Vector2(-10f, 10f);
        
        [Tooltip("Graph segment width")]
        [SerializeField] private float graphSegmentWidth = 20f;
        
        [Tooltip("Graph line color (connects price points)")]
        [SerializeField] private Color graphLineColor = new Color(0.3f, 0.7f, 0.9f, 0.6f); // Cyan/blue
        
        [Tooltip("Graph line width")]
        [SerializeField, Range(0.05f, 0.3f)] private float graphLineWidth = 0.1f;
        
        [Tooltip("Candle green color (bullish)")]
        [SerializeField] private Color candleGreenColor = new Color(0.1f, 0.6f, 0.3f, 0.08f);
        
        [Tooltip("Candle red color (bearish)")]
        [SerializeField] private Color candleRedColor = new Color(0.6f, 0.15f, 0.15f, 0.08f);

        [Header("Background Color")]
        [Tooltip("Solid background color")]
        [SerializeField] private Color backgroundColor = new Color(0.035f, 0.035f, 0.043f, 1f); // #09090B

        // Components
        private GameObject backgroundPanel;
        private AnimatedParallaxGrid animatedGrid;
        private AnimatedSRLines animatedSR;
        private RandomGraphGenerator randomGraph;
        
        private bool isInitialized = false;

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void Start()
        {
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
            
            if (enableGrid)
            {
                CreateAnimatedGrid();
            }
            
            if (enableSupportResistance)
            {
                CreateAnimatedSRLines();
            }
            
            if (enableRandomGraph)
            {
                CreateRandomGraph();
            }

            isInitialized = true;
            Debug.Log("[UIAnimatedBackground] Initialized animated background");
        }

        private void CreateBackgroundPanel()
        {
            backgroundPanel = new GameObject("BackgroundPanel");
            backgroundPanel.transform.SetParent(transform);
            backgroundPanel.transform.localPosition = Vector3.zero;

            SpriteRenderer sr = backgroundPanel.AddComponent<SpriteRenderer>();
            sr.sprite = CreateSolidSprite(backgroundColor);
            sr.sortingOrder = -1000;
            backgroundPanel.transform.localScale = new Vector3(1000, 500, 1);
        }

        private void CreateAnimatedGrid()
        {
            GameObject gridObj = new GameObject("AnimatedParallaxGrid");
            gridObj.transform.SetParent(transform);
            gridObj.transform.localPosition = Vector3.zero;

            animatedGrid = gridObj.AddComponent<AnimatedParallaxGrid>();
            animatedGrid.Initialize(
                gridColor,
                gridCellSize,
                gridLineWidth,
                gridScrollSpeed,
                mainCamera
            );
        }

        private void CreateAnimatedSRLines()
        {
            GameObject srObj = new GameObject("AnimatedSRLines");
            srObj.transform.SetParent(transform);
            srObj.transform.localPosition = Vector3.zero;

            animatedSR = srObj.AddComponent<AnimatedSRLines>();
            animatedSR.Initialize(
                resistanceColor,
                supportColor,
                resistanceLevelCount,
                supportLevelCount,
                levelSpacing,
                dashLength,
                dashGap,
                srLineWidth,
                srScrollSpeed,
                showLabels,
                labelFont,
                labelFontSize,
                mainCamera
            );
        }

        private void CreateRandomGraph()
        {
            GameObject graphObj = new GameObject("RandomGraph");
            graphObj.transform.SetParent(transform);
            graphObj.transform.localPosition = Vector3.zero;

            randomGraph = graphObj.AddComponent<RandomGraphGenerator>();
            randomGraph.Initialize(
                graphSegmentCount,
                graphSegmentWidth,
                graphHeightRange,
                graphScrollSpeed,
                candleGreenColor,
                candleRedColor,
                mainCamera,
                graphLineColor,
                graphLineWidth
            );
        }

        private Sprite CreateSolidSprite(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        /// <summary>
        /// Refreshes all background layers
        /// </summary>
        public void RefreshBackground()
        {
            if (backgroundPanel != null) Destroy(backgroundPanel);
            if (animatedGrid != null) Destroy(animatedGrid.gameObject);
            if (animatedSR != null) Destroy(animatedSR.gameObject);
            if (randomGraph != null) Destroy(randomGraph.gameObject);

            isInitialized = false;
            Initialize();
        }
    }
}

