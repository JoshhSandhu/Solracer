using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Linq;
using System.Collections;
using Privy;
using System.Threading.Tasks;
using Solracer.UI;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Wallet;

namespace Solracer.Auth
{
    /// <summary>
    /// Manages Privy authentication flow
    /// </summary>
    public class AuthenticationFlowManager : MonoBehaviour
    {
        public static AuthenticationFlowManager Instance { get; private set; }

        [Header("Privy Configuration")]
        [SerializeField] private PrivyConfig privyConfig;
        [SerializeField] private PrivyLogLevel logLevel = PrivyLogLevel.INFO;
        [SerializeField] private bool isMobileApp = true; //set to true for mobile builds, false for WebGL

        //set at runtime by login manager
        //UI Panels
        private GameObject authPanel;
        private GameObject emailLoginPanel;
        private GameObject otpVerificationPanel;
        private GameObject welcomePanel;

        //Auth Panel Elements
        private Button connectWalletButton;
        private Button loginEmailButton;
        private TextMeshProUGUI authTitleText;
        private TextMeshProUGUI authDescriptionText;

        //Email Login Panel Elements
        private TMP_InputField emailInputField;
        private Button sendCodeButton;
        private Button backToAuthButton;

        //OTP Verification Panel Elements
        private TMP_InputField otpCodeInputField;
        private Button verifyCodeButton;
        private Button backToEmailButton;

        //Welcome Panel Elements
        private TextMeshProUGUI welcomeTitleText;
        private TextMeshProUGUI walletAddressText;
        private TextMeshProUGUI userInfoText;
        private Button logoutButton;
        private Button continueButton;

        public static event Action<bool> OnAuthenticationStateChanged;

        private IPrivy privyInstance;
        private bool isAuthenticated = false;
        private bool hasWallet = false;
        private string walletAddress = "";
        private string userEmail = "";
        private LoginScreen loginScreen;

