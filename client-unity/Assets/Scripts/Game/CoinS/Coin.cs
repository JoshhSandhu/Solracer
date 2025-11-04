using UnityEngine;

namespace Solracer.Game
{
    /// <summary>
    /// Coin component, currently the tires collect coins
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Coin : MonoBehaviour
    {
        [Header("Coin Settings")]
        [Tooltip("Coin type")]
        [SerializeField] private CoinType coinType = CoinType.BONK;

        [Tooltip("Value of this coin")]
        [SerializeField] private int coinValue = 1;

        [Tooltip("Collection effect prefab")]
        [SerializeField] private GameObject collectionEffect;  //TODO: add this effect

        [Header("Debug")]
        [Tooltip("Enable debug logging for collisions")]
        [SerializeField] private bool debugLogging = false;

        private bool isCollected = false;
        private CoinCollectionManager collectionManager;

        private void Start()
        {
            // Find collection manager
            if (collectionManager == null)
            {
                collectionManager = FindAnyObjectByType<CoinCollectionManager>();
            }

            // Ensure collider is a trigger
            Collider2D col = GetComponent<Collider2D>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                if (debugLogging)
                {
                    Debug.LogWarning($"Coin: Collider was not set as trigger");
                }
            }

            // Verify collider exists
            if (col == null)
            {
                Debug.LogError($"Coin: No Collider2D found!");
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isCollected)
                return;

            if (debugLogging)
            {
                Debug.Log($"Coin: OnTriggerEnter2D called with: {other.gameObject.name}");
            }

            if (other.GetComponent<Rigidbody2D>() != null)
            {
                Transform parent = other.transform.parent;
                if (parent != null && parent.GetComponent<ATVController>() != null)
                {
                    if (debugLogging)
                    {
                        Debug.Log($"Coin: Detected ATV tire");
                    }
                    Collect();
                }
            }
        }

        /// <summary>
        /// Collects the coin
        /// </summary>
        private void Collect()
        {
            if (isCollected)
                return;

            isCollected = true;

            if (collectionManager != null)
            {
                collectionManager.OnCoinCollected(coinType, coinValue);
            }

            if (collectionEffect != null)
            {
                Instantiate(collectionEffect, transform.position, Quaternion.identity);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Sets the coin type
        /// </summary>
        public void SetCoinType(CoinType type)
        {
            coinType = type;
        }

        /// <summary>
        /// Gets the coin type
        /// </summary>
        public CoinType GetCoinType()
        {
            return coinType;
        }
    }
}

