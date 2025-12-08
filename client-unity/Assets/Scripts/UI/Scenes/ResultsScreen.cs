using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;
using System.Threading.Tasks;
using System;

namespace Solracer.UI
{
    /// <summary>
    /// Results screen UI - Handles race completion, payout display, and prize claiming
    /// </summary>
    public class ResultsScreen : MonoBehaviour
    {
        [Header("Result Card")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI trackNameText;
        [SerializeField] private TextMeshProUGUI finalTimeText;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI coinsText;

        [Header("Navigation Buttons")]
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button modeSelectionButton;

        [Header("Payout Panel")]
        [SerializeField] private GameObject payoutPanel;
        [SerializeField] private TextMeshProUGUI payoutStatusText;
        [SerializeField] private TextMeshProUGUI prizeAmountText;
        [SerializeField] private TextMeshProUGUI tokenNameText;
        [SerializeField] private GameObject payoutErrorContainer;
        [SerializeField] private TextMeshProUGUI payoutErrorText;
        [SerializeField] private GameObject loadingIndicator;

        [Header("Payout Transaction Buttons")]
        [SerializeField] private Button transferTxnButton;  // Claim Prize (SOL) - for winner
        [SerializeField] private Button swapTxnButton;      // Jupiter Swap (non-SOL tokens) - for winner
        [SerializeField] private Button fallbackTxnButton;  // Fallback when others fail - for winner
        [SerializeField] private Button loserModeSelectionButton;  // Mode Selection button for loser (in payout panel)

        [Header("Opponent Panel")]
        [SerializeField] private GameObject opponentPanel;
        [SerializeField] private TextMeshProUGUI opponentTimeText;
        [SerializeField] private TextMeshProUGUI opponentCoinsText;
        [SerializeField] private TextMeshProUGUI winnerIndicatorText;

        [Header("Settings")]
        [SerializeField] private string gameOverTitle = "Game Over";
        [SerializeField] private string raceCompleteTitle = "Race Complete!";
        [SerializeField] private float raceStatusPollInterval = 2.5f;
        [SerializeField] private float redirectDelay = 2f;

        // Runtime state
        private PayoutStatusResponse currentPayoutStatus;
        private RaceStatusResponse currentRaceStatus;
        private Coroutine raceStatusPollingCoroutine;
        private Coroutine payoutPollingCoroutine;
        private bool isProcessingTransaction;
        private bool isWinner;
        private bool isSolToken = true;
        private string myWallet;

        #region Unity Lifecycle

        private void Start()
        {
            InitializeScreen();
        }

        private void OnDestroy()
        {
            StopAllPolling();
        }

        #endregion

        #region Initialization

        private void InitializeScreen()
        {
            // Get wallet address
            var authManager = AuthenticationFlowManager.Instance;
            myWallet = authManager?.WalletAddress ?? "";

            // Setup button listeners
            SetupButtonListeners();

            // Load race results
            LoadResultsData();

            // Initialize UI state
            SetInitialUIState();

            // Start appropriate flows based on game mode
            if (GameModeData.IsCompetitive && !string.IsNullOrEmpty(RaceData.CurrentRaceId))
            {
                // Competitive mode - set up UI first
                SetCompetitiveModeUI();
                
                // Start polling and load payout
                StartRaceStatusPolling();
                LoadPayoutStatus();
            }
            else
            {
                // Practice mode - hide competitive UI
                SetPracticeModeUI();
            }
        }

        private void SetupButtonListeners()
        {
            if (playAgainButton != null)
                playAgainButton.onClick.AddListener(OnPlayAgainClicked);

            if (modeSelectionButton != null)
                modeSelectionButton.onClick.AddListener(OnModeSelectionClicked);

            if (transferTxnButton != null)
                transferTxnButton.onClick.AddListener(OnTransferTxnClicked);

            if (swapTxnButton != null)
                swapTxnButton.onClick.AddListener(OnSwapTxnClicked);

            if (fallbackTxnButton != null)
                fallbackTxnButton.onClick.AddListener(OnFallbackTxnClicked);

            // Loser's mode selection button (in payout panel)
            if (loserModeSelectionButton != null)
                loserModeSelectionButton.onClick.AddListener(OnModeSelectionClicked);
        }

        private void SetInitialUIState()
        {
            // Hide all transaction buttons initially
            SetButtonActive(transferTxnButton, false);
            SetButtonActive(swapTxnButton, false);
            SetButtonActive(fallbackTxnButton, false);
            SetButtonActive(loserModeSelectionButton, false);

            // Hide loading and error
            if (loadingIndicator != null) loadingIndicator.SetActive(false);
            if (payoutErrorContainer != null) payoutErrorContainer.SetActive(false);

            // Hide opponent panel until we have data
            if (opponentPanel != null) opponentPanel.SetActive(false);
        }

        private void SetPracticeModeUI()
        {
            // Practice mode - hide payout panel, show only navigation
            if (payoutPanel != null) payoutPanel.SetActive(false);
            if (opponentPanel != null) opponentPanel.SetActive(false);
            
            SetButtonActive(modeSelectionButton, true);
            SetButtonActive(playAgainButton, true);
        }

        private void SetCompetitiveModeUI()
        {
            // Competitive mode - hide play again (can't replay competitive races)
            // Show mode selection but disable until payout is complete
            SetButtonActive(playAgainButton, false);  // No replay in competitive
            SetButtonActive(modeSelectionButton, false);  // Will be enabled after payout
            
            // Show payout panel
            if (payoutPanel != null) payoutPanel.SetActive(true);
            
            // Update status text
            UpdatePayoutStatusText("Waiting for results...");
        }

        #endregion

        #region Results Display

        private void LoadResultsData()
        {
            bool isGameOver = GameOverData.IsGameOver;

            if (titleText != null)
                titleText.text = isGameOver ? gameOverTitle : raceCompleteTitle;

            if (trackNameText != null)
                trackNameText.text = GameOverData.TrackName;

            if (finalTimeText != null)
                finalTimeText.text = FormatTime(GameOverData.FinalTime);

            if (scoreText != null)
                scoreText.text = GameOverData.Score.ToString();

            if (coinsText != null)
                coinsText.text = GameOverData.CoinsCollected.ToString();
        }

        private string FormatTime(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);
            int milliseconds = Mathf.FloorToInt((time % 1f) * 1000f);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }

