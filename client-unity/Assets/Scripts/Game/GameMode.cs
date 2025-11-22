namespace Solracer.Game
{
    /// <summary>
    /// Game mode enum (Practice vs Competitive)
    /// </summary>
    public enum GameMode
    {
        Practice,
        Competitive
    }

    /// <summary>
    /// Static class to store current game mode
    /// </summary>
    public static class GameModeData
    {
        private static GameMode currentMode = GameMode.Practice;

        /// <summary>
        /// Current game mode
        /// </summary>
        public static GameMode CurrentMode
        {
            get => currentMode;
            set => currentMode = value;
        }

        /// <summary>
        /// Check if current mode is competitive
        /// </summary>
        public static bool IsCompetitive => currentMode == GameMode.Competitive;

        /// <summary>
        /// Check if current mode is practice
        /// </summary>
        public static bool IsPractice => currentMode == GameMode.Practice;

        /// <summary>
        /// Reset to practice mode
        /// </summary>
        public static void Reset()
        {
            currentMode = GameMode.Practice;
        }
    }
}



