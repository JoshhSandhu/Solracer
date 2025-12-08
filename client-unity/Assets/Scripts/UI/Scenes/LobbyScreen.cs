using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Solracer.Network;
using Solracer.Auth;
using Solracer.Game;
using Toggle.UI;

namespace Solracer.UI
{
    /// <summary>
    /// Lobby screen for competitive mode
    /// </summary>
    public class LobbyScreen : MonoBehaviour
    {
        [Header("Tab System")]
        [SerializeField] private Button createTabButton;
        [SerializeField] private Button joinTabButton;
        [SerializeField] private GameObject createTabPanel;
        [SerializeField] private GameObject joinTabPanel;

        [Space]
        [Header("Global Mode (PvP)")]
        [SerializeField] private ToggleSwitch pvpToggle; // On = Public mode, Off = Private mode

        [Space]
        [Header("Create Game UI")]
        [SerializeField] private TMP_Text tokenDisplayText;
        [SerializeField] private TMP_Dropdown entryFeeDropdown;
        [SerializeField] private ToggleSwitch privateToggle;
        [SerializeField] private Button createGameButton;
        [SerializeField] private GameObject waitingPanel;
        [SerializeField] private TMP_Text joinCodeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private Button cancelRaceButton;
        [SerializeField] private TMP_Text waitingStatusText;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyStatusText;

        [Space]
        [Header("Join Game UI - Private")]
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button joinByCodeButton;

        [Space]
        [Header("Join Game UI - Public")]
        [SerializeField] private ToggleSwitch publicJoinToggle;
        [SerializeField] private Transform publicRacesListParent;
        [SerializeField] private GameObject publicRaceItemPrefab;
        [SerializeField] private Button refreshRacesButton;
        [SerializeField] private TMP_Dropdown filterTokenDropdown;
        [SerializeField] private TMP_Dropdown filterEntryFeeDropdown;

        [Space]
        [Header("Settings")]
        [SerializeField] private float statusPollInterval = 2f;
        [SerializeField] private bool debugLogging = true;

        private RaceAPIClient raceClient;
        private AuthenticationFlowManager authManager;
        private string currentRaceId;
        private bool isPlayer1;
        private bool isPolling = false;
        private Coroutine statusPollCoroutine;
        private Coroutine publicRacesRefreshCoroutine;
        private List<PublicRaceListItem> currentPublicRaces = new List<PublicRaceListItem>();

        private void Start()
        {
            Initialize();
            SetupUI();
            LoadTokenInfo();
        }

        private void OnDestroy()
        {
            StopPolling();
        }

        private void Initialize()
        {
            raceClient = RaceAPIClient.Instance;
            authManager = AuthenticationFlowManager.Instance;

            if (authManager == null || !authManager.IsAuthenticated)
            {
                Debug.LogError("[LobbyScreen] User not authenticated - returning to mode selection");
                SceneManager.LoadScene("ModeSelection");
                return;
            }
        }

        private void SetupUI()
        {
            // Tab buttons
            if (createTabButton != null)
                createTabButton.onClick.AddListener(() => ShowTab(true));
            if (joinTabButton != null)
                joinTabButton.onClick.AddListener(() => ShowTab(false));

            // Create game
            if (createGameButton != null)
                createGameButton.onClick.AddListener(OnCreateGameClicked);
            if (cancelRaceButton != null)
                cancelRaceButton.onClick.AddListener(OnCancelRaceClicked);
            if (copyCodeButton != null)
                copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
            if (readyButton != null)
                readyButton.onClick.AddListener(OnReadyClicked);

            // Join game
            if (joinByCodeButton != null)
                joinByCodeButton.onClick.AddListener(OnJoinByCodeClicked);
            if (refreshRacesButton != null)
                refreshRacesButton.onClick.AddListener(RefreshPublicRaces);
            if (publicJoinToggle != null)
            {
                publicJoinToggle.OnToggleOn.AddListener(() => OnPublicJoinToggleChanged(true));
                publicJoinToggle.OnToggleOff.AddListener(() => OnPublicJoinToggleChanged(false));
            }

            // Global PvP mode toggle
            if (pvpToggle != null)
            {
                pvpToggle.OnToggleOn.AddListener(() => OnPvpModeChanged(true));   // Public mode
                pvpToggle.OnToggleOff.AddListener(() => OnPvpModeChanged(false)); // Private mode
            }

            // Setup entry fee dropdown
            if (entryFeeDropdown != null)
            {
                entryFeeDropdown.ClearOptions();
                List<string> options = new List<string> { "0.005 SOL", "0.01 SOL", "0.015 SOL", "0.02 SOL" };
                entryFeeDropdown.AddOptions(options);
                entryFeeDropdown.value = 1; // Default to 0.01
            }

            // Show create tab by default
            ShowTab(true);
        }

