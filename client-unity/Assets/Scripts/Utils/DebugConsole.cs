using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Utils
{
    /// <summary>
    /// On-screen debug console for mobile builds.
    /// Captures Debug.Log/Warning/Error messages and displays them
    /// as an overlay. Toggle with a 3-finger tap or the button.
    ///
    /// Add this to a persistent GameObject in your first scene.
    /// </summary>
    public class DebugConsole : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Maximum number of log entries to keep")]
        [SerializeField] private int maxEntries = 80;

        [Tooltip("Font size for log text")]
        [SerializeField] private int fontSize = 22;

        private struct LogEntry
        {
            public string message;
            public LogType type;
        }

        private readonly List<LogEntry> logEntries = new List<LogEntry>();
        private Vector2 scrollPosition;
        private bool isVisible = false;
        private bool showErrors = true;
        private bool showWarnings = true;
        private bool showLogs = true;

        // Stats
        private int errorCount = 0;
        private int warningCount = 0;
        private int logCount = 0;

        private static DebugConsole instance;

        private void Awake()
        {
            // Singleton persist across scenes
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string message, string stackTrace, LogType type)
        {
            // Truncate long messages
            if (message.Length > 300)
                message = message.Substring(0, 300) + "...";

            logEntries.Add(new LogEntry { message = message, type = type });

            // Track counts
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                    errorCount++;
                    break;
                case LogType.Warning:
                    warningCount++;
                    break;
                default:
                    logCount++;
                    break;
            }

            // Trim old entries
            while (logEntries.Count > maxEntries)
            {
                logEntries.RemoveAt(0);
            }
        }

        private void OnGUI()
        {
            // Always show small toggle button in corner
            float btnW = 120f;
            float btnH = 50f;
            string btnLabel = $"Log ({errorCount}E {warningCount}W)";

            if (GUI.Button(new Rect(Screen.width - btnW - 10, 10, btnW, btnH), btnLabel))
            {
                isVisible = !isVisible;
            }

            if (!isVisible)
                return;

            // Console panel
            float panelW = Screen.width * 0.95f;
            float panelH = Screen.height * 0.5f;
            float panelX = (Screen.width - panelW) / 2f;
            float panelY = 70f;

            // Background
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "Debug Console");

            // Filter buttons
            float filterY = panelY + 25f;
            float filterBtnW = 80f;
            GUIStyle activeStyle = new GUIStyle(GUI.skin.button);
            GUIStyle inactiveStyle = new GUIStyle(GUI.skin.button);

            // Log filter
            if (GUI.Button(new Rect(panelX + 10, filterY, filterBtnW, 30f), $"Log ({logCount})"))
                showLogs = !showLogs;

            // Warning filter
            if (GUI.Button(new Rect(panelX + 100, filterY, filterBtnW, 30f), $"Warn ({warningCount})"))
                showWarnings = !showWarnings;

            // Error filter
            if (GUI.Button(new Rect(panelX + 190, filterY, filterBtnW, 30f), $"Err ({errorCount})"))
                showErrors = !showErrors;

            // Clear button
            if (GUI.Button(new Rect(panelX + panelW - 90, filterY, 80f, 30f), "Clear"))
            {
                logEntries.Clear();
                errorCount = 0;
                warningCount = 0;
                logCount = 0;
            }

            // Scroll view
            float scrollY = filterY + 35f;
            float scrollH = panelH - (scrollY - panelY) - 10f;
            Rect scrollViewRect = new Rect(panelX + 5, scrollY, panelW - 10, scrollH);

            // Calculate content height
            GUIStyle logStyle = new GUIStyle(GUI.skin.label);
            logStyle.fontSize = fontSize;
            logStyle.wordWrap = true;
            logStyle.richText = true;

            float contentHeight = 0f;
            for (int i = 0; i < logEntries.Count; i++)
            {
                if (!ShouldShow(logEntries[i].type)) continue;
                contentHeight += logStyle.CalcHeight(new GUIContent(logEntries[i].message), panelW - 40f) + 4f;
            }

            scrollPosition = GUI.BeginScrollView(
                scrollViewRect,
                scrollPosition,
                new Rect(0, 0, panelW - 30f, Mathf.Max(contentHeight, scrollH))
            );

            float yPos = 0f;
            for (int i = 0; i < logEntries.Count; i++)
            {
                LogEntry entry = logEntries[i];
                if (!ShouldShow(entry.type)) continue;

                // Color by type
                switch (entry.type)
                {
                    case LogType.Error:
                    case LogType.Exception:
                        logStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
                        break;
                    case LogType.Warning:
                        logStyle.normal.textColor = new Color(1f, 0.9f, 0.3f);
                        break;
                    default:
                        logStyle.normal.textColor = Color.white;
                        break;
                }

                float lineHeight = logStyle.CalcHeight(new GUIContent(entry.message), panelW - 40f);
                GUI.Label(new Rect(5, yPos, panelW - 40f, lineHeight), entry.message, logStyle);
                yPos += lineHeight + 4f;
            }

            GUI.EndScrollView();

            // Auto-scroll to bottom
            if (contentHeight > scrollH)
            {
                scrollPosition.y = contentHeight;
            }
        }

        private bool ShouldShow(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                    return showErrors;
                case LogType.Warning:
                    return showWarnings;
                default:
                    return showLogs;
            }
        }
    }
}
