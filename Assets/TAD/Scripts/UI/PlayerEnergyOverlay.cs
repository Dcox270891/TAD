using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Reusable full-screen energy explanation and insufficient-energy modal. It deliberately
    /// contains a future-facing Get More Energy control without implementing any purchase flow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEnergyOverlay : MonoBehaviour
    {
        private const string OverlayName = "PlayerEnergyOverlay";

        private enum OverlayMode
        {
            Information,
            InsufficientEnergy
        }

        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button getMoreEnergyButton;
        [SerializeField] private Button closeButton;

        private OverlayMode mode;
        private GameObject selectionAfterClose;
        private bool showingFutureStoreNotice;
        private float nextPresentationRefreshAt;

        /// <summary>Shows the current reserve, next point timer, and time to a full reserve.</summary>
        public static PlayerEnergyOverlay ShowInformation(
            Transform canvasRoot,
            Button buttonTemplate,
            GameObject returnSelectionTarget)
        {
            return Show(canvasRoot, buttonTemplate, returnSelectionTarget, OverlayMode.Information);
        }

        /// <summary>Explains why a requested match cannot start and retains a future purchase hook.</summary>
        public static PlayerEnergyOverlay ShowInsufficientEnergy(
            Transform canvasRoot,
            Button buttonTemplate,
            GameObject returnSelectionTarget)
        {
            return Show(canvasRoot, buttonTemplate, returnSelectionTarget, OverlayMode.InsufficientEnergy);
        }

        private static PlayerEnergyOverlay Show(
            Transform canvasRoot,
            Button buttonTemplate,
            GameObject returnSelectionTarget,
            OverlayMode requestedMode)
        {
            if (canvasRoot == null)
            {
                return null;
            }

            var existingTransform = canvasRoot.Find(OverlayName);
            var overlay = existingTransform != null
                ? existingTransform.GetComponent<PlayerEnergyOverlay>()
                : null;
            if (overlay == null)
            {
                var root = new GameObject(
                    OverlayName,
                    typeof(RectTransform),
                    typeof(CanvasGroup),
                    typeof(ModalOverlay),
                    typeof(PlayerEnergyOverlay));
                root.layer = canvasRoot.gameObject.layer;
                root.transform.SetParent(canvasRoot, false);

                var rootRect = root.GetComponent<RectTransform>();
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
                overlay = root.GetComponent<PlayerEnergyOverlay>();
                overlay.Build(buttonTemplate);
            }

            overlay.Configure(buttonTemplate, returnSelectionTarget, requestedMode);
            overlay.gameObject.SetActive(true);
            overlay.modalOverlay?.Show();
            overlay.transform.SetAsLastSibling();
            FarmUiSkin.ApplyFromCanvas(canvasRoot, overlay.transform);
            return overlay;
        }

        private void Awake()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPresentationRefreshAt || showingFutureStoreNotice)
            {
                return;
            }

            RefreshPresentation();
        }

        private void Build(Button buttonTemplate)
        {
            var blocker = new GameObject(
                "Blocker",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            blocker.layer = gameObject.layer;
            blocker.transform.SetParent(transform, false);
            var blockerRect = blocker.GetComponent<RectTransform>();
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            var blockerImage = blocker.GetComponent<Image>();
            blockerImage.color = new Color(0.015f, 0.035f, 0.06f, 0.86f);
            blockerImage.raycastTarget = true;

            var panel = new GameObject(
                "EnergyPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            panel.layer = gameObject.layer;
            panel.transform.SetParent(transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 420f);
            panel.GetComponent<Image>().color = new Color(0.94f, 0.86f, 0.66f, 1f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 42, 38);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var font = ResolveFont(transform.parent, buttonTemplate);
            titleText = CreateText(panel.transform, "TitleText", font, 34, FontStyle.Bold, 56f, TextAnchor.MiddleCenter);
            bodyText = CreateText(panel.transform, "BodyText", font, 21, FontStyle.Normal, 190f, TextAnchor.UpperCenter);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;

            getMoreEnergyButton = CreateButton(
                panel.transform,
                buttonTemplate,
                "GetMoreEnergyButton",
                "GET MORE ENERGY",
                font,
                preferredHeight: 58f);
            if (getMoreEnergyButton != null)
            {
                getMoreEnergyButton.onClick.AddListener(ShowFutureEnergyStoreNotice);
            }

            closeButton = CreateButton(
                panel.transform,
                buttonTemplate,
                "CloseButton",
                "CLOSE",
                font,
                preferredHeight: 0f);
            if (closeButton != null)
            {
                var closeLayout = closeButton.GetComponent<LayoutElement>() ?? closeButton.gameObject.AddComponent<LayoutElement>();
                closeLayout.ignoreLayout = true;
                var closeRect = closeButton.transform as RectTransform;
                if (closeRect != null)
                {
                    closeRect.anchorMin = Vector2.one;
                    closeRect.anchorMax = Vector2.one;
                    closeRect.pivot = Vector2.one;
                    closeRect.anchoredPosition = new Vector2(-18f, -18f);
                    closeRect.sizeDelta = new Vector2(138f, 46f);
                }

                closeButton.onClick.AddListener(Close);
            }
        }

        private void Configure(Button buttonTemplate, GameObject returnSelectionTarget, OverlayMode requestedMode)
        {
            modalOverlay ??= GetComponent<ModalOverlay>();
            if (titleText == null || bodyText == null || getMoreEnergyButton == null || closeButton == null)
            {
                foreach (Transform child in transform)
                {
                    Destroy(child.gameObject);
                }

                Build(buttonTemplate);
            }

            selectionAfterClose = returnSelectionTarget;
            mode = requestedMode;
            showingFutureStoreNotice = false;
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            nextPresentationRefreshAt = Time.unscaledTime + 0.5f;
            if (titleText == null || bodyText == null)
            {
                return;
            }

            var energy = PlayerSettingsService.Instance.PlayerEnergy;
            if (showingFutureStoreNotice)
            {
                titleText.text = "GET MORE ENERGY";
                bodyText.text = "Energy purchases are not available in this test build yet.\n\n" +
                                $"Your reserve refills automatically: one energy every 6 minutes. " +
                                $"You currently have {energy.CurrentEnergy} / {energy.MaximumEnergy}.";
                return;
            }

            if (mode == OverlayMode.InsufficientEnergy)
            {
                titleText.text = "NOT ENOUGH ENERGY";
                bodyText.text = $"Starting a game costs {PlayerEnergyProgression.GameStartEnergyCost} energy.\n\n" +
                                $"You have {energy.CurrentEnergy} / {energy.MaximumEnergy}.\n" +
                                BuildTimingMessage(energy) +
                                "\n\nEnergy purchases will be available here in a future update.";
                return;
            }

            titleText.text = "YOUR ENERGY";
            bodyText.text = $"Energy: {energy.CurrentEnergy} / {energy.MaximumEnergy}\n\n" +
                            $"{BuildTimingMessage(energy)}\n\n" +
                            $"Starting a game costs {PlayerEnergyProgression.GameStartEnergyCost} energy. " +
                            "Energy refills automatically while you are away.";
        }

        private static string BuildTimingMessage(PlayerEnergySnapshot energy)
        {
            if (energy.ClockRollbackDetected)
            {
                return "Recovery is paused until the device clock catches up to its last recorded time.";
            }

            if (energy.IsFull)
            {
                return "Your energy is full.";
            }

            return $"Next energy in {PlayerEnergyMenuPanel.FormatCountdown(energy.NextEnergyInMilliseconds)}\n" +
                   $"Full in {PlayerEnergyMenuPanel.FormatCountdown(energy.FullRechargeInMilliseconds)}";
        }

        private void ShowFutureEnergyStoreNotice()
        {
            showingFutureStoreNotice = true;
            RefreshPresentation();
        }

        private void Close()
        {
            var returnTarget = selectionAfterClose;
            selectionAfterClose = null;
            modalOverlay?.Hide();
            gameObject.SetActive(false);
            if (returnTarget != null && returnTarget.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(returnTarget);
            }
        }

        private static Text CreateText(
            Transform parent,
            string objectName,
            Font font,
            int fontSize,
            FontStyle style,
            float preferredHeight,
            TextAnchor alignment)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text),
                typeof(LayoutElement));
            textObject.layer = parent.gameObject.layer;
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.24f, 0.105f, 0.03f, 1f);
            text.raycastTarget = false;

            var layout = textObject.GetComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            return text;
        }

        private static Button CreateButton(
            Transform parent,
            Button template,
            string objectName,
            string label,
            Font fallbackFont,
            float preferredHeight)
        {
            GameObject buttonObject;
            Button button;
            if (template != null)
            {
                buttonObject = Instantiate(template.gameObject, parent);
                buttonObject.name = objectName;
                buttonObject.layer = parent.gameObject.layer;
                button = buttonObject.GetComponent<Button>();
                if (button == null)
                {
                    Destroy(buttonObject);
                    return null;
                }

                button.onClick.RemoveAllListeners();
            }
            else
            {
                buttonObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button),
                    typeof(LayoutElement));
                buttonObject.layer = parent.gameObject.layer;
                buttonObject.transform.SetParent(parent, false);
                var image = buttonObject.GetComponent<Image>();
                image.color = new Color(0.94f, 0.86f, 0.66f, 1f);
                button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;

                var text = CreateText(buttonObject.transform, "Label", fallbackFont, 22, FontStyle.Bold, 0f, TextAnchor.MiddleCenter);
                var textRect = text.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                Destroy(text.GetComponent<LayoutElement>());
            }

            var buttonLabel = button.GetComponentInChildren<Text>(true);
            if (buttonLabel != null)
            {
                buttonLabel.text = label;
            }

            var layout = buttonObject.GetComponent<LayoutElement>() ?? buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            return button;
        }

        private static Font ResolveFont(Transform canvasRoot, Button template)
        {
            var templateText = template != null ? template.GetComponentInChildren<Text>(true) : null;
            if (templateText != null && templateText.font != null)
            {
                return templateText.font;
            }

            var canvasText = canvasRoot != null ? canvasRoot.GetComponentInChildren<Text>(true) : null;
            return canvasText != null && canvasText.font != null
                ? canvasText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    /// <summary>One paid launch that can be refunded exactly once if a scene fails to load.</summary>
    internal sealed class MatchStartEnergyCharge
    {
        private bool isRefundable = true;

        public void Refund()
        {
            if (!isRefundable)
            {
                return;
            }

            isRefundable = false;
            PlayerSettingsService.Instance.RefundEnergyForFailedNewGameStart();
        }
    }

    /// <summary>Centralizes user-facing energy checks so every new-match entry point behaves alike.</summary>
    internal static class MatchStartEnergyGate
    {
        public static bool TryPayForNewMatch(
            Transform owner,
            Button buttonTemplate,
            GameObject returnSelectionTarget,
            out MatchStartEnergyCharge charge)
        {
            if (PlayerSettingsService.Instance.TrySpendEnergyForNewGame(out _))
            {
                charge = new MatchStartEnergyCharge();
                return true;
            }

            charge = null;
            var canvas = owner != null ? owner.GetComponentInParent<Canvas>() : null;
            PlayerEnergyOverlay.ShowInsufficientEnergy(
                canvas != null ? canvas.transform : owner,
                buttonTemplate,
                returnSelectionTarget);
            return false;
        }
    }
}
