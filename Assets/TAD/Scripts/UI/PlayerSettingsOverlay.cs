using System;
using System.Collections.Generic;
using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TAD.UI
{
    /// <summary>
    /// Runtime-created Main Menu settings screen. It reads and writes the persistent player profile
    /// through <see cref="PlayerSettingsService"/>, so settings also take effect in loaded levels.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSettingsOverlay : MonoBehaviour
    {
        private const string OverlayName = "PlayerSettingsOverlay";
        private const float PanelWidth = 940f;
        private const float PanelHeight = 700f;
        private const float RowHeight = 58f;

        private readonly List<UnityEngine.UI.Button> tabButtons = new();

        private Transform contentRoot;
        private UnityEngine.UI.Button buttonTemplate;
        private UnityEngine.UI.Button firstTabButton;
        private GameObject selectionAfterClose;
        private Font font;
        private SettingsPage activePage;

        private enum SettingsPage
        {
            Gameplay,
            Audio,
            Notifications,
            Video,
            Controls
        }

        /// <summary>Creates or reopens the settings panel above the supplied Main Menu canvas.</summary>
        public static PlayerSettingsOverlay Show(
            Transform canvasRoot,
            UnityEngine.UI.Button template,
            GameObject returnSelectionTarget)
        {
            if (canvasRoot == null)
            {
                return null;
            }

            var existingTransform = canvasRoot.Find(OverlayName);
            var existing = existingTransform != null
                ? existingTransform.GetComponent<PlayerSettingsOverlay>()
                : null;
            if (existing != null)
            {
                existing.Configure(template, returnSelectionTarget);
                existing.gameObject.SetActive(true);
                existing.transform.SetAsLastSibling();
                existing.ShowPage(SettingsPage.Gameplay, selectFirstControl: true);
                return existing;
            }

            var root = new GameObject(
                OverlayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(PlayerSettingsOverlay));
            root.layer = canvasRoot.gameObject.layer;

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(canvasRoot, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var backdrop = root.GetComponent<UnityEngine.UI.Image>();
            backdrop.color = new Color(0.015f, 0.035f, 0.06f, 0.96f);
            backdrop.raycastTarget = true;

            var overlay = root.GetComponent<PlayerSettingsOverlay>();
            overlay.Configure(template, returnSelectionTarget);
            overlay.Build();
            overlay.ShowPage(SettingsPage.Gameplay, selectFirstControl: true);
            root.transform.SetAsLastSibling();
            return overlay;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void Configure(UnityEngine.UI.Button template, GameObject returnSelectionTarget)
        {
            buttonTemplate = template;
            selectionAfterClose = returnSelectionTarget;
            font = ResolveFont(transform.parent, template);
        }

        private void Build()
        {
            var panel = new GameObject(
                "SettingsPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.layer = gameObject.layer;
            panel.transform.SetParent(transform, false);

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0.075f, 0.16f, 0.21f, 1f);

            var panelLayout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(34, 34, 28, 30);
            panelLayout.spacing = 14f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            BuildHeader(panel.transform);
            BuildTabs(panel.transform);
            BuildContent(panel.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var header = new GameObject(
                "Header",
                typeof(RectTransform),
                typeof(UnityEngine.UI.HorizontalLayoutGroup),
                typeof(UnityEngine.UI.LayoutElement));
            header.layer = gameObject.layer;
            header.transform.SetParent(parent, false);
            header.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 70f;

            var layout = header.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var backButton = CreateButton(header.transform, "BackButton", "BACK", 150f, 62f);
            if (backButton != null)
            {
                backButton.onClick.AddListener(Close);
            }

            var title = CreateText(
                header.transform,
                "TitleText",
                "PLAYER SETTINGS",
                34,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white,
                preferredHeight: 62f);
            title.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

            CreateText(
                header.transform,
                "SavedLabel",
                "SAVES AUTOMATICALLY",
                15,
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                new Color(0.68f, 0.86f, 0.72f, 1f),
                preferredHeight: 62f,
                preferredWidth: 190f);
        }

        private void BuildTabs(Transform parent)
        {
            var tabs = new GameObject(
                "Tabs",
                typeof(RectTransform),
                typeof(UnityEngine.UI.HorizontalLayoutGroup),
                typeof(UnityEngine.UI.LayoutElement));
            tabs.layer = gameObject.layer;
            tabs.transform.SetParent(parent, false);
            tabs.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 58f;

            var layout = tabs.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            AddTab(tabs.transform, SettingsPage.Gameplay, "GAMEPLAY");
            AddTab(tabs.transform, SettingsPage.Audio, "AUDIO");
            AddTab(tabs.transform, SettingsPage.Notifications, "NOTIFICATIONS");
            AddTab(tabs.transform, SettingsPage.Video, "VIDEO");
            AddTab(tabs.transform, SettingsPage.Controls, "CONTROLS");
        }

        private void BuildContent(Transform parent)
        {
            var contentPanel = new GameObject(
                "ContentPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.LayoutElement));
            contentPanel.layer = gameObject.layer;
            contentPanel.transform.SetParent(parent, false);

            var image = contentPanel.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.035f, 0.085f, 0.12f, 0.98f);
            image.raycastTarget = false;

            var element = contentPanel.GetComponent<UnityEngine.UI.LayoutElement>();
            element.flexibleHeight = 1f;
            element.minHeight = 440f;

            var content = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(UnityEngine.UI.VerticalLayoutGroup));
            content.layer = gameObject.layer;
            content.transform.SetParent(contentPanel.transform, false);

            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(30f, 26f);
            contentRect.offsetMax = new Vector2(-30f, -26f);

            var layout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            contentRoot = content.transform;
        }

        private void AddTab(Transform parent, SettingsPage page, string label)
        {
            var button = CreateButton(parent, $"{page}Tab", label, 0f, 52f);
            if (button == null)
            {
                return;
            }

            button.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            button.onClick.AddListener(() => ShowPage(page, selectFirstControl: true));
            tabButtons.Add(button);
            firstTabButton ??= button;
        }

        private void ShowPage(SettingsPage page, bool selectFirstControl)
        {
            if (contentRoot == null)
            {
                return;
            }

            activePage = page;
            UpdateTabAppearance();
            ClearContent();

            UnityEngine.UI.Selectable firstControl = page switch
            {
                SettingsPage.Gameplay => BuildGameplayPage(),
                SettingsPage.Audio => BuildAudioPage(),
                SettingsPage.Notifications => BuildNotificationsPage(),
                SettingsPage.Video => BuildVideoPage(),
                SettingsPage.Controls => BuildControlsPage(),
                _ => null
            };

            if (selectFirstControl)
            {
                Select(firstControl != null ? firstControl.gameObject : firstTabButton != null ? firstTabButton.gameObject : null);
            }
        }

        private UnityEngine.UI.Selectable BuildGameplayPage()
        {
            CreatePageHeading("GAMEPLAY DISPLAY", "Choose which combat information is visible while you play.");
            var settings = PlayerSettingsService.Instance;
            var firstToggle = CreateToggleRow(
                "Show attacker health bars",
                "Displays the health bar above each animal.",
                settings.Profile.showAttackerHealthBars,
                settings.SetAttackerHealthBarsVisible);
            CreateToggleRow(
                "Show defender experience",
                "Displays each farmhand's level and experience bar.",
                settings.Profile.showDefenderExperience,
                settings.SetDefenderExperienceVisible);
            return firstToggle;
        }

        private UnityEngine.UI.Selectable BuildAudioPage()
        {
            CreatePageHeading("AUDIO", "All sliders save immediately. Master controls the complete game mix.");
            var settings = PlayerSettingsService.Instance;
            var firstSlider = CreateSliderRow("Master volume", settings.Profile.masterVolume, settings.SetMasterVolume);
            CreateSliderRow("Attacker volume", settings.Profile.attackerVolume, settings.SetAttackerVolume);
            CreateSliderRow("Defender volume", settings.Profile.defenderVolume, settings.SetDefenderVolume);
            CreateSliderRow("Projectile volume", settings.Profile.projectileVolume, settings.SetProjectileVolume);
            CreateSliderRow("Ambient game volume", settings.Profile.ambientGameVolume, settings.SetAmbientGameVolume);
            return firstSlider;
        }

        private UnityEngine.UI.Selectable BuildNotificationsPage()
        {
            CreatePageHeading("NOTIFICATIONS", "Non-critical in-game notices can be enabled or hidden here.");
            var settings = PlayerSettingsService.Instance;
            return CreateToggleRow(
                "Enable in-game notifications",
                "Important confirmation choices remain visible.",
                settings.Profile.notificationsEnabled,
                settings.SetNotificationsEnabled);
        }

        private UnityEngine.UI.Selectable BuildVideoPage()
        {
            CreatePageHeading("VIDEO", "Video quality applies immediately and is restored the next time TAD starts.");
            var settings = PlayerSettingsService.Instance;
            var qualityNames = QualitySettings.names;
            if (qualityNames == null || qualityNames.Length == 0)
            {
                CreateText(
                    contentRoot,
                    "UnavailableLabel",
                    "This build has no configurable quality levels.",
                    21,
                    FontStyle.Normal,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    preferredHeight: 92f);
                return null;
            }

            var currentIndex = Mathf.Clamp(settings.Profile.videoQuality, 0, qualityNames.Length - 1);
            return CreateChoiceRow(
                "Video quality",
                qualityNames[currentIndex],
                () =>
                {
                    settings.SetVideoQuality((currentIndex - 1 + qualityNames.Length) % qualityNames.Length);
                    ShowPage(SettingsPage.Video, selectFirstControl: true);
                },
                () =>
                {
                    settings.SetVideoQuality((currentIndex + 1) % qualityNames.Length);
                    ShowPage(SettingsPage.Video, selectFirstControl: true);
                });
        }

        private UnityEngine.UI.Selectable BuildControlsPage()
        {
            CreatePageHeading("CONTROLS", "TAD detects the active platform and presents the relevant control guide.");
            CreateText(
                contentRoot,
                "ControlGuide",
                BuildControlGuide(),
                22,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                Color.white,
                preferredHeight: 244f);

            var settings = PlayerSettingsService.Instance;
            return CreateToggleRow(
                "Show control hints",
                "Keeps contextual placement and control reminders enabled.",
                settings.Profile.controlHintsEnabled,
                settings.SetControlHintsEnabled);
        }

        private void CreatePageHeading(string title, string description)
        {
            CreateText(
                contentRoot,
                "PageTitle",
                title,
                27,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Color(0.8f, 0.94f, 0.82f, 1f),
                preferredHeight: 44f);
            CreateText(
                contentRoot,
                "PageDescription",
                description,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.82f, 0.88f, 0.9f, 1f),
                preferredHeight: 48f);
        }

        private UnityEngine.UI.Toggle CreateToggleRow(
            string title,
            string description,
            bool initialValue,
            Action<bool> onValueChanged)
        {
            var row = CreateRow(title);
            var label = CreateText(
                row.transform,
                "Label",
                $"{title}\n<size=17><color=#BBD2D7>{description}</color></size>",
                22,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                preferredHeight: RowHeight);
            label.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

            var toggleObject = new GameObject(
                "Toggle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Toggle),
                typeof(UnityEngine.UI.LayoutElement));
            toggleObject.layer = gameObject.layer;
            toggleObject.transform.SetParent(row.transform, false);
            var toggleLayout = toggleObject.GetComponent<UnityEngine.UI.LayoutElement>();
            toggleLayout.preferredWidth = 64f;
            toggleLayout.preferredHeight = 40f;

            var toggle = toggleObject.GetComponent<UnityEngine.UI.Toggle>();
            var background = CreateImage(toggleObject.transform, "Background", new Color(0.08f, 0.12f, 0.16f, 1f));
            SetStretch(background.rectTransform, 0f);
            var outline = background.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0.66f, 0.82f, 0.72f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var checkmark = CreateImage(background.transform, "Checkmark", new Color(0.2f, 0.8f, 0.35f, 1f));
            SetStretch(checkmark.rectTransform, 8f);
            checkmark.raycastTarget = false;

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.SetIsOnWithoutNotify(initialValue);
            toggle.onValueChanged.AddListener(value => onValueChanged?.Invoke(value));
            return toggle;
        }

        private UnityEngine.UI.Slider CreateSliderRow(string title, float initialValue, Action<float> onValueChanged)
        {
            var row = CreateRow(title);
            var label = CreateText(
                row.transform,
                "Label",
                BuildVolumeLabel(title, initialValue),
                21,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                preferredHeight: RowHeight,
                preferredWidth: 280f);

            var sliderObject = new GameObject(
                "Slider",
                typeof(RectTransform),
                typeof(UnityEngine.UI.Slider),
                typeof(UnityEngine.UI.LayoutElement));
            sliderObject.layer = gameObject.layer;
            sliderObject.transform.SetParent(row.transform, false);
            var sliderLayout = sliderObject.GetComponent<UnityEngine.UI.LayoutElement>();
            sliderLayout.flexibleWidth = 1f;
            sliderLayout.preferredHeight = 42f;

            var slider = sliderObject.GetComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;

            var background = CreateImage(sliderObject.transform, "Background", new Color(0.07f, 0.1f, 0.14f, 1f));
            SetStretch(background.rectTransform, new Vector2(10f, 15f), new Vector2(-10f, -15f));

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.layer = gameObject.layer;
            fillArea.transform.SetParent(sliderObject.transform, false);
            SetStretch(fillArea.GetComponent<RectTransform>(), new Vector2(10f, 15f), new Vector2(-10f, -15f));
            var fill = CreateImage(fillArea.transform, "Fill", new Color(0.22f, 0.75f, 0.35f, 1f));
            SetStretch(fill.rectTransform, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.layer = gameObject.layer;
            handleArea.transform.SetParent(sliderObject.transform, false);
            SetStretch(handleArea.GetComponent<RectTransform>(), new Vector2(10f, 0f), new Vector2(-10f, 0f));
            var handle = CreateImage(handleArea.transform, "Handle", new Color(0.92f, 0.97f, 0.92f, 1f));
            handle.rectTransform.sizeDelta = new Vector2(26f, 42f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.SetValueWithoutNotify(Mathf.Clamp01(initialValue));
            slider.onValueChanged.AddListener(value =>
            {
                label.text = BuildVolumeLabel(title, value);
                onValueChanged?.Invoke(value);
            });
            return slider;
        }

        private UnityEngine.UI.Button CreateChoiceRow(string title, string value, Action previous, Action next)
        {
            var row = CreateRow(title);
            var label = CreateText(
                row.transform,
                "Label",
                title,
                22,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                Color.white,
                preferredHeight: RowHeight,
                preferredWidth: 245f);

            var previousButton = CreateButton(row.transform, "PreviousButton", "<", 64f, 46f);
            if (previousButton != null)
            {
                previousButton.onClick.AddListener(() => previous?.Invoke());
            }

            var valueText = CreateText(
                row.transform,
                "Value",
                value,
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(0.8f, 0.94f, 0.82f, 1f),
                preferredHeight: RowHeight,
                preferredWidth: 280f);

            var nextButton = CreateButton(row.transform, "NextButton", ">", 64f, 46f);
            if (nextButton != null)
            {
                nextButton.onClick.AddListener(() => next?.Invoke());
            }

            return previousButton ?? nextButton;
        }

        private GameObject CreateRow(string name)
        {
            var row = new GameObject(
                name + "Row",
                typeof(RectTransform),
                typeof(UnityEngine.UI.HorizontalLayoutGroup),
                typeof(UnityEngine.UI.LayoutElement));
            row.layer = gameObject.layer;
            row.transform.SetParent(contentRoot, false);
            row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = RowHeight;

            var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return row;
        }

        private UnityEngine.UI.Button CreateButton(
            Transform parent,
            string objectName,
            string label,
            float preferredWidth,
            float preferredHeight)
        {
            UnityEngine.UI.Button button;
            if (buttonTemplate != null)
            {
                var buttonObject = Instantiate(buttonTemplate.gameObject, parent);
                buttonObject.name = objectName;
                SetLayerRecursively(buttonObject, gameObject.layer);
                button = buttonObject.GetComponent<UnityEngine.UI.Button>();
                if (button == null)
                {
                    Destroy(buttonObject);
                    return null;
                }

                button.onClick.RemoveAllListeners();
                var text = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (text != null)
                {
                    text.text = label;
                }
            }
            else
            {
                var buttonObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.Button));
                buttonObject.layer = gameObject.layer;
                buttonObject.transform.SetParent(parent, false);
                var image = buttonObject.GetComponent<UnityEngine.UI.Image>();
                image.color = new Color(0.18f, 0.42f, 0.25f, 1f);
                button = buttonObject.GetComponent<UnityEngine.UI.Button>();
                button.targetGraphic = image;

                var text = CreateText(
                    buttonObject.transform,
                    "Label",
                    label,
                    22,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    Color.white,
                    preferredHeight: 0f);
                SetStretch(text.rectTransform, 0f);
                Destroy(text.GetComponent<UnityEngine.UI.LayoutElement>());
            }

            var element = button.GetComponent<UnityEngine.UI.LayoutElement>();
            if (element == null)
            {
                element = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            }

            element.preferredHeight = preferredHeight;
            if (preferredWidth > 0f)
            {
                element.preferredWidth = preferredWidth;
            }

            return button;
        }

        private UnityEngine.UI.Text CreateText(
            Transform parent,
            string objectName,
            string value,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color,
            float preferredHeight,
            float preferredWidth = -1f)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text),
                typeof(UnityEngine.UI.LayoutElement));
            textObject.layer = gameObject.layer;
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var element = textObject.GetComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = preferredHeight;
            if (preferredWidth >= 0f)
            {
                element.preferredWidth = preferredWidth;
            }

            return text;
        }

        private UnityEngine.UI.Image CreateImage(Transform parent, string objectName, Color color)
        {
            var imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
            imageObject.layer = gameObject.layer;
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            return image;
        }

        private void UpdateTabAppearance()
        {
            foreach (var button in tabButtons)
            {
                if (button == null)
                {
                    continue;
                }

                var selected = button.gameObject.name == $"{activePage}Tab";
                button.interactable = !selected;
                var image = button.targetGraphic as UnityEngine.UI.Image;
                if (image != null)
                {
                    image.color = selected
                        ? new Color(0.18f, 0.52f, 0.28f, 1f)
                        : new Color(0.12f, 0.28f, 0.2f, 1f);
                }
            }
        }

        private void ClearContent()
        {
            for (var index = contentRoot.childCount - 1; index >= 0; index--)
            {
                var child = contentRoot.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void Close()
        {
            PlayerSettingsService.Instance.Save();
            var returnTarget = selectionAfterClose;
            gameObject.SetActive(false);
            Select(returnTarget);
        }

        private static string BuildVolumeLabel(string title, float value)
        {
            return $"{title}: {Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
        }

        private static string BuildControlGuide()
        {
            if (Application.isMobilePlatform || Touchscreen.current != null)
            {
                return "TOUCH CONTROLS\n\n" +
                       "• Tap BUY FARMHAND to begin placement.\n" +
                       "• Drag the preview to choose its position.\n" +
                       "• Tap the tick to confirm or the cross to cancel.\n" +
                       "• Tap a farmhand to inspect its range, upgrades, and targeting.";
            }

            if (Gamepad.current != null)
            {
                return "CONTROLLER CONTROLS\n\n" +
                       "• Left stick moves a farmhand placement preview.\n" +
                       "• South button confirms the selected placement.\n" +
                       "• East button cancels placement or closes this screen.\n" +
                       "• Use the UI selection controls to navigate menu buttons.";
            }

            return "MOUSE & KEYBOARD CONTROLS\n\n" +
                   "• Click BUY FARMHAND, then move the cursor to position the preview.\n" +
                   "• Click the tick to confirm or the cross to cancel.\n" +
                   "• Click a farmhand to inspect its range, upgrades, and targeting.\n" +
                   "• Press Escape to close menus or pause when available.\n\n" +
                   "A connected controller automatically uses the controller guide.";
        }

        private static Font ResolveFont(Transform canvasRoot, UnityEngine.UI.Button template)
        {
            var templateText = template != null ? template.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
            if (templateText != null && templateText.font != null)
            {
                return templateText.font;
            }

            var canvasText = canvasRoot != null ? canvasRoot.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
            return canvasText != null && canvasText.font != null
                ? canvasText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static void SetStretch(RectTransform rectTransform, float inset)
        {
            SetStretch(rectTransform, new Vector2(inset, inset), new Vector2(-inset, -inset));
        }

        private static void SetStretch(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void Select(GameObject target)
        {
            if (target != null && target.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }
    }
}
