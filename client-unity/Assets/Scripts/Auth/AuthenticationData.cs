namespace Solracer.Auth
{
    /// <summary>
    /// Static class to store auth state across scenes
    /// </summary>
    public static class AuthenticationData
    {
        private static bool isAuthenticated = false;
        private static bool isGuestMode = false;
        private static string walletAddress = "";
        private static string userEmail = "";

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

        //chcking if user has compi mode enabled
        public static bool CanAccessCompetitiveMode => isAuthenticated && !isGuestMode;

        //reset auth data
        public static void Reset()
        {
            isAuthenticated = false;
            isGuestMode = false;
            walletAddress = "";
            userEmail = "";
        }
    }
}

