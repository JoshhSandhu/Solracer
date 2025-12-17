namespace Solracer.Auth
{
    /// <summary>
    /// Wallet type enum to track which wallet is being used
    /// </summary>
    public enum WalletType
    {
        None,
        Privy,  // Privy embedded wallet (email login)
        MWA     // Mobile Wallet Adapter (Seeker/Mock MWA Wallet)
    }

    /// <summary>
    /// Static class to store auth state across scenes
    /// </summary>
    public static class AuthenticationData
    {
        private static bool isAuthenticated = false;
        private static bool isGuestMode = false;
        private static string walletAddress = "";
        private static string userEmail = "";
        private static bool shouldShowWelcomePanel = false;
        private static WalletType walletType = WalletType.None;
        private static string mwaAuthToken = "";

        //check if the user is auth with Privy or not
        public static bool IsAuthenticated
        {
            get => isAuthenticated;
            set => isAuthenticated = value;
        }

        //is the user playing in guest mode or not
        public static bool IsGuestMode
        {
            get => isGuestMode;
            set => isGuestMode = value;
        }

        // Flag to indicate welcome panel should be shown when returning to login scene
        public static bool ShouldShowWelcomePanel
        {
            get => shouldShowWelcomePanel;
            set => shouldShowWelcomePanel = value;
        }

        //the solana wallet address
        public static string WalletAddress
        {
            get => walletAddress;
            set => walletAddress = value ?? "";
        }

        //user email, if through email auth
        public static string UserEmail
        {
            get => userEmail;
            set => userEmail = value ?? "";
        }

        /// <summary>
        /// The type of wallet being used (Privy or MWA)
        /// </summary>
        public static WalletType CurrentWalletType
        {
            get => walletType;
            set => walletType = value;
        }

        /// <summary>
        /// MWA auth token for session management
        /// </summary>
        public static string MWAAuthToken
        {
            get => mwaAuthToken;
            set => mwaAuthToken = value ?? "";
        }

        /// <summary>
        /// Check if using Privy wallet
        /// </summary>
        public static bool IsPrivyWallet => walletType == WalletType.Privy;

        /// <summary>
        /// Check if using MWA wallet
        /// </summary>
        public static bool IsMWAWallet => walletType == WalletType.MWA;

        //chcking if user has compi mode enabled
        public static bool CanAccessCompetitiveMode => isAuthenticated && !isGuestMode;

        //reset auth data
        public static void Reset()
        {
            isAuthenticated = false;
            isGuestMode = false;
            walletAddress = "";
            userEmail = "";
            shouldShowWelcomePanel = false;
            walletType = WalletType.None;
            mwaAuthToken = "";
        }
    }
}

