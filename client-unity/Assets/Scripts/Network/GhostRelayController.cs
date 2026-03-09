using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Solracer.Network
{
    /// <summary>
    /// Drives the ghost relay tick loop and owns the dead-reckoning state
    /// for the opponent ghost car
    /// </summary>
    public class GhostRelayController : MonoBehaviour
    {
        // -----------------------------------------------------------------------
        // Inspector
        // -----------------------------------------------------------------------

        [Header("Relay Settings")]
        [Tooltip("ms between each poll cycle (reading opponent state)")]
        [SerializeField] private int pollIntervalMs = 70;

        [Tooltip("ms between each send cycle (writing local state)")]
        [SerializeField] private int sendIntervalMs = 60;

        // -----------------------------------------------------------------------
        // Public state
        // -----------------------------------------------------------------------

        /// <summary>Last received world position of the opponent (dead-reckoned).</summary>
        public Vector2 OpponentPosition { get; private set; }

        /// <summary>Last known opponent speed.</summary>
        public float OpponentSpeed { get; private set; }

        /// <summary>Last known checkpoint index of the opponent.</summary>
        public int OpponentCheckpoint { get; private set; }

        /// <summary>True if we have received at least one opponent update.</summary>
        public bool HasOpponentData { get; private set; }

        // -----------------------------------------------------------------------
        // Private fields
        // -----------------------------------------------------------------------

        private IPositionRelay _relay;
        private string _raceId;
        private string _myWallet;
        private string _opponentWallet;

        // Local player state (updated every frame by game code)
        private Vector2 _localPosition;
        private float _localSpeed;
        private int _localCheckpoint;
        private int _seq;

        // Dead-reckoning
        private Vector2 _opponentLastKnownPos;
        private Vector2 _opponentVelocityDir; // normalised movement direction
        private float _opponentLastUpdateTime;

        private CancellationTokenSource _cts;
        private bool _running;

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Start the relay loop. Call once the race becomes active and both players are known.
        /// </summary>
        public void StartRelay(string raceId, string myWallet, string opponentWallet,
                               IPositionRelay relay = null)
        {
            if (_running) StopRelay();

            _raceId          = raceId;
            _myWallet        = myWallet;
            _opponentWallet  = opponentWallet;
            _relay           = relay ?? new HttpGhostRelay();
            _seq             = 0;
            HasOpponentData  = false;
            _running         = true;

            _cts = new CancellationTokenSource();
            _ = RelayLoop(_cts.Token);

            Debug.Log($"[GhostRelayController] Started for race {raceId}");
        }

        /// <summary>
        /// Stop the relay loop. Call on race end, disconnect, or scene unload.
        /// </summary>
        public void StopRelay()
        {
            _running = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            Debug.Log("[GhostRelayController] Stopped.");
        }

        /// <summary>
        /// Update local player state. Call this every FixedUpdate or every frame
        /// from your player controller.
        /// </summary>
        public void UpdateLocalState(Vector2 position, float speed, int checkpointIndex)
        {
            _localPosition   = position;
            _localSpeed      = speed;
            _localCheckpoint = checkpointIndex;
        }

        // -----------------------------------------------------------------------
        // Dead-reckoning
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the extrapolated ghost position for this frame.
        /// Apply this to the ghost car's transform.
        /// </summary>
        public Vector2 GetExtrapolatedOpponentPosition()
        {
            if (!HasOpponentData) return OpponentPosition;

            float elapsed = Time.time - _opponentLastUpdateTime;
            return _opponentLastKnownPos + _opponentVelocityDir * OpponentSpeed * elapsed;
        }

        // -----------------------------------------------------------------------
        // Internal loop
        // -----------------------------------------------------------------------

        private async Task RelayLoop(CancellationToken ct)
        {
            float sendAccumulator = 0f;
            float lastLoopTime = Time.time;

            while (_running && !ct.IsCancellationRequested)
            {
                float now = Time.time;
                sendAccumulator += (now - lastLoopTime) * 1000f; // ms
                lastLoopTime = now;

                // 1. Send my current position (on slower cadence)
                if (sendAccumulator >= sendIntervalMs)
                {
                    sendAccumulator = 0f;
                    var update = new PositionUpdate
                    {
                        race_id          = _raceId,
                        wallet_address   = _myWallet,
                        x                = _localPosition.x,
                        y                = _localPosition.y,
                        speed            = _localSpeed,
                        checkpoint_index = _localCheckpoint,
                        seq              = ++_seq,
                    };
                    _ = _relay.SendPosition(update); // fire-and-forget
                }

                // 2. Fetch opponent positions (every poll tick)
                var state = await _relay.GetOpponentPositions(_raceId);
                if (state?.players != null)
                {
                    foreach (var p in state.players)
                    {
                        if (p.wallet == _myWallet) continue;

                        var newPos = new Vector2(p.x, p.y);

                        if (HasOpponentData)
                        {
                            var delta = newPos - _opponentLastKnownPos;
                            if (delta.sqrMagnitude > 0.01f)
                                _opponentVelocityDir = delta.normalized;

                            if (p.speed < 0.5f)
                                _opponentVelocityDir = Vector2.zero;
                        }

                        _opponentLastKnownPos    = newPos;
                        OpponentPosition         = newPos;
                        OpponentSpeed            = p.speed;
                        OpponentCheckpoint       = p.checkpoint_index;
                        _opponentLastUpdateTime  = Time.time;
                        HasOpponentData          = true;
                        break;
                    }
                }

                // Wait for next poll interval
                try
                {
                    await Task.Delay(pollIntervalMs, ct);
                }
                catch (TaskCanceledException) { break; }
            }
        }

        // -----------------------------------------------------------------------
        // Unity lifecycle
        // -----------------------------------------------------------------------

        private void OnDestroy() => StopRelay();
        private void OnApplicationQuit() => StopRelay();
    }
}
