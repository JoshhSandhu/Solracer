using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Threading.Tasks;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;
using TMPro;

namespace Solracer.Game
{
    /// <summary>
    /// manages race state, game over conditions, and scene transitions
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ATV Controller to monitor")]
        [SerializeField] private ATVController atvController;

        [Tooltip("Track Generator to check track end")]
        [SerializeField] private TrackGenerator trackGenerator;

        [Header("Flip/Respawn Settings")]
        [Tooltip("Upside down detection angle threshold (degrees)")]
        [SerializeField] private float upsideDownAngleThreshold = 90f;

        [Tooltip("Time ATV must be upside down before respawn (seconds)")]
        [SerializeField] private float upsideDownTimeThreshold = 10f;

        [Tooltip("Respawn height above flipped position (units)")]
        [SerializeField] private float respawnHeight = 10f;

        [Header("Stuck Detection Settings")]
        [Tooltip("Check if ATV is stuck/lodged in track")]
        [SerializeField] private bool enableStuckDetection = true;

        [Tooltip("Time ATV must be stuck before auto-respawn (seconds)")]
        [SerializeField] private float stuckTimeThreshold = 3f;

        [Tooltip("Minimum speed to consider ATV stuck (m/s)")]
        [SerializeField] private float stuckSpeedThreshold = 0.5f;

        [Header("Track End Detection")]
        [Tooltip("Finish line GameObject (with trigger collider)")]
        [SerializeField] private GameObject finishLine;

        [Tooltip("Auto-create finish line at track end")]
        [SerializeField] private bool autoCreateFinishLine = true;

        [Tooltip("Finish line collider size (if auto-created)")]
        [SerializeField] private Vector2 finishLineSize = new Vector2(2f, 10f);

        [Header("On-Chain Race Settings")]
        [Tooltip("Default entry fee in SOL (used if not set elsewhere)")]
        [SerializeField] private float defaultEntryFeeSol = 0.01f;

        [Header("Countdown Settings")]
        [Tooltip("Countdown UI text (for competitive mode)")]
        [SerializeField] private TextMeshProUGUI countdownText;
        [Tooltip("Countdown panel (shown during countdown)")]
        [SerializeField] private GameObject countdownPanel;
        [Tooltip("Countdown duration (seconds between each number)")]
        [SerializeField] private float countdownInterval = 1f;

        // Game state
        private bool isGameActive = false; // Start as false, enable after countdown
        private bool hasReachedEnd = false;
        private bool isUpsideDown = false;
        private float upsideDownTimer = 0f;
        private bool countdownComplete = false;
        
        // Stuck detection
        private float stuckTimer = 0f;
        private Vector3 lastPosition;
        private float lastPositionCheckTime = 0f;
        private const float POSITION_CHECK_INTERVAL = 0.5f; // Check position every 0.5 seconds

        // Input trace recorder
        private InputTraceRecorder inputTraceRecorder;

        //properties
        //check if the game is active or not
        public bool IsGameActive => isGameActive;

        //check if the player has reached the end
        public bool HasReachedEnd => hasReachedEnd;

        //is the ATV upside down
        public bool IsUpsideDown => isUpsideDown;

        private void Awake()
        {
            if (atvController == null)
            {
                GameObject atvObject = GameObject.Find("ATV");
                if (atvObject != null)
                {
                    atvController = atvObject.GetComponent<ATVController>();
                }
            }

            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
                if (trackGenerator != null)
                {
                    Debug.Log("RaceManager: Auto-found Track Generator");
                }
            }

            //find or create input trace recorder
            inputTraceRecorder = FindAnyObjectByType<InputTraceRecorder>();
            if (inputTraceRecorder == null)
            {
                GameObject recorderObj = new GameObject("InputTraceRecorder");
                inputTraceRecorder = recorderObj.AddComponent<InputTraceRecorder>();
                DontDestroyOnLoad(recorderObj);
            }

