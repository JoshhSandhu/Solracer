using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solracer.Auth;

namespace Solracer.UI
{
    /// <summary>
    /// Login/Title screen
    /// </summary>
    public class LoginScreen : MonoBehaviour
    {
        [Header("Login Screen UI")]
        [Tooltip("Game title text")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Login button")]
        [SerializeField] private Button loginButton;

        [Tooltip("Play as Guest button")]
        [SerializeField] private Button guestButton;

        [Tooltip("Login Canvas GameObject")]
        [SerializeField] private GameObject loginCanvas;

        [Header("UI Panels")]
        [Tooltip("Auth Panel GameObject")]
        [SerializeField] private GameObject authPanel;

        [Tooltip("Email Login Panel GameObject")]
        [SerializeField] private GameObject emailLoginPanel;

        [Tooltip("OTP Verification Panel GameObject")]
        [SerializeField] private GameObject otpVerificationPanel;

        [Tooltip("Welcome Panel GameObject")]
        [SerializeField] private GameObject welcomePanel;

        [Header("Auth Panel Elements")]
        [Tooltip("Connect Wallet button")]
        [SerializeField] private Button connectWalletButton;

        [Tooltip("Login with Email button")]
        [SerializeField] private Button loginEmailButton;

        [Tooltip("Auth Title Text")]
        [SerializeField] private TextMeshProUGUI authTitleText;

        [Tooltip("Auth Description Text")]
        [SerializeField] private TextMeshProUGUI authDescriptionText;

        [Header("Email Login Panel Elements")]
        [Tooltip("Email input field")]
        [SerializeField] private TMP_InputField emailInputField;

        [Tooltip("Send Code button")]
        [SerializeField] private Button sendCodeButton;

        [Tooltip("Back to Auth button")]
        [SerializeField] private Button backToAuthButton;

        [Header("OTP Verification Panel Elements")]
        [Tooltip("OTP Code input field")]
        [SerializeField] private TMP_InputField otpCodeInputField;

        [Tooltip("Verify Code button")]
        [SerializeField] private Button verifyCodeButton;

        [Tooltip("Back to Email button")]
        [SerializeField] private Button backToEmailButton;

        [Header("Welcome Panel Elements")]
        [Tooltip("Welcome Title Text")]
        [SerializeField] private TextMeshProUGUI welcomeTitleText;

        [Tooltip("Wallet Address Text")]
        [SerializeField] private TextMeshProUGUI walletAddressText;

        [Tooltip("User Info Text")]
        [SerializeField] private TextMeshProUGUI userInfoText;

        [Tooltip("Continue button")]
        [SerializeField] private Button continueButton;

        [Tooltip("Logout button")]
        [SerializeField] private Button logoutButton;

        [Header("Welcome Panel - New Design")]
        [Tooltip("Welcome panel card container (semi-transparent background)")]
        [SerializeField] private GameObject welcomePanelCard;

        [Tooltip("Subtitle text: 'Welcome to Solracer'")]
        [SerializeField] private TextMeshProUGUI welcomeSubtitleText;

        [Tooltip("Info section container (the card with wallet/user ID)")]
        [SerializeField] private GameObject infoSection;

        [Tooltip("Wallet label text: 'Solana Wallet'")]
        [SerializeField] private TextMeshProUGUI walletLabelText;

        [Tooltip("User ID label text: 'User ID'")]
        [SerializeField] private TextMeshProUGUI userIdLabelText;

        [Tooltip("Reference to SolracerColors asset (optional - will load from Resources if null)")]
        [SerializeField] private SolracerColors colorScheme;

        [Header("Scene Names")]
        [Tooltip("Scene to load after successful login")]
        [SerializeField] private string tokenPickerSceneName = "TokenPicker";

        [Tooltip("Scene to load for guest mode (ModeSelection)")]
        [SerializeField] private string modeSelectionSceneName = "ModeSelection";

        [Header("Settings")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        private void Start()
        {
            SetupButtons();
            UpdateUI();
            
            // Apply initial login screen styles (ss1)
            ApplyInitialLoginStyles();
            
            //pass auth panel references to AuthenticationFlowManager
            PassAuthPanelReferencesToManager();
           
            AuthenticationFlowManager.OnAuthenticationStateChanged += OnAuthenticationStateChanged;
            
            // Check if we should show welcome panel (user returning from token picker while authenticated)
            if (AuthenticationData.ShouldShowWelcomePanel && AuthenticationData.IsAuthenticated && !AuthenticationData.IsGuestMode)
            {
                // User is authenticated and returning - show welcome panel, hide login canvas
                HideLoginScreenUI();
                AuthenticationData.ShouldShowWelcomePanel = false; // Reset flag
                
                // Trigger welcome panel display via AuthenticationFlowManager
                if (AuthenticationFlowManager.Instance != null)
                {
                    // Wait a frame for AuthenticationFlowManager to be ready, then show welcome panel
                    StartCoroutine(ShowWelcomePanelDelayed());
                }
            }
            else
            {
                // Normal flow - show login canvas
                ShowLoginCanvas();
                EnsureAuthPanelsHidden();
            }
            
            // Apply new design styles
            ApplyWelcomePanelStyles();
        }

        /// <summary>
        /// Coroutine to show welcome panel after a short delay to ensure AuthenticationFlowManager is ready
        /// </summary>
        private System.Collections.IEnumerator ShowWelcomePanelDelayed()
        {
            yield return new WaitForEndOfFrame();
            
            var authManager = AuthenticationFlowManager.Instance;
            if (authManager != null)
            {
                // Check auth state and show welcome panel if authenticated
                StartCoroutine(CheckAuthAndShowWelcome());
            }
        }

        /// <summary>
        /// Coroutine to check authentication state and show welcome panel
        /// </summary>
        private System.Collections.IEnumerator CheckAuthAndShowWelcome()
        {
            var authManager = AuthenticationFlowManager.Instance;
            if (authManager == null) yield break;

            // Wait for Privy to initialize
            yield return new WaitForSeconds(0.2f);

            // Check if user is authenticated and show welcome panel
            if (authManager.IsAuthenticated && !AuthenticationData.IsGuestMode)
            {
                authManager.ShowWelcomePanelIfAuthenticated();
            }
        }

        /// <summary>
        /// Passes auth panel references to AuthenticationFlowManager
        /// Since panels are in Login scene but manager persists across scenes
        /// </summary>
        private void PassAuthPanelReferencesToManager()
        {
            if (AuthenticationFlowManager.Instance != null)
            {
                AuthenticationFlowManager.Instance.SetAuthPanelReferences(
                    authPanel,
                    emailLoginPanel,
                    otpVerificationPanel,
                    welcomePanel,
                    connectWalletButton,
                    loginEmailButton,
                    emailInputField,
                    sendCodeButton,
                    backToAuthButton,
                    otpCodeInputField,
                    verifyCodeButton,
                    backToEmailButton,
                    walletAddressText,
                    userInfoText,
                    continueButton,
                    logoutButton,
                    authTitleText,
                    authDescriptionText,
                    welcomeTitleText
                );
                
                AuthenticationFlowManager.Instance.SetLoginScreenReference(this);
            }
            else
            {
                Debug.LogWarning("LoginScreen: AuthenticationFlowManager.Instance is null. Make sure AuthenticationManager is in the scene.");
            }
        }

        private void SetupButtons()
        {
            if (loginButton != null)
            {
                loginButton.onClick.AddListener(OnLoginClicked);
            }
            else
            {
                Debug.LogWarning("LoginScreen: Login button not found!");
            }

            if (guestButton != null)
            {
                guestButton.onClick.AddListener(OnGuestClicked);
            }
            else
            {
                Debug.LogWarning("LoginScreen: Guest button not found!");
            }
        }

        private void UpdateUI()
        {
            if (titleText != null)
            {
                titleText.text = "Solracer";
            }
        }

        /// <summary>
        /// Applies the Solana Cyberpunk design styles to the initial login screen (ss1)
        /// </summary>
        private void ApplyInitialLoginStyles()
        {
            // Load color scheme if not assigned
            if (colorScheme == null)
            {
                colorScheme = Resources.Load<SolracerColors>("SolracerColors");
                if (colorScheme == null)
                {
                    Debug.LogWarning("LoginScreen: SolracerColors not found in Resources! Create it first.");
                    return;
                }
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style title: "Solracer"
            if (titleText != null)
            {
                UIStyleHelper.SetFont(titleText, UIStyleHelper.FontType.Orbitron);
                titleText.text = "SOLRACER";
                titleText.color = new Color32(255, 255, 255, 255); // #ffffff - white
                titleText.fontStyle = FontStyles.Bold;
                titleText.characterSpacing = 8; // letter-spacing
                titleText.alignment = TextAlignmentOptions.Center;
                
                // Add glow effect
                var outline = titleText.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = titleText.gameObject.AddComponent<Outline>();
                }
                outline.effectColor = new Color32(255, 255, 255, 77); // White glow at 30% opacity
                outline.effectDistance = new Vector2(2, 2);
            }
        }

        /// <summary>
        /// Called when Login button is clicked
        /// </summary>
        public void OnLoginClicked()
        {
            if (debugLogging)
            {
                Debug.Log("LoginScreen: Login button clicked");
            }

            if (AuthenticationFlowManager.Instance == null)
            {
                Debug.LogError("LoginScreen: AuthenticationFlowManager not found! Please add it to the scene.");
                return;
            }

            HideLoginScreenUI();
            AuthenticationFlowManager.Instance.ShowAuthPanelOnLogin();
        }

        /// <summary>
        /// Shows the login canvas
        /// </summary>
        public void ShowLoginCanvas()
        {
            if (loginCanvas != null)
            {
                loginCanvas.SetActive(true);
            }
            else if (titleText != null)
            {
                //Fallback: ensure title text is visible
                titleText.gameObject.SetActive(true);
            }
            if (loginButton != null)
            {
                loginButton.gameObject.SetActive(true);
            }
            if (guestButton != null)
            {
                guestButton.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Hides the login screen UI elements
        /// </summary>
        public void HideLoginScreenUI()
        {
            if (loginCanvas != null)
            {
                loginCanvas.SetActive(false);
            }
            else
            {
                //Fallback: hide individual elements
                if (titleText != null && titleText.gameObject != null)
                {
                    titleText.gameObject.SetActive(false);
                }
                if (loginButton != null && loginButton.gameObject != null)
                {
                    loginButton.gameObject.SetActive(false);
                }
                if (guestButton != null && guestButton.gameObject != null)
                {
                    guestButton.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Ensures AuthenticationFlowManager panels are hidden
        /// </summary>
        private void EnsureAuthPanelsHidden()
        {
            if (AuthenticationFlowManager.Instance != null)
            {
                AuthenticationFlowManager.Instance.EnsurePanelsHidden();
            }
        }

        /// <summary>
        /// Called when Play as Guest button is clicked
        /// </summary>
        public void OnGuestClicked()
        {
            if (debugLogging)
            {
                Debug.Log("LoginScreen: Play as Guest clicked - Loading ModeSelection");
            }

            AuthenticationData.IsGuestMode = true;
            AuthenticationData.IsAuthenticated = false;
            AuthenticationData.Reset();

            LoadModeSelectionScene();
        }

        /// <summary>
        /// Called when authentication state changes
        /// </summary>
        private void OnAuthenticationStateChanged(bool isAuthenticated)
        {
            if (isAuthenticated)
            {
                if (debugLogging)
                {
                    Debug.Log("LoginScreen: Authentication successful - Welcome Panel should be shown");
                }
            }
            else
            {
                //User logged out
                ShowLoginCanvas();
            }
        }

        /// <summary>
        /// Loads TokenPicker scene
        /// </summary>
        private void LoadTokenPickerScene()
        {
            if (string.IsNullOrEmpty(tokenPickerSceneName))
            {
                Debug.LogError("LoginScreen: TokenPicker scene name is empty!");
                return;
            }

            try
            {
                SceneManager.LoadScene(tokenPickerSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"LoginScreen: Failed to load scene '{tokenPickerSceneName}': {e.Message}");
            }
        }

        /// <summary>
        /// Loads ModeSelection scene
        /// </summary>
        private void LoadModeSelectionScene()
        {
            if (string.IsNullOrEmpty(modeSelectionSceneName))
            {
                Debug.LogError("LoginScreen: ModeSelection scene name is empty!");
                return;
            }

            try
            {
                SceneManager.LoadScene(modeSelectionSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"LoginScreen: Failed to load scene '{modeSelectionSceneName}': {e.Message}");
            }
        }


        /// <summary>
        /// Applies the new Solana Cyberpunk design styles to the welcome panel
        /// </summary>
        private void ApplyWelcomePanelStyles()
        {
            // Load color scheme if not assigned
            if (colorScheme == null)
            {
                colorScheme = Resources.Load<SolracerColors>("SolracerColors");
                if (colorScheme == null)
                {
                    Debug.LogWarning("LoginScreen: SolracerColors not found in Resources! Create it first.");
                    return;
                }
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style title: "Ready to Race!"
            if (welcomeTitleText != null)
            {
                UIStyleHelper.SetFont(welcomeTitleText, UIStyleHelper.FontType.Orbitron);
                welcomeTitleText.text = "Ready to Race!";
                // Force exact purple color - use Color32 for accurate hex conversion
                welcomeTitleText.color = new Color32(153, 69, 255, 255); // #9945FF - exact Sol Purple
                welcomeTitleText.fontStyle = FontStyles.Bold;
                welcomeTitleText.characterSpacing = 8; // letter-spacing: 2px
                
                // Add outline for glow effect
                var outline = welcomeTitleText.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = welcomeTitleText.gameObject.AddComponent<Outline>();
                }
                outline.effectColor = new Color32(153, 69, 255, 128); // Purple glow at 50% opacity
                outline.effectDistance = new Vector2(2, 2);
                
                if (debugLogging)
                {
                    Debug.Log($"[LoginScreen] Title color set to: {welcomeTitleText.color} (Expected: #9945FF)");
                }
            }

            // Style subtitle: "Welcome to Solracer"
            if (welcomeSubtitleText != null)
            {
                UIStyleHelper.SetFont(welcomeSubtitleText, UIStyleHelper.FontType.Exo2);
                welcomeSubtitleText.text = "Welcome to Solracer";
                welcomeSubtitleText.color = colorScheme.textSecondary;
                welcomeSubtitleText.fontStyle = FontStyles.Normal;
            }

            // Style info section card
            if (infoSection != null)
            {
                UIStyleHelper.StyleCard(infoSection, useGreenBorder: false);
            }

            // Style wallet label
            if (walletLabelText != null)
            {
                UIStyleHelper.SetFont(walletLabelText, UIStyleHelper.FontType.Exo2);
                walletLabelText.text = "Solana Wallet";
                walletLabelText.color = colorScheme.textSecondary;
                walletLabelText.fontStyle = FontStyles.Normal;
            }

            // Style wallet address value
            if (walletAddressText != null)
            {
                UIStyleHelper.SetFont(walletAddressText, UIStyleHelper.FontType.JetBrainsMono);
                walletAddressText.color = new Color32(20, 241, 149, 255); // #14F195 - exact Sol Green
                walletAddressText.fontStyle = FontStyles.Normal;
                // Truncation will be handled by AuthenticationFlowManager
            }

            // Style user ID label
            if (userIdLabelText != null)
            {
                UIStyleHelper.SetFont(userIdLabelText, UIStyleHelper.FontType.Exo2);
                userIdLabelText.text = "User ID";
                userIdLabelText.color = colorScheme.textSecondary;
                userIdLabelText.fontStyle = FontStyles.Normal;
            }

            // Style user info value
            if (userInfoText != null)
            {
                UIStyleHelper.SetFont(userInfoText, UIStyleHelper.FontType.JetBrainsMono);
                userInfoText.color = new Color32(20, 241, 149, 255); // #14F195 - exact Sol Green
                userInfoText.fontStyle = FontStyles.Normal;
                // Truncation will be handled by AuthenticationFlowManager
            }

            // Style welcome panel card background
            if (welcomePanelCard != null)
            {
                var image = welcomePanelCard.GetComponent<Image>();
                if (image == null)
                {
                    image = welcomePanelCard.AddComponent<Image>();
                }
                image.color = colorScheme.GetCardBackgroundWithOpacity(0.85f);
            }
        }

        private void OnDestroy()
        {
            //Unsubscribe from events
            if (AuthenticationFlowManager.Instance != null)
            {
                AuthenticationFlowManager.OnAuthenticationStateChanged -= OnAuthenticationStateChanged;
            }
        }
    }
}

