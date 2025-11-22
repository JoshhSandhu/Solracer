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
        [Header("UI References")]
        [Tooltip("Practice mode button")]
        [SerializeField] private Button practiceButton;

        [Tooltip("Competitive mode button")]
        [SerializeField] private Button competitiveButton;

        [Header("Authentication Modal")]
        [Tooltip("Transaction signing modal for competitive mode authentication")]
        [SerializeField] private TransactionSigningModal authModal;

        [Header("Scene Names")]
        [Tooltip("Scene to load for Practice mode")]
        [SerializeField] private string practiceSceneName = "Race";

        [Tooltip("Scene to load for Competitive mode (both modes go to Race)")]
        [SerializeField] private string competitiveSceneName = "Race";

        [Header("Settings")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        private void Start()
        {
            AutoFindButtons();
            SetupButtons();
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
                authModal = FindObjectOfType<TransactionSigningModal>();
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
                Debug.Log("ModeSelectionScreen: Competitive mode approved - Loading Race");
            }

            // Set mode to Competitive
            GameModeData.CurrentMode = GameMode.Competitive;
            
            // Load Race scene directly (hardcoded to ensure correct flow)
            LoadScene("Race");
        }


        /// <summary>
        /// Checks authentication status and updates UI accordingly
        /// </summary>
        private void CheckAuthenticationStatus()
        {
            if (competitiveButton != null)
            {
                // Disable competitive button if user is in guest mode
                bool canAccessCompetitive = AuthenticationData.CanAccessCompetitiveMode;
                competitiveButton.interactable = canAccessCompetitive;

                if (debugLogging)
                {
                    Debug.Log($"ModeSelectionScreen: Competitive mode access: {canAccessCompetitive} (Guest: {AuthenticationData.IsGuestMode}, Authenticated: {AuthenticationData.IsAuthenticated})");
                }
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
    }
}

