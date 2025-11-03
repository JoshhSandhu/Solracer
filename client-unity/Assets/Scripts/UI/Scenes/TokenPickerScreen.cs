using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace Solracer.UI
{
    /// <summary>
    /// Token Picker screen - currently shows "Under Construction" message
    /// </summary>
    public class TokenPickerScreen : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Message text (e.g., 'Under Construction')")]
        [SerializeField] private TextMeshProUGUI messageText;

        [Tooltip("Button to go back to Mode Selection")]
        [SerializeField] private Button backToModeSelectionButton;

        [Header("Settings")]
        [Tooltip("Message to display")]
        [SerializeField] private string constructionMessage = "This scene is under construction";

        [Tooltip("Scene name for Mode Selection")]
        [SerializeField] private string modeSelectionSceneName = "ModeSelection";

        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        private void Start()
        {
            // Auto-find UI elements if not assigned
            AutoFindUIElements();

            // Setup message text
            SetupMessageText();

            // Setup button listener
            SetupButton();
        }

        /// <summary>
        /// Auto-finds UI elements by name if not assigned
        /// </summary>
        private void AutoFindUIElements()
        {
            if (messageText == null)
            {
                messageText = FindTextByName("MessageText") ?? FindTextByName("Message");
            }

            if (backToModeSelectionButton == null)
            {
                backToModeSelectionButton = FindButtonByName("BackToModeSelectionButton") ?? 
                                            FindButtonByName("BackButton") ?? 
                                            FindButtonByName("ModeSelectionButton");
            }
        }

        /// <summary>
        /// Sets up message text content
        /// </summary>
        private void SetupMessageText()
        {
            if (messageText != null)
            {
                messageText.text = constructionMessage;
            }
            else
            {
                Debug.LogWarning("TokenPickerScreen: Message text not found!");
            }
        }

        /// <summary>
        /// Sets up button click listener
        /// </summary>
        private void SetupButton()
        {
            if (backToModeSelectionButton != null)
            {
                backToModeSelectionButton.onClick.AddListener(OnBackToModeSelectionClicked);
            }
            else
            {
                Debug.LogWarning("TokenPickerScreen: Back to Mode Selection button not found!");
            }
        }

        /// <summary>
        /// Called when Back to Mode Selection button is clicked
        /// </summary>
        public void OnBackToModeSelectionClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Back to Mode Selection clicked - Loading {modeSelectionSceneName}");
            }

            LoadModeSelectionScene();
        }

        /// <summary>
        /// Loads Mode Selection scene
        /// </summary>
        private void LoadModeSelectionScene()
        {
            if (string.IsNullOrEmpty(modeSelectionSceneName))
            {
                Debug.LogError("TokenPickerScreen: Mode Selection scene name is empty!");
                return;
            }

            try
            {
                SceneManager.LoadScene(modeSelectionSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Failed to load scene '{modeSelectionSceneName}': {e.Message}");
            }
        }

        /// <summary>
        /// Finds TextMeshProUGUI component by GameObject name
        /// </summary>
        private TextMeshProUGUI FindTextByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<TextMeshProUGUI>();
            }
            return null;
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

