using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Solracer.UI
{
    /// <summary>
    /// Helper class for applying consistent UI styles across the game
    /// </summary>
    public static class UIStyleHelper
    {
        private static SolracerColors _colors;
        
        public static SolracerColors Colors
        {
            get
            {
                if (_colors == null)
                {
                    _colors = Resources.Load<SolracerColors>("SolracerColors");
                    if (_colors == null)
                    {
                        Debug.LogWarning("SolracerColors not found in Resources! Create it and place in Resources folder.");
                    }
                }
                return _colors;
            }
            set
            {
                _colors = value;
            }
        }

        /// <summary>
        /// Applies Solana button style to a button
        /// Uses direct Image color assignment for more reliable color display
        /// </summary>
        /// <param name="button">Button to style</param>
        /// <param name="isPrimary">True for purple (primary), false for green (success)</param>
        /// <param name="isDanger">True for red (danger/logout)</param>
        public static void StyleButton(Button button, bool isPrimary = true, bool isDanger = false)
        {
            if (button == null) return;
            if (Colors == null)
            {
                Debug.LogWarning("SolracerColors not loaded! Button styling may not work correctly.");
                return;
            }

            // Get or add Image component - set color directly for accurate display
            var image = button.GetComponent<Image>();
            if (image == null)
            {
                image = button.gameObject.AddComponent<Image>();
            }

            // Set Image color directly (more reliable than ColorBlock tinting)
            if (isDanger)
            {
                image.color = Colors.dangerRed; // #ef4444
            }
            else if (isPrimary)
            {
                image.color = Colors.solPurple; // #9945FF - Direct assignment ensures vibrant color
            }
            else
            {
                image.color = Colors.solGreen; // #14F195
            }

            // Set ColorBlock to white so it doesn't multiply/tint the image color
            var colors = button.colors;
            colors.normalColor = Color.white; // White = no tint
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f); // Slight fade on hover
            colors.pressedColor = new Color(1f, 1f, 1f, 0.8f); // More fade on press
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.5f); // Fade for disabled
            button.colors = colors;

            // Set text color
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                text.color = Colors.textPrimary; // #f8fafc
            }

            // Ensure minimum touch target size (44px)
            var rectTransform = button.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                if (rectTransform.sizeDelta.y < 44)
                {
                    rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, 44);
                }
            }
        }

        /// <summary>
        /// Styles a card/panel with semi-transparent background
        /// </summary>
        public static void StyleCard(GameObject card, bool useGreenBorder = false)
        {
            if (card == null) return;
            if (Colors == null)
            {
                Debug.LogWarning("SolracerColors not loaded! Card styling may not work correctly.");
                return;
            }

            var image = card.GetComponent<Image>();
            if (image == null)
            {
                image = card.AddComponent<Image>();
            }

            image.color = Colors.GetCardBackgroundWithOpacity(0.85f);
            
            // Add outline for border effect
            var outline = card.GetComponent<Outline>();
            if (outline == null)
            {
                outline = card.AddComponent<Outline>();
            }
            
            outline.effectColor = useGreenBorder ? Colors.borderGreen : Colors.borderPurple;
            outline.effectDistance = new Vector2(1, 1);
            outline.useGraphicAlpha = true;
        }

        /// <summary>
        /// Styles a coin card for the token picker with enhanced glow effects
        /// </summary>
        /// <param name="card">Coin card GameObject (Button)</param>
        /// <param name="isSelected">True if this card is selected</param>
        /// <param name="isHighlighted">True if this card is highlighted (hovered)</param>
        public static void StyleCoinCard(GameObject card, bool isSelected = false, bool isHighlighted = false)
        {
            if (card == null) return;
            if (Colors == null)
            {
                Debug.LogWarning("SolracerColors not loaded! Coin card styling may not work correctly.");
                return;
            }

            var image = card.GetComponent<Image>();
            if (image == null)
            {
                image = card.AddComponent<Image>();
            }

            // Determine if card should show glow (selected or highlighted)
            bool shouldGlow = isSelected || isHighlighted;

            // Set background color based on selection state
            if (isSelected)
            {
                // Selected: green tinted background
                image.color = new Color32(20, 241, 149, 38); // rgba(20, 241, 149, 0.15)
            }
            else
            {
                // Unselected: dark semi-transparent background
                image.color = Colors.GetCardBackgroundWithOpacity(0.85f);
            }

            // Remove all existing outline/shadow effects to rebuild
            var existingOutlines = card.GetComponents<Outline>();
            var existingShadows = card.GetComponents<Shadow>();
            foreach (var outline in existingOutlines)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(outline);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(outline);
                }
            }
            foreach (var shadow in existingShadows)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(shadow);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(shadow);
                }
            }

            if (shouldGlow)
            {
                // Create multi-layer glow effect for selected/highlighted cards
                Color32 glowColor;
                if (isSelected)
                {
                    glowColor = new Color32(20, 241, 149, 255); // #14F195 - green for selected
                }
                else
                {
                    glowColor = new Color32(153, 69, 255, 255); // #9945FF - purple for highlighted
                }

                // Layer 1: Outer glow (soft, large spread)
                var outerGlow = card.AddComponent<Shadow>();
                outerGlow.effectColor = new Color32(glowColor.r, glowColor.g, glowColor.b, 100); // 40% opacity
                outerGlow.effectDistance = new Vector2(8, 8);
                outerGlow.useGraphicAlpha = true;

                // Layer 2: Middle glow (medium spread)
                var middleGlow = card.AddComponent<Shadow>();
                middleGlow.effectColor = new Color32(glowColor.r, glowColor.g, glowColor.b, 150); // 60% opacity
                middleGlow.effectDistance = new Vector2(4, 4);
                middleGlow.useGraphicAlpha = true;

                // Layer 3: Inner border (sharp, close to edge)
                var innerBorder = card.AddComponent<Outline>();
                innerBorder.effectColor = glowColor; // Full opacity
                innerBorder.effectDistance = new Vector2(2, 2);
                innerBorder.useGraphicAlpha = true;
            }
            else
            {
                // Unselected: single purple border (no glow)
                var outline = card.AddComponent<Outline>();
                outline.effectColor = new Color32(153, 69, 255, 77); // rgba(153, 69, 255, 0.3)
                outline.effectDistance = new Vector2(2, 2);
                outline.useGraphicAlpha = true;
            }
        }

        /// <summary>
        /// Styles a mode card for the mode selection screen with enhanced glow effects
        /// </summary>
        /// <param name="card">Mode card GameObject (Button)</param>
        /// <param name="isSelected">True if this card is selected</param>
        /// <param name="isHighlighted">True if this card is highlighted (hovered)</param>
        public static void StyleModeCard(GameObject card, bool isSelected = false, bool isHighlighted = false)
        {
            if (card == null) return;
            if (Colors == null)
            {
                Debug.LogWarning("SolracerColors not loaded! Mode card styling may not work correctly.");
                return;
            }

            var image = card.GetComponent<Image>();
            if (image == null)
            {
                image = card.AddComponent<Image>();
            }

            // Determine if card should show glow (selected or highlighted)
            bool shouldGlow = isSelected || isHighlighted;

            // Set background color based on selection state
            if (isSelected)
            {
                // Selected: green tinted background
                image.color = new Color32(20, 241, 149, 38); // rgba(20, 241, 149, 0.15)
            }
            else
            {
                // Unselected: dark semi-transparent background
                image.color = Colors.GetCardBackgroundWithOpacity(0.85f);
            }

            // Remove all existing outline/shadow effects to rebuild
            var existingOutlines = card.GetComponents<Outline>();
            var existingShadows = card.GetComponents<Shadow>();
            foreach (var outline in existingOutlines)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(outline);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(outline);
                }
            }
            foreach (var shadow in existingShadows)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(shadow);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(shadow);
                }
            }

            if (shouldGlow)
            {
                // Create multi-layer glow effect for selected/highlighted cards
                Color32 glowColor;
                if (isSelected)
                {
                    glowColor = new Color32(20, 241, 149, 255); // #14F195 - green for selected
                }
                else
                {
                    glowColor = new Color32(153, 69, 255, 255); // #9945FF - purple for highlighted
                }

                // Layer 1: Outer glow (soft, large spread)
                var outerGlow = card.AddComponent<Shadow>();
                outerGlow.effectColor = new Color32(glowColor.r, glowColor.g, glowColor.b, 100); // 40% opacity
                outerGlow.effectDistance = new Vector2(8, 8);
                outerGlow.useGraphicAlpha = true;

                // Layer 2: Middle glow (medium spread)
                var middleGlow = card.AddComponent<Shadow>();
                middleGlow.effectColor = new Color32(glowColor.r, glowColor.g, glowColor.b, 150); // 60% opacity
                middleGlow.effectDistance = new Vector2(4, 4);
                middleGlow.useGraphicAlpha = true;

                // Layer 3: Inner border (sharp, close to edge)
                var innerBorder = card.AddComponent<Outline>();
                innerBorder.effectColor = glowColor; // Full opacity
                innerBorder.effectDistance = new Vector2(2, 2);
                innerBorder.useGraphicAlpha = true;
            }
            else
            {
                // Unselected: single purple border (no glow)
                var outline = card.AddComponent<Outline>();
                outline.effectColor = new Color32(153, 69, 255, 77); // rgba(153, 69, 255, 0.3)
                outline.effectDistance = new Vector2(2, 2);
                outline.useGraphicAlpha = true;
            }
        }

        /// <summary>
        /// Sets font for TextMeshPro component
        /// </summary>
        public static void SetFont(TextMeshProUGUI text, FontType fontType)
        {
            if (text == null) return;
            SetFontTMP((TMP_Text)text, fontType);
        }

        /// <summary>
        /// Sets font for TMP_Text component (base class, works with TextMeshProUGUI, TMP_Text, etc.)
        /// </summary>
        public static void SetFontTMP(TMP_Text text, FontType fontType)
        {
            if (text == null) return;

            TMP_FontAsset font = null;
            string fontPath = "";
            
            switch (fontType)
            {
                case FontType.Orbitron:
                    fontPath = "Fonts/Orbitron-VariableFont_wght SDF";
                    break;
                case FontType.Exo2:
                    fontPath = "Fonts/Exo2-VariableFont_wght SDF";
                    break;
                case FontType.JetBrainsMono:
                    fontPath = "Fonts/JetBrainsMono SDF";
                    break;
            }

            if (!string.IsNullOrEmpty(fontPath))
            {
                font = Resources.Load<TMP_FontAsset>(fontPath);
                if (font == null)
                {
                    Debug.LogWarning($"Font not found at path: {fontPath}. Make sure the font asset exists in Resources folder.");
                }
                else
                {
                    text.font = font;
                }
            }
        }

        /// <summary>
        /// Applies text styling (color, size, etc.)
        /// </summary>
        public static void StyleText(TextMeshProUGUI text, bool isPrimary = true, bool isLabel = false)
        {
            if (text == null || Colors == null) return;

            if (isLabel)
            {
                text.color = Colors.textSecondary;
            }
            else if (isPrimary)
            {
                text.color = Colors.textPrimary;
            }
            else
            {
                text.color = Colors.solGreen; // For data values
            }
        }

        /// <summary>
        /// Truncates wallet address for display
        /// </summary>
        public static string TruncateWallet(string wallet, int startChars = 6, int endChars = 6)
        {
            if (string.IsNullOrEmpty(wallet)) return "";
            if (wallet.Length <= startChars + endChars) return wallet;
            return $"{wallet.Substring(0, startChars)}...{wallet.Substring(wallet.Length - endChars)}";
        }

        /// <summary>
        /// Truncates user ID for display
        /// </summary>
        public static string TruncateUserId(string userId, int startChars = 4, int endChars = 4)
        {
            if (string.IsNullOrEmpty(userId)) return "";
            if (userId.Length <= startChars + endChars) return userId;
            return $"{userId.Substring(0, startChars)}...{userId.Substring(userId.Length - endChars)}";
        }

        /// <summary>
        /// Styles a tab button for the lobby screen
        /// </summary>
        /// <param name="tabButton">Tab button GameObject</param>
        /// <param name="isActive">True if this tab is active</param>
        public static void StyleTab(Button tabButton, bool isActive = false)
        {
            if (tabButton == null) return;
            if (Colors == null)
            {
                Debug.LogWarning("SolracerColors not loaded! Tab styling may not work correctly.");
                return;
            }

            var image = tabButton.GetComponent<Image>();
            if (image == null)
            {
                image = tabButton.gameObject.AddComponent<Image>();
            }

            if (isActive)
            {
                // Active tab: purple background
                image.color = Colors.solPurple; // #9945FF
            }
            else
            {
                // Inactive tab: dark background
                image.color = Colors.GetCardBackgroundWithOpacity(0.85f);
            }

            // Add outline for border effect
            var outline = tabButton.GetComponent<Outline>();
            if (outline == null)
            {
                outline = tabButton.gameObject.AddComponent<Outline>();
            }

            if (isActive)
            {
                outline.effectColor = Colors.solPurple; // Purple glow for active
                outline.effectDistance = new Vector2(2, 2);
            }
            else
            {
                outline.effectColor = new Color32(153, 69, 255, 77); // rgba(153, 69, 255, 0.3)
                outline.effectDistance = new Vector2(1, 1);
            }
            outline.useGraphicAlpha = true;

            // Set button ColorBlock to white for no tinting
            var colors = tabButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.8f);
            tabButton.colors = colors;

            // Set text color
            var text = tabButton.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                if (isActive)
                {
                    text.color = Colors.textPrimary; // White for active
                }
                else
                {
                    text.color = Colors.textSecondary; // Secondary text for inactive
                }
            }
        }

        /// <summary>
        /// Styles a form card container
        /// </summary>
        public static void StyleFormCard(GameObject card)
        {
            if (card == null) return;
            StyleCard(card, useGreenBorder: false); // Uses purple border by default
        }

        /// <summary>
        /// Styles an input field (TMP_InputField)
        /// </summary>
        public static void StyleInputField(TMP_InputField inputField, bool isCodeInput = false)
        {
            if (inputField == null || Colors == null) return;

            // Style the text component
            if (inputField.textComponent != null)
            {
                if (isCodeInput)
                {
                    SetFontTMP(inputField.textComponent, FontType.JetBrainsMono);
                    inputField.textComponent.fontSize = 20; // 1.2rem for code
                    inputField.textComponent.characterSpacing = 3; // letter-spacing: 0.2rem
                }
                else
                {
                    SetFontTMP(inputField.textComponent, FontType.Exo2);
                }
                inputField.textComponent.color = Colors.textPrimary; // #f8fafc
            }

            // Style placeholder if exists
            if (inputField.placeholder != null)
            {
                var placeholderText = inputField.placeholder.GetComponent<TextMeshProUGUI>();
                if (placeholderText != null)
                {
                    placeholderText.color = Colors.textSecondary; // #94A3B8
                }
            }

            // Style the background image
            var image = inputField.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color32(0, 0, 0, 128); // rgba(0, 0, 0, 0.5)
            }
        }

        /// <summary>
        /// Styles a dropdown (TMP_Dropdown)
        /// </summary>
        public static void StyleDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null || Colors == null) return;

            // Style the label text
            if (dropdown.captionText != null)
            {
                SetFontTMP(dropdown.captionText, FontType.JetBrainsMono);
                dropdown.captionText.color = Colors.textPrimary; // #f8fafc
            }

            // Style item text
            if (dropdown.itemText != null)
            {
                SetFontTMP(dropdown.itemText, FontType.JetBrainsMono);
                dropdown.itemText.color = Colors.textPrimary;
            }

            // Style the background image
            var image = dropdown.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color32(0, 0, 0, 128); // rgba(0, 0, 0, 0.5)
            }
        }

        public enum FontType
        {
            Orbitron,
            Exo2,
            JetBrainsMono
        }
    }
}