        public bool IsAuthenticated => isAuthenticated;
        public bool HasWallet => hasWallet;
        public string WalletAddress => walletAddress;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                HideAllPanels();
            }
        }

        private async void Start()
        {
            HideAllPanels(); //to ensure all the panels are hidden
            
            await InitializePrivy();
            SetupUI();
            StartAuthenticationFlow();
        }

        private async Task InitializePrivy()
        {
            try
            {
                if (privyConfig == null)
                {
                    Debug.LogError("PrivyConfig is not assigned!");
                    return;
                }

                if (string.IsNullOrEmpty(privyConfig.appId) || privyConfig.appId == "your-app-id" || privyConfig.appId.Length < 10)
                {
                    Debug.LogError("Privy App ID is not set!");
                    return;
                }
                if (string.IsNullOrEmpty(privyConfig.clientId) || privyConfig.clientId == "your-client-id" || privyConfig.clientId.Length < 10)
                {
                    Debug.LogError("Privy Client ID is not set!");
                    return;
                }

                //create Privy config
                var config = new Privy.PrivyConfig
                {
                    AppId = privyConfig.appId,
                    ClientId = privyConfig.clientId,
                    LogLevel = logLevel
                };

                privyInstance = PrivyManager.Initialize(config);

                //Wait for initialization
                var authState = await privyInstance.GetAuthState();

                //Setup auth state change callback
                privyInstance.SetAuthStateChangeCallback(OnAuthStateChanged);

                Debug.Log("Privy SDK initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Privy SDK: {ex.Message}");
            }
        }

        private void SetupUI()
        {
            // Apply styling to auth panels
            ApplyAuthPanelStyles();
            
            //Auth panel buttons
            if (connectWalletButton != null)
            {
                connectWalletButton.onClick.AddListener(ConnectWallet);
            }

            if (loginEmailButton != null)
            {
                loginEmailButton.onClick.AddListener(ShowEmailLoginPanel);
            }

            //Email Login UI
            if (sendCodeButton != null)
            {
                sendCodeButton.onClick.AddListener(SendOTPCode);
            }
            if (backToAuthButton != null)
            {
                backToAuthButton.onClick.AddListener(ShowAuthPanel);
            }

            //OTP verification UI
            if (verifyCodeButton != null)
            {
                verifyCodeButton.onClick.AddListener(VerifyOTPCode);
            }
            if (backToEmailButton != null)
            {
                backToEmailButton.onClick.AddListener(ShowEmailLoginPanel);
            }

            //welcome Panel buttons
            if (logoutButton != null)
            {
                logoutButton.onClick.AddListener(Logout);
            }
            if (continueButton != null)
            {
                continueButton.onClick.AddListener(OnContinueClicked);
            }

            UpdateUI();
        }

        private async void StartAuthenticationFlow()
        {
            if (privyInstance != null)
            {
                var authState = await privyInstance.GetAuthState();
                if (authState == AuthState.Authenticated)
                {
                    isAuthenticated = true;
                    AuthenticationData.IsAuthenticated = true;
                }
                //if the user is not authenticated all the panels will remain hidden
            }
        }

        //auth panel for the user to choose a login method
        public void ShowAuthPanelOnLogin()
        {
            ShowAuthPanel();
        }

        private async Task CheckWalletStatus()
        {
            var user = await privyInstance.GetUser();
            if (user == null)
            {
                return;
            }

            //check for solana wallets
            var solanaWallets = user.EmbeddedSolanaWallets;
            if (solanaWallets != null && solanaWallets.Length > 0)
            {
                walletAddress = solanaWallets[0].Address;
                hasWallet = true;
                isAuthenticated = true;
                AuthenticationData.IsAuthenticated = true;
                AuthenticationData.WalletAddress = walletAddress;
                AuthenticationData.CurrentWalletType = WalletType.Privy; // Privy embedded wallet
                Debug.Log($"User has a Solana wallet: {walletAddress}");
                ShowWelcomePanel();
            }
            else
            {
                //if no solana wallet create one
                hasWallet = false;
                await EnsureSolanaWalletAfterAuth();
            }
        }

        private async void OnAuthStateChanged(AuthState newState)
        {
            switch (newState)
            {
                case AuthState.Authenticated:
                    isAuthenticated = true;
                    AuthenticationData.IsAuthenticated = true;
                    await CheckWalletStatus();
                    break;
                case AuthState.Unauthenticated:
                    isAuthenticated = false;
                    hasWallet = false;
                    walletAddress = "";
                    AuthenticationData.Reset();
                    HideAllPanels();        //hide all panels when user logs out
                    
                    if (loginScreen != null)        //show login canvas if LoginScreen is available
                    {
                        loginScreen.ShowLoginCanvas();
                    }
                    break;
            }

            OnAuthenticationStateChanged?.Invoke(isAuthenticated);
        }

        private void ShowAuthPanel()
        {
            HideAllPanels();
            if (authPanel != null) authPanel.SetActive(true);
        }

        private void ShowEmailLoginPanel()
        {
            HideAllPanels();
            if (emailLoginPanel != null) emailLoginPanel.SetActive(true);
        }

        private void ShowOTPVerificationPanel()
        {
            HideAllPanels();
            if (otpVerificationPanel != null) otpVerificationPanel.SetActive(true);
        }

        private void ShowWelcomePanel()
        {
            if (loginScreen != null)
            {
                loginScreen.HideLoginScreenUI();
            }
            HideAllPanels();
            if (welcomePanel != null) welcomePanel.SetActive(true);
            UpdateWelcomePanel();
        }

        /// <summary>
        /// Public method to show welcome panel (called from LoginScreen when returning authenticated)
        /// </summary>
        public void ShowWelcomePanelIfAuthenticated()
        {
            if (isAuthenticated && !AuthenticationData.IsGuestMode)
            {
                ShowWelcomePanel();
            }
        }

        private void HideAllPanels()
        {
            if (authPanel != null) authPanel.SetActive(false);
            if (emailLoginPanel != null) emailLoginPanel.SetActive(false);
            if (otpVerificationPanel != null) otpVerificationPanel.SetActive(false);
            if (welcomePanel != null) welcomePanel.SetActive(false);
        }

        /// <summary>
        /// Public method to ensure all auth panels are hidden
        /// </summary>
        public void EnsurePanelsHidden()
        {
            HideAllPanels();
        }

        /// <summary>
        /// Sets auth panel references from LoginScreen
        /// Since panels are in Login scene but manager persists across scenes
        /// </summary>
        public void SetAuthPanelReferences(
            GameObject authPanelRef,
            GameObject emailLoginPanelRef,
            GameObject otpVerificationPanelRef,
            GameObject welcomePanelRef,
            Button connectWalletButtonRef,
            Button loginEmailButtonRef,
            TMP_InputField emailInputFieldRef,
            Button sendCodeButtonRef,
            Button backToAuthButtonRef,
            TMP_InputField otpCodeInputFieldRef,
            Button verifyCodeButtonRef,
            Button backToEmailButtonRef,
            TextMeshProUGUI walletAddressTextRef,
            TextMeshProUGUI userInfoTextRef,
            Button continueButtonRef,
            Button logoutButtonRef,
            TextMeshProUGUI authTitleTextRef = null,
            TextMeshProUGUI authDescriptionTextRef = null,
            TextMeshProUGUI welcomeTitleTextRef = null)
        {
            // Set panel references
            authPanel = authPanelRef;
            emailLoginPanel = emailLoginPanelRef;
            otpVerificationPanel = otpVerificationPanelRef;
            welcomePanel = welcomePanelRef;

            // Set button references
            connectWalletButton = connectWalletButtonRef;
            loginEmailButton = loginEmailButtonRef;
            sendCodeButton = sendCodeButtonRef;
            backToAuthButton = backToAuthButtonRef;
            verifyCodeButton = verifyCodeButtonRef;
            backToEmailButton = backToEmailButtonRef;
            continueButton = continueButtonRef;
            logoutButton = logoutButtonRef;

            // Set input field references
            emailInputField = emailInputFieldRef;
            otpCodeInputField = otpCodeInputFieldRef;

            // Set text references
            walletAddressText = walletAddressTextRef;
            userInfoText = userInfoTextRef;
            authTitleText = authTitleTextRef;
            authDescriptionText = authDescriptionTextRef;
            welcomeTitleText = welcomeTitleTextRef;

            // Setup UI again with new references
            SetupUI();
            
            // Setup wallet address tap-to-copy handler
            SetupWalletAddressCopyHandler();
            
            // Apply styling after references are set
            ApplyAuthPanelStyles();
            
            // Ensure panels are hidden initially
            HideAllPanels();
        }

        /// <summary>
        /// Sets LoginScreen reference so manager can hide login UI
        /// </summary>
        public void SetLoginScreenReference(LoginScreen loginScreenRef)
        {
            loginScreen = loginScreenRef;
        }

        /// <summary>
        /// Connects to Mock MWA Wallet (Seeker Wallet) using Mobile Wallet Adapter protocol
        /// </summary>
        public async void ConnectWallet()
        {
            try
            {
                // Check if already connected via MWA
                if (AuthenticationData.IsMWAWallet && !string.IsNullOrEmpty(walletAddress))
                {
                    Debug.Log("[ConnectWallet] Already connected to MWA wallet - showing welcome panel...");
                    ShowWelcomePanel();
                    return;
                }

                Debug.Log("[ConnectWallet] Connecting to Mock MWA Wallet (Seeker)...");

                // Get or create MWA adapter instance
                var mwaAdapter = MWAWalletAdapter.Instance;

                // Use SignIn for SIWS (Sign In With Solana) - combines connect + authenticate
                var connectionResult = await mwaAdapter.SignInAsync();

                if (connectionResult.Success && !string.IsNullOrEmpty(connectionResult.WalletAddress))
                {
                    // MWA connection successful
                    walletAddress = connectionResult.WalletAddress;
                    hasWallet = true;
                    isAuthenticated = true;
                    
                    // Update AuthenticationData
                    AuthenticationData.IsAuthenticated = true;
                    AuthenticationData.WalletAddress = walletAddress;
                    AuthenticationData.CurrentWalletType = WalletType.MWA;
                    AuthenticationData.MWAAuthToken = connectionResult.AuthToken ?? "";

                    Debug.Log($"[ConnectWallet] MWA wallet connected successfully");
                    Debug.Log($"[ConnectWallet] Wallet address: {walletAddress}");
                    
                    // Notify listeners
                    OnAuthenticationStateChanged?.Invoke(true);
                    
                    ShowWelcomePanel();
                }
                else
                {
                    Debug.LogWarning($"[ConnectWallet] MWA wallet connection failed: {connectionResult.ErrorMessage}");
                    ShowAuthPanel();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConnectWallet] MWA wallet connection failed: {e.Message}");
                Debug.LogError($"[ConnectWallet] Stack trace: {e.StackTrace}");
                ShowAuthPanel();
            }
        }

        /// <summary>
        /// Checks MWA wallet status if already connected
        /// </summary>
        private async Task CheckMWAWalletStatus()
        {
            var mwaAdapter = MWAWalletAdapter.Instance;
            
            if (mwaAdapter.IsConnected && !string.IsNullOrEmpty(mwaAdapter.WalletAddress))
            {
                walletAddress = mwaAdapter.WalletAddress;
                hasWallet = true;
                isAuthenticated = true;
                AuthenticationData.IsAuthenticated = true;
                AuthenticationData.WalletAddress = walletAddress;
                AuthenticationData.CurrentWalletType = WalletType.MWA;
                
                Debug.Log($"[CheckMWAWalletStatus] MWA wallet still connected: {walletAddress}");
                ShowWelcomePanel();
            }
            else
            {
                Debug.Log("[CheckMWAWalletStatus] MWA wallet not connected, showing auth panel");
                ShowAuthPanel();
            }
            
            await Task.CompletedTask;
        }

        public async void SendOTPCode()
        {
            if (privyInstance == null) return;

            userEmail = emailInputField != null ? emailInputField.text : "";

            if (string.IsNullOrEmpty(userEmail))
            {
                Debug.LogError("Please enter an email address");
                return;
            }

            try
            {
                bool codeSent = await privyInstance.Email.SendCode(userEmail);

                if (codeSent)
                {
                    ShowOTPVerificationPanel();
                }
                else
                {
                    Debug.LogError("Failed to send OTP code");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send OTP code: {e.Message}");
            }
        }

        public async void VerifyOTPCode()
        {
            if (privyInstance == null) return;

            string code = otpCodeInputField != null ? otpCodeInputField.text : "";

            if (string.IsNullOrEmpty(code))
            {
                Debug.LogError("Please enter OTP code");
                return;
            }

            try
            {
                var authState = await privyInstance.Email.LoginWithCode(userEmail, code);

                if (authState == AuthState.Authenticated)
                {
                    await CheckWalletAfterEmailLogin();
                }
                else
                {
                    Debug.LogWarning("Email login failed - invalid code");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Email code verification failed: {e.Message}");
            }
        }

        private async Task CheckWalletAfterEmailLogin()
        {
            await Task.Delay(500);
            
            var authState = await privyInstance.GetAuthState();
            if (authState != AuthState.Authenticated)
            {
                Debug.LogError($"User is not authenticated. Auth state: {authState}");
                ShowAuthPanel();
                return;
            }

            var user = await privyInstance.GetUser();
            if (user == null)
            {
                Debug.LogError("Privy instance or User is null - cannot check wallet");
                ShowAuthPanel();
                return;
            }

            Debug.Log($"User authenticated. User ID: {user.Id}");

            var solanaWallets = user.EmbeddedSolanaWallets;
            if (solanaWallets != null && solanaWallets.Length > 0)
            {
                walletAddress = solanaWallets[0].Address;
                hasWallet = true;
                isAuthenticated = true;
                AuthenticationData.IsAuthenticated = true;
                AuthenticationData.WalletAddress = walletAddress;
                AuthenticationData.UserEmail = userEmail;
                AuthenticationData.CurrentWalletType = WalletType.Privy; // Email login uses Privy wallet
                Debug.Log($"User has Solana wallet: {walletAddress}");
                ShowWelcomePanel();
            }
            else
            {
                try
                {
                    Debug.Log("No Solana wallet found, creating one...");
                    
                    var currentAuthState = await privyInstance.GetAuthState();
                    if (currentAuthState != AuthState.Authenticated)
                    {
                        Debug.LogError($"Authentication lost before wallet creation. State: {currentAuthState}");
                        ShowAuthPanel();
                        return;
                    }

                    var createWalletTask = user.CreateSolanaWallet();
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

                    var completedTask = await Task.WhenAny(createWalletTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        Debug.LogError("Solana wallet creation timed out after 30 seconds");
                        ShowAuthPanel();
                        return;
                    }

                    var newWallet = await createWalletTask;
                    if (newWallet == null)
                    {
                        Debug.LogError("Created wallet is null");
                        ShowAuthPanel();
                        return;
                    }

                    walletAddress = newWallet.Address;
                    hasWallet = true;
                    isAuthenticated = true;
                    AuthenticationData.IsAuthenticated = true;
                    AuthenticationData.WalletAddress = walletAddress;
                    AuthenticationData.UserEmail = userEmail;
                    AuthenticationData.CurrentWalletType = WalletType.Privy; // Email login uses Privy wallet
                    Debug.Log($"Solana wallet created successfully: {walletAddress}");
                    ShowWelcomePanel();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create Solana wallet: {e.Message}");
                    Debug.LogError($"Exception type: {e.GetType().Name}");
                    Debug.LogError($"Stack trace: {e.StackTrace}");
                    
                    if (e.Message.Contains("401") || e.Message.Contains("Invalid auth token") || e.Message.Contains("Unauthorized"))
                    {
                        Debug.LogError("=== AUTHENTICATION TOKEN ERROR ===");
                        Debug.LogError("Possible causes:");
                        Debug.LogError("1. Privy App ID or Client ID is incorrect");
                        Debug.LogError("2. Solana wallet creation not enabled in Privy Dashboard");
                        Debug.LogError("3. Authentication token expired - try logging out and logging in again");
                        Debug.LogError("4. Check Privy Dashboard → Settings → Solana configuration");
                    }
                    
                    ShowAuthPanel();
                }
            }
        }

        /// <summary>
        /// Ensures Solana wallet exists after authentication
        /// </summary>
        private async Task EnsureSolanaWalletAfterAuth()
        {
            await Task.Delay(500);
            
            var authState = await privyInstance.GetAuthState();
            if (authState != AuthState.Authenticated)
            {
                Debug.LogError($"User is not authenticated. Auth state: {authState}");
                ShowAuthPanel();
                return;
            }

            var user = await privyInstance.GetUser();
            if (user == null)
            {
                Debug.LogError("User is null - cannot ensure Solana wallet");
                ShowAuthPanel();
                return;
            }

            Debug.Log($"User authenticated. User ID: {user.Id}");

            var solanaWallets = user.EmbeddedSolanaWallets;
            if (solanaWallets == null || solanaWallets.Length == 0)
            {
                try
                {
                    Debug.Log("Creating Solana wallet for authenticated user...");
                    
                    var currentAuthState = await privyInstance.GetAuthState();
                    if (currentAuthState != AuthState.Authenticated)
                    {
                        Debug.LogError($"Authentication lost before wallet creation. State: {currentAuthState}");
                        ShowAuthPanel();
                        return;
                    }

                    var newWallet = await user.CreateSolanaWallet();
                    if (newWallet == null)
                    {
                        Debug.LogError("Created wallet is null");
                        ShowAuthPanel();
                        return;
                    }

                    walletAddress = newWallet.Address;
                    hasWallet = true;
                    AuthenticationData.WalletAddress = walletAddress;
                    Debug.Log($"Solana wallet created successfully: {walletAddress}");
                    ShowWelcomePanel();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create Solana wallet: {e.Message}");
                    Debug.LogError($"Exception type: {e.GetType().Name}");
                    Debug.LogError($"Stack trace: {e.StackTrace}");
                    
                    // Check if it's an auth token error
                    if (e.Message.Contains("401") || e.Message.Contains("Invalid auth token") || e.Message.Contains("Unauthorized"))
                    {
                        Debug.LogError("=== AUTHENTICATION TOKEN ERROR ===");
                        Debug.LogError("Possible causes:");
                        Debug.LogError("1. Privy App ID or Client ID is incorrect in PrivyConfig");
                        Debug.LogError("2. Solana wallet creation not enabled in Privy Dashboard");
                        Debug.LogError("3. Authentication token expired - try logging out and logging in again");
                        Debug.LogError("4. Check Privy Dashboard → Settings → Enable Solana wallets");
                        Debug.LogError($"Current App ID: {privyConfig?.appId ?? "NULL"}");
                        Debug.LogError($"Current Client ID: {privyConfig?.clientId ?? "NULL"}");
                    }
                    
                    ShowAuthPanel();
                }
            }
            else
            {
                walletAddress = solanaWallets[0].Address;
                hasWallet = true;
                AuthenticationData.WalletAddress = walletAddress;
                Debug.Log($"User already has Solana wallet: {walletAddress}");
                ShowWelcomePanel();
            }
        }

        public void Logout()
        {
            if (privyInstance == null) return;

            try
            {
                privyInstance.Logout();
                AuthenticationData.Reset();
                HideAllPanels();
                if (loginScreen != null)
                {
                    loginScreen.ShowLoginCanvas();
                }
                else
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Login");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Logout failed: {e.Message}");
            }
        }

        private void OnContinueClicked()
        {
            HideAllPanels();
            UnityEngine.SceneManagement.SceneManager.LoadScene("TokenPicker");
        }

        private async void UpdateWelcomePanel()
        {
            // Check if user is connected via MWA or Privy
            bool isMWA = AuthenticationData.IsMWAWallet;

            if (walletAddressText != null)
            {
                string solanaAddress;
                
                if (isMWA)
                {
                    // For MWA users, get wallet address from AuthenticationData
                    solanaAddress = AuthenticationData.WalletAddress;
                }
                else
                {
                    // For Privy users, get wallet address from Privy SDK
                    solanaAddress = await GetSolanaWalletAddress();
                }

                if (!string.IsNullOrEmpty(solanaAddress) && solanaAddress != "No Wallet")
                {
                    // Use UIStyleHelper for consistent truncation (BDVvR5...hK6ckt format)
                    walletAddressText.text = UIStyleHelper.TruncateWallet(solanaAddress, 6, 6);
                }
                else
                {
                    walletAddressText.text = "Wallet Not Created";
                }
            }

            if (userInfoText != null)
            {
                if (isMWA)
                {
                    // For MWA users, show "MWA Wallet" instead of Privy user ID
                    userInfoText.text = "MWA Wallet";
                }
                else
                {
                    // For Privy users, get user ID from Privy SDK
                    var user = await privyInstance.GetUser();
                    if (user != null && !string.IsNullOrEmpty(user.Id))
                    {
                        // Use UIStyleHelper for consistent truncation (cmhk...zwl0 format)
                        userInfoText.text = UIStyleHelper.TruncateUserId(user.Id, 4, 4);
                    }
                    else
                    {
                        userInfoText.text = "Unknown";
                    }
                }
            }
        }

        private void UpdateUI()
        {
            if (authTitleText != null)
                authTitleText.text = "Welcome to Solracer!";

            if (authDescriptionText != null)
                authDescriptionText.text = "Connect your wallet to start racing!";

            if (welcomeTitleText != null)
                welcomeTitleText.text = "Ready to Race!";
        }

        /// <summary>
        /// Sets up the wallet address text to be clickable for copy-to-clipboard
        /// </summary>
        private void SetupWalletAddressCopyHandler()
        {
            if (walletAddressText == null) return;

            // Add EventTrigger component if not present
            var eventTrigger = walletAddressText.GetComponent<EventTrigger>();
            if (eventTrigger == null)
            {
                eventTrigger = walletAddressText.gameObject.AddComponent<EventTrigger>();
            }

            // Clear existing triggers to avoid duplicates
            eventTrigger.triggers.Clear();

            // Create pointer click entry
            var pointerClickEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            pointerClickEntry.callback.AddListener((data) => { CopyWalletAddressToClipboard(); });
            eventTrigger.triggers.Add(pointerClickEntry);

            // Make sure the text has a raycast target
            if (walletAddressText.raycastTarget == false)
            {
                walletAddressText.raycastTarget = true;
            }

            Debug.Log("[AuthenticationFlowManager] Wallet address copy handler set up");
        }

        /// <summary>
        /// Copies the full wallet address to clipboard and shows feedback
        /// </summary>
        private void CopyWalletAddressToClipboard()
        {
            // Get the full wallet address (not truncated)
            string fullAddress = AuthenticationData.WalletAddress;
            
            if (string.IsNullOrEmpty(fullAddress))
            {
                Debug.LogWarning("[CopyWalletAddress] No wallet address to copy");
                return;
            }

            // Copy to clipboard
            GUIUtility.systemCopyBuffer = fullAddress;
            Debug.Log($"[CopyWalletAddress] Copied wallet address to clipboard: {fullAddress}");

            // Show visual feedback
            StartCoroutine(ShowCopiedFeedback());
        }

        /// <summary>
        /// Shows "Copied!" feedback briefly then restores the wallet address
        /// </summary>
        private IEnumerator ShowCopiedFeedback()
        {
            if (walletAddressText == null) yield break;

            // Store the original text
            string originalText = walletAddressText.text;
            Color originalColor = walletAddressText.color;

            // Show "Copied!" with a green color
            walletAddressText.text = "Copied!";
            walletAddressText.color = new Color32(20, 241, 149, 255); // Sol Green

            // Wait for 1.5 seconds
            yield return new WaitForSeconds(1.5f);

            // Restore original text and color
            walletAddressText.text = originalText;
            walletAddressText.color = originalColor;
        }

        public async Task<IEmbeddedSolanaWallet> CreateSolanaWallet()
        {
            var user = await privyInstance.GetUser();
            if (user == null)
            {
                Debug.LogError("User not authenticated");
                return null;
            }
            try
            {
                var solanaWallet = await user.CreateSolanaWallet();
                return solanaWallet;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create Solana wallet: {e.Message}");
                return null;
            }
        }

        public async Task<IEmbeddedSolanaWallet[]> GetSolanaWallets()
        {
            var user = await privyInstance.GetUser();
            if (user == null)
            {
                Debug.LogError("User is null in GetSolanaWallets");
                return new IEmbeddedSolanaWallet[0];
            }
            return user.EmbeddedSolanaWallets;
        }

        public async Task<bool> EnsureSolanaWallet()
        {
            var solanaWallets = await GetSolanaWallets();
            if (solanaWallets.Length == 0)
            {
                var newWallet = await CreateSolanaWallet();
                return newWallet != null;
            }
            return true;
        }

        public async Task<string> GetSolanaWalletAddress()
        {
            var wallets = await GetSolanaWallets();
            return wallets.Length > 0 ? wallets[0].Address : "No Wallet";
        }

        /// <summary>
        /// Sign a Solana transaction using the active wallet (MWA or Privy).
        /// Routes to the appropriate signing method based on wallet type.
        /// </summary>
        public async Task<string> SignTransaction(string transactionBase64)
        {
            try
            {
                if (!isAuthenticated)
                {
                    Debug.LogError("[SignTransaction] User is not authenticated. Cannot sign transaction.");
                    return null;
                }

                if (string.IsNullOrEmpty(transactionBase64))
                {
                    Debug.LogError("[SignTransaction] Transaction bytes are empty.");
                    return null;
                }

                // Route to the appropriate wallet based on type
                WalletType currentWalletType = AuthenticationData.CurrentWalletType;
                Debug.Log($"[SignTransaction] Using wallet type: {currentWalletType}");

                if (currentWalletType == WalletType.MWA)
                {
                    // Use MWA wallet for signing
                    return await SignTransactionWithMWA(transactionBase64);
                }
                else
                {
                    // Use Privy wallet for signing (default)
                    return await SignTransactionWithPrivy(transactionBase64);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignTransaction] Failed to sign transaction: {ex.Message}");
                Debug.LogError($"[SignTransaction] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Sign a transaction using MWA (Mobile Wallet Adapter) - Mock MWA Wallet / Seeker.
        /// Uses Solana Unity SDK's built-in MWA support which triggers the wallet's bottom sheet UI.
        /// </summary>
        private async Task<string> SignTransactionWithMWA(string transactionBase64)
        {
            try
            {
                Debug.Log("[SignTransactionWithMWA] Starting MWA transaction signing...");
                Debug.Log("[SignTransactionWithMWA] The wallet app will show a bottom sheet for approval...");

                var mwaAdapter = MWAWalletAdapter.Instance;
                
                if (!mwaAdapter.IsConnected)
                {
                    Debug.LogError("[SignTransactionWithMWA] MWA wallet is not connected");
                    return null;
                }

                // MWA adapter handles the full signing flow using Solana Unity SDK
                // This will trigger the MWA wallet's bottom sheet UI for user approval
                string signedTransaction = await mwaAdapter.SignTransactionAsync(transactionBase64);

                if (string.IsNullOrEmpty(signedTransaction))
                {
                    Debug.LogError("[SignTransactionWithMWA] MWA signing failed - user may have rejected or wallet error occurred");
                    return null;
                }

                Debug.Log("[SignTransactionWithMWA] Transaction signed successfully with MWA wallet!");
                return signedTransaction;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignTransactionWithMWA] Failed: {ex.Message}");
                Debug.LogError($"[SignTransactionWithMWA] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Sign a transaction using Privy embedded wallet (for email login users)
        /// </summary>
        private async Task<string> SignTransactionWithPrivy(string transactionBase64)
        {
            try
            {
                Debug.Log("[SignTransactionWithPrivy] Starting Privy transaction signing...");

                var wallets = await GetSolanaWallets();
                if (wallets == null || wallets.Length == 0)
                {
                    Debug.LogError("[SignTransactionWithPrivy] No Solana wallets found.");
                    return null;
                }

                var wallet = wallets[0];

                if (wallet.EmbeddedSolanaWalletProvider == null)
                {
                    Debug.LogError("[SignTransactionWithPrivy] EmbeddedSolanaWalletProvider is null on Privy wallet.");
                    return null;
                }

                Debug.Log($"[SignTransactionWithPrivy] Transaction base64 length: {transactionBase64.Length}");

                // Step 1: Decode base64 to bytes
                byte[] transactionBytes = System.Convert.FromBase64String(transactionBase64);
                Debug.Log($"[SignTransactionWithPrivy] Decoded transaction: {transactionBytes.Length} bytes");

                // Step 2: Deserialize transaction using Solana Unity SDK
                Transaction transaction = null;
                try
                {
                    transaction = Transaction.Deserialize(transactionBytes);
                    Debug.Log($"[SignTransactionWithPrivy] Successfully deserialized transaction. Instructions: {transaction.Instructions?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SignTransactionWithPrivy] Error deserializing transaction: {ex.Message}");
                    Debug.LogError($"Stack trace: {ex.StackTrace}");
                    return null;
                }

                if (transaction == null)
                {
                    Debug.LogError("[SignTransactionWithPrivy] Transaction deserialization returned null");
                    return null;
                }

                // Step 3: Get the transaction message to sign
                byte[] messageBytes = null;
                try
                {
                    messageBytes = transaction.CompileMessage();
                    Debug.Log($"[SignTransactionWithPrivy] Compiled message: {messageBytes.Length} bytes");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SignTransactionWithPrivy] Error compiling message: {ex.Message}");
                    return null;
                }

                if (messageBytes == null || messageBytes.Length == 0)
                {
                    Debug.LogError("[SignTransactionWithPrivy] Failed to extract message from transaction");
                    return null;
                }

                // Step 4: Sign the message using Privy
                string messageBase64 = System.Convert.ToBase64String(messageBytes);
                Debug.Log($"[SignTransactionWithPrivy] Signing transaction message ({messageBytes.Length} bytes)...");

                var signTask = wallet.EmbeddedSolanaWalletProvider.SignMessage(messageBase64);
                var completed = await Task.WhenAny(signTask, Task.Delay(TimeSpan.FromSeconds(30)));

                if (completed != signTask)
                {
                    Debug.LogError("[SignTransactionWithPrivy] Privy signing timed out after 30 seconds.");
                    return null;
                }

                string signatureBase64 = await signTask;
                if (string.IsNullOrEmpty(signatureBase64))
                {
                    Debug.LogError("[SignTransactionWithPrivy] Privy signature failed - returned empty signature.");
                    return null;
                }

                byte[] signatureBytes = System.Convert.FromBase64String(signatureBase64);
                Debug.Log($"[SignTransactionWithPrivy] Received signature: {signatureBytes.Length} bytes");

                // Step 5: Add signature to transaction
                try
                {
                    // Get the signer's public key (first account in the transaction)
                    PublicKey signerPubKey = new PublicKey(WalletAddress);
                    
                    // Find and update the signature in the transaction's Signatures list
                    bool signatureAdded = false;
                    if (transaction.Signatures != null && transaction.Signatures.Count > 0)
                    {
                        // Find the signature slot for our public key
                        for (int i = 0; i < transaction.Signatures.Count; i++)
                        {
                            var sigPubKeyPair = transaction.Signatures[i];
                            if (sigPubKeyPair.PublicKey.Equals(signerPubKey))
                            {
                                // Update the signature
                                transaction.Signatures[i] = new SignaturePubKeyPair
                                {
                                    PublicKey = signerPubKey,
                                    Signature = signatureBytes
                                };
                                signatureAdded = true;
                                Debug.Log($"[SignTransactionWithPrivy] Updated signature at index {i}");
                                break;
                            }
                        }
                        
                        // If our public key wasn't found, add it to the first slot
                        if (!signatureAdded)
                        {
                            transaction.Signatures[0] = new SignaturePubKeyPair
                            {
                                PublicKey = signerPubKey,
                                Signature = signatureBytes
                            };
                            signatureAdded = true;
                            Debug.Log("[SignTransactionWithPrivy] Added signature to first slot");
                        }
                    }
                    else
                    {
                        // No signatures list, create one
                        transaction.Signatures = new System.Collections.Generic.List<SignaturePubKeyPair>
                        {
                            new SignaturePubKeyPair
                            {
                                PublicKey = signerPubKey,
                                Signature = signatureBytes
                            }
                        };
                        signatureAdded = true;
                        Debug.Log("[SignTransactionWithPrivy] Created new signatures list with our signature");
                    }

                    if (!signatureAdded)
                    {
                        Debug.LogError("[SignTransactionWithPrivy] Failed to add signature to transaction");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SignTransactionWithPrivy] Error adding signature: {ex.Message}");
                    Debug.LogError($"Stack trace: {ex.StackTrace}");
                    return null;
                }

                // Step 6: Serialize the signed transaction
                byte[] signedTransactionBytes = null;
                try
                {
                    signedTransactionBytes = transaction.Serialize();
                    Debug.Log($"[SignTransactionWithPrivy] Serialized signed transaction: {signedTransactionBytes.Length} bytes");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SignTransactionWithPrivy] Error serializing transaction: {ex.Message}");
                    Debug.LogError($"Stack trace: {ex.StackTrace}");
                    return null;
                }

                if (signedTransactionBytes == null || signedTransactionBytes.Length == 0)
                {
                    Debug.LogError("[SignTransactionWithPrivy] Failed to serialize signed transaction");
                    return null;
                }

                // Verify we're not just returning a signature
                if (signedTransactionBytes.Length == 64)
                {
                    Debug.LogError("[SignTransactionWithPrivy] ERROR: Serialized transaction is only 64 bytes - this is just a signature!");
                    Debug.LogError("[SignTransactionWithPrivy] The backend will reject this. Transaction signing failed.");
                    return null;
                }

                // Step 7: Return base64-encoded signed transaction
                string signedTransactionBase64 = System.Convert.ToBase64String(signedTransactionBytes);
                Debug.Log($"[SignTransactionWithPrivy] Transaction signed successfully! Signed transaction: {signedTransactionBytes.Length} bytes (base64: {signedTransactionBase64.Length} chars)");
                return signedTransactionBase64;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SignTransactionWithPrivy] Failed to sign transaction: {ex.Message}");
                Debug.LogError($"[SignTransactionWithPrivy] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        //transaction siging for testing
        public async Task<string> SignMessage(string message)
        {
            try
            {
                if (!isAuthenticated)
                {
                    Debug.LogError("User is not authenticated. Cannot sign message.");
                    return null;
                }

                if (string.IsNullOrEmpty(message))
                {
                    Debug.LogError("Message is empty.");
                    return null;
                }

                var wallets = await GetSolanaWallets();
                if (wallets == null || wallets.Length == 0)
                {
                    Debug.LogError("No Solana wallets found.");
                    return null;
                }

                var wallet = wallets[0]; //Use primary wallet

                if (wallet.EmbeddedSolanaWalletProvider == null)
                {
                    Debug.LogError("EmbeddedSolanaWalletProvider is null on Privy wallet.");
                    return null;
                }

                Debug.Log("Signing message with Privy wallet...");

                // Convert message to base64
                var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
                var messageBase64 = System.Convert.ToBase64String(messageBytes);

                var signTask = wallet.EmbeddedSolanaWalletProvider.SignMessage(messageBase64);
                var completed = await Task.WhenAny(signTask, Task.Delay(TimeSpan.FromSeconds(30)));

                if (completed != signTask)
                {
                    Debug.LogError("Privy message signing timed out after 30 seconds.");
                    return null;
                }

                string signatureString = await signTask;

                if (string.IsNullOrEmpty(signatureString))
                {
                    Debug.LogError("Privy message signature failed - returned empty signature.");
                    return null;
                }

                Debug.Log($"Message signed successfully.");
                return signatureString;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to sign message: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies the Solana Cyberpunk design styles to auth panels (ss2-ss4)
        /// </summary>
        private void ApplyAuthPanelStyles()
        {
            // Load color scheme
            var colorScheme = Resources.Load<SolracerColors>("SolracerColors");
            if (colorScheme == null)
            {
                Debug.LogWarning("AuthenticationFlowManager: SolracerColors not found in Resources!");
                return;
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style Auth Panel (ss2) - Welcome to Solracer!
            if (authTitleText != null)
            {
                UIStyleHelper.SetFont(authTitleText, UIStyleHelper.FontType.Orbitron);
                authTitleText.text = "Welcome to Solracer!";
                authTitleText.color = new Color32(255, 255, 255, 255); // #ffffff - white
                authTitleText.fontStyle = FontStyles.Bold;
                authTitleText.characterSpacing = 6;
                authTitleText.alignment = TextAlignmentOptions.Center;
            }

            if (authDescriptionText != null)
            {
                UIStyleHelper.SetFont(authDescriptionText, UIStyleHelper.FontType.Exo2);
                authDescriptionText.text = "Connect your wallet to start racing!";
                authDescriptionText.color = new Color32(255, 255, 255, 230); // White with slight opacity
                authDescriptionText.fontStyle = FontStyles.Normal;
                authDescriptionText.alignment = TextAlignmentOptions.Center;
            }

            // Style Email Login Panel (ss3)
            if (emailInputField != null)
            {
                UIStyleHelper.StyleInputField(emailInputField, isCodeInput: false);
                var placeholder = emailInputField.placeholder as TextMeshProUGUI;
                if (placeholder != null)
                {
                    placeholder.text = "Enter Email";
                    placeholder.color = new Color32(255, 255, 255, 128); // Semi-transparent white
                }
            }

            // Style OTP Verification Panel (ss4)
            if (otpCodeInputField != null)
            {
                UIStyleHelper.StyleInputField(otpCodeInputField, isCodeInput: true);
                var placeholder = otpCodeInputField.placeholder as TextMeshProUGUI;
                if (placeholder != null)
                {
                    placeholder.text = "Verify OTP";
                    placeholder.color = new Color32(255, 255, 255, 128); // Semi-transparent white
                }
            }
        }
    }
}

