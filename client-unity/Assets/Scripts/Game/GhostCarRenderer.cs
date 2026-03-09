using UnityEngine;
using Solracer.Game;

namespace Solracer.Network
{
    public class GhostCarRenderer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The RaceManager in the scene — provides access to GhostRelay.")]
        [SerializeField] private RaceManager raceManager;

        [Header("Smoothing")]
        [Tooltip("SmoothDamp time for position (lower = snappier). 0.08-0.15 is good.")]
        [SerializeField] private float smoothTime = 0.1f;

        [Tooltip("Max speed the SmoothDamp can apply (units/sec). Set high enough to not limit.")]
        [SerializeField] private float maxSmoothSpeed = 200f;

        [Tooltip("Rotation lerp speed.")]
        [SerializeField] private float rotationLerpSpeed = 8f;

        [Header("Position Correction")]
        [Tooltip("World-space offset applied on top of the received network position. " +
                 "Use this to correct any visual misalignment between the ghost and your ATV " +
                 "(e.g. set Y = 1 if the ghost appears one unit too low).")]
        [SerializeField] private Vector2 positionOffset = Vector2.zero;

        [Tooltip("If the ghost is further than this distance from the target, teleport instead of lerp. " +
                 "Prevents the ghost sliding across the whole map at the start.")]
        [SerializeField] private float snapTeleportDistance = 40f;

        [Header("Ghost Appearance")]
        [Tooltip("Alpha applied to all SpriteRenderers on this ghost (0-1).")]
        [Range(0f, 1f)]
        [SerializeField] private float ghostAlpha = 0.5f;

        [Tooltip("Tint color for the ghost car sprites.")]
        [SerializeField] private Color ghostTint = new Color(0.4f, 0.8f, 1f, 1f); // light blue

        // Internal
        private SpriteRenderer[] _renderers;
        private Vector2 _smoothVelocity;   // used by SmoothDamp
        private float _targetAngle;
        private bool _receivedFirstPosition;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            SetVisible(false); // hidden until relay sends first valid packet

            // Tint all sprites
            foreach (var sr in _renderers)
            {
                Color c = ghostTint;
                c.a = ghostAlpha;
                sr.color = c;
            }
        }

        private void Start()
        {
            // Auto-find RaceManager if not wired in Inspector
            if (raceManager == null)
                raceManager = FindAnyObjectByType<RaceManager>();

            if (raceManager == null)
                Debug.LogWarning("[GhostCarRenderer] RaceManager not found. Ghost will not move.");
        }

        private void Update()
        {
            var relay = raceManager?.GhostRelay;

            // Hide until we have valid opponent data
            if (relay == null || !relay.HasOpponentData)
            {
                SetVisible(false);
                return;
            }

            // Get dead-reckoned target position
            Vector2 targetPos = relay.GetExtrapolatedOpponentPosition() + positionOffset;

            // Don't show ghost if opponent is still at (0,0) — PDA was just initialized
            if (!_receivedFirstPosition)
            {
                if (Mathf.Abs(targetPos.x) < 0.01f && Mathf.Abs(targetPos.y) < 0.01f)
                {
                    // Still at origin — don't show yet
                    SetVisible(false);
                    return;
                }

                // First real position arrived — teleport to it
                _receivedFirstPosition = true;
                transform.position = new Vector3(targetPos.x, targetPos.y, transform.position.z);
                _smoothVelocity = Vector2.zero;
                Debug.Log($"[GhostCarRenderer] First position received — teleported to ({targetPos.x:F1}, {targetPos.y:F1})");
            }

            SetVisible(true);

            // Current position
            Vector2 current = transform.position;

            // Snap-teleport if too far (e.g. track wrap, late start, large gap)
            float dist = Vector2.Distance(current, targetPos);
            Vector2 smoothed;

            if (dist > snapTeleportDistance)
            {
                smoothed = targetPos;
                _smoothVelocity = Vector2.zero;
            }
            else
            {
                // SmoothDamp gives natural acceleration/deceleration
                smoothed = Vector2.SmoothDamp(current, targetPos, ref _smoothVelocity, smoothTime, maxSmoothSpeed, Time.deltaTime);
            }

            // Ground snapping
            LayerMask groundLayers = LayerMask.GetMask("Default", "Track");
            RaycastHit2D hit = Physics2D.Raycast(smoothed + Vector2.up * 2f, Vector2.down, 5f, groundLayers);
            if (hit.collider != null)
                smoothed.y = Mathf.Max(smoothed.y, hit.point.y);

            transform.position = new Vector3(smoothed.x, smoothed.y, transform.position.z);

            // Derive rotation from movement delta
            Vector2 visualDelta = smoothed - current;
            if (relay.OpponentSpeed > 0.5f && visualDelta.sqrMagnitude > 0.0001f)
                _targetAngle = Mathf.Atan2(visualDelta.y, visualDelta.x) * Mathf.Rad2Deg;

            // Smooth rotation
            float currentAngle = transform.eulerAngles.z;
            float smoothAngle = Mathf.LerpAngle(currentAngle, _targetAngle, rotationLerpSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, smoothAngle);
        }

        private void SetVisible(bool visible)
        {
            foreach (var sr in _renderers)
                sr.enabled = visible;
        }

        // onValidate to preview ghost in scene
        private void OnValidate()
        {
            if (_renderers == null)
                _renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);

            foreach (var sr in _renderers)
            {
                if (sr == null) continue;
                Color c = ghostTint;
                c.a = ghostAlpha;
                sr.color = c;
            }
        }
    }
}
