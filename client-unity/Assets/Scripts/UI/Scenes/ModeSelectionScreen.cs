using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Threading.Tasks;
using Solracer.Auth;
using Solracer.Game;

namespace Solracer.UI
{
    /// <summary>
    /// Mode Selection screen
    /// </summary>
    public class ModeSelectionScreen : MonoBehaviour
    {
        [Header("UI References - Legacy (Simple Buttons)")]
        [Tooltip("Practice mode button (legacy - will be hidden if mode cards are used)")]
        [SerializeField] private Button practiceButton;

        [Tooltip("Competitive mode button (legacy - will be hidden if mode cards are used)")]
        [SerializeField] private Button competitiveButton;

        [Header("UI References - New Design (Card-Based)")]
        [Tooltip("Title text: 'Mode Selection'")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Subtitle text: 'Choose your racing experience'")]
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Tooltip("Mode card buttons (Practice, Competitive)")]
        [SerializeField] private Button[] modeCardButtons = new Button[2];

        [Tooltip("Mode name texts (one per card)")]
        [SerializeField] private TextMeshProUGUI[] modeNameTexts = new TextMeshProUGUI[2];

        [Tooltip("Mode description texts (one per card)")]
        [SerializeField] private TextMeshProUGUI[] modeDescriptionTexts = new TextMeshProUGUI[2];

        [Tooltip("Continue button")]
        [SerializeField] private Button continueButton;

        [Tooltip("Back button (top left) - goes back to Token Picker")]
        [SerializeField] private Button backButton;

        [Header("Authentication Modal")]
        [Tooltip("Transaction signing modal for competitive mode authentication")]
        [SerializeField] private TransactionSigningModal authModal;

        [Header("Scene Names")]
        [Tooltip("Scene to load for Practice mode")]
        [SerializeField] private string practiceSceneName = "Race";

        [Tooltip("Scene to load for Competitive mode (goes to Lobby)")]
        [SerializeField] private string competitiveSceneName = "Lobby";

        [Tooltip("Scene to load when going back (Token Picker)")]
        [SerializeField] private string tokenPickerSceneName = "TokenPicker";

        [Header("Settings")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        [Header("Design System")]
        [Tooltip("Reference to SolracerColors asset (optional - will load from Resources if null)")]
        [SerializeField] private SolracerColors colorScheme;

        private int selectedModeIndex = 1; // 0 = Practice, 1 = Competitive (default to Competitive)
        private bool[] isCardHighlighted = new bool[2]; // Track which cards are highlighted

        private void Start()
        {
            AutoFindButtons();
            
            // Apply new design system styles
            ApplyModeSelectionStyles();

            // Setup back button
            SetupBackButton();

            // Setup UI based on which system is available
            if (modeCardButtons != null && modeCardButtons.Length >= 2 && modeCardButtons[0] != null)
            {
                // New card-based UI
                SetupModeCards();
                if (practiceButton != null) practiceButton.gameObject.SetActive(false); // Hide legacy buttons
                if (competitiveButton != null) competitiveButton.gameObject.SetActive(false);
            }
            else
            {
                // Legacy button UI
                SetupButtons();
            }
            
            CheckAuthenticationStatus();
            AutoFindAuthModal();
        }

        /// <summary>
        /// Auto-find authentication modal if not assigned
        /// </summary>
        private void AutoFindAuthModal()
        {
            if (authModal == null)
            {
                authModal = FindFirstObjectByType<TransactionSigningModal>();
                if (authModal == null && debugLogging)
                {
                    Debug.LogWarning("ModeSelectionScreen: TransactionSigningModal not found. Competitive mode will proceed without modal.");
                }
            }
        }

        private void AutoFindButtons()
        {
            if (practiceButton == null)
            {
                practiceButton = FindButtonByName("PracticeButton") ?? FindButtonByName("Practice");
            }

            if (competitiveButton == null)
            {
                competitiveButton = FindButtonByName("CompetitiveButton") ?? FindButtonByName("Competitive");
            }
        }

        /// <summary>
        /// Sets up button click listeners
        /// </summary>
        private void SetupButtons()
        {
            if (practiceButton != null)
            {
                practiceButton.onClick.AddListener(OnPracticeClicked);
            }
            else
            {
                Debug.LogWarning("ModeSelectionScreen: Practice button not found!");
            }

            // Set up competitive button listener
            if (competitiveButton != null)
            {
                // Remove any existing listeners to avoid conflicts
                competitiveButton.onClick.RemoveAllListeners();
                competitiveButton.onClick.AddListener(OnCompetitiveClicked);
            }
            else
            {
                Debug.LogWarning("ModeSelectionScreen: Competitive button not found!");
            }
        }

        /// <summary>
        /// Called when Practice button is clicked
        /// </summary>
        public void OnPracticeClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"ModeSelectionScreen: Practice mode selected - Loading {practiceSceneName}");
            }

