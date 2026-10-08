using System;
using System.Collections.Generic;
using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Main-menu account upgrade book. The page is runtime-built so every menu scene uses the
    /// same saved tree without hand-wiring a separate prefab for each upgrade category.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerUpgradesOverlay : MonoBehaviour
    {
        private const string OverlayName = "PlayerUpgradesOverlay";
        private const float PanelWidth = 980f;
        private const float PanelHeight = 700f;
        private static readonly Color InkBrown = new(0.24f, 0.105f, 0.03f, 1f);
        private static readonly Color Parchment = new(0.94f, 0.86f, 0.66f, 1f);
        private static readonly Color ActiveTab = new(0.57f, 0.34f, 0.16f, 1f);
        private static readonly Color InactiveTab = new(0.78f, 0.65f, 0.39f, 1f);

        private readonly List<Button> tabButtons = new();
        private Transform contentRoot;
        private RectTransform panelRect;
        private Button buttonTemplate;
        private Button pendingButton;
        private GameObject selectionAfterClose;
        private Font font;
        private PlayerUpgradeCategory activeCategory;

        /// <summary>Creates or reopens the account upgrade book over the main-menu canvas.</summary>
        public static PlayerUpgradesOverlay Show(
            Transform canvasRoot,
            Button template,
            GameObject returnSelectionTarget)
        {
            if (canvasRoot == null)
            {
                return null;
            }

            var existingTransform = canvasRoot.Find(OverlayName);
            var existing = existingTransform != null ? existingTransform.GetComponent<PlayerUpgradesOverlay>() : null;
            if (existing != null)
            {
                existing.Configure(template, returnSelectionTarget);
                existing.gameObject.SetActive(true);
                existing.transform.SetAsLastSibling();
                existing.ShowCategory(existing.activeCategory, selectFirst: true);
                return existing;
            }

            var root = new GameObject(
                OverlayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PlayerUpgradesOverlay));
            root.layer = canvasRoot.gameObject.layer;
            root.transform.SetParent(canvasRoot, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            var backdrop = root.GetComponent<Image>();
            backdrop.color = new Color(0.015f, 0.035f, 0.06f, 0.94f);
            backdrop.raycastTarget = true;

            var overlay = root.GetComponent<PlayerUpgradesOverlay>();
            overlay.Configure(template, returnSelectionTarget);
            overlay.Build();
            overlay.ShowCategory(PlayerUpgradeCategory.Attacker, selectFirst: true);
            FarmUiSkin.ApplyFromCanvas(canvasRoot, root.transform);
            root.transform.SetAsLastSibling();
            return overlay;
        }

        private void Configure(Button template, GameObject returnSelectionTarget)
        {
            buttonTemplate = template;
            selectionAfterClose = returnSelectionTarget;
            font = ResolveFont(transform.parent, template);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void Build()
        {
            var panel = CreateObject(
                "PlayerUpgradesPanel",
                transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = Parchment;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 28, 30);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            BuildHeader(panel.transform);
            BuildTabs(panel.transform);
            BuildScrollContent(panel.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var header = CreateObject(
                "Header",
                parent,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            header.GetComponent<LayoutElement>().preferredHeight = 86f;
            var layout = header.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var title = CreateText(header.transform, "Title", "PLAYER UPGRADES", 38, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft);
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var profile = PlayerSettingsService.Instance.Profile;
            var level = profile.playerLevel;
            var tier = PlayerUpgradeProgression.GetUnlockedChallengeTier(level);
            var summary = CreateText(
                header.transform,
                "LevelSummary",
                $"LV {level}/100\nATTACKER TIER {tier} UNLOCKED",
                17,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            summary.GetComponent<LayoutElement>().preferredWidth = 180f;

            pendingButton = CreateButton(header.transform, "PendingChoicesButton", "UPGRADES READY", 190f, 62f);
            pendingButton.onClick.AddListener(OpenNextPendingChoice);
            var close = CreateButton(header.transform, "CloseButton", "CLOSE", 120f, 62f);
            close.onClick.AddListener(Close);
        }

        private void BuildTabs(Transform parent)
        {
            var tabs = CreateObject(
                "Tabs",
                parent,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            tabs.GetComponent<LayoutElement>().preferredHeight = 56f;
            var layout = tabs.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            AddTab(tabs.transform, PlayerUpgradeCategory.Attacker, "ATTACKERS");
            AddTab(tabs.transform, PlayerUpgradeCategory.Defender, "DEFENDERS");
            AddTab(tabs.transform, PlayerUpgradeCategory.General, "GENERAL");
        }

        private void BuildScrollContent(Transform parent)
        {
            var contentPanel = CreateObject(
                "ContentPanel",
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect),
                typeof(LayoutElement));
            contentPanel.GetComponent<Image>().color = new Color(0.96f, 0.9f, 0.72f, 0.86f);
            contentPanel.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var viewport = CreateObject(
                "Viewport",
                contentPanel.transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, new Vector2(20f, 18f), new Vector2(-20f, -18f));
            viewport.GetComponent<Image>().color = Color.clear;

            var content = CreateObject(
                "Content",
                viewport.transform,
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = contentPanel.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            contentRoot = content.transform;
        }

        private void AddTab(Transform parent, PlayerUpgradeCategory category, string label)
        {
            var tab = CreateButton(parent, category + "Tab", label, 0f, 52f);
            tab.GetComponent<LayoutElement>().flexibleWidth = 1f;
            tab.onClick.AddListener(() => ShowCategory(category, selectFirst: true));
            tabButtons.Add(tab);
        }

        private void ShowCategory(PlayerUpgradeCategory category, bool selectFirst)
        {
            activeCategory = category;
            UpdatePendingButton();
            UpdateTabAppearance();
            ClearContent();
            if (contentRoot == null)
            {
                return;
            }

            var profile = PlayerSettingsService.Instance.Profile;
            var header = category switch
            {
                PlayerUpgradeCategory.Attacker => "ATTACKER UPGRADES — each rank applies to its animal archetype.",
                PlayerUpgradeCategory.Defender => "DEFENDER UPGRADES — each rank applies to Melee or Ranged weapons.",
                _ => "GENERAL UPGRADES — milestone rewards unlocked at Levels 20, 40, 60, 80 and 100."
            };
            CreateText(contentRoot, "PageHeading", header, 24, FontStyle.Bold, TextAnchor.MiddleCenter)
                .GetComponent<LayoutElement>().preferredHeight = 48f;

            string currentGroup = null;
            foreach (var definition in PlayerUpgradeProgression.AllDefinitions)
            {
                if (definition.Category != category)
                {
                    continue;
                }

                if (!string.Equals(currentGroup, definition.GroupName, StringComparison.Ordinal))
                {
                    currentGroup = definition.GroupName;
                    var group = CreateText(contentRoot, "Group_" + currentGroup, currentGroup.ToUpperInvariant(), 28, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft);
                    group.GetComponent<LayoutElement>().preferredHeight = 44f;
                }

                CreateUpgradeRow(definition, profile);
            }

            if (selectFirst && tabButtons.Count > 0 && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(tabButtons[(int)category].gameObject);
            }

            FarmUiSkin.ApplyFromCanvas(transform.parent, transform);
        }

        private void CreateUpgradeRow(PlayerUpgradeDefinition definition, PlayerProfileData profile)
        {
            var row = CreateObject(
                "Upgrade_" + definition.Id,
                contentRoot,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            row.GetComponent<Image>().color = new Color(0.84f, 0.75f, 0.53f, 0.65f);
            row.GetComponent<LayoutElement>().preferredHeight = 72f;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 8, 8);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var title = CreateText(row.transform, "Title", definition.Title, 22, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.GetComponent<LayoutElement>().preferredWidth = 210f;
            var description = CreateText(row.transform, "Description", definition.Description, 17, FontStyle.Normal, TextAnchor.MiddleLeft);
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var rank = PlayerUpgradeProgression.GetRank(profile, definition.Id);
            var state = definition.Category == PlayerUpgradeCategory.General
                ? rank > 0 ? "UNLOCKED" : "LOCKED"
                : $"RANK {rank}/{definition.MaxRank}";
            var rankText = CreateText(row.transform, "Rank", state, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            rankText.GetComponent<LayoutElement>().preferredWidth = 142f;
        }

        private void UpdatePendingButton()
        {
            if (pendingButton == null)
            {
                return;
            }

            var count = PlayerSettingsService.Instance.PendingPlayerUpgradeChoiceCount;
            pendingButton.interactable = count > 0;
            SetButtonText(pendingButton, count > 0 ? $"CHOOSE {count} READY" : "NO REWARDS READY");
        }

        private void UpdateTabAppearance()
        {
            for (var index = 0; index < tabButtons.Count; index++)
            {
                var button = tabButtons[index];
                if (button == null)
                {
                    continue;
                }

                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = (PlayerUpgradeCategory)index == activeCategory ? ActiveTab : InactiveTab;
                }
            }
        }

        private void OpenNextPendingChoice()
        {
            var profile = PlayerSettingsService.Instance.Profile;
            if (profile.pendingPlayerUpgradeChoices == null || profile.pendingPlayerUpgradeChoices.Count == 0)
            {
                return;
            }

            var earliestLevel = int.MaxValue;
            foreach (var choice in profile.pendingPlayerUpgradeChoices)
            {
                if (choice != null)
                {
                    earliestLevel = Mathf.Min(earliestLevel, choice.playerLevel);
                }
            }

            if (earliestLevel == int.MaxValue)
            {
                return;
            }

            PlayerUpgradeChoiceOverlay.Show(transform.parent, buttonTemplate, pendingButton.gameObject, earliestLevel, () =>
            {
                gameObject.SetActive(true);
                ShowCategory(activeCategory, selectFirst: false);
            });
        }

        private void ClearContent()
        {
            if (contentRoot == null)
            {
                return;
            }

            for (var index = contentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(contentRoot.GetChild(index).gameObject);
            }
        }

        private void Close()
        {
            gameObject.SetActive(false);
            if (selectionAfterClose != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectionAfterClose);
            }
        }

        private Text CreateText(Transform parent, string name, string value, int size, FontStyle style, TextAnchor alignment)
        {
            var item = CreateObject(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
            var text = item.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = InkBrown;
            text.text = value;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = size;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string name, string label, float width, float height)
        {
            var root = CreateObject(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            var image = root.GetComponent<Image>();
            image.color = InactiveTab;
            var button = root.GetComponent<Button>();
            button.targetGraphic = image;
            var layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = height;
            if (width > 0f)
            {
                layout.preferredWidth = width;
            }

            var text = CreateText(root.transform, "Label", label, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);
            return button;
        }

        private static void SetButtonText(Button button, string value)
        {
            var label = button != null ? button.GetComponentInChildren<Text>(true) : null;
            if (label != null)
            {
                label.text = value;
            }
        }

        private static GameObject CreateObject(string name, Transform parent, params Type[] components)
        {
            var item = new GameObject(name, components);
            item.layer = parent.gameObject.layer;
            item.transform.SetParent(parent, false);
            return item;
        }

        private static void Stretch(RectTransform rect, Vector2 insetMin, Vector2 insetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = insetMin;
            rect.offsetMax = insetMax;
        }

        private static Font ResolveFont(Transform canvasRoot, Button template)
        {
            var templateText = template != null ? template.GetComponentInChildren<Text>(true) : null;
            if (templateText != null && templateText.font != null)
            {
                return templateText.font;
            }

            var text = canvasRoot != null ? canvasRoot.GetComponentInChildren<Text>(true) : null;
            return text != null && text.font != null
                ? text.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
