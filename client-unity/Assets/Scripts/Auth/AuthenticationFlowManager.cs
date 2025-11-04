using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using Privy;
using System.Threading.Tasks;
using Solracer.UI;

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

        public async void ConnectWallet()
        {
            if (privyInstance == null) return;

            try
            {
                var currentAuthState = await privyInstance.GetAuthState();
                if (currentAuthState == AuthState.Authenticated)
                {
                    Debug.Log("User already authenticated - checking wallet status...");
                    await CheckWalletStatus();
                    return;
                }

                Debug.Log("Connecting wallet via Google OAuth...");
                
                // Configure redirect URI based on platform
                string redirectUri = isMobileApp
                    ? "http://localhost:3000/auth/callback"
                    : "http://localhost:3000/auth/callback";

                var authState = await privyInstance.OAuth.LoginWithProvider(OAuthProvider.Google, redirectUri);

                if (authState == AuthState.Authenticated)
                {
                    Debug.Log("OAuth login successful");
                    await CheckWalletAfterOAuthLogin();
                }
                else
                {
                    Debug.LogWarning("OAuth login failed or was cancelled");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Wallet connection failed: {e.Message}");
                Debug.LogError("Note: OAuth login may not work properly in Unity Editor. Try building and testing on device/web.");
            }
        }

        /// <summary>
        /// Checks and creates Solana wallet after OAuth login
        /// </summary>
        private async Task CheckWalletAfterOAuthLogin()
        {
            await Task.Delay(500); //wait for aut to complete
            
            // Verify authentication state
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
            if (walletAddressText != null)
            {
                string solanaAddress = await GetSolanaWalletAddress();
                if (!string.IsNullOrEmpty(solanaAddress) && solanaAddress != "No Wallet")
                {
                    string shortAddress = solanaAddress.Length > 12 ?
                        $"{solanaAddress.Substring(0, 6)}...{solanaAddress.Substring(solanaAddress.Length - 6)}" :
                        solanaAddress;
                    walletAddressText.text = $"Solana Wallet: {shortAddress}";
                }
                else
                {
                    walletAddressText.text = "Solana Wallet: Wallet Not Created";
                }
            }

            if (userInfoText != null)
            {
                var user = await privyInstance.GetUser();
                userInfoText.text = $"User ID: {user?.Id ?? "Unknown"}";
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

        //test method ( for transaction signing )
        public async Task<string> SignTransaction(string transactionBase64)
        {
            try
            {
                if (!isAuthenticated)
                {
                    Debug.LogError("User is not authenticated. Cannot sign transaction.");
                    return null;
                }

                if (string.IsNullOrEmpty(transactionBase64))
                {
                    Debug.LogError("Transaction bytes are empty.");
                    return null;
                }

                var wallets = await GetSolanaWallets();
                if (wallets == null || wallets.Length == 0)
                {
                    Debug.LogError("No Solana wallets found.");
                    return null;
                }

                var wallet = wallets[0];

                if (wallet.EmbeddedSolanaWalletProvider == null)
                {
                    Debug.LogError("EmbeddedSolanaWalletProvider is null on Privy wallet.");
                    return null;
                }

                Debug.Log("Signing transaction with Privy wallet...");

                //Sign the transaction bytes received from backend
                var signTask = wallet.EmbeddedSolanaWalletProvider.SignMessage(transactionBase64);
                var completed = await Task.WhenAny(signTask, Task.Delay(TimeSpan.FromSeconds(30)));

                if (completed != signTask)
                {
                    Debug.LogError("Privy signing timed out after 30 seconds.");
                    return null;
                }

                string signatureString = await signTask;

                if (string.IsNullOrEmpty(signatureString))
                {
                    Debug.LogError("Privy signature failed - returned empty signature.");
                    return null;
                }

                Debug.Log($"Transaction signed successfully. Signature length: {signatureString.Length}");
                return signatureString;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to sign transaction: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
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
    }
}

