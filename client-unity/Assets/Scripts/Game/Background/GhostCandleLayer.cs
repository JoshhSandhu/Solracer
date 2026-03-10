using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Creates ghost candlesticks at track data points.
    /// Each candle represents a price point on the track.
    /// 
    /// Color Logic:
    /// - Closer to RESISTANCE level = More GREEN (bullish, high price)
    /// - Closer to SUPPORT level = More RED (bearish, low price)
    /// - Creates a gradient effect showing price strength
    /// 
    /// Size Logic:
    /// - Candle height based on price change (volatility)
    /// - Bigger candles = bigger price movements
    /// </summary>
    public class GhostCandleLayer : MonoBehaviour
    {
        private Color bullishColor;     // Green - near resistance
        private Color bearishColor;     // Red - near support
        private float opacity;
        private Camera mainCamera;
        private TrackGenerator trackGenerator;
        
        // S/R levels for color calculation
        private float resistanceLevel = 10f;
        private float supportLevel = -10f;
        
        private List<GhostCandle> candles = new List<GhostCandle>();
        private bool isInitialized = false;
        
        // Candle sizing
        private float minCandleWidth = 1f;
        private float maxCandleWidth = 2f;
        private float minCandleHeight = 1f;
        private float maxCandleHeight = 5f;
        
        // Sampling - don't create a candle for every point
        private int candleSpacing = 3; // Create candle every N points

        // Instance-owned shared sprite assets (one per component, never static)
        private Texture2D _sharedTex;
        private Sprite _sharedSprite;

        // Epsilon to prevent division by zero in S/R range calculations
        private const float RANGE_EPSILON = 0.001f;

        /// <summary>
        /// Individual ghost candle data
        /// </summary>
        private class GhostCandle
        {
            public GameObject gameObject;
            public SpriteRenderer bodyRenderer;
            public SpriteRenderer topWickRenderer;
            public SpriteRenderer bottomWickRenderer;
            public float priceLevel;
            public bool isBullish;
        }

        /// <summary>
        /// Initializes the ghost candle layer using track data.
        /// candleCount parameter removed, candle count is determined by track point density.
        /// </summary>
        public void Initialize(Color greenColor, Color redColor, 
                              float opacity, Camera camera, TrackGenerator trackGenerator)
        {
            this.bullishColor = greenColor;
            this.bearishColor = redColor;
            this.opacity = opacity;
            this.mainCamera = camera;
            this.trackGenerator = trackGenerator;

            // Create shared sprite asset (instance-owned)
            CreateSharedSprite();
            
            // Calculate S/R levels from track data
            CalculateSupportResistanceLevels();
            
            // Generate candles at track points
            GenerateCandlesFromTrackData();
            
            isInitialized = true;
            Debug.Log($"[GhostCandleLayer] Initialized with {candles.Count} candles based on track data");
        }

        /// <summary>
        /// Sets the support and resistance levels for color calculation
        /// </summary>
        public void SetSupportResistanceLevels(float support, float resistance)
        {
            this.supportLevel = support;
            this.resistanceLevel = resistance;
            
            // Update existing candle colors
            UpdateCandleColors();
        }

        /// <summary>
        /// Calculates S/R levels from track min/max
        /// </summary>
        private void CalculateSupportResistanceLevels()
        {
            if (trackGenerator == null || trackGenerator.TrackPoints == null || trackGenerator.TrackPoints.Length == 0)
            {
                Debug.LogWarning("[GhostCandleLayer] No track data available for S/R calculation");
                return;
            }
            
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            
            foreach (var point in trackGenerator.TrackPoints)
            {
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            
            // Add some padding for visual effect
            float range = maxY - minY;
            if (range < RANGE_EPSILON) range = RANGE_EPSILON; // Epsilon guard
            supportLevel = minY - range * 0.1f;
            resistanceLevel = maxY + range * 0.1f;
            
            Debug.Log($"[GhostCandleLayer] S/R Levels - Support: {supportLevel:F2}, Resistance: {resistanceLevel:F2}");
        }

        /// <summary>
        /// Generates candles at actual track data points
        /// </summary>
        private void GenerateCandlesFromTrackData()
        {
            if (trackGenerator == null || trackGenerator.TrackPoints == null)
            {
                Debug.LogWarning("[GhostCandleLayer] No track generator or points available");
                return;
            }
            
            Vector2[] points = trackGenerator.TrackPoints;
            if (points.Length < 2) return;
            
            // Calculate spacing based on track length
            candleSpacing = Mathf.Max(1, points.Length / 80); // Aim for ~80 candles max
            
            for (int i = 0; i < points.Length - 1; i += candleSpacing)
            {
                Vector2 currentPoint = points[i];
                Vector2 nextPoint = points[Mathf.Min(i + candleSpacing, points.Length - 1)];
                
                CreateCandleAtPoint(i, currentPoint, nextPoint);
            }
        }

        /// <summary>
        /// Creates a single candle at a track point
        /// </summary>
        private void CreateCandleAtPoint(int index, Vector2 currentPoint, Vector2 nextPoint)
        {
            GhostCandle candle = new GhostCandle();
            candle.priceLevel = currentPoint.y;
            
            // Determine if bullish or bearish based on price movement
            float priceChange = nextPoint.y - currentPoint.y;
            candle.isBullish = priceChange >= 0;
            
            // Calculate candle size based on price change magnitude
            // Epsilon guard: prevent division by zero when S/R range collapses
            float range = resistanceLevel - supportLevel;
            if (range < RANGE_EPSILON) range = RANGE_EPSILON;
            float changeRatio = Mathf.Abs(priceChange) / range;
            changeRatio = Mathf.Clamp01(changeRatio * 3f); // Scale up for visibility
            
            float bodyWidth = Mathf.Lerp(minCandleWidth, maxCandleWidth, changeRatio);
            float bodyHeight = Mathf.Lerp(minCandleHeight, maxCandleHeight, changeRatio);
            
            // Position candle anchored to the track point's Y position.
            // Bullish: body extends upward from track. Bearish: body extends downward.
            float candleX = currentPoint.x;
            float candleY = currentPoint.y;
            
            // Offset so the candle base sits on the track line
            float bodyOffset = candle.isBullish ? bodyHeight / 2f : -bodyHeight / 2f;
            
            // Create candle container
            candle.gameObject = new GameObject($"Candle_{index}");
            candle.gameObject.transform.SetParent(transform);
            candle.gameObject.transform.position = new Vector3(candleX, candleY + bodyOffset, 0);
            
            // Calculate color based on proximity to S/R levels
            Color candleColor = CalculateCandleColor(currentPoint.y, candle.isBullish);
            
            // Create body
            CreateCandleBody(candle, bodyWidth, bodyHeight, candleColor);
            
            // Create wicks
            float wickHeight = bodyHeight * Random.Range(0.2f, 0.5f);
            CreateCandleWicks(candle, bodyWidth, bodyHeight, wickHeight, candleColor);
            
            candles.Add(candle);
        }

        /// <summary>
        /// Calculates candle color based on price position between S/R levels.
        /// Includes epsilon guard to prevent NaN from zero-range.
        /// </summary>
        private Color CalculateCandleColor(float priceLevel, bool isBullish)
        {
            // Epsilon guard: prevent division by zero
            float range = resistanceLevel - supportLevel;
            if (range < RANGE_EPSILON) range = RANGE_EPSILON;

            // Calculate how close to resistance (1.0) vs support (0.0)
            float normalizedPosition = (priceLevel - supportLevel) / range;
            normalizedPosition = Mathf.Clamp01(normalizedPosition);
            
            // Blend between bearish (red/support) and bullish (green/resistance)
            Color baseColor = Color.Lerp(bearishColor, bullishColor, normalizedPosition);
            
            // Enhance color based on candle direction
            if (isBullish)
            {
                // Bullish candles get extra green tint
                baseColor = Color.Lerp(baseColor, bullishColor, 0.3f);
            }
            else
            {
                // Bearish candles get extra red tint
                baseColor = Color.Lerp(baseColor, bearishColor, 0.3f);
            }
            
            // Apply opacity
            baseColor.a = opacity;
            
            return baseColor;
        }

        /// <summary>
        /// Creates the candle body sprite using shared sprite asset
        /// </summary>
        private void CreateCandleBody(GhostCandle candle, float width, float height, Color color)
        {
            GameObject body = new GameObject("Body");
            body.transform.SetParent(candle.gameObject.transform);
            body.transform.localPosition = Vector3.zero;
            
            candle.bodyRenderer = body.AddComponent<SpriteRenderer>();
            candle.bodyRenderer.sprite = _sharedSprite;
            candle.bodyRenderer.sortingOrder = -900;
            candle.bodyRenderer.sortingLayerName = "Default";
            candle.bodyRenderer.color = color;
            
            body.transform.localScale = new Vector3(width, height, 1);
        }

        /// <summary>
        /// Creates top and bottom wicks using shared sprite asset
        /// </summary>
        private void CreateCandleWicks(GhostCandle candle, float bodyWidth, float bodyHeight, float wickHeight, Color color)
        {
            float wickWidth = bodyWidth * 0.2f;
            Color wickColor = color;
            wickColor.a *= 0.7f; // Slightly more transparent
            
            // Top wick
            GameObject topWick = new GameObject("TopWick");
            topWick.transform.SetParent(candle.gameObject.transform);
            topWick.transform.localPosition = new Vector3(0, bodyHeight / 2 + wickHeight / 2, 0);
            
            candle.topWickRenderer = topWick.AddComponent<SpriteRenderer>();
            candle.topWickRenderer.sprite = _sharedSprite;
            candle.topWickRenderer.sortingOrder = -901;
            candle.topWickRenderer.sortingLayerName = "Default";
            candle.topWickRenderer.color = wickColor;
            topWick.transform.localScale = new Vector3(wickWidth, wickHeight, 1);
            
            // Bottom wick
            GameObject bottomWick = new GameObject("BottomWick");
            bottomWick.transform.SetParent(candle.gameObject.transform);
            bottomWick.transform.localPosition = new Vector3(0, -bodyHeight / 2 - wickHeight / 2, 0);
            
            candle.bottomWickRenderer = bottomWick.AddComponent<SpriteRenderer>();
            candle.bottomWickRenderer.sprite = _sharedSprite;
            candle.bottomWickRenderer.sortingOrder = -901;
            candle.bottomWickRenderer.sortingLayerName = "Default";
            candle.bottomWickRenderer.color = wickColor;
            bottomWick.transform.localScale = new Vector3(wickWidth, wickHeight, 1);
        }

        /// <summary>
        /// Updates all candle colors (call after S/R levels change)
        /// </summary>
        private void UpdateCandleColors()
        {
            foreach (var candle in candles)
            {
                if (candle.bodyRenderer == null) continue;
                
                Color newColor = CalculateCandleColor(candle.priceLevel, candle.isBullish);
                candle.bodyRenderer.color = newColor;
                
                Color wickColor = newColor;
                wickColor.a *= 0.7f;
                
                if (candle.topWickRenderer != null)
                    candle.topWickRenderer.color = wickColor;
                if (candle.bottomWickRenderer != null)
                    candle.bottomWickRenderer.color = wickColor;
            }
        }

        /// <summary>
        /// Creates a single shared white 1x1 texture+sprite for all candles.
        /// Instance-owned: each GhostCandleLayer has its own copy.
        /// </summary>
        private void CreateSharedSprite()
        {
            _sharedTex = new Texture2D(1, 1);
            _sharedTex.SetPixel(0, 0, Color.white);
            _sharedTex.Apply();
            _sharedSprite = Sprite.Create(_sharedTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        /// <summary>
        /// Updates candle positions with parallax effect
        /// NOTE: Now static - this method does nothing but kept for interface compatibility
        /// </summary>
        public void UpdateParallax(Vector3 cameraDelta, float parallaxSpeed)
        {
            // Candles are now STATIC - they stay at their track positions
            // No parallax movement needed
        }

        /// <summary>
        /// Updates candle opacity dynamically
        /// </summary>
        public void SetOpacity(float newOpacity)
        {
            opacity = Mathf.Clamp01(newOpacity);
            
            foreach (var candle in candles)
            {
                if (candle.bodyRenderer != null)
                {
                    Color c = candle.bodyRenderer.color;
                    c.a = opacity;
                    candle.bodyRenderer.color = c;
                }
                if (candle.topWickRenderer != null)
                {
                    Color c = candle.topWickRenderer.color;
                    c.a = opacity * 0.7f;
                    candle.topWickRenderer.color = c;
                }
                if (candle.bottomWickRenderer != null)
                {
                    Color c = candle.bottomWickRenderer.color;
                    c.a = opacity * 0.7f;
                    candle.bottomWickRenderer.color = c;
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up instance-owned texture/sprite assets
            if (_sharedSprite != null) { Destroy(_sharedSprite); _sharedSprite = null; }
            if (_sharedTex != null) { Destroy(_sharedTex); _sharedTex = null; }
            candles.Clear();
        }
    }
}
