using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Threading.Tasks;
using Solracer.Game;
using Solracer.Network;
using Solracer.Auth;
using Solracer.Config;
using Solracer.Utils;
using TMPro;

namespace Solracer.Game
{
    public class RaceManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ATVController atvController;
        [SerializeField] private TrackGenerator trackGenerator;

        [Header("Flip/Respawn Settings")]
        [SerializeField] private float upsideDownAngleThreshold = 90f;
        [SerializeField] private float upsideDownTimeThreshold = 10f;
        [SerializeField] private float respawnHeight = 10f;

        [Header("Stuck Detection Settings")]
        [SerializeField] private bool enableStuckDetection = true;
        [SerializeField] private float stuckTimeThreshold = 3f;
        [SerializeField] private float stuckSpeedThreshold = 0.5f;

        [Header("Track End Detection")]
        [SerializeField] private GameObject finishLine;
        [SerializeField] private bool autoCreateFinishLine = true;
        [SerializeField] private Vector2 finishLineSize = new Vector2(2f, 10f);

        [Header("On-Chain Race Settings")]
        [SerializeField] private float defaultEntryFeeSol = 0.01f;

        [Header("Countdown Settings")]
        [SerializeField] private TextMeshProUGUI countdownText;
        [SerializeField] private GameObject countdownPanel;
        [SerializeField] private float countdownInterval = 1f;

        private bool isGameActive = false;
        private bool hasReachedEnd = false;
        private bool isUpsideDown = false;
        private float upsideDownTimer = 0f;
        private bool countdownComplete = false;
        
        private float stuckTimer = 0f;
        private Vector3 lastPosition;
        private float lastPositionCheckTime = 0f;
        private const float POSITION_CHECK_INTERVAL = 0.5f;

        private InputTraceRecorder inputTraceRecorder;
        private bool atvNullWarned = false;

        private GhostRelayController _ghostRelay;
        /// <summary>Expose for ghost car renderer to call GetExtrapolatedOpponentPosition().</summary>
        public GhostRelayController GhostRelay => _ghostRelay;

        public bool IsGameActive => isGameActive;
        public bool HasReachedEnd => hasReachedEnd;
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

        private void Start()
        {
            if (GameModeData.IsCompetitive && RaceData.HasActiveRace())
            {
                StartCompetitiveFlow().FireAndForget();
            }
            else
            {
                StartRace();
            }
        }

