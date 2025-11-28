using UnityEngine;

namespace Solracer.Config
{
    /// <summary>
    /// Centralized API configuration for all API clients
    /// Provides a single source of truth for API base URLs
    /// </summary>
    public static class APIConfig
    {
        // API URLs for different environments
        private const string PRODUCTION_URL = "https://api.solracer.com";
        private const string STAGING_URL = "https://staging-api.solracer.com";
        private const string LOCAL_URL = "https://localhost:8000";
        
        // For local network testing (replace with your computer's IP address)
        // To find your IP: Windows: ipconfig, Mac/Linux: ifconfig or ip addr
        private const string LOCAL_NETWORK_URL = "https://192.168.29.123:8000";
        
        // PlayerPrefs key for runtime API URL override
        private const string API_URL_PREF_KEY = "API_BASE_URL";
        
        /// <summary>
        /// Get the API base URL based on build configuration or runtime settings
        /// </summary>
        public static string GetApiBaseUrl()
        {
            // Priority 1: Check PlayerPrefs (can be set at runtime for testing)
            if (PlayerPrefs.HasKey(API_URL_PREF_KEY))
            {
                string prefUrl = PlayerPrefs.GetString(API_URL_PREF_KEY);
                if (!string.IsNullOrEmpty(prefUrl))
                {
                    Debug.Log($"[APIConfig] Using API URL from PlayerPrefs: {prefUrl}");
                    return prefUrl;
                }
            }

            // Priority 2: Use build defines (set in Build Settings → Player Settings → Scripting Define Symbols)
            #if DEVELOPMENT_BUILD
                Debug.Log("[APIConfig] Development build - using local URL");
                return LOCAL_URL;
            #elif STAGING_BUILD
                Debug.Log("[APIConfig] Staging build - using staging URL");
                return STAGING_URL;
            #elif PRODUCTION_BUILD
                Debug.Log("[APIConfig] Production build - using production URL");
                return PRODUCTION_URL;
            #endif

            // Priority 3: For phone testing with local backend on same network
            // Uncomment the line below and replace IP with your computer's IP address
            #if UNITY_EDITOR
                Debug.Log("[APIConfig] Unity Editor detected - using localhost");
                return LOCAL_URL;
            #else
                // In built game (phone/device), use network IP
                Debug.Log("[APIConfig] Built game detected - using network IP");
                return LOCAL_NETWORK_URL;
            #endif

            // Default: Use local URL for development
            //Debug.LogWarning("[APIConfig] No build define set - using local URL. Set DEVELOPMENT_BUILD, STAGING_BUILD, or PRODUCTION_BUILD in Scripting Define Symbols.");
            //return LOCAL_URL;
        }

        /// <summary>
        /// Set API URL at runtime (for testing or configuration)
        /// </summary>
        public static void SetApiBaseUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[APIConfig] Cannot set empty API URL");
                return;
            }
            
            PlayerPrefs.SetString(API_URL_PREF_KEY, url);
            PlayerPrefs.Save();
            Debug.Log($"[APIConfig] API URL set to: {url}");
        }
        
        /// <summary>
        /// Clear runtime API URL override (revert to build defaults)
        /// </summary>
        public static void ClearApiBaseUrl()
        {
            PlayerPrefs.DeleteKey(API_URL_PREF_KEY);
            PlayerPrefs.Save();
            Debug.Log("[APIConfig] API URL override cleared");
        }
        
        /// <summary>
        /// Get current API URL (for debugging)
        /// </summary>
        public static string GetCurrentApiUrl()
        {
            return GetApiBaseUrl();
        }
    }
}