        private void LoadTokenInfo()
        {
            // Get selected token from previous screen
            // This would come from TokenPickerScreen - for now, use a default
            if (tokenDisplayText != null)
            {
                // TODO: Get actual token from CoinSelectionData or similar
                tokenDisplayText.text = "Token: SOL"; // Placeholder
            }
        }

        private void ShowTab(bool showCreate)
        {
            if (createTabPanel != null)
                createTabPanel.SetActive(showCreate);
            if (joinTabPanel != null)
                joinTabPanel.SetActive(!showCreate);

            if (showCreate)
            {
                // Stop public races refresh when on create tab
                if (publicRacesRefreshCoroutine != null)
                {
                    StopCoroutine(publicRacesRefreshCoroutine);
                    publicRacesRefreshCoroutine = null;
                }
            }
            else
            {
                // Start refreshing public races when on join tab
                RefreshPublicRaces();
                if (publicRacesRefreshCoroutine == null)
                {
                    publicRacesRefreshCoroutine = StartCoroutine(RefreshPublicRacesCoroutine());
                }
            }
        }

        #region Create Game

        private async void OnCreateGameClicked()
        {
            if (raceClient == null || authManager == null)
            {
                Debug.LogError("[LobbyScreen] Race client or auth manager not initialized");
                return;
            }

            bool isPrivate = privateToggle != null && privateToggle.CurrentValue;
            float entryFee = GetSelectedEntryFee();

            // Get token mint from CoinSelectionData or use SOL default
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);
            if (string.IsNullOrEmpty(tokenMint))
            {
                tokenMint = "So11111111111111111111111111111111111111112"; // SOL mint fallback
            }

            if (createGameButton != null)
                createGameButton.interactable = false;

            // LobbyScreen is always competitive mode - create race on-chain with SOL escrow
            // Set GameModeData in case it wasn't set properly
            GameModeData.CurrentMode = GameMode.Competitive;
            
            Debug.Log("[LobbyScreen] Creating race on-chain...");
            string onChainRaceId = await OnChainRaceManager.CreateRaceOnChainAsync(
                tokenMint,
                entryFee,
                (message, progress) =>
                {
                    Debug.Log($"[LobbyScreen] {message} ({progress * 100:F0}%)");
                }
            );

