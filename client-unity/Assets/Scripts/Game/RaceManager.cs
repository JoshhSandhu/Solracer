using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;

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

        [Header("Game Over Settings")]
        [Tooltip("Upside down detection angle threshold (degrees)")]
        [SerializeField] private float upsideDownAngleThreshold = 90f;

        [Tooltip("Time ATV must be upside down before game over (seconds)")]
        [SerializeField] private float upsideDownTimeThreshold = 5f;

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

        // Game state
        private bool isGameActive = true;
        private bool hasReachedEnd = false;
        private bool isUpsideDown = false;
        private float upsideDownTimer = 0f;

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
            // If competitive mode, create race on-chain before starting (if not already created)
            if (GameModeData.IsCompetitive)
            {
                await CreateRaceOnChainIfNeeded();
            }

            //start recording input trace
            if (inputTraceRecorder != null)
            {
                inputTraceRecorder.StartRecording();
                Debug.Log("RaceManager: Started input trace recording");
            }
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
            if (!isGameActive)
                return;

            CheckUpsideDown();
        }

        //checks if the ATV is flipped
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
                    TriggerGameOver("flipped");
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

            //submit result onchain if race was created onchain
            if (RaceData.HasActiveRace())
            {
                Debug.Log("[RaceManager] Submitting result on-chain (game over)...");
                await SubmitResultOnChain(finalTime, coinsCollected, inputHash);
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

            if (RaceData.HasActiveRace())
            {
                Debug.Log("[RaceManager] Submitting result on-chain...");
                await SubmitResultOnChain(finalTime, coinsCollected, inputHash);
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
        /// submit race result onchain.
        /// </summary>
        private async Task SubmitResultOnChain(float finalTime, int coinsCollected, string inputHash)
        {
            // Only submit on-chain in competitive mode
            if (!GameModeData.IsCompetitive)
            {
                Debug.Log("[RaceManager] Practice mode - skipping on-chain result submission");
                return;
            }

            try
            {
                string raceId = RaceData.CurrentRaceId;
                if (string.IsNullOrEmpty(raceId))
                {
                    Debug.LogWarning("[RaceManager] No active race ID, skipping on-chain submission");
                    return;
                }

                int finishTimeMs = Mathf.RoundToInt(finalTime * 1000f);

                if (string.IsNullOrEmpty(inputHash) || inputHash.Length != 64)
                {
                    Debug.LogWarning($"[RaceManager] Invalid input hash length: {inputHash?.Length ?? 0}. Using placeholder.");
                    inputHash = new string('0', 64); //placeholder
                }

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

                if (success)
                {
                    Debug.Log("[RaceManager] Result submitted successfully on-chain!");
                }
                else
                {
                    Debug.LogWarning("[RaceManager] Failed to submit result on-chain");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RaceManager] Error submitting result on-chain: {ex.Message}");
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

