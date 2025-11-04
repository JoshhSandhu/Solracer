using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solracer.Auth;

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

        [Header("Scene Names")]
        [Tooltip("Scene to load for Practice mode")]
        [SerializeField] private string practiceSceneName = "Race";

        [Tooltip("Scene to load for Competitive mode (usually TokenPicker)")]
        [SerializeField] private string competitiveSceneName = "TokenPicker";

        [Header("Settings")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        private void Start()
        {
            AutoFindButtons();
            SetupButtons();
            CheckAuthenticationStatus();
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

            if (competitiveButton != null)
            {
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

            // TODO, Set mode to Practice
            LoadScene(practiceSceneName);
        }

        /// <summary>
        /// Called when Competitive button is clicked
        /// </summary>
        public void OnCompetitiveClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"ModeSelectionScreen: Competitive mode selected - Loading {competitiveSceneName}");
            }

            // Check if user is authenticated (competitive mode requires authentication)
            if (!AuthenticationData.CanAccessCompetitiveMode)
            {
                Debug.LogWarning("ModeSelectionScreen: Competitive mode requires authentication. Please login first.");
                // Could redirect to login screen here if needed
                return;
            }

            // TODO, Set mode to Competitive
            LoadScene(competitiveSceneName);
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

