using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace Solracer.UI.Toast
{
    /// <summary>
    /// View component for a toast notification prefab.
    /// Handles visual setup and slide+fade animations via Coroutine + CanvasGroup.
    /// 
    /// Required prefab structure:
    ///   Root (CanvasGroup) → Background (Image) → Icon (Image), Title (TMP), Message (TMP)
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ToastItem : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Background image for color styling")]
        [SerializeField] private Image backgroundImage;

        [Tooltip("Icon image (optional, null-safe)")]
        [SerializeField] private Image iconImage;

        [Tooltip("Title text")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("Message body text")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("Optional close button (null-safe)")]
        [SerializeField] private Button closeButton;

        // Cached components
        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private Coroutine _activeCoroutine;
        private bool _isAnimating;

        /// <summary>Whether this toast is currently visible and animating or displayed.</summary>
        public bool IsActive => _isAnimating || (gameObject != null && gameObject.activeSelf);

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rectTransform = transform as RectTransform;

            // Safety for malformed prefab roots (e.g. accidentally saved with zero scale).
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        /// <summary>
        /// Populates toast content and applies style. Does not start animation.
        /// Gracefully handles null UI references.
        /// </summary>
        public void Setup(ToastRequest request, ToastStyle style)
        {
            if (titleText != null)
            {
                titleText.text = request.Title;
                titleText.color = style.titleColor;
            }

            if (messageText != null)
            {
                messageText.text = request.Message;
                messageText.color = style.messageColor;

                // Hide message text if empty
                messageText.gameObject.SetActive(!string.IsNullOrEmpty(request.Message));
            }

            if (backgroundImage != null)
            {
                backgroundImage.color = style.backgroundColor;
            }

            if (iconImage != null)
            {
                iconImage.color = style.iconTint;
            }

            // Start invisible
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        /// <summary>
        /// Runs the full toast lifecycle: slide-in → hold → slide-out → destroy.
        /// Safe to call only once per item.
        /// </summary>
        public void Play(float holdDuration, float slideDistance, float animDuration,
                        System.Action onComplete)
        {
            if (_activeCoroutine != null) return; // Already playing
            _activeCoroutine = StartCoroutine(ToastLifecycleCoroutine(
                holdDuration, slideDistance, animDuration, onComplete));
        }

        /// <summary>
        /// Force-removes the toast immediately. Cancels any running animation.
        /// </summary>
        public void ForceRemove()
        {
            SafeStopCoroutine();
            if (gameObject != null) Destroy(gameObject);
        }

        private IEnumerator ToastLifecycleCoroutine(float holdDuration, float slideDistance,
                                                     float animDuration, System.Action onComplete)
        {
            _isAnimating = true;

            // Slide in (from above, fade in)
            yield return AnimateCoroutine(0f, 1f, slideDistance, 0f, animDuration);

            // Hold
            yield return new WaitForSeconds(holdDuration);

            // Slide out (upward, fade out)
            yield return AnimateCoroutine(1f, 0f, 0f, -slideDistance, animDuration);

            _isAnimating = false;
            _activeCoroutine = null;

            onComplete?.Invoke();

            if (gameObject != null) Destroy(gameObject);
        }

        private IEnumerator AnimateCoroutine(float fromAlpha, float toAlpha,
                                              float fromOffsetY, float toOffsetY,
                                              float duration)
        {
            if (_canvasGroup == null || _rectTransform == null) yield break;

            float elapsed = 0f;
            Vector2 basePos = _rectTransform.anchoredPosition;
            // Remove any previous offset to get true base
            Vector2 startPos = new Vector2(basePos.x, basePos.y + fromOffsetY);
            Vector2 endPos = new Vector2(basePos.x, basePos.y + toOffsetY);

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // Use unscaled so toasts work during pause
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                if (_canvasGroup != null)
                    _canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);

                if (_rectTransform != null)
                    _rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

                yield return null;
            }

            // Ensure final values
            if (_canvasGroup != null) _canvasGroup.alpha = toAlpha;
            if (_rectTransform != null) _rectTransform.anchoredPosition = endPos;
        }

        private void OnCloseClicked()
        {
            SafeStopCoroutine();
            _isAnimating = false;
            if (gameObject != null) Destroy(gameObject);
        }

        private void SafeStopCoroutine()
        {
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }
            _isAnimating = false;
        }

        private void OnDisable()
        {
            // Prevent orphaned coroutine exceptions
            SafeStopCoroutine();
        }

        private void OnDestroy()
        {
            SafeStopCoroutine();
        }
    }
}
