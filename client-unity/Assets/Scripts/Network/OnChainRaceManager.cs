using System;
using System.Threading.Tasks;
using UnityEngine;
using Solracer.Network;
using Solracer.Auth;
using Solracer.Game;
using Solracer.UI;

namespace Solracer.Network
{
    /// <summary>
    /// Manages on-chain race operations
    /// Handles transaction building, signing, and submission
    /// </summary>
    public static class OnChainRaceManager
    {
        private static TransactionAPIClient apiClient;
        private static AuthenticationFlowManager authManager;

        /// <summary>
        /// init the on chain race manager
        /// </summary>
        public static void Initialize()
        {
            apiClient = TransactionAPIClient.Instance;
            authManager = AuthenticationFlowManager.Instance;
        }

        /// <summary>
        /// create a race on chain.
        /// </summary>
        public static async Task<string> CreateRaceOnChainAsync(
            string tokenMint,
            float entryFeeSol,
            Action<string, float> onProgress = null)
        {
            try
            {
                if (apiClient == null || authManager == null)
                {
                    Initialize();
                }

                if (!authManager.IsAuthenticated)
                {
                    Debug.LogError("[OnChainRaceManager] User not authenticated");
                    onProgress?.Invoke("User not authenticated", 0f);
                    return null;
                }

                string walletAddress = authManager.WalletAddress;
                if (string.IsNullOrEmpty(walletAddress))
                {
                    Debug.LogError("[OnChainRaceManager] Wallet address not available");
                    onProgress?.Invoke("Wallet address not available", 0f);
                    return null;
                }

                //build transaction
                onProgress?.Invoke("Building transaction...", 0.2f);
                var buildRequest = new BuildTransactionRequest
                {
                    instruction_type = "create_race",
                    wallet_address = walletAddress,
                    token_mint = tokenMint,
                    entry_fee_sol = entryFeeSol
                };

                BuildTransactionResponse buildResponse;
                try
                {
                    buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to build transaction: {ex.Message}");
                    onProgress?.Invoke("Failed to build transaction", 0f);
                    return null;
                }

                if (buildResponse == null || string.IsNullOrEmpty(buildResponse.transaction_bytes))
                {
                    Debug.LogError("[OnChainRaceManager] Invalid transaction response");
                    onProgress?.Invoke("Invalid transaction response", 0f);
                    return null;
                }

                // Show transaction signing modal and sign transaction
                onProgress?.Invoke("Waiting for transaction approval...", 0.5f);
                bool userApproved = await ShowTransactionSigningModal(
                    "Create Race Transaction",
                    "Please approve the transaction to create a race on-chain.",
                    onProgress
                );

                if (!userApproved)
                {
                    Debug.LogWarning("[OnChainRaceManager] User rejected transaction signing.");
                    onProgress?.Invoke("Transaction signing cancelled.", 0f);
                    return null;
                }

                onProgress?.Invoke("Signing transaction...", 0.55f);
                string signedTransaction;
                try
                {
                    signedTransaction = await authManager.SignTransaction(buildResponse.transaction_bytes);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to sign transaction: {ex.Message}");
                    onProgress?.Invoke("Transaction signing failed", 0f);
                    HideSigningModal();
                    return null;
                }

                if (string.IsNullOrEmpty(signedTransaction))
                {
                    Debug.LogError("[OnChainRaceManager] Transaction signing returned empty signature");
                    onProgress?.Invoke("Transaction signing failed", 0f);
                    HideSigningModal();
                    return null;
                }

                //submit transaction
                onProgress?.Invoke("Submitting transaction...", 0.7f);
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "create_race",
                    race_id = buildResponse.race_id,
                    token_mint = tokenMint,
                    entry_fee_sol = entryFeeSol,
                    wallet_address = walletAddress
                };

                SubmitTransactionResponse submitResponse;
                try
                {
                    submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to submit transaction: {ex.Message}");
                    onProgress?.Invoke("Failed to submit transaction", 0f);
                    return null;
                }

                if (submitResponse == null || string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    Debug.LogError("[OnChainRaceManager] Invalid submit response");
                    onProgress?.Invoke("Transaction submission failed", 0f);
                    return null;
                }

                //wait for confirmation
                HideSigningModal();
                onProgress?.Invoke("Confirming transaction...", 0.9f);
                if (submitResponse.confirmed)
                {
                    onProgress?.Invoke("Race created successfully!", 1f);
                    Debug.Log($"[OnChainRaceManager] Race created on-chain! Race ID: {buildResponse.race_id}, TX: {submitResponse.transaction_signature}");
                    return buildResponse.race_id;
                }
                else
                {
                    //transaction submitted but not confirmed yet
                    Debug.LogWarning($"[OnChainRaceManager] Transaction submitted but not confirmed yet. TX: {submitResponse.transaction_signature}");
                    onProgress?.Invoke("Transaction submitted (pending confirmation)", 0.95f);
                    return buildResponse.race_id;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OnChainRaceManager] Error creating race on-chain: {ex.Message}");
                onProgress?.Invoke($"Error: {ex.Message}", 0f);
                return null;
            }
        }

