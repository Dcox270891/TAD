using System.Collections;
using TAD.Progression;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Adds blocking acknowledgements and short, non-blocking messages to a reusable
    /// <see cref="ModalOverlay"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class ModalOverlayMessage : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Text messageText;
        [SerializeField] private UnityEngine.UI.Button acknowledgeButton;

        private CanvasGroup canvasGroup;
        private bool isSubscribed;
        private bool isTransientMessageVisible;
        private bool acknowledgeButtonWasActive;
        private Coroutine transientMessageCoroutine;

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
            StopTransientMessage();
            Unsubscribe();
        }

        /// <summary>Displays a blocking message until the player acknowledges it.</summary>
        public void ShowMessage(string message)
        {
            ResolveReferences();
            StopTransientMessage();
            SetMessageText(message);
            modalOverlay?.Show();
        }

        /// <summary>
        /// Displays a fully opaque message for a fixed real-time duration without pausing gameplay or
        /// intercepting pointer input. Repeated calls restart the duration with the newest message.
        /// </summary>
        public void ShowTransientMessage(string message, float durationSeconds)
        {
            if (Application.isPlaying && !PlayerSettingsService.Instance.AreNotificationsEnabled)
            {
                return;
            }

            ResolveReferences();
            StopTransientMessage();
            SetMessageText(message);

            // Ensure a previous acknowledgement message cannot keep the global modal state active.
            modalOverlay?.Hide();
            if (canvasGroup == null)
            {
                return;
            }

            acknowledgeButtonWasActive = acknowledgeButton != null && acknowledgeButton.gameObject.activeSelf;
            if (acknowledgeButton != null)
            {
                acknowledgeButton.gameObject.SetActive(false);
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            isTransientMessageVisible = true;
            transientMessageCoroutine = StartCoroutine(HideTransientMessageAfterDelay(Mathf.Max(0f, durationSeconds)));
        }

        /// <summary>Completes the current modal choice and restores gameplay input.</summary>
        public void CompleteChoice()
        {
            if (isTransientMessageVisible)
            {
                StopTransientMessage();
                return;
            }

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
            canvasGroup ??= GetComponent<CanvasGroup>();

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

        private void SetMessageText(string message)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }
        }

        private IEnumerator HideTransientMessageAfterDelay(float durationSeconds)
        {
            yield return new WaitForSecondsRealtime(durationSeconds);
            transientMessageCoroutine = null;
            StopTransientMessage();
        }

        private void StopTransientMessage()
        {
            if (transientMessageCoroutine != null)
            {
                StopCoroutine(transientMessageCoroutine);
                transientMessageCoroutine = null;
            }

            if (!isTransientMessageVisible)
            {
                return;
            }

            isTransientMessageVisible = false;
            if (acknowledgeButton != null)
            {
                acknowledgeButton.gameObject.SetActive(acknowledgeButtonWasActive);
            }

            modalOverlay?.Hide();
        }
    }
}
