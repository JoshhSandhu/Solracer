using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace Solracer.UI.Toast
{
    /// <summary>
    /// Singleton toast notification manager.
    /// 
    /// API:
    ///   ToastManager.Instance.ShowSuccess("Message");
    ///   ToastManager.Instance.ShowError("Message");
    ///   ToastManager.Instance.ShowWarning("Message");
    ///   ToastManager.Instance.ShowInfo("Message");
    ///   ToastManager.Instance.Show(new ToastRequest(...));
    ///
    /// Features:
    ///   - Sequential queue (configurable maxConcurrent)
    ///   - Dedup window: same DedupeKey within window is dropped (not updated)
    ///   - Prefab slots per type with fallback to code-styled generic
    ///   - Auto-creates Screen Space Overlay canvas if no toastRoot assigned
    ///   - DontDestroyOnLoad singleton with duplicate instance guard
    ///   - Domain reload / play mode re-entry resilient
    ///   - Graceful fail: if missing, callers get null Instance and should skip
    /// </summary>
    public class ToastManager : MonoBehaviour
    {
        [Header("Prefab Slots")]
        [Tooltip("Prefab for Success toasts (optional, falls back to runtime-generated)")]
        [SerializeField] private GameObject successToastPrefab;

        [Tooltip("Prefab for Error toasts")]
        [SerializeField] private GameObject errorToastPrefab;

        [Tooltip("Prefab for Warning toasts")]
        [SerializeField] private GameObject warningToastPrefab;

        [Tooltip("Prefab for Info toasts")]
        [SerializeField] private GameObject infoToastPrefab;

        [Tooltip("Fallback prefab used when type-specific prefab is not assigned")]
        [SerializeField] private GameObject fallbackToastPrefab;

        [Header("Toast Root")]
        [Tooltip("Container for toast instances. Auto-created if null.")]
        [SerializeField] private RectTransform toastRoot;

        [Header("Styles (per type)")]
        [SerializeField] private ToastStyle successStyle = new ToastStyle
        {
            backgroundColor = new Color(0.05f, 0.55f, 0.25f, 0.95f),
            titleColor = Color.white,
            messageColor = new Color(0.9f, 1f, 0.9f),
            iconTint = Color.white,
            defaultDuration = 3f
        };
        [SerializeField] private ToastStyle errorStyle = new ToastStyle
        {
            backgroundColor = new Color(0.7f, 0.1f, 0.1f, 0.95f),
            titleColor = Color.white,
            messageColor = new Color(1f, 0.9f, 0.9f),
            iconTint = Color.white,
            defaultDuration = 4f
        };
        [SerializeField] private ToastStyle warningStyle = new ToastStyle
        {
            backgroundColor = new Color(0.7f, 0.5f, 0.05f, 0.95f),
            titleColor = Color.white,
            messageColor = new Color(1f, 0.97f, 0.88f),
            iconTint = Color.white,
            defaultDuration = 3.5f
        };
        [SerializeField] private ToastStyle infoStyle = new ToastStyle
        {
            backgroundColor = new Color(0.15f, 0.35f, 0.65f, 0.95f),
            titleColor = Color.white,
            messageColor = new Color(0.9f, 0.95f, 1f),
            iconTint = Color.white,
            defaultDuration = 3f
        };

        [Header("Behavior")]
        [Tooltip("Max concurrent toasts displayed at once")]
        [SerializeField, Range(1, 5)] private int maxConcurrent = 1;

        [Tooltip("Dedup window in seconds. Same DedupeKey within window is dropped.")]
        [SerializeField] private float dedupeWindowSeconds = 2f;

        [Header("Animation")]
        [Tooltip("Slide distance in pixels for enter/exit animation")]
        [SerializeField] private float slideDistance = 60f;

        [Tooltip("Animation duration in seconds")]
        [SerializeField] private float animDuration = 0.3f;

        // Singleton
        private static ToastManager _instance;
        private static bool _hasLoggedMissing;

        /// <summary>
        /// Singleton instance. Returns null if not in scene, callers should null-check.
        /// Logs a single warning on first null access.
        /// </summary>
        public static ToastManager Instance
        {
            get
            {
                if (_instance == null && !_hasLoggedMissing)
                {
                    _hasLoggedMissing = true;
                    Debug.LogWarning("[ToastManager] Not found in scene, toasts will be skipped");
                }
                return _instance;
            }
        }

        // Queue
        private readonly Queue<ToastRequest> _queue = new Queue<ToastRequest>();
        private readonly List<ToastItem> _activeToasts = new List<ToastItem>();
        private readonly Dictionary<string, float> _dedupeTimestamps = new Dictionary<string, float>();
        private bool _isProcessing;

        // Domain reload resilience
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _hasLoggedMissing = false;
        }

        private void Awake()
        {
            // Singleton duplicate guard
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureToastRoot();
            ConfigureToastRootLayout(toastRoot);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ────────────────────── Public API ──────────────────────

        public void ShowSuccess(string message, float duration = 0f, string dedupeKey = null)
        {
            Show(new ToastRequest(ToastType.Success, "Success!", message, duration, dedupeKey ?? message));
        }

        public void ShowError(string message, float duration = 0f, string dedupeKey = null)
        {
            Show(new ToastRequest(ToastType.Error, "Error", message, duration, dedupeKey ?? message));
        }

        public void ShowWarning(string message, float duration = 0f, string dedupeKey = null)
        {
            Show(new ToastRequest(ToastType.Warning, "Warning", message, duration, dedupeKey ?? message));
        }

        public void ShowInfo(string message, float duration = 0f, string dedupeKey = null)
        {
            Show(new ToastRequest(ToastType.Info, "Info", message, duration, dedupeKey ?? message));
        }

        /// <summary>
        /// Enqueues a toast request. Dedup check applies to both active and queued toasts.
        /// </summary>
        public void Show(ToastRequest request)
        {
            if (request == null) return;

            // Dedup check: same key within window → drop
            if (!string.IsNullOrEmpty(request.DedupeKey))
            {
                if (_dedupeTimestamps.TryGetValue(request.DedupeKey, out float lastTime))
                {
                    if (Time.unscaledTime - lastTime < dedupeWindowSeconds)
                    {
                        return; // Drop duplicate
                    }
                }
                _dedupeTimestamps[request.DedupeKey] = Time.unscaledTime;
            }

            _queue.Enqueue(request);

            if (!_isProcessing)
            {
                StartCoroutine(ProcessQueueCoroutine());
            }
        }

        // ────────────────────── Queue Processing ──────────────────────

        private IEnumerator ProcessQueueCoroutine()
        {
            _isProcessing = true;

            while (_queue.Count > 0)
            {
                // Wait until there's room for another concurrent toast
                while (_activeToasts.Count >= maxConcurrent)
                {
                    CleanupDestroyedToasts();
                    yield return null;
                }

                CleanupDestroyedToasts();

                if (_queue.Count == 0) break;

                ToastRequest request = _queue.Dequeue();
                SpawnToast(request);

                // Small delay between spawns to prevent visual overlap
                yield return new WaitForSecondsRealtime(0.1f);
            }

            _isProcessing = false;
        }

        private void CleanupDestroyedToasts()
        {
            _activeToasts.RemoveAll(t => t == null || !t.IsActive);
        }

        private void SpawnToast(ToastRequest request)
        {
            if (toastRoot == null)
            {
                EnsureToastRoot();
                if (toastRoot == null) return;
            }

            ToastStyle style = GetStyleForType(request.Type);
            float holdDuration = request.Duration > 0f ? request.Duration : style.defaultDuration;

            // Select prefab
            GameObject prefab = GetPrefabForType(request.Type);
            ToastItem item;

            if (prefab != null)
            {
                // Instantiate assigned prefab
                GameObject go = Instantiate(prefab, toastRoot);
                item = go.GetComponentInChildren<ToastItem>(true);
                if (item == null)
                {
                    Debug.LogWarning("[ToastManager] Prefab missing ToastItem component, adding one");
                    item = go.AddComponent<ToastItem>();
                }

                // Some prefabs have a wrapper root and the real toast under it.
                // If so, promote ToastItem object to the direct child of toastRoot.
                if (item.transform != go.transform)
                {
                    RectTransform itemRect = item.transform as RectTransform;
                    if (itemRect != null)
                    {
                        itemRect.SetParent(toastRoot, false);
                    }
                    Destroy(go);
                    go = item.gameObject;
                }

                RectTransform rootRect = go.transform as RectTransform;
                NormalizeSpawnedToastRect(rootRect);

                // Ensure root has a LayoutElement so VLG can size it
                // Copy preferred height from ToastItem's LayoutElement if available
                if (go.GetComponent<LayoutElement>() == null)
                {
                    var childLE = item.GetComponent<LayoutElement>();
                    var rootLE = go.AddComponent<LayoutElement>();
                    rootLE.preferredHeight = childLE != null ? childLE.preferredHeight : 120f;
                }
            }
            else
            {
                // Create runtime toast (no prefab assigned)
                GameObject go = CreateRuntimeToast(request, style);
                item = go.GetComponent<ToastItem>();
            }

            item.Setup(request, style);
            _activeToasts.Add(item);

            item.Play(holdDuration, slideDistance, animDuration, () =>
            {
                _activeToasts.Remove(item);
            });
        }

        // ────────────────────── Prefab / Style Selection ──────────────────────

        private GameObject GetPrefabForType(ToastType type)
        {
            return type switch
            {
                ToastType.Success => successToastPrefab ?? fallbackToastPrefab,
                ToastType.Error => errorToastPrefab ?? fallbackToastPrefab,
                ToastType.Warning => warningToastPrefab ?? fallbackToastPrefab,
                ToastType.Info => infoToastPrefab ?? fallbackToastPrefab,
                _ => fallbackToastPrefab
            };
        }

        private ToastStyle GetStyleForType(ToastType type)
        {
            return type switch
            {
                ToastType.Success => successStyle,
                ToastType.Error => errorStyle,
                ToastType.Warning => warningStyle,
                ToastType.Info => infoStyle,
                _ => infoStyle
            };
        }

        // ────────────────────── Auto-Setup ──────────────────────

        /// <summary>
        /// Creates a Screen Space Overlay canvas and toast root if not assigned.
        /// Non-destructive: only creates if toastRoot is null.
        /// </summary>
        private void EnsureToastRoot()
        {
            if (toastRoot != null) return;

            // Create overlay canvas
            GameObject canvasObj = new GameObject("ToastCanvas");
            canvasObj.transform.SetParent(transform);
            canvasObj.transform.localScale = Vector3.one;
            canvasObj.transform.localPosition = Vector3.zero;
            canvasObj.transform.localRotation = Quaternion.identity;

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // Always on top

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Create toast root and configure to top-center stacking
            GameObject rootObj = new GameObject("ToastRoot");
            toastRoot = rootObj.AddComponent<RectTransform>();
            toastRoot.SetParent(canvasObj.transform, false);
            ConfigureToastRootLayout(toastRoot);
        }

        /// <summary>
        /// Creates a runtime toast GameObject when no prefab is assigned.
        /// Styled by code based on the provided ToastStyle.
        /// </summary>
        private GameObject CreateRuntimeToast(ToastRequest request, ToastStyle style)
        {
            // Root
            GameObject toastObj = new GameObject($"Toast_{request.Type}");
            RectTransform rootRect = toastObj.AddComponent<RectTransform>();
            rootRect.SetParent(toastRoot, false);
            rootRect.sizeDelta = new Vector2(480, 72);

            CanvasGroup cg = toastObj.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // Background
            Image bg = toastObj.AddComponent<Image>();
            bg.color = style.backgroundColor;
            bg.type = Image.Type.Sliced;

            // Horizontal layout
            HorizontalLayoutGroup hlg = toastObj.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 16, 10, 10);
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlHeight = true;
            hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;

            // Icon placeholder (colored square)
            GameObject iconObj = new GameObject("Icon");
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.SetParent(toastObj.transform, false);
            iconRect.sizeDelta = new Vector2(28, 28);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.color = style.iconTint;
            LayoutElement iconLE = iconObj.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 28;
            iconLE.preferredHeight = 28;

            // Set icon symbol based on type
            string typeSymbol = request.Type switch
            {
                ToastType.Success => "✓",
                ToastType.Error => "✕",
                ToastType.Warning => "⚠",
                ToastType.Info => "ℹ",
                _ => "•"
            };
            TMP_Text iconText = iconObj.AddComponent<TextMeshProUGUI>();
            iconText.text = typeSymbol;
            iconText.fontSize = 20;
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.color = style.iconTint;
            // Disable the image since we use text for icon
            iconImg.enabled = false;

            // Text container
            GameObject textContainer = new GameObject("TextContainer");
            RectTransform textRect = textContainer.AddComponent<RectTransform>();
            textRect.SetParent(toastObj.transform, false);
            LayoutElement textLE = textContainer.AddComponent<LayoutElement>();
            textLE.flexibleWidth = 1;

            VerticalLayoutGroup vlg = textContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            // Title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(textContainer.transform, false);
            TMP_Text titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
            titleTMP.text = request.Title;
            titleTMP.fontSize = 16;
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.color = style.titleColor;
            titleTMP.enableWordWrapping = true;
            titleTMP.overflowMode = TextOverflowModes.Ellipsis;

            // Message
            if (!string.IsNullOrEmpty(request.Message))
            {
                GameObject msgObj = new GameObject("Message");
                msgObj.transform.SetParent(textContainer.transform, false);
                TMP_Text msgTMP = msgObj.AddComponent<TextMeshProUGUI>();
                msgTMP.text = request.Message;
                msgTMP.fontSize = 13;
                msgTMP.color = style.messageColor;
                msgTMP.enableWordWrapping = true;
                msgTMP.overflowMode = TextOverflowModes.Ellipsis;
            }

            // Add ToastItem component and wire up references via reflection-free approach
            // Since we created everything in code, ToastItem.Setup will re-apply style
            toastObj.AddComponent<ToastItem>();

            return toastObj;
        }

        /// <summary>
        /// Normalizes spawned prefab rects so malformed prefab roots (e.g. zero scale)
        /// still render correctly inside ToastRoot's VerticalLayoutGroup.
        /// </summary>
        private void NormalizeSpawnedToastRect(RectTransform rootRect)
        {
            if (rootRect == null) return;

            // Ensure visible transform
            rootRect.localScale = Vector3.one;
            rootRect.localRotation = Quaternion.identity;

            // Use top-right anchors matching prefab layout
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Enforces top-center toast root layout (used for both assigned and auto-created roots).
        /// </summary>
        private void ConfigureToastRootLayout(RectTransform root)
        {
            if (root == null) return;

            // Stretch full screen width at top — padding will center the toast
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -60f);
            root.sizeDelta = new Vector2(0f, 0f); // width from stretch, height from content
            root.localScale = Vector3.one;
            root.localRotation = Quaternion.identity;

            ContentSizeFitter fitter = root.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}
