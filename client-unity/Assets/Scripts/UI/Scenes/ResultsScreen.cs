using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Solracer.Game;

namespace Solracer.UI
{
    /// <summary>
    /// Results screen UI
    /// </summary>
    public class ResultsScreen : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Title text (e.g., 'Game Over' or player name)")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Track name text")]
        [SerializeField] private TextMeshProUGUI trackNameText;

        [Tooltip("Final time text")]
        [SerializeField] private TextMeshProUGUI finalTimeText;

        [Tooltip("Score text")]
        [SerializeField] private TextMeshProUGUI scoreText;

        [Tooltip("Coins collected text")]
        [SerializeField] private TextMeshProUGUI coinsCollectedText;

        [Header("Buttons")]
        [Tooltip("Play Again button")]
        [SerializeField] private Button playAgainButton;

        [Tooltip("Go to Mode Selection button")]
        [SerializeField] private Button modeSelectionButton;

        [Tooltip("Swap on Uniswap button (only shown for completed races, not game over)")]
        [SerializeField] private Button swapButton;

        [Header("Settings")]
        [Tooltip("Title text for game over")]
        [SerializeField] private string gameOverTitle = "Game Over";

        [Tooltip("Title text for race complete (can include player name)")]
        [SerializeField] private string raceCompleteTitle = "Race Complete!";

        private void Start()
        {
            AutoFindUIElements();
            LoadResultsData();
            SetupButtons();
        }

        private void AutoFindUIElements()
        {
            if (titleText == null)
            {
                titleText = FindTextByName("TitleText") ?? FindTextByName("Title");
            }

            if (trackNameText == null)
            {
                trackNameText = FindTextByName("TrackNameText") ?? FindTextByName("TrackName");
            }

            if (finalTimeText == null)
            {
                finalTimeText = FindTextByName("FinalTimeText") ?? FindTextByName("FinalTime");
            }

            if (scoreText == null)
            {
                scoreText = FindTextByName("ScoreText") ?? FindTextByName("Score");
            }

            if (coinsCollectedText == null)
            {
                coinsCollectedText = FindTextByName("CoinsCollectedText") ?? FindTextByName("CoinsCollected");
            }

            if (playAgainButton == null)
            {
                playAgainButton = FindButtonByName("PlayAgainButton") ?? FindButtonByName("PlayAgain");
            }

            if (modeSelectionButton == null)
            {
                modeSelectionButton = FindButtonByName("ModeSelectionButton") ?? FindButtonByName("ModeSelection");
            }

            if (swapButton == null)
            {
                swapButton = FindButtonByName("SwapButton") ?? FindButtonByName("Swap");
            }
        }

        private void LoadResultsData()
        {
            bool isGameOver = GameOverData.IsGameOver;
            string trackName = GameOverData.TrackName;
            float finalTime = GameOverData.FinalTime;
            int score = GameOverData.Score;
            int coinsCollected = GameOverData.CoinsCollected;

            // Set title
            if (titleText != null)
            {
                if (isGameOver)
                {
                    titleText.text = gameOverTitle;
                }
                else
                {
                    titleText.text = raceCompleteTitle;
                }
            }

            if (trackNameText != null)
            {
                trackNameText.text = trackName;
            }

            if (finalTimeText != null)
            {
                finalTimeText.text = FormatTime(finalTime);
            }

            if (scoreText != null)
            {
                scoreText.text = score.ToString();
            }

            if (coinsCollectedText != null)
            {
                coinsCollectedText.text = coinsCollected.ToString();
            }

            if (swapButton != null)
            {
                swapButton.gameObject.SetActive(!isGameOver);
            }
        }

        /// <summary>
        /// Formats time as MM:SS.mmm
        /// </summary>
        private string FormatTime(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);
            int milliseconds = Mathf.FloorToInt((time % 1f) * 1000f);
            return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
        }

        /// <summary>
        /// Sets up button click listeners
        /// </summary>
        private void SetupButtons()
        {
            if (playAgainButton != null)
            {
                playAgainButton.onClick.AddListener(OnPlayAgainClicked);
            }

            if (modeSelectionButton != null)
            {
                modeSelectionButton.onClick.AddListener(OnModeSelectionClicked);
            }

            if (swapButton != null)
            {
                swapButton.onClick.AddListener(OnSwapClicked);
            }
        }

        public void OnPlayAgainClicked()
        {
            SceneManager.LoadScene("Race");
        }

        public void OnModeSelectionClicked()
        {
            SceneManager.LoadScene("ModeSelection");
        }

        public void OnSwapClicked()
        {
            Debug.Log("Swap on Uniswap clicked (placeholder - will implement in Phase 7)");
            // TODO, Implement swap functionality in Phase 7
        }

        private TextMeshProUGUI FindTextByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<TextMeshProUGUI>();
            }
            return null;
        }

        private Button FindButtonByName(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj.GetComponent<Button>();
            }
            return null;
        }
    }
}

