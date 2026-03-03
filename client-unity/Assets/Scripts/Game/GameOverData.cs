namespace Solracer.Game
{
    public static class GameOverData
    {
        private static bool isGameOver = false;
        private static string trackName = "";
        private static float finalTime = 0f;
        private static int score = 0;
        private static string reason = "";
        private static int coinsCollected = 0;

        public static void SetGameOverData(bool isGameOver, string trackName, float finalTime, int score, string reason)
        {
            GameOverData.isGameOver = isGameOver;
            GameOverData.trackName = trackName;
            GameOverData.finalTime = finalTime;
            GameOverData.score = score;
            GameOverData.reason = reason;
            coinsCollected = CoinSelectionData.GetSelectedCoinCount();
        }

        public static void Reset()
        {
            isGameOver = false;
            trackName = "";
            finalTime = 0f;
            score = 0;
            reason = "";
            coinsCollected = 0;
        }

        public static bool IsGameOver => isGameOver;
        public static string TrackName => trackName;
        public static float FinalTime => finalTime;
        public static int Score => score;
        public static string Reason => reason;
        public static int CoinsCollected => coinsCollected;
    }
}
