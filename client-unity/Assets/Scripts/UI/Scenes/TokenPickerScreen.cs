using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;

namespace Solracer.UI
{
    /// <summary>
    /// Token Picker screen
    /// </summary>
    public class TokenPickerScreen : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Coin selection dropdown")]
        [SerializeField] private TMP_Dropdown coinDropdown;

        [Tooltip("Button to go back to Mode Selection")]
        [SerializeField] private Button backToModeSelectionButton;

        [Header("Coin Settings")]
        [Tooltip("Coin sprites (BONK, Solana, Zcash)")]
        [SerializeField] private Sprite[] coinSpritesArray = new Sprite[3];

        [Tooltip("Path to coin sprites folder (relative to Resources folder)")]
        [SerializeField] private string coinSpritesPath = "Scripts/UI/Coins";

        [Header("Settings")]
        [Tooltip("Scene name for Mode Selection")]
        [SerializeField] private string modeSelectionSceneName = "ModeSelection";

        [Tooltip("Scene name for Race")]
        [SerializeField] private string raceSceneName = "Race";

        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = true;

        [Header("On-Chain Race Settings")]
        [Tooltip("Entry fee in SOL")]
        [SerializeField] private float entryFeeSol = 0.01f;

        [Tooltip("Show loading UI during transaction")]
        [SerializeField] private GameObject loadingPanel;

        [Tooltip("Loading text")]
        [SerializeField] private TextMeshProUGUI loadingText;

        private bool isCreatingRace = false;


        private void Start()
        {
            //auto find UI elements if not assigned
            AutoFindUIElements();

            //load coin sprites
            LoadCoinSprites();

            //setup dropdown
            SetupDropdown();

            //setup button listener
            SetupButton();
        }

