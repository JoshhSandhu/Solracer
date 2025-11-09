using System;

namespace Solracer.Network
{
    /// <summary>
    /// static class to store race data between scenes.
    /// </summary>
    public static class RaceData
    {
        private static string currentRaceId = null;
        private static string currentRacePDA = null;
        private static string currentTransactionSignature = null;
        private static float entryFeeSol = 0.01f; //default entry fee

        public static string CurrentRaceId
        {
            get => currentRaceId;
            set => currentRaceId = value;
        }

        public static string CurrentRacePDA
        {
            get => currentRacePDA;
            set => currentRacePDA = value;
        }

        public static string CurrentTransactionSignature
        {
            get => currentTransactionSignature;
            set => currentTransactionSignature = value;
        }

        public static float EntryFeeSol
        {
            get => entryFeeSol;
            set => entryFeeSol = value;
        }

        //clear race data
        public static void ClearRaceData()
        {
            currentRaceId = null;
            currentRacePDA = null;
            currentTransactionSignature = null;
            entryFeeSol = 0.01f;
        }

        //check if race is currently active
        public static bool HasActiveRace()
        {
            return !string.IsNullOrEmpty(currentRaceId);
        }
    }
}

