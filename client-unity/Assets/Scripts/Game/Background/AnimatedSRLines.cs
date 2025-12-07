using UnityEngine;
using TMPro;
using System.Collections.Generic;

namespace Solracer.Game.Background
{
    /// <summary>
    /// Auto-scrolling support/resistance lines with seamless looping.
    /// Dashed lines with labels that scroll from left to right.
    /// </summary>
    public class AnimatedSRLines : MonoBehaviour
    {
        private Color resistanceColor;
        private Color supportColor;
        private int resistanceCount;
        private int supportCount;
        private float levelSpacing;
        private float dashLength;
        private float dashGap;
        private float lineWidth;
        private float scrollSpeed;
        private bool showLabels;
        private TMP_FontAsset labelFont;
        private float labelFontSize;
        private Camera mainCamera;
        
        private List<PriceLevelLine> lines = new List<PriceLevelLine>();
        private float scrollOffset = 0f;
        private float lineExtent = 200f; // Length of each line
        
        private bool isInitialized = false;

        /// <summary>
        /// Data for a single price level line
        /// </summary>
        private class PriceLevelLine
        {
            public GameObject container;
            public List<SpriteRenderer> dashes = new List<SpriteRenderer>();
            public TextMeshPro label;
            public float worldY;
            public bool isResistance;
            public string labelText;
        }

        /// <summary>
        /// Initializes the animated S/R lines system
        /// </summary>
        public void Initialize(Color resColor, Color supColor, int resCount, int supCount,
                              float spacing, float dashLen, float gap, float width, float speed,
                              bool labels, TMP_FontAsset font, float fontSize, Camera camera)
        {
            this.resistanceColor = resColor;
            this.supportColor = supColor;
            this.resistanceCount = resCount;
            this.supportCount = supCount;
            this.levelSpacing = spacing;
            this.dashLength = dashLen;
            this.dashGap = gap;
            this.lineWidth = width;
            this.scrollSpeed = speed;
            this.showLabels = labels;
            this.labelFont = font;
            this.labelFontSize = fontSize;
            this.mainCamera = camera;
            
            CreateLines();
            
            isInitialized = true;
            Debug.Log($"[AnimatedSRLines] Created {resistanceCount} resistance and {supportCount} support lines");
        }

        private void CreateLines()
        {
            float baseHeight = 0f;
            
            // Create resistance lines (above base height)
            for (int i = 1; i <= resistanceCount; i++)
            {
                float height = baseHeight + (i * levelSpacing);
                string label = GetResistanceLabel(i, height);
                CreateLine(height, resistanceColor, true, label);
            }
            
            // Create support lines (below base height)
            for (int i = 1; i <= supportCount; i++)
            {
                float height = baseHeight - (i * levelSpacing);
                string label = GetSupportLabel(i, height);
                CreateLine(height, supportColor, false, label);
            }
        }

        private string GetResistanceLabel(int level, float height)
        {
            float basePrice = 100f;
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
            float basePrice = 100f;
            float price = basePrice - (level * levelSpacing * 5f);
            price = Mathf.Max(0.01f, price);
            
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

        private void CreateLine(float worldY, Color color, bool isResistance, string labelText)
        {
            PriceLevelLine line = new PriceLevelLine
            {
                worldY = worldY,
                isResistance = isResistance,
                labelText = labelText
            };
            
            // Create container
            line.container = new GameObject(isResistance ? $"Resistance_{lines.Count}" : $"Support_{lines.Count}");
            line.container.transform.SetParent(transform);
            line.container.transform.position = new Vector3(0, worldY, 0);
            
            // Create dashed line using individual dash sprites
            float currentX = -lineExtent;
            int dashIndex = 0;
            
            while (currentX < lineExtent)
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
                
                line.dashes.Add(sr);
                
                currentX += dashLength + dashGap;
                dashIndex++;
            }
            
            // Create label if enabled
            if (showLabels && mainCamera != null)
            {
                CreateLabel(line);
            }
            
            lines.Add(line);
        }

        private void CreateLabel(PriceLevelLine line)
        {
            GameObject labelObj = new GameObject($"Label_{line.labelText}");
            labelObj.transform.SetParent(transform); // Parent to manager, not line
            
            line.label = labelObj.AddComponent<TextMeshPro>();
            line.label.text = line.labelText;
            line.label.fontSize = labelFontSize;
            line.label.alignment = TextAlignmentOptions.MidlineRight;
            line.label.sortingOrder = -400;
            
            // Set rect transform size for proper alignment
            RectTransform rectTransform = line.label.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(10f, 2f);
                rectTransform.pivot = new Vector2(1f, 0.5f);
            }
            
            if (labelFont != null)
            {
                line.label.font = labelFont;
            }
            
            Color labelColor = line.isResistance ? resistanceColor : supportColor;
            labelColor.a = 0.9f;
            line.label.color = labelColor;
        }

        private Sprite CreateDashSprite()
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }

        private void Update()
        {
            if (!isInitialized) return;
            
            // Scroll lines from left to right
            scrollOffset += scrollSpeed * Time.deltaTime;
            
            // Loop when offset exceeds line extent
            if (scrollOffset > lineExtent * 2)
            {
                scrollOffset -= lineExtent * 2;
            }
            
            // Apply scroll offset to lines
            transform.localPosition = new Vector3(-scrollOffset, 0, 0);
            
            // Update label positions to stay on right side of screen
            if (showLabels && mainCamera != null)
            {
                float screenRightX = mainCamera.transform.position.x + mainCamera.orthographicSize * mainCamera.aspect;
                
                foreach (var line in lines)
                {
                    if (line.label != null)
                    {
                        line.label.transform.position = new Vector3(screenRightX - 0.3f, line.worldY, 0);
                    }
                }
            }
        }
    }
}

