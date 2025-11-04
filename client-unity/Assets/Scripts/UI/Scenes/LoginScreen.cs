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
            
            //pass auth panel references to AuthenticationFlowManager
            PassAuthPanelReferencesToManager();
           
            AuthenticationFlowManager.OnAuthenticationStateChanged += OnAuthenticationStateChanged;
            
            ShowLoginCanvas();
            EnsureAuthPanelsHidden();
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

