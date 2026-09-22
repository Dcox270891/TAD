using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Presents the Buy, confirm, and cancel controls for farmhand placement.
    /// It only forwards player intent; <see cref="FarmHandPlacementController"/> owns placement state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmHandPlacementUi : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button buyButton;
        [SerializeField] private UnityEngine.UI.Button confirmButton;
        [SerializeField] private UnityEngine.UI.Button cancelButton;

        private FarmHandPlacementController placementController;
        private bool isSubscribed;

        private void Awake()
        {
            ResolveReferences();
            SetPlacementActive(false);
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>Connects this view to the controller that owns farmhand placement.</summary>
        public void Bind(FarmHandPlacementController controller)
        {
            placementController = controller;
            SetPlacementActive(controller != null && controller.IsPlacementActive);
        }

        /// <summary>Shows Buy while idle, or the tick and cross buttons while a preview is active.</summary>
        public void SetPlacementActive(bool isActive)
        {
            if (buyButton != null)
            {
                buyButton.gameObject.SetActive(!isActive);
            }

            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(isActive);
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(isActive);
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            ResolveReferences();
            if (buyButton != null)
            {
                buyButton.onClick.AddListener(HandleBuyPressed);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmPressed);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(HandleCancelPressed);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyPressed);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmPressed);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(HandleCancelPressed);
            }

            isSubscribed = false;
        }

        private void ResolveReferences()
        {
            if (buyButton != null && confirmButton != null && cancelButton != null)
            {
                return;
            }

            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                switch (button.gameObject.name)
                {
                    case "BuyButton":
                        buyButton ??= button;
                        break;
                    case "ConfirmButton":
                        confirmButton ??= button;
                        break;
                    case "CancelButton":
                        cancelButton ??= button;
                        break;
                }
            }
        }

        private void HandleBuyPressed()
        {
            placementController?.BeginPlacement();
        }

        private void HandleConfirmPressed()
        {
            placementController?.ConfirmPlacement();
        }

        private void HandleCancelPressed()
        {
            placementController?.CancelPlacement();
        }
    }
}