        /// <summary>
        /// auto find  UI elements by name if not assigned
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
        /// loads coin sprites from Resources folder or uses assigned sprites
        /// </summary>
        private void LoadCoinSprites()
        {
            //check if sprites are already assigned
            bool hasAssignedSprites = coinSpritesArray != null && coinSpritesArray.Length >= 3 && 
                                      coinSpritesArray[0] != null && coinSpritesArray[1] != null 
                                      && coinSpritesArray[2] != null;

            if (hasAssignedSprites)
            {
                if (debugLogging)
                {
                    Debug.Log("TokenPickerScreen: Using assigned coin sprites");
                }
                return;
            }

            //load from Resources if not assigned
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
        /// sets up the coin dropdown with options
        /// </summary>
        private void SetupDropdown()
        {
            if (coinDropdown == null)
            {
                Debug.LogWarning("TokenPickerScreen: Coin dropdown not found!");
                return;
            }
            coinDropdown.ClearOptions();

            List<string> options = new List<string>
            {
                CoinSelectionData.GetCoinName(CoinType.BONK),
                CoinSelectionData.GetCoinName(CoinType.Solana),
                CoinSelectionData.GetCoinName(CoinType.Zcash)
            };

            coinDropdown.AddOptions(options);
            coinDropdown.value = 0;
            OnCoinSelected(0);
            coinDropdown.onValueChanged.AddListener(OnCoinSelected);

            if (debugLogging)
            {
                Debug.Log("TokenPickerScreen: Coin dropdown setup complete");
            }
        }

        /// <summary>
        /// called when coin selection changes
        /// </summary>
        private void OnCoinSelected(int index)
        {
            if (index < 0 || index >= System.Enum.GetValues(typeof(CoinType)).Length)
            {
                Debug.LogWarning($"TokenPickerScreen: Invalid coin index {index}");
                return;
            }

            CoinType selectedCoin = (CoinType)index;
            CoinSelectionData.SelectedCoin = selectedCoin;
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
        /// sets up button click listener
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
        /// called when Back to Mode Selection button is clicked
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
        /// called when Start Race button is clicked (now goes to Mode Selection)
        /// </summary>
        public void OnStartRaceClicked()
        {
            if (debugLogging)
            {
                Debug.Log($"TokenPickerScreen: Continue clicked - Loading {modeSelectionSceneName}");
            }

            // Just load ModeSelection scene - race creation will happen in RaceManager
            LoadModeSelectionScene();
        }

        /// <summary>
        /// creates race on chain and loads race scene.
        /// </summary>
        private async Task CreateRaceAndLoadScene()
        {
            isCreatingRace = true;
            ShowLoadingUI(true);
            UpdateLoadingText("Preparing race...");

            try
            {
                CoinType selectedCoin = CoinSelectionData.SelectedCoin;
                string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);

                if (string.IsNullOrEmpty(tokenMint))
                {
                    Debug.LogError("TokenPickerScreen: Invalid token mint address");
                    UpdateLoadingText("Invalid token selection");
                    ShowLoadingUI(false);
                    isCreatingRace = false;
                    return;
                }

                // Only create race on-chain in competitive mode
                if (GameModeData.IsCompetitive)
                {
                    var authManager = AuthenticationFlowManager.Instance;
                    if (authManager == null || !authManager.IsAuthenticated)
                    {
                        Debug.LogError("TokenPickerScreen: User not authenticated");
                        UpdateLoadingText("Please log in first");
                        ShowLoadingUI(false);
                        isCreatingRace = false;
                        return;
                    }

                    UpdateLoadingText("Creating race on-chain...");
                    string raceId = await OnChainRaceManager.CreateRaceOnChainAsync(
                        tokenMint,
                        entryFeeSol,
                        (message, progress) =>
                        {
                            UpdateLoadingText(message);
                        }
                    );

                    if (string.IsNullOrEmpty(raceId))
                    {
                        Debug.LogError("TokenPickerScreen: Failed to create race on-chain");
                        UpdateLoadingText("Failed to create race. Please try again.");
                        ShowLoadingUI(false);
                        isCreatingRace = false;
                        return;
                    }

                    RaceData.CurrentRaceId = raceId;
                    RaceData.EntryFeeSol = entryFeeSol;

                    if (debugLogging)
                    {
                        Debug.Log($"TokenPickerScreen: Race created successfully! Race ID: {raceId}");
                    }
                }
                else
                {
                    // Practice mode: no on-chain race creation
                    RaceData.ClearRaceData();
                    if (debugLogging)
                    {
                        Debug.Log("TokenPickerScreen: Practice mode - skipping on-chain race creation");
                    }
                }

                UpdateLoadingText("Loading race...");
                await Task.Delay(500);                  //brief delay to show success message
                LoadRaceScene();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Error creating race: {e.Message}");
                UpdateLoadingText($"Error: {e.Message}");
                ShowLoadingUI(false);
                isCreatingRace = false;
            }
        }

        /// <summary>
        /// loads Race scene
        /// </summary>
        private void LoadRaceScene()
        {
            if (string.IsNullOrEmpty(raceSceneName))
            {
                Debug.LogError("TokenPickerScreen: Race scene name is empty!");
                ShowLoadingUI(false);
                isCreatingRace = false;
                return;
            }

            try
            {
                SceneManager.LoadScene(raceSceneName);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"TokenPickerScreen: Failed to load scene '{raceSceneName}': {e.Message}");
                ShowLoadingUI(false);
                isCreatingRace = false;
            }
        }

        /// <summary>
        /// show or hide loading UI.
        /// </summary>
        private void ShowLoadingUI(bool show)
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(show);
            }
        }

        /// <summary>
        /// update loading text.
        /// </summary>
        private void UpdateLoadingText(string text)
        {
            if (loadingText != null)
            {
                loadingText.text = text;
            }
            else if (debugLogging)
            {
                Debug.Log($"[TokenPickerScreen] {text}");
            }
        }

        /// <summary>
        /// loads Mode Selection scene
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