        /// <summary>
        /// join a race onchain.
        /// </summary>
        public static async Task<bool> JoinRaceOnChainAsync(
            string raceId,
            Action<string, float> onProgress = null)
        {
            try
            {
                if (apiClient == null || authManager == null)
                {
                    Initialize();
                }

                if (!authManager.IsAuthenticated)
                {
                    Debug.LogError("[OnChainRaceManager] User not authenticated");
                    return false;
                }

                string walletAddress = authManager.WalletAddress;

                //build transaction
                onProgress?.Invoke("Building join transaction...", 0.3f);
                var buildRequest = new BuildTransactionRequest
                {
                    instruction_type = "join_race",
                    wallet_address = walletAddress,
                    race_id = raceId
                };

                var buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
                if (buildResponse == null)
                {
                    return false;
                }

                // Show transaction signing modal and sign transaction
                onProgress?.Invoke("Waiting for transaction approval...", 0.6f);
                bool userApproved = await ShowTransactionSigningModal(
                    "Join Race Transaction",
                    "Please approve the transaction to join the race on-chain.",
                    onProgress
                );

                if (!userApproved)
                {
                    Debug.LogWarning("[OnChainRaceManager] User rejected transaction signing.");
                    return false;
                }

                onProgress?.Invoke("Signing transaction...", 0.65f);
                string signedTransaction = await authManager.SignTransaction(buildResponse.transaction_bytes);
                if (string.IsNullOrEmpty(signedTransaction))
                {
                    HideSigningModal();
                    return false;
                }

                // Submit transaction
                onProgress?.Invoke("Submitting transaction...", 0.8f);
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "join_race",
                    race_id = raceId
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                if (submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    HideSigningModal();
                    onProgress?.Invoke("Joined race successfully!", 1f);
                    Debug.Log($"[OnChainRaceManager] Joined race on-chain! TX: {submitResponse.transaction_signature}");
                    return true;
                }

                HideSigningModal();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OnChainRaceManager] Error joining race on-chain: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// submit race result onchain.
        /// </summary>
        public static async Task<bool> SubmitResultOnChainAsync(
            string raceId,
            int finishTimeMs,
            int coinsCollected,
            string inputHash,
            Action<string, float> onProgress = null)
        {
            try
            {
                if (apiClient == null || authManager == null)
                {
                    Initialize();
                }

                if (!authManager.IsAuthenticated)
                {
                    Debug.LogError("[OnChainRaceManager] User not authenticated");
                    return false;
                }

                string walletAddress = authManager.WalletAddress;

                //build transaction
                onProgress?.Invoke("Building result submission...", 0.3f);
                var buildRequest = new BuildTransactionRequest
                {
                    instruction_type = "submit_result",
                    wallet_address = walletAddress,
                    race_id = raceId,
                    finish_time_ms = finishTimeMs,
                    coins_collected = coinsCollected,
                    input_hash = inputHash
                };

                BuildTransactionResponse buildResponse;
                try
                {
                    buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to build submit_result transaction: {ex.Message}");
                    onProgress?.Invoke("Failed to build transaction - race may not exist on-chain", 0f);
                    return false;
                }

                if (buildResponse == null || string.IsNullOrEmpty(buildResponse.transaction_bytes))
                {
                    Debug.LogError("[OnChainRaceManager] Invalid transaction response for submit_result");
                    onProgress?.Invoke("Race not found on-chain", 0f);
                    return false;
                }

                // Show transaction signing modal and sign transaction
                onProgress?.Invoke("Waiting for transaction approval...", 0.6f);
                bool userApproved = await ShowTransactionSigningModal(
                    "Submit Result Transaction",
                    "Please approve the transaction to submit your race result on-chain.",
                    onProgress
                );

                if (!userApproved)
                {
                    Debug.LogWarning("[OnChainRaceManager] User rejected transaction signing.");
                    return false;
                }

                onProgress?.Invoke("Signing transaction...", 0.65f);
                string signedTransaction = await authManager.SignTransaction(buildResponse.transaction_bytes);
                if (string.IsNullOrEmpty(signedTransaction))
                {
                    HideSigningModal();
                    return false;
                }

                //submit transaction with result data for database storage
                onProgress?.Invoke("Submitting result...", 0.8f);
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "submit_result",
                    race_id = raceId,
                    // Include result data for backend to store in database
                    wallet_address = walletAddress,
                    finish_time_ms = finishTimeMs,
                    coins_collected = coinsCollected,
                    input_hash = inputHash
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                if (submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    HideSigningModal();
                    onProgress?.Invoke("Result submitted successfully!", 1f);
                    Debug.Log($"[OnChainRaceManager] Result submitted on-chain! TX: {submitResponse.transaction_signature}");
                    return true;
                }

                HideSigningModal();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OnChainRaceManager] Error submitting result on-chain: {ex.Message}");
                return false;
            }
        }

