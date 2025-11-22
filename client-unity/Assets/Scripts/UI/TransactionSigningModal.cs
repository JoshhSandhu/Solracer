using UnityEngine;
using UnityEngine.UI;

namespace Solracer.UI
{
    /// <summary>
    /// Simplified transaction signing modal
    /// The Competitive button is assigned as the approve button
    /// When clicked, it approves the transaction signing
    /// </summary>
    public class TransactionSigningModal : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Approve button (assign Competitive button here)")]
        [SerializeField] private Button approveButton;

        private System.Action<bool> onUserDecision;

        private void Start()
        {
            // Set up approve button listener
            if (approveButton != null)
            {
                approveButton.onClick.AddListener(OnApproveClicked);
            }
            else
            {
                Debug.LogWarning("TransactionSigningModal: Approve button not assigned!");
            }
        }

        /// <summary>
        /// Show the transaction signing modal
        /// Sets up callback - when approve button is clicked, it will be invoked
        /// </summary>
        public void ShowModal(string title, string description, System.Action<bool> callback)
        {
            onUserDecision = callback;
            
            // Enable the approve button if it exists
            if (approveButton != null)
            {
                approveButton.interactable = true;
            }
        }

        /// <summary>
        /// Hide the modal (disable approve button)
        /// </summary>
        public void HideModal()
        {
            if (approveButton != null)
            {
                approveButton.interactable = false;
            }
        }

        /// <summary>
        /// Called when approve button (Competitive button) is clicked
        /// </summary>
        private void OnApproveClicked()
        {
            if (approveButton != null)
            {
                approveButton.interactable = false;
            }

            // Invoke callback with approval
            onUserDecision?.Invoke(true);
        }

        /// <summary>
        /// Approve the transaction programmatically
        /// </summary>
        public void Approve()
        {
            OnApproveClicked();
        }

        /// <summary>
        /// Reject the transaction (optional - not used for Competitive button)
        /// </summary>
        public void Reject()
        {
            onUserDecision?.Invoke(false);
        }

        /// <summary>
        /// Set loading state
        /// </summary>
        public void SetLoading(bool isLoading, string message = null)
        {
            if (approveButton != null)
            {
                approveButton.interactable = !isLoading;
            }
        }

        /// <summary>
        /// Update loading message (no-op, kept for compatibility)
        /// </summary>
        public void UpdateLoadingMessage(string message)
        {
            // Loading message handled by calling component if needed
        }
    }
}



