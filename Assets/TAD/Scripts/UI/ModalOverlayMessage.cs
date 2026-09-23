using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Adds a message and acknowledgement action to a reusable <see cref="ModalOverlay"/>.
    /// The overlay remains responsible for blocking input and pausing gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class ModalOverlayMessage : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Text messageText;
        [SerializeField] private UnityEngine.UI.Button acknowledgeButton;

        private bool isSubscribed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>Displays a blocking message until the player acknowledges it.</summary>
        public void ShowMessage(string message)
        {
            ResolveReferences();
            if (messageText != null)
            {
                messageText.text = message;
            }

            modalOverlay?.Show();
        }

        /// <summary>Completes the current modal choice and restores gameplay input.</summary>
        public void CompleteChoice()
        {
            modalOverlay?.CompleteChoice();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            ResolveReferences();
            if (acknowledgeButton != null)
            {
                acknowledgeButton.onClick.AddListener(CompleteChoice);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (acknowledgeButton != null)
            {
                acknowledgeButton.onClick.RemoveListener(CompleteChoice);
            }

            isSubscribed = false;
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();

            if (messageText == null)
            {
                foreach (var text in GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (text.gameObject.name == "MessageText")
                    {
                        messageText = text;
                        break;
                    }
                }
            }

            if (acknowledgeButton == null)
            {
                foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    if (button.gameObject.name == "AcknowledgeButton")
                    {
                        acknowledgeButton = button;
                        break;
                    }
                }
            }
        }
    }
}