            SetupFinishLine();
            Debug.Log($"RaceManager: Initialized - ATV: {(atvController != null ? "Found" : "Missing")}, Track: {(trackGenerator != null ? "Found" : "Missing")}, Finish Line: {(finishLine != null ? "Found" : "Missing")}");
        }

        private async void Start()
        {
            // If competitive mode, check if both players are ready and show countdown
            if (GameModeData.IsCompetitive && RaceData.HasActiveRace())
            {
                await WaitForBothPlayersReady();
            }
            else
            {
                // Practice mode or no race - start immediately
                StartRace();
            }
        }

        private async Task WaitForBothPlayersReady()
        {
            var raceClient = RaceAPIClient.Instance;
            if (raceClient == null)
            {
                Debug.LogWarning("[RaceManager] RaceAPIClient not found - starting race immediately");
                StartRace();
                return;
            }

            // Poll until both players are ready
            bool bothReady = false;
            int maxAttempts = 30; // 30 attempts * 2 seconds = 60 seconds max wait
            int attempts = 0;

            while (!bothReady && attempts < maxAttempts)
            {
                var status = await raceClient.GetRaceStatusAsync(RaceData.CurrentRaceId);
                if (status != null && status.both_ready && status.status == "active")
                {
                    bothReady = true;
                    break;
                }

                await Task.Delay(2000); // Wait 2 seconds between checks
                attempts++;
            }

            if (bothReady)
            {
                // Start countdown
                StartCoroutine(CountdownCoroutine());
            }
            else
            {
                Debug.LogWarning("[RaceManager] Both players not ready - starting race anyway");
                StartRace();
            }
        }

        private IEnumerator CountdownCoroutine()
        {
            // Show countdown panel
            if (countdownPanel != null)
                countdownPanel.SetActive(true);
            if (countdownText != null)
                countdownText.gameObject.SetActive(true);

            // 3-2-1 countdown
            for (int i = 3; i > 0; i--)
            {
                if (countdownText != null)
                    countdownText.text = i.ToString();
                
                yield return new WaitForSeconds(countdownInterval);
            }

            // Show "GO!"
            if (countdownText != null)
                countdownText.text = "GO!";
            
            yield return new WaitForSeconds(countdownInterval);

            // Hide countdown
            if (countdownPanel != null)
                countdownPanel.SetActive(false);
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            countdownComplete = true;
            StartRace();
        }

        private void StartRace()
        {
            isGameActive = true;
            countdownComplete = true;

            // Start recording input trace
            if (inputTraceRecorder != null)
            {
                inputTraceRecorder.StartRecording();
                Debug.Log("RaceManager: Started input trace recording");
            }

            // Start race HUD timer
            var raceHUD = FindAnyObjectByType<Solracer.UI.RaceHUD>();
            if (raceHUD != null)
            {
                raceHUD.StartTimer();
            }

            Debug.Log("RaceManager: Race started!");
        }

        private void Update()
        {
            //creating finishline
            if (autoCreateFinishLine && finishLine == null && trackGenerator != null)
            {
                Vector2[] trackPoints = trackGenerator.TrackPoints;
                if (trackPoints != null && trackPoints.Length > 0)
                {
                    CreateFinishLine();
                }
            }
            
            // Don't process game logic until countdown is complete
            if (!countdownComplete || !isGameActive)
                return;

            CheckUpsideDown();
            
            // Check for stuck ATV
            if (enableStuckDetection)
            {
                CheckIfStuck();
            }
        }

        //checks if the ATV is flipped and respawns if upside down for too long
        private void CheckUpsideDown()
        {
            if (atvController == null)
            {
                Debug.LogWarning("RaceManager: ATV Controller is null!");
                return;
            }

            //ATV rotation
            Transform atvTransform = atvController.transform;
            float rotationZ = atvTransform.eulerAngles.z;
            
            //normalize rotation to 180
            float normalizedRotation = rotationZ;
            if (normalizedRotation > 180f)
            {
                normalizedRotation -= 360f;
            }

            bool wasUpsideDown = isUpsideDown;
            isUpsideDown = Mathf.Abs(normalizedRotation) > upsideDownAngleThreshold;

            if (isUpsideDown)
            {
                upsideDownTimer += Time.deltaTime;
                if (upsideDownTimer >= upsideDownTimeThreshold)
                {
                    // Respawn player 10 units above current position
                    RespawnPlayer();
                }
            }
            else
            {
                if (upsideDownTimer > 0f)
                {
                    Debug.Log("RaceManager: ATV recovered from upside down position");
                }
                upsideDownTimer = 0f;
            }
        }

        /// <summary>
        /// Respawn the player 10 units above their current flipped position
        /// </summary>
        private void RespawnPlayer()
        {
            if (atvController == null)
            {
                Debug.LogWarning("RaceManager: Cannot respawn - ATV Controller is null!");
                return;
            }

            Transform atvTransform = atvController.transform;
            Vector3 currentPosition = atvTransform.position;
            
            // Calculate respawn position (10 units above current position)
            Vector3 respawnPosition = currentPosition + Vector3.up * respawnHeight;
            
            // Reset rotation to upright (0 degrees on Z axis)
            Quaternion respawnRotation = Quaternion.Euler(0f, 0f, 0f);
            
            // Get Rigidbody2D to reset velocity
            Rigidbody2D rb = atvController.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            
            // Set new position and rotation
            atvTransform.position = respawnPosition;
            atvTransform.rotation = respawnRotation;
            
            // Reset timers
            upsideDownTimer = 0f;
            stuckTimer = 0f;
            lastPosition = respawnPosition; // Update last position to prevent immediate re-trigger
            
            Debug.Log($"RaceManager: Player respawned at {respawnPosition} (was flipped for {upsideDownTimeThreshold} seconds)");
        }

        /// <summary>
        /// Check if ATV is stuck/lodged in the track and respawn if needed
        /// </summary>
        private void CheckIfStuck()
        {
            if (atvController == null)
                return;

            float currentSpeed = atvController.CurrentSpeed;
            Transform atvTransform = atvController.transform;
            Vector3 currentPosition = atvTransform.position;

            // Check position periodically
            if (Time.time - lastPositionCheckTime >= POSITION_CHECK_INTERVAL)
            {
                float distanceMoved = Vector3.Distance(currentPosition, lastPosition);
                
                // ATV is stuck if:
                // 1. Speed is very low (below threshold)
                // 2. Hasn't moved much since last check
                // 3. Is not upside down (separate check)
                bool isMovingSlowly = currentSpeed < stuckSpeedThreshold;
                bool hasntMovedMuch = distanceMoved < 0.1f; // Less than 0.1 units moved
                bool notUpsideDown = !isUpsideDown;

                if (isMovingSlowly && hasntMovedMuch && notUpsideDown)
                {
                    stuckTimer += POSITION_CHECK_INTERVAL;
                    
                    if (stuckTimer >= stuckTimeThreshold)
                    {
                        Debug.Log($"RaceManager: ATV detected as stuck (speed: {currentSpeed:F2} m/s, moved: {distanceMoved:F2} units) - respawning");
                        RespawnPlayer();
                        stuckTimer = 0f;
                    }
                }
                else
                {
                    // Reset stuck timer if ATV is moving normally
                    stuckTimer = 0f;
                }

                lastPosition = currentPosition;
                lastPositionCheckTime = Time.time;
            }
        }

        //finish line setup
        private void SetupFinishLine()
        {
            if (finishLine == null)
            {
                GameObject foundLine = GameObject.Find("FinishLine");
                if (foundLine != null)
                {
                    finishLine = foundLine;
                    Debug.Log("RaceManager: Found existing FinishLine GameObject");
                }
            }
        }

        //creating finish line
        private void CreateFinishLine()
        {
            if (trackGenerator == null)
                return;

            Vector2[] trackPoints = trackGenerator.TrackPoints;
            if (trackPoints == null || trackPoints.Length == 0)
                return;

            //last point of the track
            Vector2 trackEnd = trackPoints[trackPoints.Length - 1];

            finishLine = new GameObject("FinishLine");
            finishLine.transform.position = new Vector3(trackEnd.x, trackEnd.y, 0f);
            BoxCollider2D collider = finishLine.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = finishLineSize;

            //adding finish line componenet
            FinishLineTrigger trigger = finishLine.AddComponent<FinishLineTrigger>();
            trigger.SetRaceManager(this);

            Debug.Log($"RaceManager: Created FinishLine at track end ({trackEnd.x:F2}, {trackEnd.y:F2})");
        }

        //called when ATV crosses finish line
        public void OnFinishLineCrossed()
        {
            if (!isGameActive || hasReachedEnd)
                return;

            hasReachedEnd = true;
            TriggerRaceComplete();
        }

        //game over when ATV flipped
        public async void TriggerGameOver(string reason = "flipped")
        {
            if (!isGameActive)
                return;

            isGameActive = false;
            Debug.Log($"RaceManager: Game Over - {reason}");

            //stop recording input trace
            if (inputTraceRecorder != null)
            {
                inputTraceRecorder.StopRecording();
            }

            //race data for results
            float finalTime = 0f;
            float finalSpeed = 0f;

            var raceHUD =   FindAnyObjectByType<Solracer.UI.RaceHUD>();
            if (raceHUD != null)
            {
                raceHUD.StopTimer();
                finalTime = raceHUD.CurrentTime;
            }

            if (atvController != null)
            {
                finalSpeed = atvController.CurrentSpeed;
            }

            //save collected coins before game over
            var coinManager = FindAnyObjectByType<CoinCollectionManager>();
            int coinsCollected = 0;
            if (coinManager != null)
            {
                coinManager.SaveCollectedCoins();
                coinsCollected = CoinSelectionData.GetSelectedCoinCount();
            }

            //calculate input hash for replay verification
            string inputHash = "";
            if (inputTraceRecorder != null)
            {
                inputHash = inputTraceRecorder.CalculateInputHash();
            }

            // *** IMPORTANT: Mark race as finished and store results ***
            RaceData.SetRaceFinished(finalTime, coinsCollected, inputHash);

            //submit result onchain if competitive mode
            if (GameModeData.IsCompetitive)
            {
                if (RaceData.HasActiveRace())
                {
                    Debug.Log($"[RaceManager] Submitting result on-chain (game over) for race: {RaceData.CurrentRaceId}");
                    bool submitted = false;
                    try
                    {
                        submitted = await SubmitResultOnChainWithResult(finalTime, coinsCollected, inputHash);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RaceManager] ❌ Exception submitting result (game over): {ex.Message}");
                    }
                    
                    // *** Store submission result ***
                    RaceData.SetResultSubmitted(submitted);
                    
                    if (!submitted)
                    {
                        Debug.LogWarning("[RaceManager] ⚠ Result submission failed or was cancelled (game over)");
                    }
                    else
                    {
                        Debug.Log("[RaceManager] ✅ Result submitted successfully (game over)!");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RaceManager] ⚠ Competitive mode but no active race (game over)! RaceId: '{RaceData.CurrentRaceId}'");
                }
            }
            else
            {
                Debug.Log("[RaceManager] Practice mode - skipping on-chain result submission (game over)");
            }

            //storing game over data
            string trackName = GetTrackName();
            GameOverData.SetGameOverData(
                isGameOver: true,
                trackName: trackName,
                finalTime: finalTime,
                score: CalculateScore(finalTime, finalSpeed),
                reason: reason
            );

            LoadGameOverScene();
        }

        //race complete
        public async void TriggerRaceComplete()
        {
            if (!isGameActive)
                return;

            isGameActive = false;
            Debug.Log("RaceManager: Race Complete!");

            if (inputTraceRecorder != null)
            {
                inputTraceRecorder.StopRecording();
            }

            float finalTime = 0f;
            float finalSpeed = 0f;

            var raceHUD = FindAnyObjectByType<Solracer.UI.RaceHUD>();
            if (raceHUD != null)
            {
                raceHUD.StopTimer();
                finalTime = raceHUD.CurrentTime;
            }

            if (atvController != null)
            {
                finalSpeed = atvController.CurrentSpeed;
            }

            // Save collected coins before race complete
            var coinManager = FindAnyObjectByType<CoinCollectionManager>();
            int coinsCollected = 0;
            if (coinManager != null)
            {
                coinManager.SaveCollectedCoins();
                coinsCollected = CoinSelectionData.GetSelectedCoinCount();
            }

            //calculate input hash for replay verification
            string inputHash = "";
            if (inputTraceRecorder != null)
            {
                inputHash = inputTraceRecorder.CalculateInputHash();
                Debug.Log($"[RaceManager] Input hash calculated: {inputHash.Substring(0, Mathf.Min(16, inputHash.Length))}...");
            }

            // *** IMPORTANT: Mark race as finished and store results ***
            RaceData.SetRaceFinished(finalTime, coinsCollected, inputHash);

            // Always attempt to submit in competitive mode
            if (GameModeData.IsCompetitive)
            {
                if (RaceData.HasActiveRace())
                {
                    Debug.Log($"[RaceManager] Submitting result on-chain for race: {RaceData.CurrentRaceId}");
                    bool submitted = false;
                    try
                    {
                        submitted = await SubmitResultOnChainWithResult(finalTime, coinsCollected, inputHash);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RaceManager] ❌ Exception submitting result: {ex.Message}");
                    }
                    
                    // *** Store submission result ***
                    RaceData.SetResultSubmitted(submitted);
                    
                    if (!submitted)
                    {
                        Debug.LogWarning("[RaceManager] ⚠ Result submission failed or was cancelled");
                    }
                    else
                    {
                        Debug.Log("[RaceManager] ✅ Result submitted successfully!");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RaceManager] ⚠ Competitive mode but no active race! RaceId: '{RaceData.CurrentRaceId}'");
                }
            }
            else
            {
                Debug.Log("[RaceManager] Practice mode - skipping on-chain result submission");
            }

            string trackName = GetTrackName();
            GameOverData.SetGameOverData(
                isGameOver: false,
                trackName: trackName,
                finalTime: finalTime,
                score: CalculateScore(finalTime, finalSpeed),
                reason: "completed"
            );

            LoadResultsScene();
        }

        /// <summary>
        /// Creates race on-chain if in competitive mode and race hasn't been created yet
        /// </summary>
        private async Task CreateRaceOnChainIfNeeded()
        {
            // Only create if not already created
            if (!string.IsNullOrEmpty(RaceData.CurrentRaceId))
            {
                Debug.Log($"[RaceManager] Race already created on-chain: {RaceData.CurrentRaceId}");
                return;
            }

            // Check authentication
            var authManager = AuthenticationFlowManager.Instance;
            if (authManager == null || !authManager.IsAuthenticated)
            {
                Debug.LogError("[RaceManager] User not authenticated - cannot create race on-chain");
                return;
            }

            // Get selected token
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            string tokenMint = CoinSelectionData.GetCoinMintAddress(selectedCoin);

            if (string.IsNullOrEmpty(tokenMint))
            {
                Debug.LogError("[RaceManager] Invalid token mint address - cannot create race on-chain");
                return;
            }

            // Get entry fee (use stored value or default)
            float entryFee = RaceData.EntryFeeSol > 0 ? RaceData.EntryFeeSol : defaultEntryFeeSol;

            Debug.Log($"[RaceManager] Creating race on-chain... Token: {tokenMint}, Entry Fee: {entryFee} SOL");

            // Create race on-chain
            string raceId = await OnChainRaceManager.CreateRaceOnChainAsync(
                tokenMint,
                entryFee,
                (message, progress) =>
                {
                    Debug.Log($"[RaceManager] {message} ({progress * 100:F0}%)");
                }
            );

            if (!string.IsNullOrEmpty(raceId))
            {
                RaceData.CurrentRaceId = raceId;
                RaceData.EntryFeeSol = entryFee;
                Debug.Log($"[RaceManager] Race created successfully on-chain! Race ID: {raceId}");
            }
            else
            {
                Debug.LogError("[RaceManager] Failed to create race on-chain");
            }
        }

        /// <summary>
        /// submit race result onchain (legacy method for game over).
        /// </summary>
        private async Task SubmitResultOnChain(float finalTime, int coinsCollected, string inputHash)
        {
            await SubmitResultOnChainWithResult(finalTime, coinsCollected, inputHash);
        }

        /// <summary>
        /// submit race result onchain and return success status.
        /// </summary>
        private async Task<bool> SubmitResultOnChainWithResult(float finalTime, int coinsCollected, string inputHash)
        {
            try
            {
                string raceId = RaceData.CurrentRaceId;
                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogWarning("[RaceManager] No active race ID, skipping on-chain submission");
                    return false;
                }

                int finishTimeMs = Mathf.RoundToInt(finalTime * 1000f);

                if (string.IsNullOrEmpty(inputHash) || inputHash.Length != 64)
                {
                    Debug.LogWarning($"[RaceManager] Invalid input hash length: {inputHash?.Length ?? 0}. Using placeholder.");
                    inputHash = new string('0', 64); //placeholder
                }

                Debug.Log($"[RaceManager] Calling SubmitResultOnChainAsync with raceId={raceId}, time={finishTimeMs}ms, coins={coinsCollected}");

                bool success = await OnChainRaceManager.SubmitResultOnChainAsync(
                    raceId,
                    finishTimeMs,
                    coinsCollected,
                    inputHash,
                    (message, progress) =>
                    {
                        Debug.Log($"[RaceManager] {message} ({progress * 100:F0}%)");
                    }
                );

                return success;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RaceManager] Error submitting result on-chain: {ex.Message}");
                return false;
            }
        }

        //score based on speed { later will be based on coins }
        private int CalculateScore(float time, float speed)
        {
            //faster time higher score
            if (time <= 0f)
                return 0;

            int baseScore = 10000;
            int timePenalty = Mathf.RoundToInt(time * 100f);  //a lil time penatlty for the score
            int speedBonus = Mathf.RoundToInt(speed * 100f);

            return Mathf.Max(0, baseScore - timePenalty + speedBonus);
        }

        //loading game over scene
        private void LoadGameOverScene()
        {
            SceneManager.LoadScene("Results");
        }

       //results scene
        private void LoadResultsScene()
        {
            SceneManager.LoadScene("Results");
        }

        //get track name based on selected coin
        private string GetTrackName()
        {
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            return $"{CoinSelectionData.GetCoinName(selectedCoin)} Track";
        }
    }

    /// <summary>
    /// static class to store game over/results data between scenes
    /// </summary>
    public static class GameOverData
    {
        private static bool isGameOver = false;
        private static string trackName = "";
        private static float finalTime = 0f;
        private static int score = 0;
        private static string reason = "";
        private static int coinsCollected = 0;

        public static void SetGameOverData(bool isGameOver, string trackName, float finalTime, int score, string reason)
        {
            GameOverData.isGameOver = isGameOver;
            GameOverData.trackName = trackName;
            GameOverData.finalTime = finalTime;
            GameOverData.score = score;
            GameOverData.reason = reason;
            
            // Get coins collected from CoinSelectionData
            coinsCollected = CoinSelectionData.GetSelectedCoinCount();
        }

        public static bool IsGameOver => isGameOver;
        public static string TrackName => trackName;
        public static float FinalTime => finalTime;
        public static int Score => score;
        public static string Reason => reason;
        public static int CoinsCollected => coinsCollected;
    }
}

