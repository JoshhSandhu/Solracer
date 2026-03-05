using System.Threading.Tasks;
using UnityEngine;

namespace Solracer.Network
{
    public interface IPositionRelay
    {
        /// <summary>
        /// Send this player's current position to the relay
        /// </summary>
        Task SendPosition(PositionUpdate update);

        /// <summary>
        /// Fetch the latest ghost state for all opponents in this race
        /// Called every 200ms from GhostRelayTicker
        /// </summary>
        Task<GhostRaceState> GetOpponentPositions(string raceId);
    }

    [System.Serializable]
    public class PositionUpdate
    {
        public string race_id;
        public string wallet_address;
        public float x;
        public float y;
        public float speed;
        public int checkpoint_index;
        public int seq;
    }

    [System.Serializable]
    public class GhostPlayerState
    {
        public string wallet;
        public float x;
        public float y;
        public float speed;
        public int checkpoint_index;
        public long updated_at; // ms epoch
    }

    [System.Serializable]
    public class GhostRaceState
    {
        public string race_id;
        public GhostPlayerState[] players;
    }
}
