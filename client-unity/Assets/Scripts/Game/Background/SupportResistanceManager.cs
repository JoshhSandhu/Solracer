using UnityEngine;
using TMPro;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Manages Support and Resistance price level lines.
    /// These act as visual "altitude" markers in the trading terminal theme.
    /// 
    /// Resistance (Green): Above the player - visual goals to climb towards
    /// Support (Red): Below the player - visual "floor" levels
    /// 
    /// Visual Style:
    /// - Dashed/Dotted lines (not solid - players shouldn't think they can drive on them)
    /// - Fixed to specific Y-positions in world space
    /// - Optional price labels on the right side
    /// </summary>
    public class SupportResistanceManager : MonoBehaviour
    {
        private Color resistanceColor;
        private Color supportColor;
        private int resistanceCount;
        private int supportCount;
        private float levelSpacing;
        private float baseHeight;
        private float dashLength;
        private float dashGap;
        private float lineWidth;
        private bool showLabels;
        private TMP_FontAsset labelFont;
        private float labelFontSize;
        private Camera mainCamera;
        private TrackGenerator trackGenerator;
        
        private List<PriceLevelLine> resistanceLines = new List<PriceLevelLine>();
        private List<PriceLevelLine> supportLines = new List<PriceLevelLine>();
        
        private bool isInitialized = false;

        /// <summary>
        /// Data for a single price level line
        /// </summary>
        private class PriceLevelLine
        {
            public GameObject container;
            public List<SpriteRenderer> dashSprites = new List<SpriteRenderer>();
            public TextMeshPro label;
            public float worldY;
            public float priceValue;
            public bool isResistance;
            public string labelText;
        }

        /// <summary>
        /// Initializes the support/resistance line system
        /// </summary>
        public void Initialize(Color resistanceColor, Color supportColor,
                              int resistanceCount, int supportCount,
                              float levelSpacing, float baseHeight,
                              float dashLength, float dashGap, float lineWidth,
                              bool showLabels, TMP_FontAsset labelFont, float labelFontSize,
                              Camera camera, TrackGenerator trackGenerator)
        {
            this.resistanceColor = resistanceColor;
            this.supportColor = supportColor;
            this.resistanceCount = resistanceCount;
            this.supportCount = supportCount;
            this.levelSpacing = levelSpacing;
            this.baseHeight = baseHeight;
            this.dashLength = dashLength;
            this.dashGap = dashGap;
            this.lineWidth = lineWidth;
            this.showLabels = showLabels;
            this.labelFont = labelFont;
            this.labelFontSize = labelFontSize;
            this.mainCamera = camera;
            this.trackGenerator = trackGenerator;
            
            // Recalculate base height from track if available
            if (trackGenerator != null && trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                float maxY = float.MinValue;
                float minY = float.MaxValue;
                
                foreach (var point in trackGenerator.TrackPoints)
                {
                    maxY = Mathf.Max(maxY, point.y);
                    minY = Mathf.Min(minY, point.y);
                }
                
                this.baseHeight = (maxY + minY) / 2f;
            }
            
            CreateLines();
            
            isInitialized = true;
            Debug.Log($"[SupportResistanceManager] Created {resistanceCount} resistance and {supportCount} support lines. Base height: {this.baseHeight}");
        }

        private void CreateLines()
        {
            // Create resistance lines (above base height)
            for (int i = 1; i <= resistanceCount; i++)
            {
                float height = baseHeight + (i * levelSpacing);
                string label = GetResistanceLabel(i, height);
                CreatePriceLevelLine(height, resistanceColor, true, label);
            }
            
            // Create support lines (below base height)
            for (int i = 1; i <= supportCount; i++)
            {
                float height = baseHeight - (i * levelSpacing);
                string label = GetSupportLabel(i, height);
                CreatePriceLevelLine(height, supportColor, false, label);
            }
        }

        private string GetResistanceLabel(int level, float height)
        {
            // Generate realistic-looking price labels
            float basePrice = 100f + (baseHeight * 10f);
            float price = basePrice + (level * levelSpacing * 5f);
            
            switch (level)
            {
                case 1:
                    return $"R1: ${price:F2}";
                case 2:
                    return $"R2: ${price:F2}";
                case 3:
                    return "ATH (All Time High)";
                default:
                    return $"RES: ${price:F2}";
            }
        }

        private string GetSupportLabel(int level, float height)
        {
            float basePrice = 100f + (baseHeight * 10f);
            float price = basePrice - (level * levelSpacing * 5f);
            price = Mathf.Max(0.01f, price); // Don't go negative
            
            switch (level)
            {
                case 1:
                    return $"S1: ${price:F2}";
                case 2:
                    return $"S2: ${price:F2}";
                default:
                    return $"SUP: ${price:F2}";
            }
        }

        private void CreatePriceLevelLine(float worldY, Color color, bool isResistance, string labelText)
        {
            PriceLevelLine line = new PriceLevelLine
            {
                worldY = worldY,
                isResistance = isResistance,
                labelText = labelText
            };
            
            
            // Create container
            line.container = new GameObject(isResistance ? $"Resistance_{resistanceLines.Count}" : $"Support_{supportLines.Count}");
            line.container.transform.SetParent(transform);
            line.container.transform.position = new Vector3(0, worldY, 0);
            
            // Calculate line extent based on track length and camera view
            float lineExtent = 500f; // Default fallback
            float startX = 0f;
            
            if (trackGenerator != null)
            {
                // Get track length and add padding
                lineExtent = trackGenerator.TrackLength + 100f;
                
                // Get track start position from first point
                if (trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
                {
                    startX = trackGenerator.TrackPoints[0].x - 50f; // Start a bit before track
                }
            }
            
            // Create dashed line using individual dash sprites
            float currentX = startX;
            float endX = startX + lineExtent + 100f; // Extra padding at end
            int dashIndex = 0;
            
            while (currentX < endX)
            {
                GameObject dashObj = new GameObject($"Dash_{dashIndex}");
                dashObj.transform.SetParent(line.container.transform);
                dashObj.transform.localPosition = new Vector3(currentX + dashLength / 2f, 0, 0);
                
                SpriteRenderer sr = dashObj.AddComponent<SpriteRenderer>();
                sr.sprite = CreateDashSprite();
                sr.color = color;
                sr.sortingOrder = -500;
                
                // Scale dash to proper size
                dashObj.transform.localScale = new Vector3(dashLength, lineWidth, 1);
                
                line.dashSprites.Add(sr);
                
                currentX += dashLength + dashGap;
                dashIndex++;
            }
            
            // Create label if enabled
            if (showLabels)
            {
                CreateLabel(line);
            }
            
            if (isResistance)
            {
                resistanceLines.Add(line);
            }
            else
            {
                supportLines.Add(line);
            }
        }

        private void CreateLabel(PriceLevelLine line)
        {
            if (mainCamera == null) return;
            
            // Create label as a root object (not parented to line container)
            // This makes positioning easier since we set world position directly
            GameObject labelObj = new GameObject($"Label_{line.labelText}");
            labelObj.transform.SetParent(transform); // Parent to manager, not line
            
            line.label = labelObj.AddComponent<TextMeshPro>();
            line.label.text = line.labelText;
            line.label.fontSize = labelFontSize;
            line.label.alignment = TextAlignmentOptions.MidlineRight; // Right-aligned, vertically centered
            line.label.sortingOrder = -400;
            
            // Set rect transform size for proper alignment
            RectTransform rectTransform = line.label.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(10f, 2f); // Width for text, height for line
                rectTransform.pivot = new Vector2(1f, 0.5f); // Pivot on right side, center vertically
            }
            
            // Use provided font or default
            if (labelFont != null)
            {
                line.label.font = labelFont;
            }
            
            // Color based on line type
            Color labelColor = line.isResistance ? resistanceColor : supportColor;
            labelColor.a = 0.9f; // Visible for labels
            line.label.color = labelColor;
            
            // Try to add glow effect for neon look (may not work on all TMP versions)
            try
            {
                if (line.label.fontMaterial != null)
                {
                    line.label.fontMaterial.EnableKeyword("GLOW_ON");
                    line.label.fontMaterial.SetFloat("_GlowOffset", 0.3f);
                    line.label.fontMaterial.SetFloat("_GlowOuter", 0.5f);
                }
            }
            catch (System.Exception) { /* Glow not supported */ }
            
            // Text settings
            line.label.enableWordWrapping = false;
            line.label.overflowMode = TextOverflowModes.Overflow;
            
            // Initial position (will be updated in UpdateVisibility)
            float screenRightX = mainCamera.transform.position.x + mainCamera.orthographicSize * mainCamera.aspect;
            labelObj.transform.position = new Vector3(screenRightX - 0.5f, line.worldY, 0);
        }

        private Sprite CreateDashSprite()
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        /// <summary>
        /// Updates label positions to stay on the right side of screen
        /// and updates line visibility based on camera position
        /// </summary>
        public void UpdateVisibility(Vector3 cameraPosition)
        {
            if (!isInitialized || mainCamera == null) return;
            
            float screenRightX = cameraPosition.x + mainCamera.orthographicSize * mainCamera.aspect;
            float viewHeight = mainCamera.orthographicSize;
            
            // Update all lines
            UpdateLineSet(resistanceLines, cameraPosition, screenRightX, viewHeight);
            UpdateLineSet(supportLines, cameraPosition, screenRightX, viewHeight);
        }

        private void UpdateLineSet(List<PriceLevelLine> lines, Vector3 cameraPos, float screenRightX, float viewHeight)
        {
            // Small padding from screen edge
            float labelPadding = 0.3f;
            
            foreach (var line in lines)
            {
                if (line.container == null) continue;
                
                // Update label position to stay on right side of screen
                // Labels should be at the right edge of the camera view, aligned with their S/R line
                if (line.label != null)
                {
                    // Position at right edge of screen, at the line's Y level
                    line.label.transform.position = new Vector3(
                        screenRightX - labelPadding,  // Right edge with small padding
                        line.worldY,                   // Same Y as the S/R line
                        0f
                    );
                }
                
                // Fade lines based on distance from camera center (vertical)
                float distanceFromCenter = Mathf.Abs(line.worldY - cameraPos.y);
                float fadeDistance = viewHeight * 1.5f;
                float alpha = 1f - Mathf.Clamp01((distanceFromCenter - viewHeight) / fadeDistance);
                
                // Apply fade to dashes
                Color baseColor = line.isResistance ? resistanceColor : supportColor;
                Color fadedColor = baseColor;
                fadedColor.a = baseColor.a * alpha;
                
                foreach (var dash in line.dashSprites)
                {
                    if (dash != null)
                    {
                        dash.color = fadedColor;
                    }
                }
                
                // Apply fade to label
                if (line.label != null)
                {
                    Color labelColor = line.isResistance ? resistanceColor : supportColor;
                    labelColor.a = 0.9f * alpha;
                    line.label.color = labelColor;
                }
            }
        }

        /// <summary>
        /// Updates the base height and regenerates lines
        /// </summary>
        public void UpdateBaseHeight(float newBaseHeight)
        {
            baseHeight = newBaseHeight;
            
            // Clean up existing lines
            foreach (var line in resistanceLines)
            {
                if (line.container != null) Destroy(line.container);
            }
            foreach (var line in supportLines)
            {
                if (line.container != null) Destroy(line.container);
            }
            resistanceLines.Clear();
            supportLines.Clear();
            
            // Regenerate
            CreateLines();
        }

        /// <summary>
        /// Adds a new price level at specific height
        /// </summary>
        public void AddPriceLevel(float worldY, bool isResistance, string customLabel = null)
        {
            Color color = isResistance ? resistanceColor : supportColor;
            string label = customLabel ?? (isResistance ? $"RES: {worldY:F2}" : $"SUP: {worldY:F2}");
            CreatePriceLevelLine(worldY, color, isResistance, label);
        }

        /// <summary>
        /// Gets the nearest resistance level above a given height
        /// </summary>
        public float GetNearestResistanceAbove(float currentHeight)
        {
            float nearest = float.MaxValue;
            foreach (var line in resistanceLines)
            {
                if (line.worldY > currentHeight && line.worldY < nearest)
                {
                    nearest = line.worldY;
                }
            }
            return nearest;
        }

        /// <summary>
        /// Gets the nearest support level below a given height
        /// </summary>
        public float GetNearestSupportBelow(float currentHeight)
        {
            float nearest = float.MinValue;
            foreach (var line in supportLines)
            {
                if (line.worldY < currentHeight && line.worldY > nearest)
                {
                    nearest = line.worldY;
                }
            }
            return nearest;
        }
    }
}
