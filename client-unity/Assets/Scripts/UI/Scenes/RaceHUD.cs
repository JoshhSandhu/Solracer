using System.Collections;
using TMPro;
using Solracer.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Solracer.UI
{
    /// <summary>
    /// HUD for Race scene
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("ATV Controller to get speed from")]
        [SerializeField] private ATVController atvController;

        [Header("Spawn Intro")]
        [Tooltip("Play HUD intro animation when the race starts")]
        [SerializeField] private bool playSpawnIntro = true;

        [Tooltip("Duration of the slide/fade intro")]
        [SerializeField] private float spawnIntroDuration = 0.18f;

        [Tooltip("Horizontal distance for the side buttons")]
        [SerializeField] private float sideButtonOffset = 260f;

        [Tooltip("Vertical distance for the hold button")]
        [SerializeField] private float holdButtonOffset = 180f;

        [Tooltip("Vertical distance for the speed/timer text")]
        [SerializeField] private float topTextOffset = 120f;

        [Tooltip("Reverse button that slides in from the left")]
        [SerializeField] private RectTransform reverseButtonRect;

        [Tooltip("Accelerate button that slides in from the right")]
        [SerializeField] private RectTransform accelerateButtonRect;

        [Tooltip("Hold/handbrake button that slides in from the bottom")]
        [SerializeField] private RectTransform holdButtonRect;

        [Header("Speed Display")]
        [Tooltip("Text component for speed display")]
        [SerializeField] private TextMeshProUGUI speedText;

        [Tooltip("Speed unit display")]
        [SerializeField] private string speedUnit = "m/s";

        [Tooltip("Speed multiplier for display")]
        [SerializeField] private float speedMultiplier = 1f;

        [Tooltip("Speed decimal places")]
        [SerializeField] private int speedDecimals = 1;

        [Header("Timer Display")]
        [Tooltip("Text component for timer display")]
        [SerializeField] private TextMeshProUGUI timerText;

        [Tooltip("Timer format")]
        [SerializeField] private TimerFormat timerFormat = TimerFormat.MMSSmmm;

        [Header("Timer Settings")]
        [Tooltip("Start timer automatically on Start (should be false RaceManager controls start)")]
        [SerializeField] private bool autoStartTimer = false;

        [Tooltip("Pause timer when game is paused")]
        [SerializeField] private bool pauseWithTimeScale = true;

        //timer state
        private float currentTime = 0f;
        private bool isTimerRunning = false;
        private float lastUpdateTime;
        private bool spawnIntroPrepared = false;
        private bool spawnIntroPlayed = false;
        private Coroutine spawnIntroCoroutine;

        private Vector2 reverseButtonTargetPosition;
        private Vector2 accelerateButtonTargetPosition;
        private Vector2 holdButtonTargetPosition;
        private Vector2 speedTextTargetPosition;
        private Vector2 timerTextTargetPosition;

        //properties
        //current timer time in seconds
        public float CurrentTime => currentTime;
        public bool IsTimerRunning => isTimerRunning;

        public enum TimerFormat
        {
            MMSSmmm,   // MM:SS.mmm (01:23.456)
            SSmmm,     // SS.mmm (83.456)
            MMM,       // mmm (123456)
            MMSS       // MM:SS (01:23)
        }

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

            if (speedText == null)
            {
                speedText = FindTextByName("SpeedText") ?? FindTextByName("Speed");
            }

            if (timerText == null)
            {
                timerText = FindTextByName("TimerText") ?? FindTextByName("Timer");
            }

            AutoFindSpawnTargets();
            CacheTargetPositions();
            PrepareSpawnIntroState();
        }

        private void Start()
        {
            if (autoStartTimer)
            {
                StartTimer();
            }

            lastUpdateTime = Time.time;
        }

        private void Update()
        {
            UpdateSpeedDisplay();
            UpdateTimerDisplay();
        }

        //updates speed display text
        private void UpdateSpeedDisplay()
        {
            if (speedText == null || atvController == null)
                return;

            float speed = atvController.CurrentSpeed * speedMultiplier;
            speedText.text = $"{speed.ToString($"F{speedDecimals}")} {speedUnit}";
        }

        //updates timer display text
        private void UpdateTimerDisplay()
        {
            if (timerText == null)
                return;

            if (isTimerRunning)
            {
                UpdateTimer();
            }

            timerText.text = FormatTimer(currentTime);
        }

        //updates the timer value based on delta time
        private void UpdateTimer()
        {
            if (pauseWithTimeScale && Time.timeScale == 0f)
                return;

            float deltaTime = Time.time - lastUpdateTime;
            currentTime += deltaTime;
            lastUpdateTime = Time.time;
        }

        //fomats the timer value based on selected format
        private string FormatTimer(float time)
        {
            switch (timerFormat)
            {
                case TimerFormat.MMSSmmm:
                    {
                        int minutes = Mathf.FloorToInt(time / 60f);
                        int seconds = Mathf.FloorToInt(time % 60f);
                        int milliseconds = Mathf.FloorToInt((time % 1f) * 1000f);
                        return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
                    }
                case TimerFormat.SSmmm:
                    {
                        int seconds = Mathf.FloorToInt(time);
                        int milliseconds = Mathf.FloorToInt((time % 1f) * 1000f);
                        return $"{seconds:00}.{milliseconds:000}";
                    }
                case TimerFormat.MMM:
                    {
                        int totalMilliseconds = Mathf.FloorToInt(time * 1000f);
                        return $"{totalMilliseconds:000000}";
                    }
                case TimerFormat.MMSS:
                    {
                        int minutes = Mathf.FloorToInt(time / 60f);
                        int seconds = Mathf.FloorToInt(time % 60f);
                        return $"{minutes:00}:{seconds:00}";
                    }
                default:
                    return time.ToString("F2");
            }
        }

        private TextMeshProUGUI FindTextByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<TextMeshProUGUI>();
            }
            return null;
        }

        private void AutoFindSpawnTargets()
        {
            if (reverseButtonRect == null)
            {
                reverseButtonRect = FindRectByName("ReverseButton");
            }

            if (accelerateButtonRect == null)
            {
                accelerateButtonRect = FindRectByName("AccelerateButton");
            }

            if (holdButtonRect == null)
            {
                holdButtonRect = FindRectByName("Handbrake") ?? FindRectByName("HoldButton");
            }
        }

        private RectTransform FindRectByName(string objectName)
        {
            GameObject obj = GameObject.Find(objectName);
            if (obj == null)
            {
                return null;
            }

            return obj.GetComponent<RectTransform>();
        }

        private void CacheTargetPositions()
        {
            if (reverseButtonRect != null)
            {
                reverseButtonTargetPosition = reverseButtonRect.anchoredPosition;
            }

            if (accelerateButtonRect != null)
            {
                accelerateButtonTargetPosition = accelerateButtonRect.anchoredPosition;
            }

            if (holdButtonRect != null)
            {
                holdButtonTargetPosition = holdButtonRect.anchoredPosition;
            }

            if (speedText != null)
            {
                speedTextTargetPosition = speedText.rectTransform.anchoredPosition;
            }

            if (timerText != null)
            {
                timerTextTargetPosition = timerText.rectTransform.anchoredPosition;
            }
        }

        private void PrepareSpawnIntroState()
        {
            if (!playSpawnIntro || spawnIntroPrepared)
            {
                return;
            }

            SetElementState(reverseButtonRect, reverseButtonTargetPosition + Vector2.left * sideButtonOffset, 0f);
            SetElementState(accelerateButtonRect, accelerateButtonTargetPosition + Vector2.right * sideButtonOffset, 0f);
            SetElementState(holdButtonRect, holdButtonTargetPosition + Vector2.down * holdButtonOffset, 0f);
            SetElementState(speedText != null ? speedText.rectTransform : null, speedTextTargetPosition + Vector2.up * topTextOffset, 0f);
            SetElementState(timerText != null ? timerText.rectTransform : null, timerTextTargetPosition + Vector2.up * topTextOffset, 0f);

            spawnIntroPrepared = true;
        }

        private void SetElementState(RectTransform rectTransform, Vector2 anchoredPosition, float alpha)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchoredPosition = anchoredPosition;

            CanvasGroup canvasGroup = GetOrAddCanvasGroup(rectTransform.gameObject);
            canvasGroup.alpha = alpha;
        }

        private CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = target.AddComponent<CanvasGroup>();
            }

            return canvasGroup;
        }

        public void PlaySpawnIntro()
        {
            if (!playSpawnIntro)
            {
                return;
            }

            if (!spawnIntroPrepared)
            {
                CacheTargetPositions();
                PrepareSpawnIntroState();
            }

            if (spawnIntroPlayed)
            {
                return;
            }

            spawnIntroPlayed = true;

            if (spawnIntroCoroutine != null)
            {
                StopCoroutine(spawnIntroCoroutine);
            }

            spawnIntroCoroutine = StartCoroutine(PlaySpawnIntroCoroutine());
        }

        private IEnumerator PlaySpawnIntroCoroutine()
        {
            float duration = Mathf.Max(0.01f, spawnIntroDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = EaseOutCubic(t);

                UpdateSpawnElement(reverseButtonRect, reverseButtonTargetPosition + Vector2.left * sideButtonOffset, reverseButtonTargetPosition, eased);
                UpdateSpawnElement(accelerateButtonRect, accelerateButtonTargetPosition + Vector2.right * sideButtonOffset, accelerateButtonTargetPosition, eased);
                UpdateSpawnElement(holdButtonRect, holdButtonTargetPosition + Vector2.down * holdButtonOffset, holdButtonTargetPosition, eased);
                UpdateSpawnElement(speedText != null ? speedText.rectTransform : null, speedTextTargetPosition + Vector2.up * topTextOffset, speedTextTargetPosition, eased);
                UpdateSpawnElement(timerText != null ? timerText.rectTransform : null, timerTextTargetPosition + Vector2.up * topTextOffset, timerTextTargetPosition, eased);

                yield return null;
            }

            UpdateSpawnElement(reverseButtonRect, reverseButtonTargetPosition, reverseButtonTargetPosition, 1f);
            UpdateSpawnElement(accelerateButtonRect, accelerateButtonTargetPosition, accelerateButtonTargetPosition, 1f);
            UpdateSpawnElement(holdButtonRect, holdButtonTargetPosition, holdButtonTargetPosition, 1f);
            UpdateSpawnElement(speedText != null ? speedText.rectTransform : null, speedTextTargetPosition, speedTextTargetPosition, 1f);
            UpdateSpawnElement(timerText != null ? timerText.rectTransform : null, timerTextTargetPosition, timerTextTargetPosition, 1f);

            spawnIntroCoroutine = null;
        }

        private void UpdateSpawnElement(RectTransform rectTransform, Vector2 startPosition, Vector2 endPosition, float t)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, t);
            GetOrAddCanvasGroup(rectTransform.gameObject).alpha = t;
        }

        private float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        //starts the timer
        public void StartTimer()
        {
            if (!isTimerRunning)
            {
                isTimerRunning = true;
                lastUpdateTime = Time.time;
            }
        }

        //stop the timer
        public void StopTimer()
        {
            isTimerRunning = false;
        }

        //resets the timer to zero
        public void ResetTimer()
        {
            currentTime = 0f;
            lastUpdateTime = Time.time;
        }

        //sresets and starts the timer
        public void ResetAndStartTimer()
        {
            ResetTimer();
            StartTimer();
        }

        public void SetATVController(ATVController controller)
        {
            atvController = controller;
        }
    }
}