            if (!string.IsNullOrEmpty(onChainRaceId))
            {
                Debug.Log($"[LobbyScreen] Race created on-chain: {onChainRaceId}");
                
                // Race was created on-chain AND in database by /transactions/submit
                // Use the on-chain race_id directly - don't call CreateRaceAsync again!
                currentRaceId = onChainRaceId;
                isPlayer1 = true;
                RaceData.CurrentRaceId = onChainRaceId;
                RaceData.EntryFeeSol = entryFee;
                
                // Get race status to show waiting UI
                var raceStatus = await raceClient.GetRaceStatusAsync(onChainRaceId);
                if (raceStatus != null)
                {
                    var raceResponse = new RaceResponse
                    {
                        race_id = onChainRaceId,
                        token_mint = tokenMint,
                        entry_fee_sol = entryFee,
                        player1_wallet = authManager.WalletAddress,
                        status = raceStatus.status,
                        is_private = isPrivate
                        // Note: join_code not available from status endpoint - will be shown if private race
                    };
                    ShowWaitingUI(raceResponse);
                    StartStatusPolling();
                }
                else
                {
                    Debug.LogWarning("[LobbyScreen] Could not get race status, but race was created");
                    // Create a minimal response for UI
                    var raceResponse = new RaceResponse
                    {
                        race_id = onChainRaceId,
                        token_mint = tokenMint,
                        entry_fee_sol = entryFee,
                        player1_wallet = authManager.WalletAddress,
                        status = "waiting",
                        is_private = isPrivate
                    };
                    ShowWaitingUI(raceResponse);
                    StartStatusPolling();
                }
            }
            else
            {
                Debug.LogWarning("[LobbyScreen] On-chain race creation failed, falling back to database-only mode");
                
                // Fallback: Create race in database only (practice mode behavior)
                var request = new CreateRaceRequest
                {
                    token_mint = tokenMint,
                    wallet_address = authManager.WalletAddress,
                    entry_fee_sol = entryFee,
                    is_private = isPrivate
                };

                var response = await raceClient.CreateRaceAsync(request);

                if (response != null && !string.IsNullOrEmpty(response.race_id))
                {
                    currentRaceId = response.race_id;
                    isPlayer1 = true;
                    RaceData.CurrentRaceId = response.race_id;
                    RaceData.EntryFeeSol = entryFee;

                    ShowWaitingUI(response);
                    StartStatusPolling();
                }
                else
                {
                    Debug.LogError("[LobbyScreen] Failed to create race");
                    if (createGameButton != null)
                        createGameButton.interactable = true;
                }
            }
        }

        private void ShowWaitingUI(RaceResponse race)
        {
            if (createGameButton != null)
                createGameButton.gameObject.SetActive(false);
            if (waitingPanel != null)
                waitingPanel.SetActive(true);

            if (race.is_private && !string.IsNullOrEmpty(race.join_code))
            {
                if (joinCodeText != null)
                {
                    joinCodeText.text = $"Join Code: {race.join_code}";
                    joinCodeText.gameObject.SetActive(true);
                }
                if (copyCodeButton != null)
                    copyCodeButton.gameObject.SetActive(true);
            }
            else
            {
                if (joinCodeText != null)
                    joinCodeText.gameObject.SetActive(false);
                if (copyCodeButton != null)
                    copyCodeButton.gameObject.SetActive(false);
            }

            UpdateWaitingStatus();
        }

        private void UpdateWaitingStatus()
        {
            if (waitingStatusText == null) return;

            if (string.IsNullOrEmpty(currentRaceId))
            {
                waitingStatusText.text = "Waiting for opponent...";
                if (readyButton != null)
                    readyButton.gameObject.SetActive(false);
                if (readyStatusText != null)
                    readyStatusText.gameObject.SetActive(false);
                return;
            }

            // Status will be updated by polling
        }

        private void OnCopyCodeClicked()
        {
            if (joinCodeText != null)
            {
                string code = joinCodeText.text.Replace("Join Code: ", "").Trim();
                GUIUtility.systemCopyBuffer = code;
                Debug.Log($"[LobbyScreen] Copied join code: {code}");
                // TODO: Show toast notification
            }
        }

        private async void OnCancelRaceClicked()
        {
            if (string.IsNullOrEmpty(currentRaceId) || authManager == null)
                return;

            bool cancelled = await raceClient.CancelRaceAsync(currentRaceId, authManager.WalletAddress);
            if (cancelled)
            {
                ResetCreateUI();
                StopPolling();
            }
        }

        private void ResetCreateUI()
        {
            currentRaceId = null;
            isPlayer1 = false;
            RaceData.ClearRaceData();

            if (createGameButton != null)
            {
                createGameButton.gameObject.SetActive(true);
                createGameButton.interactable = true;
            }
            if (waitingPanel != null)
                waitingPanel.SetActive(false);
            if (joinCodeText != null)
                joinCodeText.gameObject.SetActive(false);
            if (copyCodeButton != null)
                copyCodeButton.gameObject.SetActive(false);
        }

