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
                    race_id = buildResponse.race_id
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

                var buildResponse = await apiClient.BuildTransactionAsync(buildRequest);
                if (buildResponse == null)
                {
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

                //submit transaction
                onProgress?.Invoke("Submitting result...", 0.8f);
                var submitRequest = new SubmitTransactionRequest
                {
                    signed_transaction_bytes = signedTransaction,
                    instruction_type = "submit_result",
                    race_id = raceId
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

                // Build transaction
                onProgress?.Invoke("Building claim prize transaction...", 0.3f);
                var buildRequest = new BuildTransactionRequest
                {
                    instruction_type = "claim_prize",
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
                    "Claim Prize Transaction",
                    "Please approve the transaction to claim your prize.",
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

        private static async Task<bool> ShowTransactionSigningModal(string title, string description, Action<string, float> onProgressUpdate)
        {
            var modal = GetSigningModal();
            if (modal == null)
            {
                // No modal found, proceed without UI (for testing)
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

            // Wait for user decision
            while (!decisionMade)
            {
                await Task.Delay(100);
            }

            if (userDecision)
            {
                modal.SetLoading(true, "Signing...");
            }
            else
            {
                modal.HideModal();
            }

            return userDecision;
        }
    }
}

