using UnityEngine;

namespace Solracer.Auth
{
    /// <summary>
    /// ScriptableObject to store Privy configuration
    /// </summary>
    [CreateAssetMenu(fileName = "PrivyConfig", menuName = "Solracer/Privy Config", order = 1)]
    public class PrivyConfig : ScriptableObject
    {
        [Header("Privy Configuration")]
        [Tooltip("Privy App ID from Privy Dashboard")]
        public string appId = "your-app-id";

        [Tooltip("Privy Client ID from Privy Dashboard")]
        public string clientId = "your-client-id";

        [Header("Solana Configuration")]
        [Tooltip("Solana RPC URL")]
        public string solanaRpcUrl = "https://api.devnet.solana.com";

        [Tooltip("Solana Network (devnet, mainnet-beta)")]
        public string solanaNetwork = "devnet";

        [Tooltip("Enable Solana wallet support")]
        public bool enableSolana = true;
    }
}