        #endregion

        #region Join Game

        /// <summary>
        /// Called when the global PvP slider changes.
        /// Off  = private mode
        /// On   = public mode
        /// This keeps the other toggles and panels in sync.
        /// </summary>
        /// <param name="isPublicMode">True if public mode, false if private mode.</param>
        private void OnPvpModeChanged(bool isPublicMode)
        {
            if (debugLogging)
                Debug.Log($"[LobbyScreen] PvP mode changed. isPublicMode={isPublicMode}");

            // Sync create-game private toggle (null checks)
            if (privateToggle != null)
            {
                bool desiredPrivate = !isPublicMode; // private when PvP is in private mode
                if (privateToggle.CurrentValue != desiredPrivate)
                {
                    // Use ToggleByGroupManager so animation & events still fire
                    privateToggle.ToggleByGroupManager(desiredPrivate);
                }
            }

            // Sync join-game public/private toggle (null checks)
            if (publicJoinToggle != null)
            {
                if (publicJoinToggle.CurrentValue != isPublicMode)
                {
                    publicJoinToggle.ToggleByGroupManager(isPublicMode);
                }
            }
            else
            {
                // Fallback: directly toggle join UI if no publicJoinToggle is wired
                OnPublicJoinToggleChanged(isPublicMode);
            }
        }

        private void OnPublicJoinToggleChanged(bool isPublic)
        {
            // Show/hide appropriate UI
            if (joinCodeInput != null)
                joinCodeInput.gameObject.SetActive(!isPublic);
            if (joinByCodeButton != null)
                joinByCodeButton.gameObject.SetActive(!isPublic);
            if (publicRacesListParent != null)
                publicRacesListParent.gameObject.SetActive(isPublic);
            if (refreshRacesButton != null)
                refreshRacesButton.gameObject.SetActive(isPublic);
        }

        private async void OnJoinByCodeClicked()
        {
            if (raceClient == null || authManager == null || joinCodeInput == null)
                return;

            string code = joinCodeInput.text.Trim().ToUpper();
            if (string.IsNullOrEmpty(code) || code.Length != 6)
            {
                Debug.LogWarning("[LobbyScreen] Invalid join code");
                return;
            }

            if (joinByCodeButton != null)
                joinByCodeButton.interactable = false;

            var response = await raceClient.JoinRaceByCodeAsync(code, authManager.WalletAddress);

            if (response != null && !string.IsNullOrEmpty(response.race_id))
            {
                currentRaceId = response.race_id;
                isPlayer1 = false;
                RaceData.CurrentRaceId = response.race_id;
                RaceData.EntryFeeSol = response.entry_fee_sol;

                // Join race on-chain (competitive mode - this activates the race on-chain)
                GameModeData.CurrentMode = GameMode.Competitive;
                Debug.Log($"[LobbyScreen] Joining race on-chain: {response.race_id}");
                bool joinedOnChain = await OnChainRaceManager.JoinRaceOnChainAsync(
                    response.race_id,
                    (message, progress) =>
                    {
                        Debug.Log($"[LobbyScreen] {message} ({progress * 100:F0}%)");
                    }
                );

                if (!joinedOnChain)
                {
                    Debug.LogWarning("[LobbyScreen] On-chain join failed, but continuing with database join");
                }
                else
                {
                    Debug.Log($"[LobbyScreen] Successfully joined race on-chain!");
                }

                // Switch to create tab to show waiting UI
                ShowTab(true);
                ShowWaitingUI(response);
                StartStatusPolling();
            }
            else
            {
                Debug.LogError("[LobbyScreen] Failed to join race by code");
                if (joinByCodeButton != null)
                    joinByCodeButton.interactable = true;
            }
        }

