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
        [Header("Result Cards - New Design")]
        [Tooltip("Competitive result card container (shown for competitive mode)")]
        [SerializeField] private GameObject resultCardCompetitive;

        [Tooltip("Practice result card container (shown for practice mode)")]
        [SerializeField] private GameObject resultCardPractice;

        [Header("Title")]
        [Tooltip("Title text (shared or competitive card)")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Title text for practice card (if separate)")]
        [SerializeField] private TextMeshProUGUI practiceTitleText;

        [Header("Stats Grid (2x2)")]
        [Tooltip("Stat card containers (Track, Final Time, Score, Coins)")]
        [SerializeField] private GameObject[] statCards = new GameObject[4];

        [Tooltip("Stat labels (Track, Final Time, Score, Coins)")]
        [SerializeField] private TextMeshProUGUI[] statLabels = new TextMeshProUGUI[4];

        [Tooltip("Stat values (Track Name, Time, Score Value, Coins Value)")]
        [SerializeField] private TextMeshProUGUI[] statValues = new TextMeshProUGUI[4];

        [Header("Competitive Result Section")]
        [Tooltip("CompetitiveResult section container (winner/loser info)")]
        [SerializeField] private GameObject competitiveResultSection;

        [Tooltip("Winner/Loser indicator text (You Won / You Lost)")]
        [SerializeField] private TextMeshProUGUI winnerIndicatorText;

        [Tooltip("Opponent time text")]
        [SerializeField] private TextMeshProUGUI opponentTimeText;

        [Header("Prize Claim Section")]
        [Tooltip("PrizeClaim section container")]
        [SerializeField] private GameObject prizeClaimSection;

        [Tooltip("Prize status text (Prize Ready to Claim / Prize Wagered)")]
        [SerializeField] private TextMeshProUGUI prizeStatusText;

        [Tooltip("Prize amount text (0.0200 SOL)")]
        [SerializeField] private TextMeshProUGUI prizeAmountText;

        [Tooltip("Token name text (SOL)")]
        [SerializeField] private TextMeshProUGUI tokenNameText;

        [Header("Waiting Section")]
        [Tooltip("Waiting section container")]
        [SerializeField] private GameObject waitingSection;

        [Tooltip("Waiting status text (Waiting For Opponent...)")]
        [SerializeField] private TextMeshProUGUI waitingStatusText;

        [Tooltip("Error status container")]
        [SerializeField] private GameObject errorStatusContainer;

        [Tooltip("Error message text")]
        [SerializeField] private TextMeshProUGUI errorText;

        [Header("Practice Card - Specific Elements")]
        [Tooltip("Track name text in practice card (Track Details > Track Name)")]
        [SerializeField] private TextMeshProUGUI practiceTrackNameText;

        [Tooltip("Time text in practice card (Time Details > Time)")]
        [SerializeField] private TextMeshProUGUI practiceTimeText;

        [Tooltip("Score text in practice card (Score > Score)")]
        [SerializeField] private TextMeshProUGUI practiceScoreText;

        [Tooltip("Coins text in practice card (Coins > Score)")]
        [SerializeField] private TextMeshProUGUI practiceCoinsText;

        [Header("Buttons")]
        [Tooltip("Mode Selection button")]
        [SerializeField] private Button modeSelectionButton;

        [Tooltip("Play Again button (practice mode only)")]
        [SerializeField] private Button playAgainButton;

        [Tooltip("Claim Prize button (winner)")]
        [SerializeField] private Button claimPrizeButton;

        [Tooltip("Fallback Txn button (error state)")]
        [SerializeField] private Button fallbackTxnButton;

        [Header("Settings")]
        [SerializeField] private string gameOverTitle = "Game Over";
        [SerializeField] private string raceCompleteTitle = "Race Complete!";
        [SerializeField] private float raceStatusPollInterval = 2.5f;
        [SerializeField] private float redirectDelay = 2f;

        [Header("Design System")]
        [Tooltip("Reference to SolracerColors asset (optional - will load from Resources if null)")]
        [SerializeField] private SolracerColors colorScheme;

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
            // Apply design system styles first
            ApplyResultsStyles();

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
                
                // Check if we need to submit result (player finished but submit failed)
                if (RaceData.NeedsResultSubmission())
                {
                    Debug.Log("[ResultsScreen] Player finished but result not submitted - attempting retry...");
                    RetryResultSubmission();
                }
                else
                {
                    Debug.Log($"[ResultsScreen] Race state: Finished={RaceData.HasFinishedRace}, Submitted={RaceData.ResultSubmittedOnChain}");
                }
                
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

        /// <summary>
        /// Retry submitting the result if the player finished but submission failed
        /// </summary>
        private async void RetryResultSubmission()
        {
            if (!RaceData.NeedsResultSubmission())
                return;

            ShowWaitingState("Submitting result...");

            try
            {
                string raceId = RaceData.CurrentRaceId;
                float finishTime = RaceData.PlayerFinishTime;
                int coinsCollected = RaceData.PlayerCoinsCollected;
                string inputHash = RaceData.PlayerInputHash;

                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogError("[ResultsScreen] No race ID for retry submission");
                    return;
                }

                int finishTimeMs = Mathf.RoundToInt(finishTime * 1000f);
                
                // Ensure valid input hash
                if (string.IsNullOrEmpty(inputHash) || inputHash.Length != 64)
                {
                    inputHash = new string('0', 64);
                }

                Debug.Log($"[ResultsScreen] Retrying submit: race={raceId}, time={finishTimeMs}ms, coins={coinsCollected}");

                bool success = await OnChainRaceManager.SubmitResultOnChainAsync(
                    raceId,
                    finishTimeMs,
                    coinsCollected,
                    inputHash,
                    (message, progress) =>
                    {
                        Debug.Log($"[ResultsScreen] {message} ({progress * 100:F0}%)");
                        ShowWaitingState(message);
                    }
                );

                RaceData.SetResultSubmitted(success);

                if (success)
                {
                    Debug.Log("[ResultsScreen] ✅ Retry submission successful!");
                    ShowWaitingState("Waiting for opponent...");
                }
                else
                {
                    Debug.LogWarning("[ResultsScreen] ⚠ Retry submission failed");
                    ShowErrorState("Failed to submit result. Please try again.");
                    SetButtonActive(fallbackTxnButton, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Retry submission error: {ex.Message}");
                ShowErrorState($"Error: {ex.Message}");
            }
        }

        private void SetupButtonListeners()
        {
            if (playAgainButton != null)
                playAgainButton.onClick.AddListener(OnPlayAgainClicked);

            if (modeSelectionButton != null)
                modeSelectionButton.onClick.AddListener(OnModeSelectionClicked);

            if (claimPrizeButton != null)
                claimPrizeButton.onClick.AddListener(OnClaimPrizeClicked);

            if (fallbackTxnButton != null)
                fallbackTxnButton.onClick.AddListener(OnFallbackTxnClicked);
        }

        private void SetInitialUIState()
        {
            // Hide all buttons initially
            SetButtonActive(claimPrizeButton, false);
            SetButtonActive(fallbackTxnButton, false);
            SetButtonActive(modeSelectionButton, false);
            SetButtonActive(playAgainButton, false);

            // Hide all sections initially
            if (competitiveResultSection != null) competitiveResultSection.SetActive(false);
            if (prizeClaimSection != null) prizeClaimSection.SetActive(false);
            if (waitingSection != null) waitingSection.SetActive(false);
            if (errorStatusContainer != null) errorStatusContainer.SetActive(false);
        }

        private void SetPracticeModeUI()
        {
            // Practice mode - show practice card, hide competitive card
            if (resultCardPractice != null) resultCardPractice.SetActive(true);
            if (resultCardCompetitive != null) resultCardCompetitive.SetActive(false);
            
            // Hide competitive sections
            if (competitiveResultSection != null) competitiveResultSection.SetActive(false);
            if (prizeClaimSection != null) prizeClaimSection.SetActive(false);
            if (waitingSection != null) waitingSection.SetActive(false);
            
            // Show navigation buttons
            SetButtonActive(modeSelectionButton, true);
            SetButtonActive(playAgainButton, true);
        }

        private void SetCompetitiveModeUI()
        {
            // Competitive mode - show competitive card, hide practice card
            if (resultCardCompetitive != null) resultCardCompetitive.SetActive(true);
            if (resultCardPractice != null) resultCardPractice.SetActive(false);
            
            // Hide play again (can't replay competitive races)
            SetButtonActive(playAgainButton, false);
            
            // Show waiting section initially
            ShowWaitingState("Waiting for opponent...");
        }

        #endregion

        #region Design System Styling

        /// <summary>
        /// Applies the new Solana Cyberpunk design styles to the results screen
        /// </summary>
        private void ApplyResultsStyles()
        {
            // Load color scheme if not assigned
            if (colorScheme == null)
            {
                colorScheme = Resources.Load<SolracerColors>("SolracerColors");
                if (colorScheme == null)
                {
                    Debug.LogWarning("ResultsScreen: SolracerColors not found in Resources! Create it first.");
                    return;
                }
            }

            // Set color scheme in helper
            UIStyleHelper.Colors = colorScheme;

            // Style result cards
            if (resultCardCompetitive != null)
            {
                var image = resultCardCompetitive.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color32(30, 35, 41, 230); // rgba(30, 35, 41, 0.9)
                }
                UIStyleHelper.StyleCard(resultCardCompetitive, useGreenBorder: false);
            }

            if (resultCardPractice != null)
            {
                var image = resultCardPractice.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color32(30, 35, 41, 230); // rgba(30, 35, 41, 0.9)
                }
                UIStyleHelper.StyleCard(resultCardPractice, useGreenBorder: false);
            }

            // Style title (competitive)
            if (titleText != null)
            {
                UIStyleHelper.SetFont(titleText, UIStyleHelper.FontType.Orbitron);
                titleText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                titleText.fontStyle = FontStyles.Bold;
                titleText.characterSpacing = 4;
                titleText.alignment = TextAlignmentOptions.Center;
            }

            // Style practice title (if separate)
            if (practiceTitleText != null)
            {
                UIStyleHelper.SetFont(practiceTitleText, UIStyleHelper.FontType.Orbitron);
                practiceTitleText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                practiceTitleText.fontStyle = FontStyles.Bold;
                practiceTitleText.characterSpacing = 4;
                practiceTitleText.alignment = TextAlignmentOptions.Center;
            }

            // Style practice card specific elements
            if (practiceTrackNameText != null)
            {
                UIStyleHelper.SetFont(practiceTrackNameText, UIStyleHelper.FontType.JetBrainsMono);
                practiceTrackNameText.color = new Color32(153, 69, 255, 255); // #9945FF - purple
                practiceTrackNameText.fontStyle = FontStyles.Bold;
                practiceTrackNameText.alignment = TextAlignmentOptions.Center;
            }

            if (practiceTimeText != null)
            {
                UIStyleHelper.SetFont(practiceTimeText, UIStyleHelper.FontType.JetBrainsMono);
                practiceTimeText.color = new Color32(153, 69, 255, 255); // #9945FF - purple
                practiceTimeText.fontStyle = FontStyles.Bold;
                practiceTimeText.alignment = TextAlignmentOptions.Center;
            }

            if (practiceScoreText != null)
            {
                UIStyleHelper.SetFont(practiceScoreText, UIStyleHelper.FontType.JetBrainsMono);
                practiceScoreText.color = new Color32(20, 241, 149, 255); // #14F195 - green
                practiceScoreText.fontStyle = FontStyles.Bold;
                practiceScoreText.alignment = TextAlignmentOptions.Center;
            }

            if (practiceCoinsText != null)
            {
                UIStyleHelper.SetFont(practiceCoinsText, UIStyleHelper.FontType.JetBrainsMono);
                practiceCoinsText.color = new Color32(20, 241, 149, 255); // #14F195 - green
                practiceCoinsText.fontStyle = FontStyles.Bold;
                practiceCoinsText.alignment = TextAlignmentOptions.Center;
            }

            // Style stat cards (2x2 grid)
            if (statCards != null)
            {
                for (int i = 0; i < statCards.Length && i < 4; i++)
                {
                    if (statCards[i] != null)
                    {
                        UIStyleHelper.StyleStatCard(statCards[i]);
                    }
                }
            }

            // Style stat labels
            if (statLabels != null)
            {
                for (int i = 0; i < statLabels.Length && i < 4; i++)
                {
                    if (statLabels[i] != null)
                    {
                        UIStyleHelper.SetFont(statLabels[i], UIStyleHelper.FontType.Exo2);
                        statLabels[i].color = new Color32(248, 250, 252, 255); // #f8fafc - white
                        statLabels[i].characterSpacing = 4;
                        statLabels[i].alignment = TextAlignmentOptions.Center;
                    }
                }
            }

            // Style stat values (Track and Time = purple, Score and Coins = conditional)
            if (statValues != null)
            {
                for (int i = 0; i < statValues.Length && i < 4; i++)
                {
                    if (statValues[i] != null)
                    {
                        UIStyleHelper.SetFont(statValues[i], UIStyleHelper.FontType.JetBrainsMono);
                        statValues[i].fontStyle = FontStyles.Bold;
                        statValues[i].alignment = TextAlignmentOptions.Center;
                        
                        // Track (0) and Final Time (1) = purple
                        // Score (2) and Coins (3) = will be set based on winner/loser state
                        if (i < 2)
                        {
                            statValues[i].color = new Color32(153, 69, 255, 255); // #9945FF - purple
                        }
                        else
                        {
                            // Score and Coins - will be updated based on winner/loser
                            statValues[i].color = new Color32(153, 69, 255, 255); // Default purple, will change
                        }
                    }
                }
            }

            // Style CompetitiveResult section
            if (competitiveResultSection != null)
            {
                UIStyleHelper.StyleSection(competitiveResultSection, isGreen: true);
            }

            if (winnerIndicatorText != null)
            {
                UIStyleHelper.SetFont(winnerIndicatorText, UIStyleHelper.FontType.Orbitron);
                winnerIndicatorText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                winnerIndicatorText.fontStyle = FontStyles.Bold;
                winnerIndicatorText.alignment = TextAlignmentOptions.Center;
            }

            if (opponentTimeText != null)
            {
                UIStyleHelper.SetFont(opponentTimeText, UIStyleHelper.FontType.JetBrainsMono);
                opponentTimeText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                opponentTimeText.alignment = TextAlignmentOptions.Center;
            }

            // Style PrizeClaim section
            if (prizeClaimSection != null)
            {
                UIStyleHelper.StyleSection(prizeClaimSection, isGreen: true);
            }

            if (prizeStatusText != null)
            {
                UIStyleHelper.SetFont(prizeStatusText, UIStyleHelper.FontType.Exo2);
                prizeStatusText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                prizeStatusText.fontStyle = FontStyles.Bold;
                prizeStatusText.alignment = TextAlignmentOptions.Center;
            }

            if (prizeAmountText != null)
            {
                UIStyleHelper.SetFont(prizeAmountText, UIStyleHelper.FontType.JetBrainsMono);
                prizeAmountText.color = new Color32(153, 69, 255, 255); // #9945FF
                prizeAmountText.fontStyle = FontStyles.Bold;
                prizeAmountText.alignment = TextAlignmentOptions.Center;
                
                // Add glow effect
                var outline = prizeAmountText.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = prizeAmountText.gameObject.AddComponent<Outline>();
                }
                outline.effectColor = new Color32(153, 69, 255, 128); // rgba(153, 69, 255, 0.5)
                outline.effectDistance = new Vector2(2, 2);
            }

            if (tokenNameText != null)
            {
                UIStyleHelper.SetFont(tokenNameText, UIStyleHelper.FontType.Exo2);
                tokenNameText.color = new Color32(148, 163, 184, 255); // #94A3B8
                tokenNameText.alignment = TextAlignmentOptions.Center;
            }

            // Style Waiting section
            if (waitingSection != null)
            {
                UIStyleHelper.StyleSection(waitingSection, isGreen: true);
            }

            if (waitingStatusText != null)
            {
                UIStyleHelper.SetFont(waitingStatusText, UIStyleHelper.FontType.Exo2);
                waitingStatusText.color = new Color32(248, 250, 252, 255); // #f8fafc - white
                waitingStatusText.fontStyle = FontStyles.Bold;
                waitingStatusText.alignment = TextAlignmentOptions.Center;
            }

            if (errorText != null)
            {
                UIStyleHelper.SetFont(errorText, UIStyleHelper.FontType.Exo2);
                errorText.color = new Color32(20, 241, 149, 255); // #14F195 - green
                errorText.fontStyle = FontStyles.Bold;
                errorText.alignment = TextAlignmentOptions.Center;
            }

            // Style buttons
            if (modeSelectionButton != null)
            {
                UIStyleHelper.StyleButton(modeSelectionButton, isPrimary: true);
                var btnText = modeSelectionButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    UIStyleHelper.SetFont(btnText, UIStyleHelper.FontType.Exo2);
                    btnText.text = "MODE SELECTION";
                    btnText.fontStyle = FontStyles.Bold;
                    btnText.characterSpacing = 4;
                }
            }

            if (playAgainButton != null)
            {
                UIStyleHelper.StyleButton(playAgainButton, isPrimary: false); // Green button
                var btnText = playAgainButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    UIStyleHelper.SetFont(btnText, UIStyleHelper.FontType.Exo2);
                    btnText.text = "PLAY AGAIN";
                    btnText.color = new Color32(11, 14, 17, 255); // #0b0e11 - dark text on green
                    btnText.fontStyle = FontStyles.Bold;
                    btnText.characterSpacing = 4;
                }
            }

            if (claimPrizeButton != null)
            {
                UIStyleHelper.StyleButton(claimPrizeButton, isPrimary: false); // Green button
                var btnText = claimPrizeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    UIStyleHelper.SetFont(btnText, UIStyleHelper.FontType.Exo2);
                    btnText.text = "CLAIM PRIZE";
                    btnText.color = new Color32(11, 14, 17, 255); // #0b0e11 - dark text on green
                    btnText.fontStyle = FontStyles.Bold;
                    btnText.characterSpacing = 4;
                }
            }

            if (fallbackTxnButton != null)
            {
                UIStyleHelper.StyleButton(fallbackTxnButton, isPrimary: true); // Purple button
                var btnText = fallbackTxnButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    UIStyleHelper.SetFont(btnText, UIStyleHelper.FontType.Exo2);
                    btnText.text = "FALLBACK TXN";
                    btnText.fontStyle = FontStyles.Bold;
                    btnText.characterSpacing = 4;
                }
            }
        }

        #endregion

        #region Results Display

        private void LoadResultsData()
        {
            bool isGameOver = GameOverData.IsGameOver;
            string titleTextValue = isGameOver ? gameOverTitle : raceCompleteTitle;

            // Update title (competitive card)
            if (titleText != null)
                titleText.text = titleTextValue;

            // Update practice title (if separate)
            if (practiceTitleText != null)
                practiceTitleText.text = titleTextValue;

            // Check if we're in practice mode
            bool isPracticeMode = !GameModeData.IsCompetitive;

            if (isPracticeMode)
            {
                // Practice mode - update practice card specific elements
                if (practiceTrackNameText != null)
                    practiceTrackNameText.text = GameOverData.TrackName;

                if (practiceTimeText != null)
                    practiceTimeText.text = FormatTime(GameOverData.FinalTime);

                if (practiceScoreText != null)
                    practiceScoreText.text = GameOverData.Score.ToString();

                if (practiceCoinsText != null)
                    practiceCoinsText.text = GameOverData.CoinsCollected.ToString();
            }
            else
            {
                // Competitive mode - update stats grid
                // Update stat labels
                if (statLabels != null && statLabels.Length >= 4)
                {
                    if (statLabels[0] != null) statLabels[0].text = "TRACK";
                    if (statLabels[1] != null) statLabels[1].text = "FINAL TIME";
                    if (statLabels[2] != null) statLabels[2].text = "SCORE";
                    if (statLabels[3] != null) statLabels[3].text = "COINS";
                }

                // Update stat values
                if (statValues != null && statValues.Length >= 4)
                {
                    if (statValues[0] != null) statValues[0].text = GameOverData.TrackName;
                    if (statValues[1] != null) statValues[1].text = FormatTime(GameOverData.FinalTime);
                    if (statValues[2] != null) statValues[2].text = GameOverData.Score.ToString();
                    if (statValues[3] != null) statValues[3].text = GameOverData.CoinsCollected.ToString();
                }
            }
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
        /// Claim Prize button - Handles both SOL transfer and Jupiter swap based on token
        /// </summary>
        public async void OnClaimPrizeClicked()
        {
            if (isProcessingTransaction) return;
            
            // Determine method based on token type
            string method = isSolToken ? "claim_prize" : "jupiter_swap";
            await ProcessClaimPrize(method);
        }

        /// <summary>
        /// Fallback Transaction button - Retry result submission or claim prize
        /// </summary>
        public async void OnFallbackTxnClicked()
        {
            if (isProcessingTransaction) return;
            
            // First check if we need to submit result
            if (RaceData.NeedsResultSubmission())
            {
                Debug.Log("[ResultsScreen] Fallback button: Retrying result submission...");
                isProcessingTransaction = true;
                SetButtonActive(fallbackTxnButton, false);
                await RetryResultSubmissionAsync();
                isProcessingTransaction = false;
                return;
            }
            
            // Otherwise, process as claim prize fallback
            await ProcessClaimPrize("fallback_sol");
        }
        
        /// <summary>
        /// Async version of retry submission for button click handling
        /// </summary>
        private async Task RetryResultSubmissionAsync()
        {
            if (!RaceData.NeedsResultSubmission())
            {
                SetButtonActive(fallbackTxnButton, false);
                return;
            }

            ShowWaitingState("Retrying result submission...");

            try
            {
                string raceId = RaceData.CurrentRaceId;
                float finishTime = RaceData.PlayerFinishTime;
                int coinsCollected = RaceData.PlayerCoinsCollected;
                string inputHash = RaceData.PlayerInputHash;

                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogError("[ResultsScreen] No race ID for retry submission");
                    ShowErrorState("No race ID found");
                    SetButtonActive(fallbackTxnButton, true);
                    return;
                }

                int finishTimeMs = Mathf.RoundToInt(finishTime * 1000f);
                
                if (string.IsNullOrEmpty(inputHash) || inputHash.Length != 64)
                {
                    inputHash = new string('0', 64);
                }

                Debug.Log($"[ResultsScreen] Retry submit (button): race={raceId}, time={finishTimeMs}ms");

                bool success = await OnChainRaceManager.SubmitResultOnChainAsync(
                    raceId,
                    finishTimeMs,
                    coinsCollected,
                    inputHash,
                    (message, progress) =>
                    {
                        ShowWaitingState(message);
                    }
                );

                RaceData.SetResultSubmitted(success);

                if (success)
                {
                    Debug.Log("[ResultsScreen] ✅ Retry submission successful!");
                    ShowWaitingState("Waiting for opponent...");
                    SetButtonActive(fallbackTxnButton, false);
                }
                else
                {
                    Debug.LogWarning("[ResultsScreen] ⚠ Retry submission failed again");
                    ShowErrorState("Failed to submit result. Please try again.");
                    SetButtonActive(fallbackTxnButton, true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Retry submission error: {ex.Message}");
                ShowErrorState($"Error: {ex.Message}");
                SetButtonActive(fallbackTxnButton, true);
            }
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
                SetButtonActive(claimPrizeButton, false);
                SetButtonActive(fallbackTxnButton, false);
                ShowLoading(true);
                HideError();

                Debug.Log($"[ResultsScreen] Processing claim with method: {method}");

                // Step 1: Check if race needs on-chain settlement
                Debug.Log($"[ResultsScreen] Step 1: Checking if on-chain settlement is needed...");
                var payoutClient = PayoutAPIClient.Instance;
                
                try
                {
                    // Pass wallet address so the transaction payer matches the signer
                    var settleResponse = await payoutClient.GetSettleTransaction(raceId, authManager.WalletAddress);
                    
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
                    if (prizeStatusText != null)
                        prizeStatusText.text = "Prize Claimed! ✓";
                    
                    // Enable mode selection button
                    SetButtonActive(modeSelectionButton, true);
                    SetButtonActive(claimPrizeButton, false);
                    
                    // Auto-redirect to mode selection after delay
                    await Task.Delay((int)(redirectDelay * 1000));
                    OnModeSelectionClicked();
                }
                else
                {
                    // Transaction failed - show error and enable fallback button
                    ShowErrorState("Transaction failed. Try fallback option.");
                    SetButtonActive(fallbackTxnButton, true);
                    SetButtonActive(claimPrizeButton, false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error claiming prize: {ex.Message}");
                ShowErrorState($"Error: {ex.Message}");
                SetButtonActive(fallbackTxnButton, true);
                SetButtonActive(claimPrizeButton, false);
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
                    // No payout yet - check race status to see if we're the loser
                    Debug.Log("[ResultsScreen] No payout status - checking race status for winner info");
                    await CheckRaceStatusForWinner();
                    return;
                }

                // IMPORTANT: Stop race status polling since we have payout info now
                StopRaceStatusPolling();
                Debug.Log("[ResultsScreen] Payout status received - stopped race status polling");

                currentPayoutStatus = payoutStatus;
                UpdatePayoutUI(payoutStatus);

                // Start payout polling if still pending (for winner waiting for swap)
                if (payoutStatus.swap_status == "pending" || payoutStatus.swap_status == "swapping")
                {
                    // Only start payout polling for winner
                    bool imWinner = !string.IsNullOrEmpty(payoutStatus.winner_wallet) && payoutStatus.winner_wallet == myWallet;
                    if (imWinner)
                    {
                        StartPayoutPolling();
                    }
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
        
        /// <summary>
        /// Check race status to determine winner when no payout exists
        /// (Losers don't have payout records)
        /// </summary>
        private async Task CheckRaceStatusForWinner()
        {
            string raceId = RaceData.CurrentRaceId;
            if (string.IsNullOrEmpty(raceId)) return;
            
            try
            {
                var raceClient = RaceAPIClient.Instance;
                var status = await raceClient.GetRaceStatusAsync(raceId);
                
                if (status == null)
                {
                    Debug.LogWarning("[ResultsScreen] Could not get race status");
                    ShowWaitingState("Waiting for opponent...");
                    return;
                }
                
                if (status.is_settled && !string.IsNullOrEmpty(status.winner_wallet))
                {
                    // IMPORTANT: Stop race status polling since race is settled
                    StopRaceStatusPolling();
                    
                    // Race is settled - check if we're the winner or loser
                    bool imWinner = status.winner_wallet == myWallet;
                    
                    Debug.Log($"[ResultsScreen] Race settled. Winner: {status.winner_wallet}, Me: {myWallet}, Am I winner: {imWinner}");
                    
                    if (!imWinner)
                    {
                        // We're the LOSER - show mode selection and loser UI
                        isWinner = false;
                        
                        // Hide waiting, show result
                        if (waitingSection != null) waitingSection.SetActive(false);
                        
                        // Update winner indicator text
                        if (winnerIndicatorText != null)
                        {
                            winnerIndicatorText.text = "You Lost";
                            winnerIndicatorText.color = new Color32(239, 68, 68, 255); // Red
                        }
                        
                        // Show competitive result section
                        if (competitiveResultSection != null)
                            competitiveResultSection.SetActive(true);
                        
                        // Update prize section to show "Prize Wagered"
                        if (prizeClaimSection != null)
                            prizeClaimSection.SetActive(true);
                        if (prizeStatusText != null)
                            prizeStatusText.text = "Prize Wagered";
                        if (prizeAmountText != null)
                            prizeAmountText.text = $"{RaceData.EntryFeeSol * 2:F4} SOL";
                        
                        // Show ONLY mode selection button for loser
                        SetButtonActive(claimPrizeButton, false);
                        SetButtonActive(fallbackTxnButton, false);
                        SetButtonActive(modeSelectionButton, true);
                        
                        Debug.Log("[ResultsScreen] ✅ Loser UI activated - mode selection button shown");
                        
                        // Verify button state
                        if (modeSelectionButton != null)
                        {
                            Debug.Log($"[ResultsScreen] Mode selection button: active={modeSelectionButton.gameObject.activeSelf}, interactable={modeSelectionButton.interactable}");
                        }
                    }
                    else
                    {
                        // We're the winner but no payout record exists yet - wait
                        Debug.Log("[ResultsScreen] Winner but no payout yet - waiting for payout creation");
                        ShowWaitingState("Processing your prize...");
                        StartPayoutPolling();
                    }
                }
                else
                {
                    // Race not settled yet
                    Debug.Log("[ResultsScreen] Race not settled yet");
                    ShowWaitingState("Waiting for opponent...");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResultsScreen] Error checking race status: {ex.Message}");
                ShowWaitingState("Waiting for opponent...");
            }
        }

        private void UpdatePayoutUI(PayoutStatusResponse payout)
        {
            if (payout == null) return;

            // Determine if SOL token
            isSolToken = IsSolMint(payout.token_mint);

            // Check if current player is winner
            isWinner = !string.IsNullOrEmpty(payout.winner_wallet) && payout.winner_wallet == myWallet;

            Debug.Log($"[ResultsScreen] ====== UpdatePayoutUI ======");
            Debug.Log($"[ResultsScreen] payout.winner_wallet = '{payout.winner_wallet}'");
            Debug.Log($"[ResultsScreen] myWallet = '{myWallet}'");
            Debug.Log($"[ResultsScreen] Comparison: {payout.winner_wallet} == {myWallet} => {payout.winner_wallet == myWallet}");
            Debug.Log($"[ResultsScreen] isWinner = {isWinner}, isSolToken = {isSolToken}, status = {payout.swap_status}");

            // Update prize section
            if (prizeClaimSection != null)
            {
                prizeClaimSection.SetActive(true);
            }

            // Update prize status text
            if (prizeStatusText != null)
            {
                if (isWinner)
                {
                    prizeStatusText.text = GetStatusText(payout.swap_status);
                }
                else
                {
                    prizeStatusText.text = "Prize Wagered";
                }
            }

            // Update prize amount
            if (prizeAmountText != null)
            {
                prizeAmountText.text = $"{payout.prize_amount_sol:F4} SOL";
            }

            // Update token name
            if (tokenNameText != null)
            {
                tokenNameText.text = isSolToken ? "SOL" : GetShortMint(payout.token_mint);
            }

            // Update competitive result section (winner/loser)
            UpdateCompetitiveResultSection();

            // Update stat values colors based on winner/loser
            UpdateStatValueColors();

            // Hide waiting section
            if (waitingSection != null) waitingSection.SetActive(false);
            if (errorStatusContainer != null) errorStatusContainer.SetActive(false);

            // Update button states based on winner/loser and status
            UpdateButtonsForPayoutStatus(payout);
        }

        private void UpdateButtonsForPayoutStatus(PayoutStatusResponse payout)
        {
            Debug.Log($"[ResultsScreen] UpdateButtonsForPayoutStatus - isWinner={isWinner}, status={payout?.swap_status}, winnerWallet={payout?.winner_wallet}, myWallet={myWallet}");
            
            if (isWinner)
            {
                Debug.Log("[ResultsScreen] 🏆 Setting up WINNER buttons");
                // WINNER flow
                switch (payout.swap_status?.ToLower())
                {
                    case "pending":
                        // Show Claim Prize button
                        SetButtonActive(claimPrizeButton, true);
                        SetButtonActive(fallbackTxnButton, false);
                        SetButtonActive(modeSelectionButton, false);
                        Debug.Log("[ResultsScreen] Winner pending - showing Claim Prize button");
                        break;

                    case "swapping":
                        // In progress - disable all
                        SetButtonActive(claimPrizeButton, false);
                        SetButtonActive(fallbackTxnButton, false);
                        SetButtonActive(modeSelectionButton, false);
                        if (prizeStatusText != null)
                            prizeStatusText.text = "Swapping in progress...";
                        break;

                    case "paid":
                    case "fallback_sol":
                        // Already claimed - auto redirect will happen
                        SetButtonActive(claimPrizeButton, false);
                        SetButtonActive(fallbackTxnButton, false);
                        SetButtonActive(modeSelectionButton, true);
                        if (prizeStatusText != null)
                            prizeStatusText.text = "Prize Claimed! ✓";
                        break;

                    case "failed":
                        // Failed - show fallback
                        SetButtonActive(claimPrizeButton, false);
                        SetButtonActive(fallbackTxnButton, true);
                        SetButtonActive(modeSelectionButton, false);
                        ShowErrorState("Transaction failed. Try fallback option.");
                        break;

                    default:
                        // Unknown - show claim prize as default
                        SetButtonActive(claimPrizeButton, true);
                        SetButtonActive(fallbackTxnButton, false);
                        break;
                }
            }
            else
            {
                // LOSER flow - show mode selection button only
                Debug.Log("[ResultsScreen] 😢 Setting up LOSER buttons - enabling mode selection");
                SetButtonActive(claimPrizeButton, false);
                SetButtonActive(fallbackTxnButton, false);
                SetButtonActive(modeSelectionButton, true);
                
                // Extra verification
                if (modeSelectionButton != null)
                {
                    Debug.Log($"[ResultsScreen] Mode selection button active: {modeSelectionButton.gameObject.activeSelf}, interactable: {modeSelectionButton.interactable}");
                }
                else
                {
                    Debug.LogError("[ResultsScreen] ❌ modeSelectionButton is NULL!");
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
                    else
                    {
                        // Check if we need to submit our result
                        if (RaceData.NeedsResultSubmission())
                        {
                            ShowWaitingState("Your result not submitted - tap retry below");
                            SetButtonActive(fallbackTxnButton, true);
                        }
                        else if (RaceData.ResultSubmittedOnChain)
                        {
                            // Our result is submitted, waiting for opponent
                            ShowWaitingState("Waiting for opponent...");
                        }
                        else if (!RaceData.HasFinishedRace)
                        {
                            // We haven't finished yet (shouldn't happen on results screen)
                            ShowWaitingState("Race in progress...");
                        }
                        else
                        {
                            ShowWaitingState("Waiting for opponent...");
                        }
                    }
                }
                else
                {
                    // No status yet - check our local state
                    if (RaceData.NeedsResultSubmission())
                    {
                        ShowWaitingState("Your result not submitted - tap retry below");
                        SetButtonActive(fallbackTxnButton, true);
                    }
                    else
                    {
                        ShowWaitingState("Connecting...");
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

            // Hide competitive result section if opponent hasn't finished
            if (opponentResult == null || opponentResult.finish_time_ms == null)
            {
                if (competitiveResultSection != null) competitiveResultSection.SetActive(false);
                return;
            }

            // Update opponent time
            if (opponentTimeText != null && opponentResult.finish_time_ms.HasValue)
            {
                float opponentTime = opponentResult.finish_time_ms.Value / 1000f;
                opponentTimeText.text = $"{FormatTime(opponentTime)}";
            }

            // Update winner/loser indicator when race is settled
            if (status.is_settled && !string.IsNullOrEmpty(status.winner_wallet))
            {
                bool iWon = status.winner_wallet == myWallet;
                isWinner = iWon;

                UpdateCompetitiveResultSection();
                UpdateStatValueColors();
            }
        }

        /// <summary>
        /// Updates the CompetitiveResult section to show winner or loser state
        /// </summary>
        private void UpdateCompetitiveResultSection()
        {
            if (competitiveResultSection != null)
            {
                competitiveResultSection.SetActive(true);
            }

            if (winnerIndicatorText != null)
            {
                if (isWinner)
                {
                    winnerIndicatorText.text = "🏆 You Won!";
                }
                else
                {
                    winnerIndicatorText.text = "You Lost";
                }
            }
        }

        /// <summary>
        /// Updates stat value colors based on winner/loser state
        /// Score and Coins are green for winner, purple for loser
        /// </summary>
        private void UpdateStatValueColors()
        {
            if (statValues == null || statValues.Length < 4) return;

            // Score (index 2) and Coins (index 3)
            if (statValues[2] != null) // Score
            {
                statValues[2].color = isWinner 
                    ? new Color32(20, 241, 149, 255)  // #14F195 - green for winner
                    : new Color32(153, 69, 255, 255); // #9945FF - purple for loser
            }

            if (statValues[3] != null) // Coins
            {
                statValues[3].color = isWinner 
                    ? new Color32(20, 241, 149, 255)  // #14F195 - green for winner
                    : new Color32(153, 69, 255, 255); // #9945FF - purple for loser
            }
        }

        /// <summary>
        /// Shows the waiting state
        /// </summary>
        private void ShowWaitingState(string message)
        {
            if (waitingSection != null) waitingSection.SetActive(true);
            if (waitingStatusText != null) waitingStatusText.text = message;
            if (errorStatusContainer != null) errorStatusContainer.SetActive(false);
            
            // Hide other sections
            if (competitiveResultSection != null) competitiveResultSection.SetActive(false);
            if (prizeClaimSection != null) prizeClaimSection.SetActive(false);
        }

        /// <summary>
        /// Shows the error state
        /// </summary>
        private void ShowErrorState(string errorMessage)
        {
            if (waitingSection != null) waitingSection.SetActive(true);
            if (errorStatusContainer != null) errorStatusContainer.SetActive(true);
            if (errorText != null) errorText.text = errorMessage;
            if (waitingStatusText != null) waitingStatusText.text = "Error Cause";
            
            // Hide other sections
            if (competitiveResultSection != null) competitiveResultSection.SetActive(false);
            if (prizeClaimSection != null) prizeClaimSection.SetActive(false);
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

        private void ShowLoading(bool show)
        {
            // Loading is now handled by showing waiting state
            if (show)
            {
                ShowWaitingState("Processing...");
            }
        }

        private void ShowError(string message)
        {
            Debug.LogError($"[ResultsScreen] {message}");
            ShowErrorState(message);
        }

        private void HideError()
        {
            if (errorStatusContainer != null)
                errorStatusContainer.SetActive(false);
        }

        #endregion
    }
}