            // Set mode to Practice
            GameModeData.CurrentMode = GameMode.Practice;
            LoadScene(practiceSceneName);
        }

        /// <summary>
        /// Called when Competitive button is clicked
        /// </summary>
        public void OnCompetitiveClicked()
        {
            if (debugLogging)
            {
                Debug.Log("ModeSelectionScreen: Competitive button clicked (user approved)");
            }

            if (!AuthenticationData.CanAccessCompetitiveMode)
            {
                Debug.LogWarning("ModeSelectionScreen: Competitive mode requires authentication. Please login first.");
                return;
            }

            if (debugLogging)
            {
                Debug.Log("ModeSelectionScreen: Competitive mode approved - Loading Lobby");
            }

            // Set mode to Competitive
            GameModeData.CurrentMode = GameMode.Competitive;
            
            // Load Lobby scene for race creation/joining
            LoadScene(competitiveSceneName);
        }


        /// <summary>
        /// Checks authentication status and updates UI accordingly
        /// </summary>
        private void CheckAuthenticationStatus()
        {
            bool canAccessCompetitive = AuthenticationData.CanAccessCompetitiveMode;

            // Update legacy competitive button if it exists
            if (competitiveButton != null)
            {
                competitiveButton.interactable = canAccessCompetitive;
            }

            // Update competitive mode card if it exists
            if (modeCardButtons != null && modeCardButtons.Length > 1 && modeCardButtons[1] != null)
            {
                modeCardButtons[1].interactable = canAccessCompetitive;
                
                // If competitive is selected but user can't access it, switch to practice
                if (selectedModeIndex == 1 && !canAccessCompetitive)
                {
                    selectedModeIndex = 0;
                    UpdateModeCardStyles();
                    UpdateContinueButton();
                }
            }

            if (debugLogging)
            {
                Debug.Log($"ModeSelectionScreen: Competitive mode access: {canAccessCompetitive} (Guest: {AuthenticationData.IsGuestMode}, Authenticated: {AuthenticationData.IsAuthenticated})");
            }
        }

        /// <summary>
        /// Loads a scene by name
        /// </summary>
        private void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"ModeSelectionScreen: Scene name is empty! Cannot load scene.");
                return;
            }

            try
            {
                SceneManager.LoadScene(sceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ModeSelectionScreen: Failed to load scene '{sceneName}': {e.Message}");
            }
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

        /// <summary>
        /// Applies the new Solana Cyberpunk design styles to the mode selection screen
        /// </summary>
        private void ApplyModeSelectionStyles()
        {
            // Load color scheme if not assigned
            if (colorScheme == null)
            {
                colorScheme = Resources.Load<SolracerColors>("SolracerColors");
                if (colorScheme == null)
                {
                    Debug.LogWarning("ModeSelectionScreen: SolracerColors not found in Resources! Create it first.");
                    return;
                }
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style title
            if (titleText != null)
            {
                UIStyleHelper.SetFont(titleText, UIStyleHelper.FontType.Orbitron);
                titleText.text = "Mode Selection";
                titleText.color = new Color32(153, 69, 255, 255); // #9945FF
                titleText.fontStyle = FontStyles.Bold;
                titleText.characterSpacing = 4;
                titleText.alignment = TextAlignmentOptions.Center;
            }

            // Style subtitle
            if (subtitleText != null)
            {
                UIStyleHelper.SetFont(subtitleText, UIStyleHelper.FontType.Exo2);
                subtitleText.text = "Choose your racing experience";
                subtitleText.color = new Color32(148, 163, 184, 255); // #94A3B8
                subtitleText.alignment = TextAlignmentOptions.Center;
            }
        }

        /// <summary>
        /// Sets up mode cards for the new card-based UI
        /// </summary>
        private void SetupModeCards()
        {
            if (modeCardButtons == null || modeCardButtons.Length < 2)
            {
                Debug.LogWarning("ModeSelectionScreen: Mode card buttons not properly assigned!");
                return;
            }

            // Mode data
            string[] modeNames = { "Practice", "Competitive" };
            string[] modeDescriptions = 
            { 
                "Race solo to improve your skills. No entry fee, no pressure.",
                "Race against others for real prizes. Entry fee required."
            };
            string[] modeIconTexts = { "🏎️", "🏁" }; // Emoji icons

            for (int i = 0; i < 2; i++)
            {
                int index = i; // Capture for closure

                // Setup button click listener
                if (modeCardButtons[i] != null)
                {
                    modeCardButtons[i].onClick.RemoveAllListeners();
                    modeCardButtons[i].onClick.AddListener(() => OnModeCardClicked(index));
                }

                // Setup mode name text
                if (modeNameTexts != null && i < modeNameTexts.Length && modeNameTexts[i] != null)
                {
                    UIStyleHelper.SetFont(modeNameTexts[i], UIStyleHelper.FontType.Orbitron);
                    modeNameTexts[i].text = modeNames[i];
                    modeNameTexts[i].alignment = TextAlignmentOptions.Center;
                    modeNameTexts[i].color = new Color32(153, 69, 255, 255); // #9945FF (will change to green when selected)
                }

                // Setup mode description text
                if (modeDescriptionTexts != null && i < modeDescriptionTexts.Length && modeDescriptionTexts[i] != null)
                {
                    UIStyleHelper.SetFont(modeDescriptionTexts[i], UIStyleHelper.FontType.Exo2);
                    modeDescriptionTexts[i].text = modeDescriptions[i];
                    modeDescriptionTexts[i].alignment = TextAlignmentOptions.Center;
                    modeDescriptionTexts[i].color = new Color32(148, 163, 184, 255); // #94A3B8
                }

                // Style mode card (initial state)
                if (modeCardButtons[i] != null)
                {
                    if (i < isCardHighlighted.Length)
                    {
                        isCardHighlighted[i] = false; // Initialize highlight state
                    }
                    UIStyleHelper.StyleModeCard(modeCardButtons[i].gameObject, isSelected: (i == selectedModeIndex), isHighlighted: false);
                }

                // Setup hover tracking for glow effect
                SetupCardHoverTracking(i);
            }

            // Set initial selection
            UpdateModeCardStyles();
            UpdateContinueButton();
        }

        /// <summary>
        /// Sets up hover tracking for a mode card
        /// </summary>
        private void SetupCardHoverTracking(int index)
        {
            if (modeCardButtons == null || index >= modeCardButtons.Length || modeCardButtons[index] == null)
                return;

            var eventTrigger = modeCardButtons[index].gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (eventTrigger == null)
            {
                eventTrigger = modeCardButtons[index].gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            }

            eventTrigger.triggers.Clear();

            // Pointer Enter (hover/highlight)
            var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry();
            pointerEnter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            pointerEnter.callback.AddListener((data) => OnModeCardHighlighted(index, true));
            eventTrigger.triggers.Add(pointerEnter);

            // Pointer Exit (unhighlight)
            var pointerExit = new UnityEngine.EventSystems.EventTrigger.Entry();
            pointerExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            pointerExit.callback.AddListener((data) => OnModeCardHighlighted(index, false));
            eventTrigger.triggers.Add(pointerExit);
        }

        /// <summary>
        /// Called when a mode card is clicked
        /// </summary>
        private void OnModeCardClicked(int index)
        {
            if (index < 0 || index >= 2)
            {
                Debug.LogWarning($"ModeSelectionScreen: Invalid mode card index {index}");
                return;
            }

            // Don't allow selection of Competitive if not authenticated
            if (index == 1 && !AuthenticationData.CanAccessCompetitiveMode)
            {
                Debug.LogWarning("ModeSelectionScreen: Competitive mode requires authentication. Please login first.");
                return;
            }

            selectedModeIndex = index;
            UpdateModeCardStyles();
            UpdateContinueButton();
        }

        /// <summary>
        /// Updates the visual styles of all mode cards based on selection and highlight state
        /// </summary>
        private void UpdateModeCardStyles()
        {
            if (modeCardButtons == null) return;

            for (int i = 0; i < modeCardButtons.Length && i < 2; i++)
            {
                if (modeCardButtons[i] != null)
                {
                    bool isSelected = (i == selectedModeIndex);
                    bool isHighlighted = (i < isCardHighlighted.Length && isCardHighlighted[i]);
                    UIStyleHelper.StyleModeCard(modeCardButtons[i].gameObject, isSelected, isHighlighted);

                    // Update mode name color
                    if (modeNameTexts != null && i < modeNameTexts.Length && modeNameTexts[i] != null)
                    {
                        if (isSelected)
                        {
                            modeNameTexts[i].color = new Color32(20, 241, 149, 255); // #14F195 - green when selected
                        }
                        else
                        {
                            modeNameTexts[i].color = new Color32(153, 69, 255, 255); // #9945FF - purple when unselected
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when a mode card is highlighted (hovered)
        /// </summary>
        private void OnModeCardHighlighted(int index, bool highlighted)
        {
            if (index < 0 || index >= 2) return;

            if (index < isCardHighlighted.Length)
            {
                isCardHighlighted[index] = highlighted;
            }

            // Update visual styles to show/hide highlight glow
            UpdateModeCardStyles();
        }

        /// <summary>
        /// Updates the Continue button state based on selection
        /// </summary>
        private void UpdateContinueButton()
        {
            if (continueButton != null)
            {
                // Enable button if a mode is selected
                continueButton.interactable = true;

                // Setup continue button click listener
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(OnContinueClicked);
            }
        }

        /// <summary>
        /// Called when Continue button is clicked
        /// </summary>
        private void OnContinueClicked()
        {
            if (selectedModeIndex == 0)
            {
                // Practice mode
                OnPracticeClicked();
            }
            else if (selectedModeIndex == 1)
            {
                // Competitive mode
                OnCompetitiveClicked();
            }
        }

        /// <summary>
        /// Sets up the back button to navigate to Token Picker
        /// </summary>
        private void SetupBackButton()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackButtonClicked);
            }
        }


        /// <summary>
        /// Called when back button is clicked - navigates to Token Picker
        /// </summary>
        private void OnBackButtonClicked()
        {
            SceneManager.LoadScene(tokenPickerSceneName);
        }
    }
}