        #endregion

        #region Button Click Handlers

        public void OnPlayAgainClicked()
        {
            StopAllPolling();
            SceneManager.LoadScene("Race");
        }

        public void OnModeSelectionClicked()
        {
            StopAllPolling();
            SceneManager.LoadScene("ModeSelection");
        }

        /// <summary>
        /// Transfer/Claim Prize button - Direct SOL transfer
        /// </summary>
        public async void OnTransferTxnClicked()
        {
            if (isProcessingTransaction) return;
            await ProcessClaimPrize("claim_prize");
        }

        /// <summary>
        /// Swap Transaction button - Jupiter swap for non-SOL tokens
        /// </summary>
        public async void OnSwapTxnClicked()
        {
            if (isProcessingTransaction) return;
            await ProcessClaimPrize("jupiter_swap");
        }

        /// <summary>
        /// Fallback Transaction button - When other methods fail
        /// </summary>
        public async void OnFallbackTxnClicked()
        {
            if (isProcessingTransaction) return;
            await ProcessClaimPrize("fallback_sol");
        }

        #endregion

        #region Claim Prize Processing

        private async Task ProcessClaimPrize(string method)
        {
            if (isProcessingTransaction) return;

            string raceId = RaceData.CurrentRaceId;
            if (string.IsNullOrEmpty(raceId))
            {
                ShowError("No race ID found");
                return;
            }

            var authManager = AuthenticationFlowManager.Instance;
            if (authManager == null || !authManager.IsAuthenticated)
            {
                ShowError("Please login to claim your prize");
                return;
            }

            try
            {
                isProcessingTransaction = true;
                DisableAllTxnButtons();
                ShowLoading(true);
                HideError();

                Debug.Log($"[ResultsScreen] Processing claim with method: {method}");

                // Step 1: Check if race needs on-chain settlement
                Debug.Log($"[ResultsScreen] Step 1: Checking if on-chain settlement is needed...");
                var payoutClient = PayoutAPIClient.Instance;
                
                try
                {
                    var settleResponse = await payoutClient.GetSettleTransaction(raceId);
                    
                    if (settleResponse != null && !string.IsNullOrEmpty(settleResponse.transaction_bytes))
                    {
                        Debug.Log($"[ResultsScreen] On-chain settlement required. Signing and submitting settle_race transaction...");
                        bool settleSuccess = await SignAndSubmitSettleTransaction(settleResponse);
                        
                        if (!settleSuccess)
                        {
                            Debug.LogWarning("[ResultsScreen] Failed to settle race on-chain, but continuing with payout...");
                            // Continue anyway - maybe it's already settled
                        }
                        else
                        {
                            Debug.Log("[ResultsScreen] ✅ Race settled on-chain successfully!");
                            // Wait a moment for the settlement to propagate
                            await Task.Delay(2000);
                        }
                    }
                    else
                    {
                        Debug.Log($"[ResultsScreen] On-chain settlement not needed or already settled.");
                    }
                }
                catch (Exception ex)
                {
                    // If 400 error, race might already be settled - continue
                    if (ex.Message.Contains("400") || ex.Message.Contains("does not need"))
                    {
                        Debug.Log($"[ResultsScreen] Race already settled on-chain or settlement not needed: {ex.Message}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ResultsScreen] Error checking settle transaction: {ex.Message}. Continuing...");
                    }
                }

                // Step 2: Process the actual claim
                bool success = false;

                if (method == "jupiter_swap")
                {
                    success = await HandleJupiterSwap(raceId);
                }
                else if (method == "fallback_sol")
                {
                    success = await HandleFallbackClaim(raceId);
                }
                else // claim_prize (direct SOL transfer)
                {
                    success = await OnChainRaceManager.ClaimPrizeOnChainAsync(
                        raceId,
                        (msg, progress) => Debug.Log($"[ResultsScreen] {msg} ({progress * 100:F0}%)")
                    );
                }

                ShowLoading(false);

                if (success)
                {
                    Debug.Log("[ResultsScreen] ✅ Prize claimed successfully!");
                    UpdatePayoutStatusText("Prize Claimed! ✓");
                    
                    // Auto-redirect to mode selection after delay
                    await Task.Delay((int)(redirectDelay * 1000));
                    OnModeSelectionClicked();
                }
                else
                {
                    // Transaction failed - enable fallback button
                    ShowError("Transaction failed. Try fallback option.");
                    EnableFallbackButton();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error claiming prize: {ex.Message}");
                ShowError($"Error: {ex.Message}");
                EnableFallbackButton();
            }
            finally
            {
                isProcessingTransaction = false;
                ShowLoading(false);
            }
        }

        private async Task<bool> HandleJupiterSwap(string raceId)
        {
            try
            {
                var payoutClient = PayoutAPIClient.Instance;
                var payoutResponse = await payoutClient.ProcessPayout(raceId);

                if (payoutResponse == null || string.IsNullOrEmpty(payoutResponse.swap_transaction))
                {
                    Debug.LogError("[ResultsScreen] No swap transaction received");
                    return false;
                }

                var authManager = AuthenticationFlowManager.Instance;
                string signedTx = await authManager.SignTransaction(payoutResponse.swap_transaction);
                
                if (string.IsNullOrEmpty(signedTx))
                {
                    Debug.LogError("[ResultsScreen] Failed to sign swap transaction");
                    return false;
                }

                var apiClient = TransactionAPIClient.Instance;
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTx,
                    instruction_type = "jupiter_swap",
                    race_id = raceId
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                return submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Jupiter swap error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> HandleFallbackClaim(string raceId)
        {
            try
            {
                var payoutClient = PayoutAPIClient.Instance;
                var retryResponse = await payoutClient.RetryPayout(raceId);

                if (retryResponse == null)
                {
                    Debug.LogError("[ResultsScreen] Fallback retry failed");
                    return false;
                }

                // If retry provides a transaction, sign and submit it
                if (!string.IsNullOrEmpty(retryResponse.transaction))
                {
                    var authManager = AuthenticationFlowManager.Instance;
                    string signedTx = await authManager.SignTransaction(retryResponse.transaction);
                    
                    if (string.IsNullOrEmpty(signedTx))
                        return false;

                    var apiClient = TransactionAPIClient.Instance;
                    var submitRequest = new SubmitTransactionRequest
                    {
                        signed_transaction_bytes = signedTx,
                        instruction_type = "fallback_sol",
                        race_id = raceId
                    };

                    var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                    return submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature);
                }

                // Otherwise use the standard claim flow
                return await OnChainRaceManager.ClaimPrizeOnChainAsync(
                    raceId,
                    (msg, progress) => Debug.Log($"[ResultsScreen] Fallback: {msg}")
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Fallback claim error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SignAndSubmitSettleTransaction(SettleTransactionResponse settleResponse)
        {
            try
            {
                var authManager = AuthenticationFlowManager.Instance;
                if (authManager == null || !authManager.IsAuthenticated)
                {
                    Debug.LogError("[ResultsScreen] Not authenticated for settle transaction");
                    return false;
                }

                Debug.Log($"[ResultsScreen] Signing settle_race transaction...");
                string signedTx = await authManager.SignTransaction(settleResponse.transaction_bytes);
                
                if (string.IsNullOrEmpty(signedTx))
                {
                    Debug.LogError("[ResultsScreen] Failed to sign settle transaction");
                    return false;
                }

                var apiClient = TransactionAPIClient.Instance;
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTx,
                    instruction_type = "settle_race",
                    race_id = settleResponse.race_id
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                bool success = submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature);
                
                if (success)
                {
                    Debug.Log($"[ResultsScreen] ✅ Settle transaction submitted: {submitResponse.transaction_signature}");
                }
                else
                {
                    Debug.LogError("[ResultsScreen] Failed to submit settle transaction");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error in SignAndSubmitSettleTransaction: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Payout Status

        private async void LoadPayoutStatus()
        {
            string raceId = RaceData.CurrentRaceId;
            if (string.IsNullOrEmpty(raceId)) return;

            try
            {
                ShowLoading(true);

                var payoutClient = PayoutAPIClient.Instance;
                var payoutStatus = await payoutClient.GetPayoutStatus(raceId);

                if (payoutStatus == null)
                {
                    // No payout yet - race might not be settled
                    Debug.Log("[ResultsScreen] No payout status yet - waiting for settlement");
                    UpdatePayoutStatusText("Waiting for opponent...");
                    return;
                }

                currentPayoutStatus = payoutStatus;
                UpdatePayoutUI(payoutStatus);

                // Start polling if still pending
                if (payoutStatus.swap_status == "pending" || payoutStatus.swap_status == "swapping")
                {
                    StartPayoutPolling();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error loading payout: {ex.Message}");
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void UpdatePayoutUI(PayoutStatusResponse payout)
        {
            if (payout == null) return;

            // Show payout panel
            if (payoutPanel != null) payoutPanel.SetActive(true);

            // Update status text
            UpdatePayoutStatusText(GetStatusText(payout.swap_status));

            // Update prize amount
            if (prizeAmountText != null)
                prizeAmountText.text = $"{payout.prize_amount_sol:F4} SOL";

            // Determine if SOL token
            isSolToken = IsSolMint(payout.token_mint);

            // Update token name
            if (tokenNameText != null)
            {
                tokenNameText.text = isSolToken ? "SOL" : GetShortMint(payout.token_mint);
            }

            // Check if current player is winner
            isWinner = !string.IsNullOrEmpty(payout.winner_wallet) && payout.winner_wallet == myWallet;

            Debug.Log($"[ResultsScreen] Payout UI: isWinner={isWinner}, isSolToken={isSolToken}, status={payout.swap_status}");

            // Update button states based on winner/loser and status
            UpdateButtonsForPayoutStatus(payout);
        }

        private void UpdateButtonsForPayoutStatus(PayoutStatusResponse payout)
        {
            // Hide loser button initially
            SetButtonActive(loserModeSelectionButton, false);

            if (isWinner)
            {
                // WINNER flow
                switch (payout.swap_status?.ToLower())
                {
                    case "pending":
                        if (isSolToken)
                        {
                            // SOL token - show Transfer (Claim Prize)
                            SetButtonActive(transferTxnButton, true);
                            SetButtonActive(swapTxnButton, false);
                            SetButtonActive(fallbackTxnButton, false);
                        }
                        else
                        {
                            // Non-SOL token - show Swap button
                            SetButtonActive(transferTxnButton, false);
                            SetButtonActive(swapTxnButton, true);
                            SetButtonActive(fallbackTxnButton, false);
                        }
                        break;

                    case "swapping":
                        // In progress - disable all
                        DisableAllTxnButtons();
                        UpdatePayoutStatusText("Swapping in progress...");
                        break;

                    case "paid":
                    case "fallback_sol":
                        // Already claimed - auto redirect will happen
                        DisableAllTxnButtons();
                        UpdatePayoutStatusText("Prize Claimed! ✓");
                        break;

                    case "failed":
                        // Failed - show fallback
                        EnableFallbackButton();
                        break;

                    default:
                        // Unknown - show transfer as default
                        SetButtonActive(transferTxnButton, true);
                        break;
                }

                // Update winner indicator
                if (winnerIndicatorText != null)
                {
                    winnerIndicatorText.text = "🏆 You Won!";
                    winnerIndicatorText.color = Color.green;
                    winnerIndicatorText.gameObject.SetActive(true);
                }
            }
            else
            {
                // LOSER flow - show loser mode selection button in payout panel
                DisableAllTxnButtons();
                SetButtonActive(loserModeSelectionButton, true);
                SetButtonActive(modeSelectionButton, true);  // Also enable main mode selection for loser
                
                if (winnerIndicatorText != null)
                {
                    winnerIndicatorText.text = "Better Luck Next Time!";
                    winnerIndicatorText.color = Color.red;
                    winnerIndicatorText.gameObject.SetActive(true);
                }
            }
        }

        private string GetStatusText(string status)
        {
            return status?.ToLower() switch
            {
                "pending" => "Prize Ready to Claim",
                "swapping" => "Swapping...",
                "paid" => "Prize Claimed! ✓",
                "fallback_sol" => "Paid in SOL ✓",
                "failed" => "Claim Failed",
                _ => "Processing..."
            };
        }

        private bool IsSolMint(string mint)
        {
            return string.IsNullOrEmpty(mint) || mint == "So11111111111111111111111111111111111111112";
        }

        private string GetShortMint(string mint)
        {
            if (string.IsNullOrEmpty(mint) || mint.Length < 8) return mint ?? "Unknown";
            return $"{mint[..4]}...{mint[^4..]}";
        }

        #endregion

        #region Race Status Polling

        private void StartRaceStatusPolling()
        {
            StopRaceStatusPolling();
            raceStatusPollingCoroutine = StartCoroutine(PollRaceStatus());
        }

        private void StopRaceStatusPolling()
        {
            if (raceStatusPollingCoroutine != null)
            {
                StopCoroutine(raceStatusPollingCoroutine);
                raceStatusPollingCoroutine = null;
            }
        }

        private System.Collections.IEnumerator PollRaceStatus()
        {
            string raceId = RaceData.CurrentRaceId;
            var raceClient = RaceAPIClient.Instance;

            if (string.IsNullOrEmpty(raceId) || raceClient == null)
                yield break;

            while (true)
            {
                yield return new WaitForSeconds(raceStatusPollInterval);

                var task = raceClient.GetRaceStatusAsync(raceId);
                
                while (!task.IsCompleted)
                    yield return null;

                RaceStatusResponse status = null;
                try { status = task.Result; }
                catch (Exception ex) { Debug.LogWarning($"[ResultsScreen] Poll error: {ex.Message}"); }

                if (status != null)
                {
                    currentRaceStatus = status;
                    UpdateOpponentComparison(status);

                    if (status.is_settled)
                    {
                        // Race settled - stop polling and refresh payout
                        StopRaceStatusPolling();
                        LoadPayoutStatus();
                        yield break;
                    }
                }
            }
        }

        private void StartPayoutPolling()
        {
            if (payoutPollingCoroutine != null)
                StopCoroutine(payoutPollingCoroutine);
            payoutPollingCoroutine = StartCoroutine(PollPayoutStatus());
        }

        private System.Collections.IEnumerator PollPayoutStatus()
        {
            string raceId = RaceData.CurrentRaceId;
            var payoutClient = PayoutAPIClient.Instance;

            while (true)
            {
                yield return new WaitForSeconds(3f);

                if (string.IsNullOrEmpty(raceId)) break;

                var task = payoutClient.GetPayoutStatus(raceId);
                while (!task.IsCompleted) yield return null;

                if (task.Result != null)
                {
                    var newStatus = task.Result;
                    
                    if (currentPayoutStatus == null || currentPayoutStatus.swap_status != newStatus.swap_status)
                    {
                        currentPayoutStatus = newStatus;
                        UpdatePayoutUI(newStatus);
                    }

                    // Stop polling if final state
                    if (newStatus.swap_status == "paid" || 
                        newStatus.swap_status == "fallback_sol" || 
                        newStatus.swap_status == "failed")
                    {
                        break;
                    }
                }
            }

            payoutPollingCoroutine = null;
        }

        private void StopAllPolling()
        {
            StopRaceStatusPolling();
            if (payoutPollingCoroutine != null)
            {
                StopCoroutine(payoutPollingCoroutine);
                payoutPollingCoroutine = null;
            }
        }

        #endregion

        #region Opponent Comparison

        private void UpdateOpponentComparison(RaceStatusResponse status)
        {
            if (status == null) return;

            bool isPlayer1 = status.player1_wallet == myWallet;
            var opponentResult = isPlayer1 ? status.player2_result : status.player1_result;

            // Hide if opponent hasn't finished
            if (opponentResult == null || opponentResult.finish_time_ms == null)
            {
                if (opponentPanel != null) opponentPanel.SetActive(false);
                return;
            }

            // Show opponent panel
            if (opponentPanel != null) opponentPanel.SetActive(true);

            // Update opponent time
            if (opponentTimeText != null && opponentResult.finish_time_ms.HasValue)
            {
                float opponentTime = opponentResult.finish_time_ms.Value / 1000f;
                opponentTimeText.text = $"Opponent: {FormatTime(opponentTime)}";
            }

            // Update opponent coins
            if (opponentCoinsText != null && opponentResult.coins_collected.HasValue)
            {
                opponentCoinsText.text = $"Coins: {opponentResult.coins_collected.Value}";
            }

            // Update winner indicator
            if (status.is_settled && !string.IsNullOrEmpty(status.winner_wallet))
            {
                bool iWon = status.winner_wallet == myWallet;
                isWinner = iWon;

                if (winnerIndicatorText != null)
                {
                    winnerIndicatorText.text = iWon ? "🏆 You Won!" : "Opponent Won";
                    winnerIndicatorText.color = iWon ? Color.green : Color.red;
                    winnerIndicatorText.gameObject.SetActive(true);
                }

                // Highlight times
                if (finalTimeText != null)
                    finalTimeText.color = iWon ? Color.green : Color.white;
                if (opponentTimeText != null)
                    opponentTimeText.color = !iWon ? Color.green : Color.white;
            }
        }

        #endregion

        #region UI Helpers

        private void SetButtonActive(Button button, bool active)
        {
            if (button != null)
            {
                button.gameObject.SetActive(active);
                button.interactable = active;
            }
        }

        private void DisableAllTxnButtons()
        {
            SetButtonActive(transferTxnButton, false);
            SetButtonActive(swapTxnButton, false);
            SetButtonActive(fallbackTxnButton, false);
        }

        private void EnableFallbackButton()
        {
            SetButtonActive(transferTxnButton, false);
            SetButtonActive(swapTxnButton, false);
            SetButtonActive(fallbackTxnButton, true);
        }

        private void ShowLoading(bool show)
        {
            if (loadingIndicator != null)
                loadingIndicator.SetActive(show);
        }

        private void ShowError(string message)
        {
            Debug.LogError($"[ResultsScreen] {message}");
            if (payoutErrorText != null)
            {
                payoutErrorText.text = message;
                if (payoutErrorContainer != null)
                    payoutErrorContainer.SetActive(true);
            }
        }

        private void HideError()
        {
            if (payoutErrorContainer != null)
                payoutErrorContainer.SetActive(false);
        }

        private void UpdatePayoutStatusText(string text)
        {
            if (payoutStatusText != null)
                payoutStatusText.text = text;
        }

        #endregion
    }
}
