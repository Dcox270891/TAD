using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    /// <summary>
    /// Reusable Main Menu modal for buttons whose supporting game systems are planned but not available yet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class NotImplementedOverlay : MonoBehaviour
    {
        private const string DefaultMessage = "This has not yet been implemented.";

        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Button closeButton;
        [SerializeField] private UnityEngine.UI.Text titleText;
        [SerializeField] private UnityEngine.UI.Text messageText;

        private GameObject selectionAfterClose;
        private bool isSubscribed;

        public bool IsVisible => modalOverlay != null && modalOverlay.IsVisible;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            selectionAfterClose = null;
        }

        /// <summary>Shows the standard planned-feature message and restores focus to the invoking control when closed.</summary>
        public bool Show(GameObject returnSelectionTarget, string title)
        {
            ResolveReferences();
            if (modalOverlay == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            selectionAfterClose = returnSelectionTarget;
            if (titleText != null)
            {
                titleText.text = string.IsNullOrWhiteSpace(title) ? "NOT IMPLEMENTED" : title;
            }

            if (messageText != null)
            {
                messageText.text = DefaultMessage;
            }

            modalOverlay.Show();
            SelectCloseButton();
            return true;
        }

        /// <summary>Closes the notice and returns controller focus to the control that opened it.</summary>
        public void Close()
        {
            if (modalOverlay == null || !modalOverlay.IsVisible)
            {
                return;
            }

            var selectionTarget = selectionAfterClose;
            selectionAfterClose = null;
            modalOverlay.Hide();

            if (EventSystem.current != null && selectionTarget != null && selectionTarget.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(selectionTarget);
            }
        }

        /// <summary>Assigns Main Menu-owned controls without registering listeners in the Editor.</summary>
        public void Configure(
            ModalOverlay overlay,
            UnityEngine.UI.Button close,
            UnityEngine.UI.Text title,
            UnityEngine.UI.Text message)
        {
            Unsubscribe();
            modalOverlay = overlay;
            closeButton = close;
            titleText = title;
            messageText = message;

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            ResolveReferences();
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }

            isSubscribed = false;
        }

        private void SelectCloseButton()
        {
            if (EventSystem.current != null && closeButton != null)
            {
                EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
            }
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();

            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.gameObject.name == "CloseButton")
                {
                    closeButton ??= button;
                    break;
                }
            }

            foreach (var text in GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                switch (text.gameObject.name)
                {
                    case "TitleText":
                        titleText ??= text;
                        break;
                    case "MessageText":
                        messageText ??= text;
                        break;
                }
            }
        }
    }
}