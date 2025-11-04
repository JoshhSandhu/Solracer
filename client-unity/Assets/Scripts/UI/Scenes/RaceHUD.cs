using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Solracer.Game;

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
        [Tooltip("Start timer automatically on Start")]
        [SerializeField] private bool autoStartTimer = true;

        [Tooltip("Pause timer when game is paused")]
        [SerializeField] private bool pauseWithTimeScale = true;

        //timer state
        private float currentTime = 0f;
        private bool isTimerRunning = false;
        private float lastUpdateTime;

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

