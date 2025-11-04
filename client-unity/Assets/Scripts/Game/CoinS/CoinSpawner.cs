using UnityEngine;
using System.Collections.Generic;

namespace Solracer.Game
{
    /// <summary>
    /// Spawns coins randomly on the track based on selected coin type
    /// </summary>
    public class CoinSpawner : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Track Generator to get track points")]
        [SerializeField] private TrackGenerator trackGenerator;

        [Header("Coin Settings")]
        [Tooltip("Coin prefab to spawn")]
        [SerializeField] private GameObject coinPrefab;

        [Tooltip("Coin sprites")]
        [SerializeField] private Sprite[] coinSprites = new Sprite[3];

        [Tooltip("Number of coins to spawn")]
        [SerializeField] private int coinCount = 10;

        [Tooltip("Minimum distance between coins")]
        [SerializeField] private float minCoinDistance = 5f;

        [Tooltip("Height offset above track for coins")]
        [SerializeField] private float coinHeightOffset = 1f;

        [Tooltip("Random height variation for coins")]
        [SerializeField] private float heightVariation = 0.5f;

        [Header("Spawning")]
        [Tooltip("Skip first N% of track for spawning")]
        [SerializeField] [Range(0f, 0.3f)] private float skipStartPercent = 0.1f;

        [Tooltip("Skip last N% of track for spawning")]
        [SerializeField] [Range(0f, 0.3f)] private float skipEndPercent = 0.1f;

        private List<GameObject> spawnedCoins = new List<GameObject>();
        private CoinType selectedCoinType;

        private void Awake()
        {
            if (trackGenerator == null)
            {
                trackGenerator = FindAnyObjectByType<TrackGenerator>();
            }
        }

        private void Start()
        {
            if (trackGenerator != null && trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                SpawnCoins();
            }
            else
            {
                //try again in Update if track not ready
            }
        }

        private void Update()
        {
            if (spawnedCoins.Count == 0 && trackGenerator != null && trackGenerator.TrackPoints != null && trackGenerator.TrackPoints.Length > 0)
            {
                SpawnCoins();
            }
        }

        /// <summary>
        /// spawns coins randomly on the track
        /// </summary>
        private void SpawnCoins()
        {
            selectedCoinType = CoinSelectionData.SelectedCoin;
            Sprite coinSprite = GetCoinSprite(selectedCoinType);
            ClearCoins();

            Vector2[] trackPoints = trackGenerator.TrackPoints;
            if (trackPoints == null || trackPoints.Length < 2)
            {
                Debug.LogWarning("CoinSpawner: Track points not available!");
                return;
            }

            int startIndex = Mathf.RoundToInt(trackPoints.Length * skipStartPercent);
            int endIndex = Mathf.RoundToInt(trackPoints.Length * (1f - skipEndPercent));
            int spawnRange = endIndex - startIndex;

            if (spawnRange < coinCount)
            {
                Debug.LogWarning($"CoinSpawner: Track too short for {coinCount} coins. Spawning {spawnRange} coins instead.");
                coinCount = spawnRange;
            }

            List<int> usedIndices = new List<int>();
            int attempts = 0;
            int maxAttempts = coinCount * 10;

            while (spawnedCoins.Count < coinCount && attempts < maxAttempts)
            {
                attempts++;
                int randomIndex = Random.Range(startIndex, endIndex);
                bool tooClose = false;
                foreach (int usedIndex in usedIndices)
                {
                    float distance = Vector2.Distance(trackPoints[randomIndex], trackPoints[usedIndex]);
                    if (distance < minCoinDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                    continue;

                Vector2 spawnPosition = trackPoints[randomIndex];
                spawnPosition.y += coinHeightOffset + Random.Range(-heightVariation, heightVariation);
                GameObject coin = SpawnCoin(spawnPosition, coinSprite);
                if (coin != null)
                {
                    spawnedCoins.Add(coin);
                    usedIndices.Add(randomIndex);
                }
            }

            Debug.Log($"CoinSpawner: Spawned {spawnedCoins.Count} {CoinSelectionData.GetCoinName(selectedCoinType)} coins on track");
        }

        /// <summary>
        /// spawining a single coin at the specified position
        /// </summary>
        private GameObject SpawnCoin(Vector2 position, Sprite sprite)
        {
            GameObject coin;

            if (coinPrefab != null)
            {
                coin = Instantiate(coinPrefab, position, Quaternion.identity);
                Coin coinComponent = coin.GetComponent<Coin>();
                if (coinComponent != null)
                {
                    coinComponent.SetCoinType(selectedCoinType);
                }
            }
            else
            {
                coin = new GameObject("Coin");
                coin.transform.position = new Vector3(position.x, position.y, 0f);
                SpriteRenderer spriteRenderer = coin.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = sprite;
                spriteRenderer.sortingOrder = 5;

                CircleCollider2D collider = coin.AddComponent<CircleCollider2D>();
                collider.isTrigger = true;
                collider.radius = 0.5f;

                Coin coinComponent = coin.AddComponent<Coin>();
                coinComponent.SetCoinType(selectedCoinType);
            }

            if (sprite != null)
            {
                SpriteRenderer renderer = coin.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    renderer.sprite = sprite;
                }
            }

            return coin;
        }

        /// <summary>
        /// Clear all spawned coins
        /// </summary>
        private void ClearCoins()
        {
            foreach (GameObject coin in spawnedCoins)
            {
                if (coin != null)
                {
                    Destroy(coin);
                }
            }
            spawnedCoins.Clear();
        }

        /// <summary>
        /// sprite for the specified coin
        /// </summary>
        private Sprite GetCoinSprite(CoinType coinType)
        {
            int index = (int)coinType;
            if (coinSprites != null && index >= 0 && index < coinSprites.Length && coinSprites[index] != null)
            {
                return coinSprites[index];
            }

            Sprite fallbackSprite = CoinSelectionData.CoinSprite;
            if (fallbackSprite != null)
            {
                Debug.LogWarning($"CoinSpawner: Coin sprite not assigned in inspector for {CoinSelectionData.GetCoinName(coinType)}. Using fallback from CoinSelectionData. Please assign sprites in inspector for better performance.");
                return fallbackSprite;
            }

            Debug.LogError($"CoinSpawner: No sprite found for {CoinSelectionData.GetCoinName(coinType)}. Please assign sprites in CoinSpawner inspector!");
            return null;
        }

        private void OnDestroy()
        {
            ClearCoins();
        }
    }
}

