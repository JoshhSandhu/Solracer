using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json;

namespace Solracer.UI
{
    /// <summary>
    /// Results screen UI
    /// </summary>
    public class ResultsScreen : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Title text")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Track name text")]
        [SerializeField] private TextMeshProUGUI trackNameText;

        [Tooltip("Final time text")]
        [SerializeField] private TextMeshProUGUI finalTimeText;

        [Tooltip("Score text")]
        [SerializeField] private TextMeshProUGUI scoreText;

        [Tooltip("Coins collected text")]
        [SerializeField] private TextMeshProUGUI coinsCollectedText;

        [Space]
        [Header("Payout UI References")]
        [Tooltip("Payout Stutus text")]
        [SerializeField] private TextMeshProUGUI payoutStatusText;

        [Tooltip("Prize amount text (SOL)")]
        [SerializeField] private TextMeshProUGUI prizeAmountText;

        [Tooltip("Token amount text")]
        [SerializeField] private GameObject tokenAmountTextContainer;
        [SerializeField] private TextMeshProUGUI tokenAmountText;

        [Tooltip("Token name text")]
        [SerializeField] private TextMeshProUGUI tokenNameText;

        [Tooltip("Payout Status container")]
        [SerializeField] private GameObject payoutStatusContainer;

        [Tooltip("Loading spinner for payout processing")]
        [SerializeField] private GameObject payoutLoadingSpinner;

        [Tooltip("Error message text for payout processing")]
        [SerializeField] private GameObject payoutErrorContainer;
        [SerializeField] private TextMeshProUGUI payoutErrorText;

        [Tooltip("txn link btns (for viewing on explorer)")]
        [SerializeField] private Button swapTxButton;
        [SerializeField] private Button transferTxButton;
        [SerializeField] private Button fallbackTxButton;

        [Tooltip("Payout status panel background")]
        [SerializeField] private GameObject payoutPanel;

        [Space]
        [Header("Buttons")]
        [Tooltip("Play Again button")]
        [SerializeField] private Button playAgainButton;

        [Tooltip("Go to Mode Selection button")]
        [SerializeField] private Button modeSelectionButton;

        [Tooltip("Swap on Uniswap button (only shown for completed races, not game over)")]
        [SerializeField] private Button swapButton;

        [Space]
        [Header("Opponent Comparison UI")]
        [Tooltip("Opponent comparison container")]
        [SerializeField] private GameObject opponentComparisonContainer;
        
        [Tooltip("Opponent time text")]
        [SerializeField] private TextMeshProUGUI opponentTimeText;
        
        [Tooltip("Opponent coins text")]
        [SerializeField] private TextMeshProUGUI opponentCoinsText;
        
        [Tooltip("Winner indicator text")]
        [SerializeField] private TextMeshProUGUI winnerIndicatorText;

        [Space]
        [Header("Settings")]
        [Tooltip("Title text for game over")]
        [SerializeField] private string gameOverTitle = "Game Over";

        [Tooltip("Title text for race complete (can include player name)")]
        [SerializeField] private string raceCompleteTitle = "Race Complete!";

        [Tooltip("Race status polling interval (seconds)")]
        [SerializeField] private float raceStatusPollInterval = 2.5f;

        // Private feilds
        private PayoutStatusResponse currentPayoutStatus = null;
        private bool isPayoutLoading = false;
        private Coroutine payoutStatusPollingCoroutine = null;
        private Coroutine raceStatusPollingCoroutine = null;
        private RaceStatusResponse currentRaceStatus = null;

        private void Start()
        {
            AutoFindUIElements();
            LoadResultsData();
            SetupButtons();
            LoadPayoutStatus();
            
            // Start race status polling if in competitive mode
            if (GameModeData.IsCompetitive && !string.IsNullOrEmpty(RaceData.CurrentRaceId))
            {
                StartRaceStatusPolling();
            }
        }

        private void OnDestroy()
        {
            StopRaceStatusPolling();
        }

        private void AutoFindUIElements()
        {
            if (titleText == null)
            {
                titleText = FindTextByName("TitleText") ?? FindTextByName("Title");
            }

            if (trackNameText == null)
            {
                trackNameText = FindTextByName("TrackNameText") ?? FindTextByName("TrackName");
            }

            if (finalTimeText == null)
            {
                finalTimeText = FindTextByName("FinalTimeText") ?? FindTextByName("FinalTime");
            }

            if (scoreText == null)
            {
                scoreText = FindTextByName("ScoreText") ?? FindTextByName("Score");
            }

            if (coinsCollectedText == null)
            {
                coinsCollectedText = FindTextByName("CoinsCollectedText") ?? FindTextByName("CoinsCollected");
            }

            if (playAgainButton == null)
            {
                playAgainButton = FindButtonByName("PlayAgainButton") ?? FindButtonByName("PlayAgain");
            }

            if (modeSelectionButton == null)
            {
                modeSelectionButton = FindButtonByName("ModeSelectionButton") ?? FindButtonByName("ModeSelection");
            }

            if (swapButton == null)
            {
                swapButton = FindButtonByName("SwapButton") ?? FindButtonByName("Swap");
            }
        }

        private void LoadResultsData()
        {
            bool isGameOver = GameOverData.IsGameOver;
            string trackName = GameOverData.TrackName;
            float finalTime = GameOverData.FinalTime;
            int score = GameOverData.Score;
            int coinsCollected = GameOverData.CoinsCollected;

            // Set title
            if (titleText != null)
            {
                if (isGameOver)
                {
                    titleText.text = gameOverTitle;
                }
                else
                {
                    titleText.text = raceCompleteTitle;
                }
            }

            if (trackNameText != null)
            {
                trackNameText.text = trackName;
            }

            if (finalTimeText != null)
            {
                finalTimeText.text = FormatTime(finalTime);
            }

            if (scoreText != null)
            {
                scoreText.text = score.ToString();
            }

            if (coinsCollectedText != null)
            {
                coinsCollectedText.text = coinsCollected.ToString();
            }

            if (swapButton != null)
            {
                // Show swap button if not game over AND in competitive mode
                bool shouldShow = !isGameOver && GameModeData.IsCompetitive;
                swapButton.gameObject.SetActive(shouldShow);
                Debug.Log($"[ResultsScreen] Swap button visibility: {shouldShow} (isGameOver: {isGameOver}, isCompetitive: {GameModeData.IsCompetitive})");
            }
        }

        /// <summary>
        /// Formats time as MM:SS.mmm
        /// </summary>
        private string FormatTime(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);
            int milliseconds = Mathf.FloorToInt((time % 1f) * 1000f);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }

        /// <summary>
        /// Sets up button click listeners
        /// </summary>
        private void SetupButtons()
        {
            if (playAgainButton != null)
            {
                playAgainButton.onClick.AddListener(OnPlayAgainClicked);
            }

            if (modeSelectionButton != null)
            {
                modeSelectionButton.onClick.AddListener(OnModeSelectionClicked);
            }

            if (swapButton != null)
            {
                swapButton.onClick.AddListener(OnSwapClicked);
            }
        }

        public void OnPlayAgainClicked()
        {
            SceneManager.LoadScene("Race");
        }

        public void OnModeSelectionClicked()
        {
            SceneManager.LoadScene("ModeSelection");
        }

        public async void OnSwapClicked()
        {
            try
            {
                // Disable button during processing
                if (swapButton != null)
                {
                    swapButton.interactable = false;
                }

                // Only process payout in competitive mode
                if (!GameModeData.IsCompetitive)
                {
                    Debug.LogWarning("[ResultsScreen] Practice mode - payouts are not available.");
                    ShowError("Payouts are only available in competitive mode.");
                    if (swapButton != null)
                    {
                        swapButton.interactable = true;
                    }
                    return;
                }

                // Get race ID
                string raceId = RaceData.CurrentRaceId;
                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogWarning("[ResultsScreen] No race ID found. Race may not have been created on-chain.");
                    ShowError("No race ID found. Cannot process payout.");
                    if (swapButton != null)
                    {
                        swapButton.interactable = true;
                    }
                    return;
                }

                Debug.Log($"[ResultsScreen] Processing payout for race: {raceId}");

                // Check authentication
                var authManager = AuthenticationFlowManager.Instance;
                if (authManager == null || !authManager.IsAuthenticated)
                {
                    Debug.LogError("[ResultsScreen] User not authenticated. Cannot process payout.");
                    ShowError("Please login to claim your prize.");
                    if (swapButton != null)
                    {
                        swapButton.interactable = true;
                    }
                    return;
                }

                // Check if race is settled before processing payout
                var raceClient = RaceAPIClient.Instance;
                if (raceClient != null)
                {
                    var raceStatus = await raceClient.GetRaceStatusAsync(raceId);
                    if (raceStatus != null && !raceStatus.is_settled)
                    {
                        Debug.LogWarning($"[ResultsScreen] Race is not settled yet. Status: {raceStatus.status}");
                        ShowError("Race is not finished yet. Please wait for the opponent to complete the race.");
                        if (swapButton != null)
                        {
                            swapButton.interactable = true;
                        }
                        return;
                    }
                }

                // Step 1: Settle race on-chain (if needed)
                Debug.Log($"[ResultsScreen] Step 1: Checking if on-chain settlement is needed...");
                var payoutClient = PayoutAPIClient.Instance;
                
                try
                {
                    var settleResponse = await payoutClient.GetSettleTransaction(raceId);
                    
                    if (settleResponse != null && !string.IsNullOrEmpty(settleResponse.transaction_bytes))
                    {
                        Debug.Log($"[ResultsScreen] On-chain settlement required. Signing and submitting settle_race transaction...");
                        
                        // Sign and submit the settle_race transaction
                        bool settleSuccess = await SignAndSubmitSettleTransaction(settleResponse);
                        
                        if (!settleSuccess)
                        {
                            Debug.LogWarning("[ResultsScreen] Failed to settle race on-chain, but continuing with payout...");
                            // Don't fail - the on-chain settlement might not be strictly required
                            // or the race might already be settled on-chain
                        }
                        else
                        {
                            Debug.Log("[ResultsScreen] ✅ Race settled on-chain successfully!");
                        }
                    }
                    else
                    {
                        Debug.Log($"[ResultsScreen] On-chain settlement not needed or already settled.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ResultsScreen] Error checking settle transaction: {ex.Message}. Continuing...");
                    // Continue - settlement might not be required
                }

                // Step 2: Process payout (get transaction from backend)
                var payoutResponse = await payoutClient.ProcessPayout(raceId);

                if (payoutResponse == null)
                {
                    Debug.LogError("[ResultsScreen] Failed to process payout. Backend returned null.");
                    // Check if race is settled - if not, show a more helpful message
                    if (raceClient != null)
                    {
                        var raceStatus = await raceClient.GetRaceStatusAsync(raceId);
                        if (raceStatus != null && !raceStatus.is_settled)
                        {
                            ShowError("Race is not finished yet. Please wait for the opponent to complete the race.");
                        }
                        else
                        {
                            ShowError("Failed to process payout. Please try again.");
                        }
                    }
                    else
                    {
                        ShowError("Failed to process payout. Please try again.");
                    }
                    if (swapButton != null)
                    {
                        swapButton.interactable = true;
                    }
                    return;
                }

                Debug.Log($"[ResultsScreen] Payout processed. Method: {payoutResponse.method}, Status: {payoutResponse.status}");

                // Handle based on payout method
                bool success = false;
                if (payoutResponse.method == "claim_prize" || payoutResponse.method == "fallback_sol")
                {
                    // Use existing claim_prize flow
                    Debug.Log("[ResultsScreen] Using claim_prize method");
                    success = await OnChainRaceManager.ClaimPrizeOnChainAsync(
                        raceId,
                        (message, progress) => {
                            Debug.Log($"[ResultsScreen] {message} ({progress * 100:F0}%)");
                        }
                    );
                }
                else if (payoutResponse.method == "jupiter_swap")
                {
                    // Handle Jupiter swap transaction
                    Debug.Log("[ResultsScreen] Using Jupiter swap method");
                    success = await HandleJupiterSwap(payoutResponse, raceId);
                }
                else
                {
                    Debug.LogError($"[ResultsScreen] Unknown payout method: {payoutResponse.method}");
                    ShowError($"Unknown payout method: {payoutResponse.method}");
                }

                if (success)
                {
                    Debug.Log("[ResultsScreen] Payout transaction submitted successfully!");
                    ShowSuccess("Prize claimed successfully! Transaction submitted.");

                    LoadPayoutStatus();
                }
                else
                {
                    Debug.LogError("[ResultsScreen] Failed to submit payout transaction.");
                    ShowError("Failed to submit transaction. Please try again.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error in OnSwapClicked: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
                ShowError($"Error: {ex.Message}");
            }
            finally
            {
                // Re-enable button
                if (swapButton != null)
                {
                    swapButton.interactable = true;
                }
            }
        }

        /// <summary>
        /// Handle Jupiter swap transaction signing and submission
        /// </summary>
        private async Task<bool> HandleJupiterSwap(ProcessPayoutResponse payoutResponse, string raceId)
        {
            try
            {
                if (string.IsNullOrEmpty(payoutResponse.swap_transaction))
                {
                    Debug.LogError("[ResultsScreen] Jupiter swap transaction is empty");
                    return false;
                }

                var authManager = AuthenticationFlowManager.Instance;
                if (authManager == null || !authManager.IsAuthenticated)
                {
                    Debug.LogError("[ResultsScreen] User not authenticated");
                    return false;
                }

                // The swap_transaction field contains the base64-encoded transaction string
                // Backend already extracts it from Jupiter's response
                string transactionBase64 = payoutResponse.swap_transaction;

                if (string.IsNullOrEmpty(transactionBase64))
                {
                    Debug.LogError("[ResultsScreen] Jupiter swap transaction is empty");
                    return false;
                }

                Debug.Log("[ResultsScreen] Signing Jupiter swap transaction...");

                // Show transaction signing modal
                var modal = GetTransactionSigningModal();
                if (modal != null)
                {
                    bool approved = await ShowTransactionSigningModalAsync(
                        "Jupiter Swap Transaction",
                        "Please approve the transaction to swap SOL for tokens.",
                        modal
                    );
                    if (!approved)
                    {
                        Debug.LogWarning("[ResultsScreen] User rejected Jupiter swap transaction");
                        return false;
                    }
                }

                // Sign the transaction
                string signedTransaction = await authManager.SignTransaction(transactionBase64);
                if (string.IsNullOrEmpty(signedTransaction))
                {
                    Debug.LogError("[ResultsScreen] Failed to sign Jupiter swap transaction");
                    if (modal != null)
                    {
                        modal.HideModal();
                    }
                    return false;
                }

                Debug.Log("[ResultsScreen] Submitting Jupiter swap transaction...");

                // Submit transaction to Solana network
                // Note: Jupiter swap transactions are standard Solana transactions
                // We can submit them through our backend's transaction submission endpoint
                var apiClient = TransactionAPIClient.Instance;
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "jupiter_swap", // Special instruction type for Jupiter swaps
                    race_id = raceId
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                if (submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    if (modal != null)
                    {
                        modal.HideModal();
                    }
                    Debug.Log($"[ResultsScreen] Jupiter swap transaction submitted! TX: {submitResponse.transaction_signature}");
                    return true;
                }

                if (modal != null)
                {
                    modal.HideModal();
                }
                Debug.LogError("[ResultsScreen] Failed to submit Jupiter swap transaction");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error handling Jupiter swap: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sign and submit the settle_race transaction
        /// </summary>
        private async Task<bool> SignAndSubmitSettleTransaction(SettleTransactionResponse settleResponse)
        {
            try
            {
                var authManager = AuthenticationFlowManager.Instance;
                if (authManager == null || !authManager.IsAuthenticated)
                {
                    Debug.LogError("[ResultsScreen] User not authenticated. Cannot sign settle transaction.");
                    return false;
                }

                Debug.Log($"[ResultsScreen] Signing settle_race transaction...");

                // Sign the transaction
                string signedTransaction = await authManager.SignTransaction(settleResponse.transaction_bytes);
                if (string.IsNullOrEmpty(signedTransaction))
                {
                    Debug.LogError("[ResultsScreen] Failed to sign settle_race transaction");
                    return false;
                }

                Debug.Log($"[ResultsScreen] Submitting settle_race transaction...");

                // Submit transaction to Solana network
                var apiClient = TransactionAPIClient.Instance;
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "settle_race",
                    race_id = settleResponse.race_id
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                if (submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    Debug.Log($"[ResultsScreen] ✅ settle_race transaction submitted! TX: {submitResponse.transaction_signature}");
                    return true;
                }

                Debug.LogError("[ResultsScreen] Failed to submit settle_race transaction");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error signing/submitting settle transaction: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get transaction signing modal from scene
        /// </summary>
        private TransactionSigningModal GetTransactionSigningModal()
        {
            return UnityEngine.Object.FindAnyObjectByType<TransactionSigningModal>();
        }

        /// <summary>
        /// Show transaction signing modal and wait for user approval
        /// </summary>
        private async Task<bool> ShowTransactionSigningModalAsync(
            string title,
            string description,
            TransactionSigningModal modal)
        {
            if (modal == null)
            {
                Debug.LogWarning("[ResultsScreen] TransactionSigningModal not found, proceeding without UI");
                return true; // Proceed if no modal found
            }

            bool userApproved = false;
            System.Action<bool> callback = (approved) => { userApproved = approved; };

            modal.ShowModal(title, description, callback);

            // Wait for user decision (with timeout)
            float timeout = 60f; // 60 seconds timeout
            float elapsed = 0f;
            while (!userApproved && elapsed < timeout)
            {
                await Task.Yield();
                elapsed += Time.deltaTime;
            }

            if (elapsed >= timeout)
            {
                Debug.LogWarning("[ResultsScreen] Transaction signing modal timed out");
                modal.HideModal();
                return false;
            }

            return userApproved;
        }

        /// <summary>
        /// Show error message to user
        /// </summary>
        private void ShowError(string message)
        {
            Debug.LogError($"[ResultsScreen] Error: {message}");
            ShowPayoutError(message);
        }

        /// <summary>
        /// Show success message to user
        /// </summary>
        private void ShowSuccess(string message)
        {
            Debug.Log($"[ResultsScreen] Success: {message}");
            // Hide any error messages and refresh payout status to show updated state
            HidePayoutError();
            // The payout status will be refreshed automatically after transaction submission
            // which will update the UI with the new status (e.g., "paid", "swapping", etc.)
        }

        /// <summary>
        /// Fetch and display payout status for the current race
        /// </summary>
        private async void LoadPayoutStatus()
        {
            // Only show payout UI in competitive mode
            if (!GameModeData.IsCompetitive)
            {
                // Practice mode - hide payout section
                Debug.Log("[ResultsScreen] Practice mode - hiding payout UI");
                if (payoutStatusContainer != null)
                {
                    payoutStatusContainer.SetActive(false);
                }
                if (swapButton != null)
                {
                    swapButton.gameObject.SetActive(false);
                }
                return;
            }

            string raceId = RaceData.CurrentRaceId;
            if(string.IsNullOrEmpty(raceId))
            {
                // No race ID in competitive mode - this shouldn't happen, but handle gracefully
                Debug.LogWarning("[ResultsScreen] Competitive mode but no race ID found. Race may not have been created on-chain.");
                if(payoutStatusContainer != null)
                {
                    payoutStatusContainer.SetActive(false);
                }
                if (swapButton != null)
                {
                    swapButton.gameObject.SetActive(false);
                }
                return;
            }

            // Competitive mode with race ID - show swap button and fetch payout status
            Debug.Log($"[ResultsScreen] Competitive mode with race ID: {raceId} - showing swap button");

            // Ensure swap button is visible (it might have been hidden by LoadResultsData if game over)
            if (swapButton != null && !GameOverData.IsGameOver)
            {
                swapButton.gameObject.SetActive(true);
            }

            try
            {
                isPayoutLoading = true;
                ShowPayoutLoading(true);

                var payoutClient = PayoutAPIClient.Instance;
                var payoutStatus = await payoutClient.GetPayoutStatus(raceId);

                if(payoutStatus == null)
                {
                    // No payout exists yet (race not settled) - show button but hide status container
                    Debug.Log("[ResultsScreen] No payout status found - race may not be settled yet. Checking race status.");
                    if(payoutStatusContainer != null)
                    {
                        payoutStatusContainer.SetActive(false);
                    }
                    // Check race status to determine if button should be enabled
                    var raceClient = RaceAPIClient.Instance;
                    if (raceClient != null && swapButton != null)
                    {
                        var raceStatus = await raceClient.GetRaceStatusAsync(raceId);
                        if (raceStatus != null && !raceStatus.is_settled)
                        {
                            // Race not settled - disable button and show waiting message
                            swapButton.interactable = false;
                            var buttonText = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                            if (buttonText != null)
                            {
                                buttonText.text = "Waiting for Opponent...";
                            }
                        }
                        else
                        {
                            // Race is settled or status unknown - enable button
                            swapButton.interactable = true;
                            var buttonText = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                            if (buttonText != null)
                            {
                                buttonText.text = "Claim Prize";
                            }
                        }
                    }
                    else if (swapButton != null)
                    {
                        swapButton.interactable = true;
                        var buttonText = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                        if (buttonText != null)
                        {
                            buttonText.text = "Claim Prize";
                        }
                    }
                    // Race status polling will check for settlement
                    return;
                }

                currentPayoutStatus = payoutStatus;
                UpdatePayoutUI(payoutStatus);

                // If the status is still processing, start polling
                if(payoutStatus.swap_status == "pending" || payoutStatus.swap_status == "swapping")
                {
                    StartPayoutStatusPolling();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error loading payout status: {ex.Message}");
                Debug.LogError($"Stack trace: {ex.StackTrace}");
                ShowPayoutError("Failed to load payout status");
                // Still show button even if there's an error
                if (swapButton != null && !GameOverData.IsGameOver)
                {
                    swapButton.gameObject.SetActive(true);
                    swapButton.interactable = true;
                }
            }
            finally
            {
                isPayoutLoading = false;
                ShowPayoutLoading(false);
            }
        }


        /// <summary>
        /// Upadte UI elements based on payout status
        /// </summary>
        private void UpdatePayoutUI(PayoutStatusResponse payout)
        {
            if(payout == null) return;

            // Show payout status container
            if (payoutStatusContainer != null)
            {
                payoutStatusContainer.SetActive(true);
            }

            // Update payout status text
            if (payoutStatusText != null)
            {
                string statusText = GetPayoutStatusText(payout.swap_status);
                payoutStatusText.text = statusText;
            }

            // Update prize amount
            if (prizeAmountText != null)
            {
                prizeAmountText.text = $"{payout.prize_amount_sol:F4} SOL";
            }

            // Update token amount (if not SOL)
            bool isSol = IsSolMint(payout.token_mint);
            if (tokenAmountText != null)
            {
                if (isSol || payout.token_amount == null)
                {
                    tokenAmountTextContainer.gameObject.SetActive(false);
                    tokenAmountText.gameObject.SetActive(false);
                }
                else
                {
                    tokenAmountTextContainer.gameObject.SetActive(true);
                    tokenAmountText.gameObject.SetActive(true);
                    tokenAmountText.text = $"{payout.token_amount:F2} tokens";
                }
            }

            // Update token name
            if (tokenNameText != null)
            {
                if (isSol)
                {
                    tokenNameText.text = "SOL";
                }
                else
                {
                    // You might want to fetch token name/symbol from an API
                    // For now, show shortened mint address
                    string shortMint = payout.token_mint?.Length > 8
                        ? $"{payout.token_mint.Substring(0, 4)}...{payout.token_mint.Substring(payout.token_mint.Length - 4)}"
                        : payout.token_mint ?? "Unknown";
                    tokenNameText.text = shortMint;
                }
            }

            // Update button state and text
            UpdatePayoutButton(payout);

            // Show/hide transaction links
            UpdateTransactionLinks(payout);

            // Show error if any
            if (!string.IsNullOrEmpty(payout.error_message))
            {
                ShowPayoutError(payout.error_message);
            }
            else
            {
                HidePayoutError();
            }
        }

        /// <summary>
        /// Get human-readable payout status text
        /// </summary>
        private string GetPayoutStatusText(string status)
        {
            switch (status?.ToLower())
            {
                case "pending":
                    return "Prize Pending";
                case "swapping":
                    return "Swapping Prize...";
                case "paid":
                    return "Prize Paid ✓";
                case "fallback_sol":
                    return "Paid in SOL (Swap Failed)";
                case "failed":
                    return "Payout Failed";
                default:
                    return "Unknown Status";
            }
        }

        /// <summary>
        /// Check if mint address is SOL
        /// </summary>
        private bool IsSolMint(string mint)
        {
            return mint == "So11111111111111111111111111111111111111112" || string.IsNullOrEmpty(mint);
        }

        /// <summary>
        /// Update payout button based on status
        /// </summary>
        private void UpdatePayoutButton(PayoutStatusResponse payout)
        {
            if (swapButton == null) return;

            switch (payout.swap_status?.ToLower())
            {
                case "pending":
                    swapButton.interactable = true;
                    // Update button text component if you have one
                    var buttonText = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "Claim Prize";
                    }
                    break;

                case "swapping":
                    swapButton.interactable = false;
                    var buttonTextSwapping = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonTextSwapping != null)
                    {
                        buttonTextSwapping.text = "Swapping...";
                    }
                    break;

                case "paid":
                    swapButton.interactable = false;
                    var buttonTextPaid = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonTextPaid != null)
                    {
                        buttonTextPaid.text = "Prize Claimed ✓";
                    }
                    break;

                case "fallback_sol":
                    swapButton.interactable = false;
                    var buttonTextFallback = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonTextFallback != null)
                    {
                        buttonTextFallback.text = "Paid in SOL";
                    }
                    break;

                case "failed":
                    swapButton.interactable = true;
                    var buttonTextFailed = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonTextFailed != null)
                    {
                        buttonTextFailed.text = "Retry Payout";
                    }
                    break;

                default:
                    swapButton.interactable = true;
                    break;
            }
        }

        /// <summary>
        /// Update transaction link buttons
        /// </summary>
        private void UpdateTransactionLinks(PayoutStatusResponse payout)
        {
            // Swap transaction link
            if (swapTxButton != null)
            {
                bool hasSwapTx = !string.IsNullOrEmpty(payout.swap_tx_signature);
                swapTxButton.gameObject.SetActive(hasSwapTx);
                if (hasSwapTx)
                {
                    swapTxButton.onClick.RemoveAllListeners();
                    swapTxButton.onClick.AddListener(() => OpenSolscanLink(payout.swap_tx_signature));
                }
            }

            // Transfer transaction link
            if (transferTxButton != null)
            {
                bool hasTransferTx = !string.IsNullOrEmpty(payout.transfer_tx_signature);
                transferTxButton.gameObject.SetActive(hasTransferTx);
                if (hasTransferTx)
                {
                    transferTxButton.onClick.RemoveAllListeners();
                    transferTxButton.onClick.AddListener(() => OpenSolscanLink(payout.transfer_tx_signature));
                }
            }

            // Fallback transaction link
            if (fallbackTxButton != null)
            {
                bool hasFallbackTx = !string.IsNullOrEmpty(payout.fallback_tx_signature);
                fallbackTxButton.gameObject.SetActive(hasFallbackTx);
                if (hasFallbackTx)
                {
                    fallbackTxButton.onClick.RemoveAllListeners();
                    fallbackTxButton.onClick.AddListener(() => OpenSolscanLink(payout.fallback_tx_signature));
                }
            }
        }

        /// <summary>
        /// Open Solscan link for transaction
        /// </summary>
        private void OpenSolscanLink(string txSignature)
        {
            // Determine network (devnet/mainnet) - you might want to make this configurable
            string network = "devnet"; // or "mainnet"
            string url = $"https://solscan.io/tx/{txSignature}?cluster={network}";
            Application.OpenURL(url);
        }

        /// <summary>
        /// Start polling payout status (for pending/swapping states)
        /// </summary>
        private void StartPayoutStatusPolling()
        {
            if (payoutStatusPollingCoroutine != null)
            {
                StopCoroutine(payoutStatusPollingCoroutine);
            }
            payoutStatusPollingCoroutine = StartCoroutine(PollPayoutStatus());
        }

        /// <summary>
        /// Poll payout status every few seconds
        /// </summary>
        private System.Collections.IEnumerator PollPayoutStatus()
        {
            while (true)
            {
                yield return new WaitForSeconds(3f); // Poll every 3 seconds

                string raceId = RaceData.CurrentRaceId;
                if (string.IsNullOrEmpty(raceId)) break;

                var payoutClient = PayoutAPIClient.Instance;
                var task = payoutClient.GetPayoutStatus(raceId);

                // Wait for async task
                while (!task.IsCompleted)
                {
                    yield return null;
                }

                if (task.Result != null)
                {
                    var newStatus = task.Result;

                    // Update UI if status changed
                    if (currentPayoutStatus == null ||
                        currentPayoutStatus.swap_status != newStatus.swap_status)
                    {
                        currentPayoutStatus = newStatus;
                        UpdatePayoutUI(newStatus);
                    }

                    // Stop polling if status is final
                    if (newStatus.swap_status == "paid" ||
                        newStatus.swap_status == "fallback_sol" ||
                        newStatus.swap_status == "failed")
                    {
                        break;
                    }
                }
            }

            payoutStatusPollingCoroutine = null;
        }

        #region Helper methods for UI State
        /// <summary>
        /// Show/hide loading indicator
        /// </summary>
        private void ShowPayoutLoading(bool show)
        {
            if (payoutLoadingSpinner != null)
            {
                payoutLoadingSpinner.SetActive(show);
            }
        }

        /// <summary>
        /// Show error message
        /// </summary>
        private void ShowPayoutError(string message)
        {
            if (payoutErrorText != null)
            {
                payoutErrorText.text = message;
                payoutErrorContainer.gameObject.SetActive(true);
                payoutErrorText.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Hide error message
        /// </summary>
        private void HidePayoutError()
        {
            if (payoutErrorText != null)
            {
                payoutErrorContainer.gameObject.SetActive(false);
                payoutErrorText.gameObject.SetActive(false);
            }
        }
        #endregion

        #region Race Status Polling & Opponent Comparison

        /// <summary>
        /// Start polling race status to check when opponent finishes and race is settled
        /// </summary>
        private void StartRaceStatusPolling()
        {
            if (raceStatusPollingCoroutine != null)
            {
                StopCoroutine(raceStatusPollingCoroutine);
            }
            raceStatusPollingCoroutine = StartCoroutine(PollRaceStatus());
        }

        /// <summary>
        /// Stop race status polling
        /// </summary>
        private void StopRaceStatusPolling()
        {
            if (raceStatusPollingCoroutine != null)
            {
                StopCoroutine(raceStatusPollingCoroutine);
                raceStatusPollingCoroutine = null;
            }
        }

        /// <summary>
        /// Poll race status every few seconds to check for settlement and opponent results
        /// </summary>
        private System.Collections.IEnumerator PollRaceStatus()
        {
            string raceId = RaceData.CurrentRaceId;
            if (string.IsNullOrEmpty(raceId))
            {
                yield break;
            }

            var raceClient = RaceAPIClient.Instance;
            if (raceClient == null)
            {
                yield break;
            }

            var authManager = AuthenticationFlowManager.Instance;
            if (authManager == null)
            {
                yield break;
            }

            string myWallet = authManager.WalletAddress;

            while (true)
            {
                yield return new WaitForSeconds(raceStatusPollInterval);

                var task = raceClient.GetRaceStatusAsync(raceId);
                
                // Wait for async task to complete
                while (!task.IsCompleted)
                {
                    yield return null;
                }
                
                // Handle result outside try-catch to avoid yield issues
                RaceStatusResponse status = null;
                bool hasError = false;
                
                try
                {
                    status = task.Result;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ResultsScreen] Error polling race status: {ex.Message}");
                    hasError = true;
                }
                
                if (!hasError && status != null)
                {
                    currentRaceStatus = status;
                    
                        // Update opponent comparison if race is settled
                        if (status.is_settled)
                        {
                            UpdateOpponentComparison(status, myWallet);
                            StopRaceStatusPolling(); // Stop polling once settled
                            
                            // Enable claim button and update text
                            if (swapButton != null)
                            {
                                swapButton.interactable = true;
                                var buttonText = swapButton.GetComponentInChildren<TextMeshProUGUI>();
                                if (buttonText != null)
                                {
                                    buttonText.text = "Claim Prize";
                                }
                            }
                            
                            // Refresh payout status since race is now settled
                            LoadPayoutStatus();
                        }
                    else if (status.player1_result != null || status.player2_result != null)
                    {
                        // Race not settled yet but at least one player finished - show waiting message
                        UpdateOpponentComparison(status, myWallet);
                    }
                }
            }
        }

        /// <summary>
        /// Update opponent comparison UI based on race status
        /// </summary>
        private void UpdateOpponentComparison(RaceStatusResponse status, string myWallet)
        {
            if (status == null)
                return;

            // Determine which player I am
            bool isPlayer1 = status.player1_wallet == myWallet;
            PlayerResult myResult = isPlayer1 ? status.player1_result : status.player2_result;
            PlayerResult opponentResult = isPlayer1 ? status.player2_result : status.player1_result;

            // Hide comparison if opponent hasn't finished yet
            if (opponentResult == null || opponentResult.finish_time_ms == null)
            {
                if (opponentComparisonContainer != null)
                {
                    opponentComparisonContainer.SetActive(false);
                }
                return;
            }

            // Show comparison container
            if (opponentComparisonContainer != null)
            {
                opponentComparisonContainer.SetActive(true);
            }

            // Update opponent time
            if (opponentTimeText != null && opponentResult.finish_time_ms.HasValue)
            {
                float opponentTimeSeconds = opponentResult.finish_time_ms.Value / 1000f;
                opponentTimeText.text = $"Opponent: {FormatTime(opponentTimeSeconds)}";
            }

            // Update opponent coins
            if (opponentCoinsText != null && opponentResult.coins_collected.HasValue)
            {
                opponentCoinsText.text = $"Coins: {opponentResult.coins_collected.Value}";
            }

            // Update winner indicator
            if (winnerIndicatorText != null && status.is_settled && !string.IsNullOrEmpty(status.winner_wallet))
            {
                bool iWon = status.winner_wallet == myWallet;
                if (iWon)
                {
                    winnerIndicatorText.text = "🏆 You Won!";
                    winnerIndicatorText.color = Color.green;
                }
                else
                {
                    winnerIndicatorText.text = "Opponent Won";
                    winnerIndicatorText.color = Color.red;
                }
                winnerIndicatorText.gameObject.SetActive(true);
            }
            else if (winnerIndicatorText != null)
            {
                winnerIndicatorText.gameObject.SetActive(false);
            }

            // Highlight winner's time if race is settled
            if (status.is_settled && !string.IsNullOrEmpty(status.winner_wallet))
            {
                bool iWon = status.winner_wallet == myWallet;
                
                // Highlight player's time if they won
                if (finalTimeText != null)
                {
                    finalTimeText.color = iWon ? Color.green : Color.white;
                }
                
                // Highlight opponent's time if they won
                if (opponentTimeText != null)
                {
                    opponentTimeText.color = !iWon ? Color.green : Color.white;
                }
            }
        }

        #endregion

        private TextMeshProUGUI FindTextByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<TextMeshProUGUI>();
            }
            return null;
        }

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

    /// <summary>
    /// Jupiter swap transaction response model
    /// </summary>
    [Serializable]
    public class JupiterSwapTransaction
    {
        public string swapTransaction;
    }
}