        private async Task StartCompetitiveFlow()
        {
            await WaitForBothPlayersReady();
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

            bool bothReady = false;
            int maxAttempts = 30;
            int attempts = 0;

            while (!bothReady && attempts < maxAttempts)
            {
                var status = await raceClient.GetRaceStatusAsync(RaceData.CurrentRaceId);
                if (this == null) return;

                if (status != null && status.both_ready && status.status == "active")
                {
                    bothReady = true;
                    break;
                }

                await Task.Delay(2000);
                if (this == null) return;

                attempts++;
            }

            if (this == null) return;

            if (bothReady)
            {
                StartGhostRelay();
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
            if (countdownPanel != null)
                countdownPanel.SetActive(true);
            if (countdownText != null)
                countdownText.gameObject.SetActive(true);

            for (int i = 3; i > 0; i--)
            {
                if (countdownText != null)
                    countdownText.text = i.ToString();
                
                yield return new WaitForSeconds(countdownInterval);
            }

            if (countdownText != null)
                countdownText.text = "GO!";
            
            yield return new WaitForSeconds(countdownInterval);

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

            if (inputTraceRecorder != null)
            {
                inputTraceRecorder.StartRecording();
                Debug.Log("RaceManager: Started input trace recording");
            }

            var raceHUD = FindAnyObjectByType<Solracer.UI.RaceHUD>();
            if (raceHUD != null)
            {
                raceHUD.StartTimer();
            }

            Debug.Log("RaceManager: Race started!");
        }

        private void Update()
        {
            if (autoCreateFinishLine && finishLine == null && trackGenerator != null)
            {
                Vector2[] trackPoints = trackGenerator.TrackPoints;
                if (trackPoints != null && trackPoints.Length > 0)
                {
                    CreateFinishLine();
                }
            }
            
            if (!countdownComplete || !isGameActive)
                return;

            CheckUpsideDown();

            if (enableStuckDetection)
            {
                CheckIfStuck();
            }

            // local player position to ghost relay every frame
            if (_ghostRelay != null && atvController != null)
            {
                Vector2 pos2d = atvController.transform.root.position;
                float spd = atvController.CurrentSpeed;
                _ghostRelay.UpdateLocalState(pos2d, spd, 0);
            }
        }

        private void CheckUpsideDown()
        {
            if (atvController == null)
            {
                if (!atvNullWarned)
                {
                    Debug.LogWarning("RaceManager: ATV Controller is null!");
                    atvNullWarned = true;
                }
                return;
            }

            Transform atvTransform = atvController.transform;
            float rotationZ = atvTransform.eulerAngles.z;
            
            float normalizedRotation = rotationZ;
            if (normalizedRotation > 180f)
            {
                normalizedRotation -= 360f;
            }

            isUpsideDown = Mathf.Abs(normalizedRotation) > upsideDownAngleThreshold;

            if (isUpsideDown)
            {
                upsideDownTimer += Time.deltaTime;
                if (upsideDownTimer >= upsideDownTimeThreshold)
                {
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

        private void RespawnPlayer()
        {
            if (atvController == null)
            {
                Debug.LogWarning("RaceManager: Cannot respawn - ATV Controller is null!");
                return;
            }

            Transform atvTransform = atvController.transform;
            Vector3 currentPosition = atvTransform.position;
            
            Vector3 respawnPosition = currentPosition + Vector3.up * respawnHeight;
            Quaternion respawnRotation = Quaternion.Euler(0f, 0f, 0f);
            
            Rigidbody2D rb = atvController.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            
            atvTransform.position = respawnPosition;
            atvTransform.rotation = respawnRotation;
            
            upsideDownTimer = 0f;
            stuckTimer = 0f;
            lastPosition = respawnPosition;
            
            Debug.Log($"RaceManager: Player respawned at {respawnPosition}");
        }

        private void CheckIfStuck()
        {
            if (atvController == null)
                return;

            float currentSpeed = atvController.CurrentSpeed;
            Transform atvTransform = atvController.transform;
            Vector3 currentPosition = atvTransform.position;

            if (Time.time - lastPositionCheckTime >= POSITION_CHECK_INTERVAL)
            {
                float distanceMoved = Vector3.Distance(currentPosition, lastPosition);
                
                bool isMovingSlowly = currentSpeed < stuckSpeedThreshold;
                bool hasntMovedMuch = distanceMoved < 0.1f;
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
                    stuckTimer = 0f;
                }

                lastPosition = currentPosition;
                lastPositionCheckTime = Time.time;
            }
        }

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

        private void CreateFinishLine()
        {
            if (trackGenerator == null)
                return;

            Vector2[] trackPoints = trackGenerator.TrackPoints;
            if (trackPoints == null || trackPoints.Length == 0)
                return;

            Vector2 trackEnd = trackPoints[trackPoints.Length - 1];

            finishLine = new GameObject("FinishLine");
            finishLine.tag = "Finish";
            finishLine.transform.position = new Vector3(trackEnd.x, trackEnd.y, 0f);
            BoxCollider2D collider = finishLine.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = finishLineSize;

            FinishLineTrigger trigger = finishLine.AddComponent<FinishLineTrigger>();
            trigger.SetRaceManager(this);

            Debug.Log($"RaceManager: Created FinishLine at track end ({trackEnd.x:F2}, {trackEnd.y:F2})");
        }

        public void OnFinishLineCrossed()
        {
            if (!isGameActive || hasReachedEnd)
                return;

            hasReachedEnd = true;
            HandleRaceEnd(isGameOver: false, reason: "completed").FireAndForget();
        }

        public void TriggerGameOver(string reason = "flipped")
        {
            if (!isGameActive)
                return;

            HandleRaceEnd(isGameOver: true, reason: reason).FireAndForget();
        }

        public void TriggerRaceComplete()
        {
            if (!isGameActive)
                return;

            HandleRaceEnd(isGameOver: false, reason: "completed").FireAndForget();
        }

        private async Task HandleRaceEnd(bool isGameOver, string reason)
        {
            try
            {
                if (!isGameActive)
                    return;

                isGameActive = false;
                Debug.Log($"RaceManager: {(isGameOver ? "Game Over" : "Race Complete")} - {reason}");

                // Stop ghost relay
                if (_ghostRelay != null)
                {
                    _ghostRelay.StopRelay();
                }

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

                var coinManager = FindAnyObjectByType<CoinCollectionManager>();
                int coinsCollected = 0;
                if (coinManager != null)
                {
                    coinManager.SaveCollectedCoins();
                    coinsCollected = CoinSelectionData.GetSelectedCoinCount();
                }

                string inputHash = "";
                if (inputTraceRecorder != null)
                {
                    inputHash = inputTraceRecorder.CalculateInputHash();
                }

                RaceData.SetRaceFinished(finalTime, coinsCollected, inputHash);

                if (GameModeData.IsCompetitive && RaceData.HasActiveRace())
                {
                    Debug.Log($"[RaceManager] Submitting result on-chain for race: {RaceData.CurrentRaceId}");
                    bool submitted = false;
                    try
                    {
                        submitted = await SubmitResultOnChainWithResult(finalTime, coinsCollected, inputHash);
                        if (this == null) return;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[RaceManager] Exception submitting result: {ex.Message}");
                    }

                    RaceData.SetResultSubmitted(submitted);
                    Debug.Log(submitted
                        ? "[RaceManager] Result submitted successfully!"
                        : "[RaceManager] Result submission failed or was cancelled");
                }
                else if (GameModeData.IsCompetitive)
                {
                    Debug.LogWarning($"[RaceManager] Competitive mode but no active race! RaceId: '{RaceData.CurrentRaceId}'");
                }

                if (this == null) return;

                string trackName = GetTrackName();
                GameOverData.SetGameOverData(
                    isGameOver: isGameOver,
                    trackName: trackName,
                    finalTime: finalTime,
                    score: CalculateScore(finalTime, finalSpeed),
                    reason: reason
                );

                SceneManager.LoadScene(SceneNames.Results);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RaceManager] Error in HandleRaceEnd: {ex}");
            }
        }

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
                    inputHash = new string('0', 64);
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

        [Header("Ghost Relay")]
        [Tooltip("When true, use on-chain MagicBlock ER relay instead of HTTP backend")]
        [SerializeField] private bool useErRelay = false;

        private void StartGhostRelay()
        {
            if (!GameModeData.IsCompetitive || !RaceData.HasActiveRace())
                return;

            string raceId       = RaceData.CurrentRaceId;
            string myWallet     = AuthenticationData.WalletAddress;
            string opponentWallet = RaceData.OpponentWalletAddress;

            if (string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(myWallet))
            {
                Debug.LogWarning("[RaceManager] Cannot start ghost relay — missing raceId or wallet");
                return;
            }

            _ghostRelay = gameObject.AddComponent<GhostRelayController>();

            if (useErRelay)
            {
                StartErGhostRelayAsync(raceId, myWallet, opponentWallet).FireAndForget();
            }
            else
            {
                _ghostRelay.StartRelay(raceId, myWallet, opponentWallet);
                Debug.Log($"[RaceManager] HTTP ghost relay started. race={raceId}");
            }
        }

        private async Task StartErGhostRelayAsync(string raceId, string myWallet, string opponentWallet)
        {
            try
            {
                Debug.Log("[RaceManager] Initializing ER ghost relay...");

                // Init + delegate on base devnet, get session keypair
                var initResult = await ErLifecycleManager.InitAndDelegateAsync(
                    raceId,
                    myWallet,
                    signAndSendCallback: null // TODO: connect to wallet signing
                );

                if (this == null) return; // destroyed during await

                var erRelay = new ErGhostRelay(
                    raceId,
                    myWallet,
                    opponentWallet,
                    initResult.SessionPrivateKey,
                    initResult.SessionPublicKey
                );

                _ghostRelay.StartRelay(raceId, myWallet, opponentWallet, erRelay);
                Debug.Log($"[RaceManager] ER ghost relay started. race={raceId} pda={initResult.PositionPdaBase58}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RaceManager] ER ghost relay init failed: {ex.Message}. Falling back to HTTP.");
                _ghostRelay.StartRelay(raceId, myWallet, opponentWallet);
            }
        }

        private int CalculateScore(float time, float speed)
        {
            if (time <= 0f)
                return 0;

            int baseScore = 10000;
            int timePenalty = Mathf.RoundToInt(time * 100f);
            int speedBonus = Mathf.RoundToInt(speed * 100f);

            return Mathf.Max(0, baseScore - timePenalty + speedBonus);
        }

        private string GetTrackName()
        {
            CoinType selectedCoin = CoinSelectionData.SelectedCoin;
            return $"{CoinSelectionData.GetCoinName(selectedCoin)} Track";
        }
    }
}