        private static TransactionSigningModal GetSigningModal()
        {
            // Find existing modal in scene
            TransactionSigningModal modal = UnityEngine.Object.FindAnyObjectByType<TransactionSigningModal>();
            if (modal == null)
            {
                Debug.LogWarning("[OnChainRaceManager] TransactionSigningModal not found in scene. Transaction signing will proceed without UI.");
            }
            return modal;
        }

        private static void HideSigningModal()
        {
            var modal = GetSigningModal();
            if (modal != null)
            {
                modal.HideModal();
            }
        }

        /// <summary>
        /// Claim prize for a race (winner only)
        /// First settles the race on-chain, then claims the prize.
        /// </summary>
        public static async Task<bool> ClaimPrizeOnChainAsync(
            string raceId,
            Action<string, float> onProgress = null)
        {
            try
            {
                if (apiClient == null || authManager == null)
                {
                    Initialize();
                }

                if (!authManager.IsAuthenticated)
                {
                    Debug.LogError("[OnChainRaceManager] User not authenticated");
                    return false;
                }

                string walletAddress = authManager.WalletAddress;

                // === Step 1: Settle the race on-chain ===
                onProgress?.Invoke("Settling race on-chain...", 0.1f);
                var settleBuildRequest = new BuildTransactionRequest
                {
                    instruction_type = "settle_race",
                    wallet_address = walletAddress,
                    race_id = raceId
                };

                BuildTransactionResponse settleBuildResponse;
                try
                {
                    settleBuildResponse = await apiClient.BuildTransactionAsync(settleBuildRequest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to build settle_race transaction: {ex.Message}");
                    onProgress?.Invoke("Failed to build settle transaction", 0f);
                    return false;
                }

                if (settleBuildResponse == null || string.IsNullOrEmpty(settleBuildResponse.transaction_bytes))
                {
                    Debug.LogError("[OnChainRaceManager] Invalid transaction response for settle_race");
                    onProgress?.Invoke("Failed to build settle transaction", 0f);
                    return false;
                }

                // Sign settle_race
                onProgress?.Invoke("Signing settle transaction...", 0.2f);
                string signedSettle;
                try
                {
                    signedSettle = await authManager.SignTransaction(settleBuildResponse.transaction_bytes);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to sign settle_race: {ex.Message}");
                    onProgress?.Invoke("Failed to sign settle transaction", 0f);
                    return false;
                }

                if (string.IsNullOrEmpty(signedSettle))
                {
                    Debug.LogError("[OnChainRaceManager] settle_race signing returned empty");
                    return false;
                }

                // Submit settle_race
                onProgress?.Invoke("Submitting settle transaction...", 0.3f);
                var settleSubmitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedSettle,
                    instruction_type = "settle_race",
                    race_id = raceId,
                    wallet_address = walletAddress
                };

                SubmitTransactionResponse settleSubmitResponse;
                try
                {
                    settleSubmitResponse = await apiClient.SubmitTransactionAsync(settleSubmitRequest);
                }
                catch (Exception ex)
                {
                    // settle_race may already have been called  continue to claim_prize
                    Debug.LogWarning($"[OnChainRaceManager] settle_race submit failed (may already be settled): {ex.Message}");
                }

                Debug.Log($"[OnChainRaceManager] Race settled on-chain (or was already settled)");

                // === Step 2: Claim the prize ===
                onProgress?.Invoke("Building claim prize transaction...", 0.5f);
                var buildRequest = new BuildTransactionRequest
                {
                    instruction_type = "claim_prize",
                    wallet_address = walletAddress,
                    race_id = raceId
                };

                BuildTransactionResponse buildResponse;
                try
                {
                    buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OnChainRaceManager] Failed to build claim_prize transaction: {ex.Message}");
                    onProgress?.Invoke("Failed to build claim transaction", 0f);
                    return false;
                }

                if (buildResponse == null || string.IsNullOrEmpty(buildResponse.transaction_bytes))
                {
                    Debug.LogError("[OnChainRaceManager] Invalid transaction response for claim_prize");
                    onProgress?.Invoke("Race not found on-chain", 0f);
                    return false;
                }

                // Show transaction signing modal for prize claiming
                onProgress?.Invoke("Waiting for transaction approval...", 0.6f);
                bool userApproved = await ShowTransactionSigningModal(
                    "Claim Prize Transaction",
                    "Please approve the transaction to claim your prize.",
                    onProgress
                );

                if (!userApproved)
                {
                    Debug.LogWarning("[OnChainRaceManager] User rejected transaction signing.");
                    return false;
                }

                onProgress?.Invoke("Signing transaction...", 0.7f);
                string signedTransaction = await authManager.SignTransaction(buildResponse.transaction_bytes);
                if (string.IsNullOrEmpty(signedTransaction))
                {
                    HideSigningModal();
                    return false;
                }

                // Submit claim_prize
                onProgress?.Invoke("Submitting claim transaction...", 0.85f);
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "claim_prize",
                    race_id = raceId
                };

