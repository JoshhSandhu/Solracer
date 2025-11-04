using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using Solracer.Game;

namespace Solracer.UI
{
    /// <summary>
    /// Token Picker screen - coin selection dropdown
    /// </summary>
    public class TokenPickerScreen : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Coin selection dropdown")]
        [SerializeField] private TMP_Dropdown coinDropdown;

        [Tooltip("Button to go back to Mode Selection")]
        [SerializeField] private Button backToModeSelectionButton;

        [Header("Coin Settings")]
        [Tooltip("Coin sprites (BONK, Solana, Zcash) - assign directly or load from Resources")]
        [SerializeField] private Sprite[] coinSpritesArray = new Sprite[3];

        [Tooltip("Path to coin sprites folder (relative to Resources folder) - only used if sprites not assigned")]
        [SerializeField] private string coinSpritesPath = "Scripts/UI/Coins";

        [Header("Settings")]
        [Tooltip("Scene name for Mode Selection")]
        [SerializeField] private string modeSelectionSceneName = "ModeSelection";

        [Tooltip("Scene name for Race")]
        [SerializeField] private string raceSceneName = "Race";

        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;


        private void Start()
        {
            // Auto-find UI elements if not assigned
            AutoFindUIElements();

            // Load coin sprites
            LoadCoinSprites();

            // Setup dropdown
            SetupDropdown();

            // Setup button listener
            SetupButton();
        }

        /// <summary>
        /// Auto-finds UI elements by name if not assigned
        /// </summary>
        private void AutoFindUIElements()
        {
            if (coinDropdown == null)
            {
                GameObject dropdownObj = GameObject.Find("CoinDropdown") ?? GameObject.Find("CoinSelectionDropdown");
                if (dropdownObj != null)
                {
                    coinDropdown = dropdownObj.GetComponent<TMP_Dropdown>();
                }
            }

            if (backToModeSelectionButton == null)
            {
                backToModeSelectionButton = FindButtonByName("BackToModeSelectionButton") ?? 
                                            FindButtonByName("BackButton") ?? 
                                            FindButtonByName("ModeSelectionButton");
            }
        }

        /// <summary>
        /// Loads coin sprites from Resources folder or uses assigned sprites
        /// </summary>
        private void LoadCoinSprites()
        {
            // Check if sprites are already assigned
            bool hasAssignedSprites = coinSpritesArray != null && coinSpritesArray.Length >= 3 && 
                                      coinSpritesArray[0] != null && coinSpritesArray[1] != null && coinSpritesArray[2] != null;

            if (hasAssignedSprites)
            {
                if (debugLogging)
                {
                    Debug.Log("TokenPickerScreen: Using assigned coin sprites");
                }
                return;
            }

            // Load from Resources if not assigned
            string[] coinNames = { "BONK_Coin", "Solana_Coin", "Zcash_Coin" };
            
            for (int i = 0; i < coinNames.Length; i++)
            {
                if (coinSpritesArray == null || i >= coinSpritesArray.Length || coinSpritesArray[i] == null)
                {
                    Sprite sprite = Resources.Load<Sprite>($"{coinSpritesPath}/{coinNames[i]}");
                    if (sprite != null)
                    {
                        if (coinSpritesArray == null || i >= coinSpritesArray.Length)
                        {
                            System.Array.Resize(ref coinSpritesArray, coinNames.Length);
                        }
                        coinSpritesArray[i] = sprite;
                        if (debugLogging)
                        {
                            Debug.Log($"TokenPickerScreen: Loaded sprite for {coinNames[i]} from Resources");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"TokenPickerScreen: Could not load sprite for {coinNames[i]} from Resources/{coinSpritesPath}. Please assign sprites directly in inspector.");
                    }
                }
            }
        }

        /// <summary>
        /// Sets up the coin dropdown with options
        /// </summary>
        private void SetupDropdown()
        {
            if (coinDropdown == null)
            {
                Debug.LogWarning("TokenPickerScreen: Coin dropdown not found!");
                return;
            }

            // Clear existing options
            coinDropdown.ClearOptions();

            // Add coin options
            List<string> options = new List<string>
            {
                CoinSelectionData.GetCoinName(CoinType.BONK),
                CoinSelectionData.GetCoinName(CoinType.Solana),
                CoinSelectionData.GetCoinName(CoinType.Zcash)
            };

            coinDropdown.AddOptions(options);

            // Set default selection (BONK)
            coinDropdown.value = 0;
            OnCoinSelected(0);

            // Add listener for selection changes
            coinDropdown.onValueChanged.AddListener(OnCoinSelected);

            if (debugLogging)
            {
                Debug.Log("TokenPickerScreen: Coin dropdown setup complete");
            }
        }

        /// <summary>
        /// Called when coin selection changes
        /// </summary>
        private void OnCoinSelected(int index)
        {
            if (index < 0 || index >= System.Enum.GetValues(typeof(CoinType)).Length)
            {
                Debug.LogWarning($"TokenPickerScreen: Invalid coin index {index}");
                return;
            }

            // Convert index to CoinType
            CoinType selectedCoin = (CoinType)index;

            // Store selected coin
            CoinSelectionData.SelectedCoin = selectedCoin;

            // Store sprite if available
            if (coinSpritesArray != null && index < coinSpritesArray.Length && coinSpritesArray[index] != null)
            {
                CoinSelectionData.CoinSprite = coinSpritesArray[index];
            }

            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Selected coin: {CoinSelectionData.GetCoinName(selectedCoin)}");
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
        /// Called when Start Race button is clicked (if you add one)
        /// </summary>
        public void OnStartRaceClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Start Race clicked - Loading {raceSceneName}");
            }

            LoadRaceScene();
        }

        /// <summary>
        /// Loads Race scene
        /// </summary>
        private void LoadRaceScene()
        {
            if (string.IsNullOrEmpty(raceSceneName))
            {
                Debug.LogError("TokenPickerScreen: Race scene name is empty!");
                return;
            }

            try
            {
                SceneManager.LoadScene(raceSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Failed to load scene '{raceSceneName}': {e.Message}");
            }
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

