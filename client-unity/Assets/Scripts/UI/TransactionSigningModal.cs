using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Solracer.UI
{
    /// <summary>
    /// Modal UI for transaction signing with Privy
    /// </summary>
    public class TransactionSigningModal : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Main modal panel GameObject")]
        [SerializeField] private GameObject modalPanel;

        [Tooltip("Modal title text")]
        [SerializeField] private TextMeshProUGUI titleText;

        [Tooltip("Modal description/instruction text")]
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Tooltip("Approve/Sign button")]
        [SerializeField] private Button approveButton;

        [Tooltip("Reject/Cancel button")]
        [SerializeField] private Button rejectButton;

        [Tooltip("Loading indicator GameObject")]
        [SerializeField] private GameObject loadingIndicator;

        [Tooltip("Loading text (optional)")]
        [SerializeField] private TextMeshProUGUI loadingText;

        private System.Action<bool> onUserDecision;

        private void Start()
        {
            if (approveButton != null)
                approveButton.onClick.AddListener(OnApproveClicked);
            
            if (rejectButton != null)
                rejectButton.onClick.AddListener(OnRejectClicked);

            HideModal();
        }

        /// <summary>
        /// Show the transaction signing modal
        /// </summary>
        public void ShowModal(string title, string description, System.Action<bool> callback)
        {
            onUserDecision = callback;
            
            if (titleText != null)
                titleText.text = title ?? "Sign Transaction";
            
            if (descriptionText != null)
                descriptionText.text = description ?? "Please approve this transaction in your wallet.";

            if (modalPanel != null)
                modalPanel.SetActive(true);

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            if (approveButton != null)
                approveButton.interactable = true;

            if (rejectButton != null)
                rejectButton.interactable = true;
        }

        /// <summary>
        /// Hide the modal
        /// </summary>
        public void HideModal()
        {
            if (modalPanel != null)
                modalPanel.SetActive(false);
        }

        /// <summary>
        /// Called when user clicks Approve button
        /// </summary>
        private void OnApproveClicked()
        {
            if (loadingIndicator != null)
                loadingIndicator.SetActive(true);

            if (loadingText != null)
                loadingText.text = "Signing transaction...";

            if (approveButton != null)
                approveButton.interactable = false;

            if (rejectButton != null)
                rejectButton.interactable = false;

            onUserDecision?.Invoke(true);
        }

        /// <summary>
        /// Called when user clicks Reject button
        /// </summary>
        private void OnRejectClicked()
        {
            HideModal();
            onUserDecision?.Invoke(false);
        }

        /// <summary>
        /// Set loading state
        /// </summary>
        public void SetLoading(bool isLoading, string message = null)
        {
            if (loadingIndicator != null)
                loadingIndicator.SetActive(isLoading);

            if (loadingText != null && !string.IsNullOrEmpty(message))
                loadingText.text = message;

            if (approveButton != null)
                approveButton.interactable = !isLoading;

            if (rejectButton != null)
                rejectButton.interactable = !isLoading;
        }

        /// <summary>
        /// Update loading message
        /// </summary>
        public void UpdateLoadingMessage(string message)
        {
            if (loadingText != null)
                loadingText.text = message;
        }
    }
}

