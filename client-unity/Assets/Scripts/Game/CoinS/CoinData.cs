using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game
{
    /// <summary>
    /// Coin types available in the game
    /// </summary>
    public enum CoinType
    {
        BONK,
        Solana,
        Zcash
    }

    /// <summary>
    /// to store selected coin data between scenes
    /// </summary>
    public static class CoinSelectionData
    {
        private static CoinType selectedCoin = CoinType.BONK;
        private static Sprite coinSprite = null;
        private static Dictionary<CoinType, int> collectedCoins = new Dictionary<CoinType, int>();

        public static CoinType SelectedCoin
        {
            get => selectedCoin;
            set => selectedCoin = value;
        }

        public static Sprite CoinSprite
        {
            get => coinSprite;
            set => coinSprite = value;
        }

        public static Dictionary<CoinType, int> CollectedCoins
        {
            get => collectedCoins;
            set => collectedCoins = value ?? new Dictionary<CoinType, int>();
        }

        /// <summary>
        /// display name for the coin
        /// </summary>
        public static string GetCoinName(CoinType coinType)
        {
            switch (coinType)
            {
                case CoinType.BONK:
                    return "BONK";
                case CoinType.Solana:
                    return "Solana";
                case CoinType.Zcash:
                    return "Zcash";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// sprite name/path for the coin
        /// </summary>
        public static string GetCoinSpriteName(CoinType coinType)
        {
            switch (coinType)
            {
                case CoinType.BONK:
                    return "BONK_Coin";
                case CoinType.Solana:
                    return "Solana_Coin";
                case CoinType.Zcash:
                    return "Zcash_Coin";
                default:
                    return "BONK_Coin";
            }
        }

        public static int GetSelectedCoinCount()
        {
            if (collectedCoins.ContainsKey(selectedCoin))
            {
                return collectedCoins[selectedCoin];
            }
            return 0;
        }

        /// <summary>
        /// Get the Solana mint address for a coin type.
        /// </summary>
        public static string GetCoinMintAddress(CoinType coinType)
        {
            switch (coinType)
            {
                case CoinType.BONK:
                    return "DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263";
                case CoinType.Solana:
                    return "So11111111111111111111111111111111111111112";
                case CoinType.Zcash:
                    return "METADDFL6wWMWEoKTFJwcThTbUcafjRB9ivkSqYJWy";
                default:
                    return "So11111111111111111111111111111111111111112";
            }
        }
    }
}