        private async void RefreshPublicRaces()
        {
            if (raceClient == null)
                return;

            string filterToken = null;
            float? filterEntryFee = null;

            // Get filters from dropdowns
            if (filterTokenDropdown != null && filterTokenDropdown.value > 0)
            {
                // TODO: Map dropdown value to token mint
            }
            if (filterEntryFeeDropdown != null && filterEntryFeeDropdown.value >= 0)
            {
                filterEntryFee = GetEntryFeeFromIndex(filterEntryFeeDropdown.value);
            }

            var races = await raceClient.GetPublicRacesAsync(filterToken, filterEntryFee);
            currentPublicRaces = races;
            UpdatePublicRacesList();
        }

        private void UpdatePublicRacesList()
        {
            if (publicRacesListParent == null || publicRaceItemPrefab == null)
                return;

            // Clear existing items
            foreach (Transform child in publicRacesListParent)
            {
                Destroy(child.gameObject);
            }

            // Create items for each race
            foreach (var race in currentPublicRaces)
            {
                GameObject item = Instantiate(publicRaceItemPrefab, publicRacesListParent);
                SetupPublicRaceItem(item, race);
            }
        }

        private void SetupPublicRaceItem(GameObject item, PublicRaceListItem race)
        {
            // Find text components in the prefab
            var texts = item.GetComponentsInChildren<TMP_Text>();
            foreach (var text in texts)
            {
                if (text.name.Contains("Token"))
                    text.text = race.token_symbol;
                else if (text.name.Contains("EntryFee") || text.name.Contains("Fee"))
                    text.text = $"{race.entry_fee_sol} SOL";
                else if (text.name.Contains("Player") || text.name.Contains("Creator"))
                    text.text = TruncateWallet(race.player1_wallet);
            }

            // Setup join button
            Button joinButton = item.GetComponentInChildren<Button>();
            if (joinButton != null)
            {
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(() => OnJoinPublicRace(race.race_id));
            }
        }

        private async void OnJoinPublicRace(string raceId)
        {
            if (raceClient == null || authManager == null)
                return;

            var response = await raceClient.JoinRaceByIdAsync(raceId, authManager.WalletAddress);

            if (response != null && !string.IsNullOrEmpty(response.race_id))
            {
                currentRaceId = response.race_id;
                isPlayer1 = false;
                RaceData.CurrentRaceId = response.race_id;
                RaceData.EntryFeeSol = response.entry_fee_sol;

                // Join race on-chain (competitive mode - this activates the race on-chain)
                GameModeData.CurrentMode = GameMode.Competitive;
                Debug.Log($"[LobbyScreen] Joining race on-chain: {response.race_id}");
                bool joinedOnChain = await OnChainRaceManager.JoinRaceOnChainAsync(
                    response.race_id,
                    (message, progress) =>
                    {
                        Debug.Log($"[LobbyScreen] {message} ({progress * 100:F0}%)");
                    }
                );

                if (!joinedOnChain)
                {
                    Debug.LogWarning("[LobbyScreen] On-chain join failed, but continuing with database join");
                }
                else
                {
                    Debug.Log($"[LobbyScreen] Successfully joined race on-chain!");
                }

                // Switch to create tab to show waiting UI
                ShowTab(true);
                ShowWaitingUI(response);
                StartStatusPolling();
            }
            else
            {
                Debug.LogError("[LobbyScreen] Failed to join public race");
                // Refresh list to remove invalid race
                RefreshPublicRaces();
            }
        }

