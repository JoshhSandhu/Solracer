using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;

namespace Solracer.UI
{
    /// <summary>
    /// Token Picker screen
    /// </summary>
    public class TokenPickerScreen : MonoBehaviour
    {
        [Header("UI References - Legacy (Dropdown)")]
        [Tooltip("Coin selection dropdown (legacy - will be hidden if coin cards are used)")]
        [SerializeField] private TMP_Dropdown coinDropdown;

        [Header("UI References - New Design (Card Carousel)")]
        [Tooltip("Title text: 'Select Your Coin'")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Coin card buttons (BONK, SOL, ZEC)")]
        [SerializeField] private Button[] coinCardButtons = new Button[3];

        [Tooltip("Coin icon images (one per card)")]
        [SerializeField] private Image[] coinIcons = new Image[3];

        [Tooltip("Coin name texts (one per card)")]
        [SerializeField] private TextMeshProUGUI[] coinNameTexts = new TextMeshProUGUI[3];

        [Tooltip("Coin symbol texts (one per card)")]
        [SerializeField] private TextMeshProUGUI[] coinSymbolTexts = new TextMeshProUGUI[3];

        [Tooltip("Selected indicator text")]
        [SerializeField] private TextMeshProUGUI selectedIndicatorText;

        [Tooltip("Go to Mode Selection button")]
        [SerializeField] private Button goToModeSelectionButton;

        [Tooltip("Button to go back to Mode Selection (legacy)")]
        [SerializeField] private Button backToModeSelectionButton;

        [Header("Coin Settings")]
        [Tooltip("Coin sprites (BONK, Solana, Zcash)")]
        [SerializeField] private Sprite[] coinSpritesArray = new Sprite[3];

        [Tooltip("Path to coin sprites folder (relative to Resources folder)")]
        [SerializeField] private string coinSpritesPath = "Scripts/UI/Coins";

        [Header("Settings")]
        [Tooltip("Scene name for Mode Selection")]
        [SerializeField] private string modeSelectionSceneName = "ModeSelection";

        [Tooltip("Scene name for Race")]
        [SerializeField] private string raceSceneName = "Race";

        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        [Header("On-Chain Race Settings")]
        [Tooltip("Entry fee in SOL")]
        [SerializeField] private float entryFeeSol = 0.01f;

        [Tooltip("Show loading UI during transaction")]
        [SerializeField] private GameObject loadingPanel;

        [Tooltip("Loading text")]
        [SerializeField] private TextMeshProUGUI loadingText;

        [Header("Design System")]
        [Tooltip("Reference to SolracerColors asset (optional - will load from Resources if null)")]
        [SerializeField] private SolracerColors colorScheme;

        private bool isCreatingRace = false;
        private int selectedCoinIndex = 0; // 0 = BONK, 1 = SOL, 2 = ZEC
        private bool[] isCardHighlighted = new bool[3]; // Track which cards are highlighted


        private void Start()
        {
            //auto find UI elements if not assigned
            AutoFindUIElements();

            //load coin sprites
            LoadCoinSprites();

            // Apply new design system styles
            ApplyTokenPickerStyles();

            // Setup UI based on which system is available
            if (coinCardButtons != null && coinCardButtons.Length >= 3 && coinCardButtons[0] != null)
            {
                // New card-based UI
                SetupCoinCards();
                if (coinDropdown != null)
                {
                    coinDropdown.gameObject.SetActive(false); // Hide legacy dropdown
                }
            }
            else
            {
                // Legacy dropdown UI
                SetupDropdown();
            }

            //setup button listener
            SetupButton();
        }

        /// <summary>
        /// auto find  UI elements by name if not assigned
        /// </summary>
        private void AutoFindUIElements()
        {
            if (coinDropdown == null)
            {
                GameObject dropdownObj = GameObject.Find("CoinDropdown") ?? GameObject.Find("CoinSelectionDropdown");
                if (dropdownObj != null)
                {
                    coinDropdown = dropdownObj.GetComponent<TMP_Dropdown>();
                }
            }

            if (backToModeSelectionButton == null)
            {
                backToModeSelectionButton = FindButtonByName("BackToModeSelectionButton") ?? 
                                            FindButtonByName("BackButton") ?? 
                                            FindButtonByName("ModeSelectionButton");
            }
        }

        /// <summary>
        /// loads coin sprites from Resources folder or uses assigned sprites
        /// </summary>
        private void LoadCoinSprites()
        {
            //check if sprites are already assigned
            bool hasAssignedSprites = coinSpritesArray != null && coinSpritesArray.Length >= 3 && 
                                      coinSpritesArray[0] != null && coinSpritesArray[1] != null 
                                      && coinSpritesArray[2] != null;

            if (hasAssignedSprites)
            {
                if (debugLogging)
                {
                    Debug.Log("TokenPickerScreen: Using assigned coin sprites");
                }
                return;
            }

            //load from Resources if not assigned
            string[] coinNames = { "BONK_Coin", "Solana_Coin", "Zcash_Coin" };
            
            for (int i = 0; i < coinNames.Length; i++)
            {
                if (coinSpritesArray == null || i >= coinSpritesArray.Length || coinSpritesArray[i] == null)
                {
                    Sprite sprite = Resources.Load<Sprite>($"{coinSpritesPath}/{coinNames[i]}");
                    if (sprite != null)
                    {
                        if (coinSpritesArray == null || i >= coinSpritesArray.Length)
                        {
                            System.Array.Resize(ref coinSpritesArray, coinNames.Length);
                        }
                        coinSpritesArray[i] = sprite;
                        if (debugLogging)
                        {
                            Debug.Log($"TokenPickerScreen: Loaded sprite for {coinNames[i]} from Resources");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"TokenPickerScreen: Could not load sprite for {coinNames[i]} from Resources/{coinSpritesPath}. Please assign sprites directly in inspector.");
                    }
                }
            }
        }

        /// <summary>
        /// sets up the coin dropdown with options
        /// </summary>
        private void SetupDropdown()
        {
            if (coinDropdown == null)
            {
                Debug.LogWarning("TokenPickerScreen: Coin dropdown not found!");
                return;
            }
            coinDropdown.ClearOptions();

            List<string> options = new List<string>
            {
                CoinSelectionData.GetCoinName(CoinType.BONK),
                CoinSelectionData.GetCoinName(CoinType.Solana),
                CoinSelectionData.GetCoinName(CoinType.Zcash)
            };

            coinDropdown.AddOptions(options);
            coinDropdown.value = 0;
            OnCoinSelected(0);
            coinDropdown.onValueChanged.AddListener(OnCoinSelected);

            if (debugLogging)
            {
                Debug.Log("TokenPickerScreen: Coin dropdown setup complete");
            }
        }

        /// <summary>
        /// called when coin selection changes
        /// </summary>
        private void OnCoinSelected(int index)
        {
            if (index < 0 || index >= System.Enum.GetValues(typeof(CoinType)).Length)
            {
                Debug.LogWarning($"TokenPickerScreen: Invalid coin index {index}");
                return;
            }

            CoinType selectedCoin = (CoinType)index;
            CoinSelectionData.SelectedCoin = selectedCoin;
            if (coinSpritesArray != null && index < coinSpritesArray.Length && coinSpritesArray[index] != null)
            {
                CoinSelectionData.CoinSprite = coinSpritesArray[index];
            }

            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Selected coin: {CoinSelectionData.GetCoinName(selectedCoin)}");
            }
        }

