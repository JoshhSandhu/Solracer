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
        /// Initializes the ghost candle layer using track data
        /// </summary>
        public void Initialize(int candleCount, Color greenColor, Color redColor, 
                              float opacity, Camera camera, TrackGenerator trackGenerator)
        {
            this.bullishColor = greenColor;
            this.bearishColor = redColor;
            this.opacity = opacity;
            this.mainCamera = camera;
            this.trackGenerator = trackGenerator;
            
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
            float changeRatio = Mathf.Abs(priceChange) / (resistanceLevel - supportLevel);
            changeRatio = Mathf.Clamp01(changeRatio * 3f); // Scale up for visibility
            
            float bodyWidth = Mathf.Lerp(minCandleWidth, maxCandleWidth, changeRatio);
            float bodyHeight = Mathf.Lerp(minCandleHeight, maxCandleHeight, changeRatio);
            
            // Position candle - slightly offset from track line (behind it)
            float candleX = currentPoint.x;
            float candleY = currentPoint.y;
            
            // For bullish: candle body goes from current to next (green)
            // For bearish: candle body goes from next to current (red)
            float openPrice = candle.isBullish ? currentPoint.y : nextPoint.y;
            float closePrice = candle.isBullish ? nextPoint.y : currentPoint.y;
            float bodyCenter = (openPrice + closePrice) / 2f;
            
            // Create candle container
            candle.gameObject = new GameObject($"Candle_{index}");
            candle.gameObject.transform.SetParent(transform);
            candle.gameObject.transform.position = new Vector3(candleX, bodyCenter, 0);
            
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
        /// Calculates candle color based on price position between S/R levels
        /// </summary>
        private Color CalculateCandleColor(float priceLevel, bool isBullish)
        {
            // Calculate how close to resistance (1.0) vs support (0.0)
            float range = resistanceLevel - supportLevel;
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
        /// Creates the candle body sprite
        /// </summary>
        private void CreateCandleBody(GhostCandle candle, float width, float height, Color color)
        {
            GameObject body = new GameObject("Body");
            body.transform.SetParent(candle.gameObject.transform);
            body.transform.localPosition = Vector3.zero;
            
            candle.bodyRenderer = body.AddComponent<SpriteRenderer>();
            candle.bodyRenderer.sprite = CreateRectSprite();
            candle.bodyRenderer.sortingOrder = -900;
            candle.bodyRenderer.sortingLayerName = "Default";
            candle.bodyRenderer.color = color;
            
            body.transform.localScale = new Vector3(width, height, 1);
        }

        /// <summary>
        /// Creates top and bottom wicks
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
            candle.topWickRenderer.sprite = CreateRectSprite();
            candle.topWickRenderer.sortingOrder = -901;
            candle.topWickRenderer.sortingLayerName = "Default";
            candle.topWickRenderer.color = wickColor;
            topWick.transform.localScale = new Vector3(wickWidth, wickHeight, 1);
            
            // Bottom wick
            GameObject bottomWick = new GameObject("BottomWick");
            bottomWick.transform.SetParent(candle.gameObject.transform);
            bottomWick.transform.localPosition = new Vector3(0, -bodyHeight / 2 - wickHeight / 2, 0);
            
            candle.bottomWickRenderer = bottomWick.AddComponent<SpriteRenderer>();
            candle.bottomWickRenderer.sprite = CreateRectSprite();
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

        private Sprite CreateRectSprite()
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
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
    }
}