        private IEnumerator RefreshPublicRacesCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(3f); // Refresh every 3 seconds
                if (!createTabPanel.activeSelf) // Only refresh if on join tab
                {
                    RefreshPublicRaces();
                }
            }
        }

        #endregion

        #region Status Polling & Ready

        private void StartStatusPolling()
        {
            if (isPolling)
                return;

            isPolling = true;
            statusPollCoroutine = StartCoroutine(PollRaceStatusCoroutine());
        }

        private void StopPolling()
        {
            isPolling = false;
            if (statusPollCoroutine != null)
            {
                StopCoroutine(statusPollCoroutine);
                statusPollCoroutine = null;
            }
        }

        private IEnumerator PollRaceStatusCoroutine()
        {
            while (isPolling && !string.IsNullOrEmpty(currentRaceId))
            {
                PollRaceStatus();
                yield return new WaitForSeconds(statusPollInterval);
            }
        }

        private async void PollRaceStatus()
        {
            if (raceClient == null || string.IsNullOrEmpty(currentRaceId))
                return;

            var status = await raceClient.GetRaceStatusAsync(currentRaceId);

            if (status == null)
            {
                Debug.LogWarning("[LobbyScreen] Failed to get race status");
                return;
            }

            UpdateUIFromStatus(status);

            // Check if both players are ready
            if (status.both_ready && status.status == "active")
            {
                // Both ready - start race countdown
                StopPolling();
                LoadRaceScene();
            }
        }

        private void UpdateUIFromStatus(RaceStatusResponse status)
        {
            if (waitingStatusText == null)
                return;

            if (status.status == "waiting")
            {
                waitingStatusText.text = "Waiting for opponent...";
                if (readyButton != null)
                    readyButton.gameObject.SetActive(false);
                if (readyStatusText != null)
                    readyStatusText.gameObject.SetActive(false);
            }
            else if (status.status == "active")
            {
                if (status.player2_wallet != null)
                {
                    waitingStatusText.text = "Opponent joined!";
                    
                    // Show ready button if not ready yet
                    bool isReady = isPlayer1 ? status.player1_ready : status.player2_ready;
                    if (readyButton != null)
                    {
                        readyButton.gameObject.SetActive(!isReady);
                        readyButton.interactable = !isReady;
                    }

                    // Show ready status
                    if (readyStatusText != null)
                    {
                        readyStatusText.gameObject.SetActive(true);
                        string statusMsg = $"You: {(isPlayer1 ? (status.player1_ready ? "Ready" : "Not Ready") : (status.player2_ready ? "Ready" : "Not Ready"))}\n";
                        statusMsg += $"Opponent: {(isPlayer1 ? (status.player2_ready ? "Ready" : "Not Ready") : (status.player1_ready ? "Ready" : "Not Ready"))}";
                        readyStatusText.text = statusMsg;
                    }
                }
            }
            else if (status.status == "cancelled")
            {
                waitingStatusText.text = "Race was cancelled";
                ResetCreateUI();
                StopPolling();
            }
        }

        private async void OnReadyClicked()
        {
            if (raceClient == null || authManager == null || string.IsNullOrEmpty(currentRaceId))
                return;

            bool success = await raceClient.MarkPlayerReadyAsync(currentRaceId, authManager.WalletAddress);
            if (success)
            {
                // Refresh status immediately
                PollRaceStatus();
            }
        }

        #endregion

        #region Helpers

        private float GetSelectedEntryFee()
        {
            if (entryFeeDropdown == null)
                return 0.01f;

            int index = entryFeeDropdown.value;
            return index switch
            {
                0 => 0.005f,
                1 => 0.01f,
                2 => 0.015f,
                3 => 0.02f,
                _ => 0.01f
            };
        }

        private float? GetEntryFeeFromIndex(int index)
        {
            return index switch
            {
                0 => 0.005f,
                1 => 0.01f,
                2 => 0.015f,
                3 => 0.02f,
                _ => null
            };
        }

        private string TruncateWallet(string wallet)
        {
            if (string.IsNullOrEmpty(wallet) || wallet.Length <= 8)
                return wallet;
            return $"{wallet.Substring(0, 4)}...{wallet.Substring(wallet.Length - 4)}";
        }

        private void LoadRaceScene()
        {
            if (debugLogging)
                Debug.Log("[LobbyScreen] Loading Race scene - both players ready");
            
            SceneManager.LoadScene("Race");
        }

        private Button FindButtonByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            return obj != null ? obj.GetComponent<Button>() : null;
        }

        #endregion
    }
}