        /// <summary>
        /// sets up button click listener
        /// </summary>
        private void SetupButton()
        {
            // Use new button if available, otherwise fall back to legacy
            Button buttonToUse = goToModeSelectionButton != null ? goToModeSelectionButton : backToModeSelectionButton;
            
            if (buttonToUse != null)
            {
                buttonToUse.onClick.AddListener(OnBackToModeSelectionClicked);
            }
            else
            {
                Debug.LogWarning("TokenPickerScreen: Go to Mode Selection button not found!");
            }
        }

        /// <summary>
        /// Applies the new Solana Cyberpunk design styles to the token picker screen
        /// </summary>
        private void ApplyTokenPickerStyles()
        {
            // Load color scheme if not assigned
            if (colorScheme == null)
            {
                colorScheme = Resources.Load<SolracerColors>("SolracerColors");
                if (colorScheme == null)
                {
                    Debug.LogWarning("TokenPickerScreen: SolracerColors not found in Resources! Create it first.");
                    return;
                }
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style title
            if (titleText != null)
            {
                UIStyleHelper.SetFont(titleText, UIStyleHelper.FontType.Orbitron);
                titleText.text = "Select Your Coin";
                titleText.color = new Color32(153, 69, 255, 255); // #9945FF
                titleText.fontStyle = FontStyles.Bold;
                titleText.characterSpacing = 4;
                titleText.alignment = TextAlignmentOptions.Center;
            }

            // Style selected indicator
            if (selectedIndicatorText != null)
            {
                UIStyleHelper.SetFont(selectedIndicatorText, UIStyleHelper.FontType.Exo2);
                selectedIndicatorText.color = new Color32(148, 163, 184, 255); // #94A3B8
                selectedIndicatorText.alignment = TextAlignmentOptions.Center;
            }

        }

        /// <summary>
        /// Sets up coin cards for the new card-based UI
        /// </summary>
        private void SetupCoinCards()
        {
            if (coinCardButtons == null || coinCardButtons.Length < 3)
            {
                Debug.LogWarning("TokenPickerScreen: Coin card buttons not properly assigned!");
                return;
            }

            // Coin data
            string[] coinNames = { "BONK", "SOL", "ZEC" };
            string[] coinSymbols = { "BONK", "Solana", "Zcash" };
            CoinType[] coinTypes = { CoinType.BONK, CoinType.Solana, CoinType.Zcash };

            for (int i = 0; i < 3; i++)
            {
                int index = i; // Capture for closure

                // Setup button click listener and highlight tracking
                if (coinCardButtons[i] != null)
                {
                    coinCardButtons[i].onClick.RemoveAllListeners();
                    coinCardButtons[i].onClick.AddListener(() => OnCoinCardClicked(index));

                    // Track highlight state for glow effect
                    int cardIndex = index; // Capture for closure
                    
                    // Create event trigger for hover states
                    var eventTrigger = coinCardButtons[i].gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                    if (eventTrigger == null)
                    {
                        eventTrigger = coinCardButtons[i].gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    }

                    // Clear existing triggers
                    eventTrigger.triggers.Clear();

                    // Pointer Enter (hover/highlight)
                    var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry();
                    pointerEnter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                    pointerEnter.callback.AddListener((data) => OnCoinCardHighlighted(cardIndex, true));
                    eventTrigger.triggers.Add(pointerEnter);

                    // Pointer Exit (unhighlight)
                    var pointerExit = new UnityEngine.EventSystems.EventTrigger.Entry();
                    pointerExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                    pointerExit.callback.AddListener((data) => OnCoinCardHighlighted(cardIndex, false));
                    eventTrigger.triggers.Add(pointerExit);

                    // Pointer Down (press)
                    var pointerDown = new UnityEngine.EventSystems.EventTrigger.Entry();
                    pointerDown.eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown;
                    pointerDown.callback.AddListener((data) => OnCoinCardPressed(cardIndex, true));
                    eventTrigger.triggers.Add(pointerDown);

                    // Pointer Up (release)
                    var pointerUp = new UnityEngine.EventSystems.EventTrigger.Entry();
                    pointerUp.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
                    pointerUp.callback.AddListener((data) => OnCoinCardPressed(cardIndex, false));
                    eventTrigger.triggers.Add(pointerUp);
                }

                // Setup coin icon
                if (coinIcons != null && i < coinIcons.Length && coinIcons[i] != null)
                {
                    if (coinSpritesArray != null && i < coinSpritesArray.Length && coinSpritesArray[i] != null)
                    {
                        coinIcons[i].sprite = coinSpritesArray[i];
                        coinIcons[i].preserveAspect = true;
                    }
                }

                // Setup coin name text
                if (coinNameTexts != null && i < coinNameTexts.Length && coinNameTexts[i] != null)
                {
                    UIStyleHelper.SetFont(coinNameTexts[i], UIStyleHelper.FontType.Orbitron);
                    coinNameTexts[i].text = coinNames[i];
                    coinNameTexts[i].fontStyle = FontStyles.Bold;
                    coinNameTexts[i].alignment = TextAlignmentOptions.Center;
                    coinNameTexts[i].color = new Color32(153, 69, 255, 255); // #9945FF (will change to green when selected)
                }

                // Setup coin symbol text
                if (coinSymbolTexts != null && i < coinSymbolTexts.Length && coinSymbolTexts[i] != null)
                {
                    UIStyleHelper.SetFont(coinSymbolTexts[i], UIStyleHelper.FontType.JetBrainsMono);
                    coinSymbolTexts[i].text = coinSymbols[i];
                    coinSymbolTexts[i].alignment = TextAlignmentOptions.Center;
                    coinSymbolTexts[i].color = new Color32(148, 163, 184, 255); // #94A3B8
                }

                // Style coin card (initial state - no highlight yet)
                if (coinCardButtons[i] != null)
                {
                    if (i < isCardHighlighted.Length)
                    {
                        isCardHighlighted[i] = false; // Initialize highlight state
                    }
                    UIStyleHelper.StyleCoinCard(coinCardButtons[i].gameObject, isSelected: (i == selectedCoinIndex), isHighlighted: false);
                }
            }

            // Set initial selection
            OnCoinSelected(selectedCoinIndex);
            UpdateSelectedIndicator();
        }

        /// <summary>
        /// Called when a coin card is clicked
        /// </summary>
        private void OnCoinCardClicked(int index)
        {
            if (index < 0 || index >= 3)
            {
                Debug.LogWarning($"TokenPickerScreen: Invalid coin card index {index}");
                return;
            }

            selectedCoinIndex = index;
            OnCoinSelected(index);
            UpdateSelectedIndicator();
            UpdateCoinCardStyles(); // This will apply both selection and highlight glow
        }

        /// <summary>
        /// Updates the visual styles of all coin cards based on selection and highlight state
        /// </summary>
        private void UpdateCoinCardStyles()
        {
            if (coinCardButtons == null) return;

            for (int i = 0; i < coinCardButtons.Length && i < 3; i++)
            {
                if (coinCardButtons[i] != null)
                {
                    bool isSelected = (i == selectedCoinIndex);
                    bool isHighlighted = (i < isCardHighlighted.Length && isCardHighlighted[i]);
                    UIStyleHelper.StyleCoinCard(coinCardButtons[i].gameObject, isSelected, isHighlighted);

                    // Update coin name color
                    if (coinNameTexts != null && i < coinNameTexts.Length && coinNameTexts[i] != null)
                    {
                        if (isSelected)
                        {
                            coinNameTexts[i].color = new Color32(20, 241, 149, 255); // #14F195 - green when selected
                        }
                        else
                        {
                            coinNameTexts[i].color = new Color32(153, 69, 255, 255); // #9945FF - purple when unselected
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when a coin card is highlighted (hovered)
        /// </summary>
        private void OnCoinCardHighlighted(int index, bool highlighted)
        {
            if (index < 0 || index >= 3) return;

            if (index < isCardHighlighted.Length)
            {
                isCardHighlighted[index] = highlighted;
            }

            // Update visual styles to show/hide highlight glow
            UpdateCoinCardStyles();
        }

        /// <summary>
        /// Called when a coin card is pressed/released
        /// </summary>
        private void OnCoinCardPressed(int index, bool isPressed)
        {
            // Optional: Add press animation or visual feedback
            // For now, we'll just ensure the highlight state is maintained
        }

        /// <summary>
        /// Updates the selected indicator text
        /// </summary>
        private void UpdateSelectedIndicator()
        {
            if (selectedIndicatorText == null) return;

            string[] coinNames = { "BONK", "SOL", "ZEC" };
            if (selectedCoinIndex >= 0 && selectedCoinIndex < coinNames.Length)
            {
                selectedIndicatorText.text = $"{coinNames[selectedCoinIndex]}";
            }
        }

        /// <summary>
        /// called when Back to Mode Selection button is clicked
        /// </summary>
        public void OnBackToModeSelectionClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Back to Mode Selection clicked - Loading {modeSelectionSceneName}");
            }

            LoadModeSelectionScene();
        }

        /// <summary>
        /// called when Start Race button is clicked (now goes to Mode Selection)
        /// </summary>
        public void OnStartRaceClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Continue clicked - Loading {modeSelectionSceneName}");
            }

            // Just load ModeSelection scene - race creation will happen in RaceManager
            LoadModeSelectionScene();
        }

        /// <summary>
        /// creates race on chain and loads race scene.
        /// </summary>
        private async Task CreateRaceAndLoadScene()
        {
            isCreatingRace = true;
            ShowLoadingUI(true);
            UpdateLoadingText("Preparing race...");

            try
            {
                CoinType selectedCoin = CoinSelectionData.SelectedCoin;
                string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);

                if (string.IsNullOrEmpty(tokenMint))
                {
                    Debug.LogError("TokenPickerScreen: Invalid token mint address");
                    UpdateLoadingText("Invalid token selection");
                    ShowLoadingUI(false);
                    isCreatingRace = false;
                    return;
                }

                // Only create race on-chain in competitive mode
                if (GameModeData.IsCompetitive)
                {
                    var authManager = AuthenticationFlowManager.Instance;
                    if (authManager == null || !authManager.IsAuthenticated)
                    {
                        Debug.LogError("TokenPickerScreen: User not authenticated");
                        UpdateLoadingText("Please log in first");
                        ShowLoadingUI(false);
                        isCreatingRace = false;
                        return;
                    }

                    UpdateLoadingText("Creating race on-chain...");
                    string raceId = await OnChainRaceManager.CreateRaceOnChainAsync(
                        tokenMint,
                        entryFeeSol,
                        (message, progress) =>
                        {
                            UpdateLoadingText(message);
                        }
                    );

                    if (string.IsNullOrEmpty(raceId))
                    {
                        Debug.LogError("TokenPickerScreen: Failed to create race on-chain");
                        UpdateLoadingText("Failed to create race. Please try again.");
                        ShowLoadingUI(false);
                        isCreatingRace = false;
                        return;
                    }

                    RaceData.CurrentRaceId = raceId;
                    RaceData.EntryFeeSol = entryFeeSol;

                    if (debugLogging)
                    {
                        Debug.Log($"TokenPickerScreen: Race created successfully! Race ID: {raceId}");
                    }
                }
                else
                {
                    // Practice mode: no on-chain race creation
                    RaceData.ClearRaceData();
                    if (debugLogging)
                    {
                        Debug.Log("TokenPickerScreen: Practice mode - skipping on-chain race creation");
                    }
                }

                UpdateLoadingText("Loading race...");
                await Task.Delay(500);                  //brief delay to show success message
                LoadRaceScene();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Error creating race: {e.Message}");
                UpdateLoadingText($"Error: {e.Message}");
                ShowLoadingUI(false);
                isCreatingRace = false;
            }
        }

        /// <summary>
        /// loads Race scene
        /// </summary>
        private void LoadRaceScene()
        {
            if (string.IsNullOrEmpty(raceSceneName))
            {
                Debug.LogError("TokenPickerScreen: Race scene name is empty!");
                ShowLoadingUI(false);
                isCreatingRace = false;
                return;
            }

            try
            {
                SceneManager.LoadScene(raceSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Failed to load scene '{raceSceneName}': {e.Message}");
                ShowLoadingUI(false);
                isCreatingRace = false;
            }
        }

        /// <summary>
        /// show or hide loading UI.
        /// </summary>
        private void ShowLoadingUI(bool show)
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(show);
            }
        }

        /// <summary>
        /// update loading text.
        /// </summary>
        private void UpdateLoadingText(string text)
        {
            if (loadingText != null)
            {
                loadingText.text = text;
            }
            else if (debugLogging)
            {
                Debug.Log($"[TokenPickerScreen] {text}");
            }
        }

        /// <summary>
        /// loads Mode Selection scene
        /// </summary>
        private void LoadModeSelectionScene()
        {
            if (string.IsNullOrEmpty(modeSelectionSceneName))
            {
                Debug.LogError("TokenPickerScreen: Mode Selection scene name is empty!");
                return;
            }

            try
            {
                SceneManager.LoadScene(modeSelectionSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Failed to load scene '{modeSelectionSceneName}': {e.Message}");
            }
        }

        /// <summary>
        /// Finds TextMeshProUGUI component by GameObject name
        /// </summary>
        private TextMeshProUGUI FindTextByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<TextMeshProUGUI>();
            }
            return null;
        }

        /// <summary>
        /// Finds Button component by GameObject name
        /// </summary>
        private Button FindButtonByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<Button>();
            }
            return null;
        }
    }
}

