using System;
using TAD.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Compact, runtime-owned Main Menu HUD for the persistent match-energy reserve. Keeping the
    /// widget outside MainMenuPanel means the menu's authored vertical layout remains unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEnergyMenuPanel : MonoBehaviour
    {
        private const string PanelName = "PlayerEnergyMenuPanel";

        [SerializeField] private Text energyText;
        [SerializeField] private Text timerText;
        [SerializeField] private Button informationButton;

        private Button buttonTemplate;
        private PlayerSettingsService settingsService;
        private bool isSubscribed;
        private float nextPresentationRefreshAt;

        /// <summary>Creates or restores the top-centre Main Menu energy display.</summary>
        public static PlayerEnergyMenuPanel Ensure(Transform canvasRoot, Button buttonTemplate)
        {
            if (canvasRoot == null)
            {
                return null;
            }

            var existingTransform = canvasRoot.Find(PanelName);
            var panel = existingTransform != null
                ? existingTransform.GetComponent<PlayerEnergyMenuPanel>()
                : null;

            if (panel == null)
            {
                var root = new GameObject(
                    PanelName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(VerticalLayoutGroup));
                // Keep the object inactive until it has inherited the canvas, template, and
                // parent font. This prevents Awake/OnEnable from styling it with a fallback.
                root.SetActive(false);
                root.layer = canvasRoot.gameObject.layer;
                root.transform.SetParent(canvasRoot, false);
                panel = root.AddComponent<PlayerEnergyMenuPanel>();
            }

            panel.buttonTemplate = buttonTemplate;
            panel.EnsureLayout();
            panel.gameObject.SetActive(true);
            SetLayerRecursively(panel.gameObject, canvasRoot.gameObject.layer);
            FarmUiSkin.ApplyFromCanvas(canvasRoot, panel.transform);
            panel.RefreshPresentation();
            return panel;
        }

        private void Awake()
        {
            // Layout is deliberately built by Ensure after a menu button/template has been
            // supplied. See the inactive construction path above.
        }

        private void OnEnable()
        {
            Subscribe();
            RefreshPresentation();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPresentationRefreshAt)
            {
                return;
            }

            RefreshPresentation();
        }

        private void Build()
        {
            var rect = transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -24f);
                rect.sizeDelta = new Vector2(360f, 104f);
            }

            var surface = GetComponent<Image>();
            if (surface != null)
            {
                surface.color = new Color(0.94f, 0.86f, 0.66f, 0.98f);
                surface.raycastTarget = false;
            }

            var layout = GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 62, 13, 10);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var font = ResolveFont(transform.parent, buttonTemplate);
            energyText = CreateText(
                transform,
                "EnergyText",
                font,
                28,
                FontStyle.Bold,
                44f,
                TextAnchor.MiddleLeft);
            timerText = CreateText(
                transform,
                "NextEnergyText",
                font,
                18,
                FontStyle.Bold,
                30f,
                TextAnchor.MiddleLeft);
            informationButton = CreateInformationButton(font);
        }

        private void EnsureLayout()
        {
            energyText ??= transform.Find("EnergyText")?.GetComponent<Text>();
            timerText ??= transform.Find("NextEnergyText")?.GetComponent<Text>();
            informationButton ??= transform.Find("EnergyInfoButton")?.GetComponent<Button>();

            if (energyText == null || timerText == null || informationButton == null)
            {
                foreach (Transform child in transform)
                {
                    Destroy(child.gameObject);
                }

                Build();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            EnsureLayout();
            if (informationButton != null)
            {
                informationButton.onClick.AddListener(OpenInformation);
            }

            settingsService ??= PlayerSettingsService.Instance;
            settingsService.EnergyChanged += HandleEnergyChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (informationButton != null)
            {
                informationButton.onClick.RemoveListener(OpenInformation);
            }

            if (settingsService != null)
            {
                settingsService.EnergyChanged -= HandleEnergyChanged;
                settingsService = null;
            }

            isSubscribed = false;
        }

        private void OpenInformation()
        {
            PlayerEnergyOverlay.ShowInformation(
                transform.parent,
                buttonTemplate,
                informationButton != null ? informationButton.gameObject : null);
        }

        private void HandleEnergyChanged(PlayerEnergySnapshot _)
        {
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            nextPresentationRefreshAt = Time.unscaledTime + 0.5f;
            if (energyText == null || timerText == null)
            {
                return;
            }

            var energy = PlayerSettingsService.Instance.PlayerEnergy;
            energyText.text = $"ENERGY  {energy.CurrentEnergy} / {energy.MaximumEnergy}";
            timerText.text = energy.ClockRollbackDetected
                ? "RECOVERY WAITS FOR DEVICE TIME"
                : energy.IsFull
                    ? "ENERGY FULL"
                    : $"NEXT ENERGY IN {FormatCountdown(energy.NextEnergyInMilliseconds)}";
        }

        internal static string FormatCountdown(long milliseconds)
        {
            var roundedUpSeconds = Math.Max(0L, (milliseconds + 999L) / 1000L);
            var duration = TimeSpan.FromSeconds(roundedUpSeconds);
            return duration.TotalHours >= 1d
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        private Button CreateInformationButton(Font font)
        {
            var buttonObject = new GameObject(
                "EnergyInfoButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.layer = gameObject.layer;
            buttonObject.transform.SetParent(transform, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-13f, 0f);
            buttonRect.sizeDelta = new Vector2(44f, 44f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.94f, 0.86f, 0.66f, 1f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var label = CreateText(
                buttonObject.transform,
                "Label",
                font,
                24,
                FontStyle.Bold,
                0f,
                TextAnchor.MiddleCenter);
            label.text = "?";
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Destroy(label.GetComponent<LayoutElement>());
            return button;
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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var layout = textObject.GetComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            return text;
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

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            if (target == null)
            {
                return;
            }

            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
