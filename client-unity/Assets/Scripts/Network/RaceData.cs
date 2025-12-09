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

        // Race completion flags
        private static bool hasFinishedRace = false;
        private static bool resultSubmittedOnChain = false;
        private static float playerFinishTime = 0f;
        private static int playerCoinsCollected = 0;
        private static string playerInputHash = null;

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

        /// <summary>
        /// Flag indicating if the player has crossed the finish line (or game over)
        /// </summary>
        public static bool HasFinishedRace
        {
            get => hasFinishedRace;
            set => hasFinishedRace = value;
        }

        /// <summary>
        /// Flag indicating if the result was successfully submitted on-chain
        /// </summary>
        public static bool ResultSubmittedOnChain
        {
            get => resultSubmittedOnChain;
            set => resultSubmittedOnChain = value;
        }

        /// <summary>
        /// Player's finish time in seconds
        /// </summary>
        public static float PlayerFinishTime
        {
            get => playerFinishTime;
            set => playerFinishTime = value;
        }

        /// <summary>
        /// Number of coins collected by the player
        /// </summary>
        public static int PlayerCoinsCollected
        {
            get => playerCoinsCollected;
            set => playerCoinsCollected = value;
        }

        /// <summary>
        /// Input hash for replay verification
        /// </summary>
        public static string PlayerInputHash
        {
            get => playerInputHash;
            set => playerInputHash = value;
        }

        /// <summary>
        /// Mark the race as finished and store player results
        /// </summary>
        public static void SetRaceFinished(float finishTime, int coinsCollected, string inputHash)
        {
            hasFinishedRace = true;
            playerFinishTime = finishTime;
            playerCoinsCollected = coinsCollected;
            playerInputHash = inputHash;
            UnityEngine.Debug.Log($"[RaceData] Race finished: time={finishTime:F2}s, coins={coinsCollected}, hash={inputHash?.Substring(0, Math.Min(16, inputHash?.Length ?? 0))}...");
        }

        /// <summary>
        /// Mark that result was submitted on-chain
        /// </summary>
        public static void SetResultSubmitted(bool success)
        {
            resultSubmittedOnChain = success;
            UnityEngine.Debug.Log($"[RaceData] Result submitted on-chain: {success}");
        }

        //clear race data
        public static void ClearRaceData()
        {
            currentRaceId = null;
            currentRacePDA = null;
            currentTransactionSignature = null;
            entryFeeSol = 0.01f;
            
            // Clear completion flags
            hasFinishedRace = false;
            resultSubmittedOnChain = false;
            playerFinishTime = 0f;
            playerCoinsCollected = 0;
            playerInputHash = null;
        }

        //check if race is currently active
        public static bool HasActiveRace()
        {
            return !string.IsNullOrEmpty(currentRaceId);
        }

        /// <summary>
        /// Check if the player needs to submit their result on-chain
        /// Returns true if: race is active, player finished, but result not yet submitted
        /// </summary>
        public static bool NeedsResultSubmission()
        {
            return HasActiveRace() && hasFinishedRace && !resultSubmittedOnChain;
        }
    }
}

