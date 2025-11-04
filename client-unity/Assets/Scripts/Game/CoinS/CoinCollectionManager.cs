using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game
{
    /// <summary>
    /// manages coin collection tracking
    /// </summary>
    public class CoinCollectionManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLogging = false;

        private Dictionary<CoinType, int> collectedCoins = new Dictionary<CoinType, int>();

        private void Awake()
        {
            collectedCoins[CoinType.BONK] = 0;
            collectedCoins[CoinType.Solana] = 0;
            collectedCoins[CoinType.Zcash] = 0;
        }

        /// <summary>
        /// called when a coin is collected
        /// </summary>
        public void OnCoinCollected(CoinType coinType, int value)
        {
            if (!collectedCoins.ContainsKey(coinType))
            {
                collectedCoins[coinType] = 0;
            }

            collectedCoins[coinType] += value;

            if (debugLogging)
            {
                Debug.Log($"CoinCollectionManager: Collected {CoinSelectionData.GetCoinName(coinType)} coin. Total: {collectedCoins[coinType]}");
            }
        }

        /// <summary>
        /// count of collected coins for a specific type
        /// </summary>
        public int GetCollectedCount(CoinType coinType)
        {
            if (collectedCoins.ContainsKey(coinType))
            {
                return collectedCoins[coinType];
            }
            return 0;
        }

        /// <summary>
        /// total count of collected coins for the selected coin type
        /// </summary>
        public int GetSelectedCoinCount()
        {
            CoinType selected = CoinSelectionData.SelectedCoin;
            return GetCollectedCount(selected);
        }

        /// <summary>
        /// collected coin data
        /// </summary>
        public Dictionary<CoinType, int> GetAllCollectedCoins()
        {
            return new Dictionary<CoinType, int>(collectedCoins);
        }

        public void ResetCoins()
        {
            collectedCoins[CoinType.BONK] = 0;
            collectedCoins[CoinType.Solana] = 0;
            collectedCoins[CoinType.Zcash] = 0;
        }

        public void SaveCollectedCoins()
        {
            CoinSelectionData.CollectedCoins = new Dictionary<CoinType, int>(collectedCoins);
        }
    }
}