                var submitResponse = await apiClient.SubmitTransactionAsync(submitRequest);
                if (submitResponse != null && !string.IsNullOrEmpty(submitResponse.transaction_signature))
                {
                    HideSigningModal();
                    onProgress?.Invoke("Prize claimed successfully!", 1f);
                    Debug.Log($"[OnChainRaceManager] Prize claimed on-chain! TX: {submitResponse.transaction_signature}");
                    return true;
                }

                HideSigningModal();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OnChainRaceManager] Error claiming prize on-chain: {ex.Message}");
                return false;
            }
        }

        private static readonly int SIGNING_MODAL_TIMEOUT_MS = 60000;

        private static async Task<bool> ShowTransactionSigningModal(string title, string description, Action<string, float> onProgressUpdate)
        {
            if (AuthenticationData.IsMWAWallet)
            {
                Debug.Log("[OnChainRaceManager] MWA wallet detected - skipping in-game modal (wallet will show bottom sheet)");
                return true;
            }

            var modal = GetSigningModal();
            if (modal == null)
            {
                Debug.LogWarning("[OnChainRaceManager] No TransactionSigningModal found. Proceeding without user confirmation.");
                return true;
            }

            bool userDecision = false;
            bool decisionMade = false;

            modal.ShowModal(title, description, (approved) =>
            {
                userDecision = approved;
                decisionMade = true;
            });

            var decisionTask = Task.Run(async () =>
            {
                while (!decisionMade)
                {
                    await Task.Delay(100);
                }
                return userDecision;
            });

            var timeoutTask = Task.Delay(SIGNING_MODAL_TIMEOUT_MS);
            var completed = await Task.WhenAny(decisionTask, timeoutTask);

            if (completed == timeoutTask)
            {
                Debug.LogWarning("[OnChainRaceManager] Transaction signing modal timed out after 60 seconds");
                HideSigningModal();
                return false;
            }

            bool result = await decisionTask;

            if (result)
            {
                modal.SetLoading(true, "Signing...");
            }
            else
            {
                modal.HideModal();
            }

            return result;
        }
    }
}

