using System;
using System.Collections.Generic;
using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Mandatory account-level reward picker. It presents the persisted cards created when a
    /// player gains a level, so closing/reopening the game never changes a random offer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerUpgradeChoiceOverlay : MonoBehaviour
    {
        private const string OverlayName = "PlayerUpgradeChoiceOverlay";
        private static readonly Color InkBrown = new(0.24f, 0.105f, 0.03f, 1f);
        private static readonly Color Parchment = new(0.94f, 0.86f, 0.66f, 1f);
        private static readonly Color AttackerTint = new(0.79f, 0.35f, 0.19f, 1f);
        private static readonly Color DefenderTint = new(0.23f, 0.48f, 0.31f, 1f);
        private static readonly Color GeneralTint = new(0.28f, 0.46f, 0.69f, 1f);

        private readonly List<PendingPlayerUpgradeChoiceData> pendingChoices = new();

        private int playerLevel;
        private int activeChoiceIndex;
        private Action resolvedCallback;
        private GameObject selectionAfterClose;
        private Font font;
        private Transform cardsRoot;
        private Text titleText;
        private Text subtitleText;
        private Text progressText;

        /// <summary>Shows saved upgrade choices for a particular newly reached player level.</summary>
        public static PlayerUpgradeChoiceOverlay Show(
            Transform canvasRoot,
            UnityEngine.UI.Button template,
            GameObject returnSelectionTarget,
            int reachedPlayerLevel,
            Action onResolved)
        {
            if (canvasRoot == null)
            {
                onResolved?.Invoke();
                return null;
            }

            var existingTransform = canvasRoot.Find(OverlayName);
            var existing = existingTransform != null
                ? existingTransform.GetComponent<PlayerUpgradeChoiceOverlay>()
                : null;
            if (existing != null)
            {
                existing.Configure(template, returnSelectionTarget, reachedPlayerLevel, onResolved);
                existing.gameObject.SetActive(true);
                existing.transform.SetAsLastSibling();
                existing.Refresh();
                return existing;
            }

            var root = new GameObject(
                OverlayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(PlayerUpgradeChoiceOverlay));
            root.layer = canvasRoot.gameObject.layer;
            root.transform.SetParent(canvasRoot, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            var backdrop = root.GetComponent<Image>();
            backdrop.color = new Color(0.015f, 0.035f, 0.06f, 0.92f);
            backdrop.raycastTarget = true;

            var overlay = root.GetComponent<PlayerUpgradeChoiceOverlay>();
            overlay.Configure(template, returnSelectionTarget, reachedPlayerLevel, onResolved);
            overlay.Build();
            overlay.Refresh();
            FarmUiSkin.ApplyFromCanvas(canvasRoot, root.transform);
            root.transform.SetAsLastSibling();
            return overlay;
        }

        private void Configure(
            UnityEngine.UI.Button template,
            GameObject returnSelectionTarget,
            int reachedPlayerLevel,
            Action onResolved)
        {
            playerLevel = Mathf.Clamp(
                reachedPlayerLevel,
                PlayerLevelProgression.StartingLevel,
                PlayerLevelProgression.MaximumLevel);
            selectionAfterClose = returnSelectionTarget;
            resolvedCallback = onResolved;
            font = ResolveFont(transform.parent, template);
        }

        private void Build()
        {
            var panel = CreateObject(
                "UpgradeChoicePanel",
                transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(960f, 600f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = Parchment;
            FarmUiThinWoodOutline.EnsureFrame(panelImage, null);

            var panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(42, 42, 34, 34);
            panelLayout.spacing = 16f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            titleText = CreateText(panel.transform, "Title", 45, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter);
            titleText.GetComponent<LayoutElement>().preferredHeight = 68f;

            subtitleText = CreateText(panel.transform, "Subtitle", 25, FontStyle.Bold, TextAnchor.MiddleCenter);
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitleText.GetComponent<LayoutElement>().preferredHeight = 66f;

            var cards = CreateObject(
                "Cards",
                panel.transform,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            cardsRoot = cards.transform;
            var cardsLayout = cards.GetComponent<HorizontalLayoutGroup>();
            cardsLayout.spacing = 16f;
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            cardsLayout.childControlWidth = true;
            cardsLayout.childControlHeight = true;
            cardsLayout.childForceExpandWidth = true;
            cardsLayout.childForceExpandHeight = true;
            cards.GetComponent<LayoutElement>().preferredHeight = 290f;
            cards.GetComponent<LayoutElement>().flexibleHeight = 1f;

            progressText = CreateText(panel.transform, "Progress", 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            progressText.GetComponent<LayoutElement>().preferredHeight = 34f;
        }

        private void Refresh()
        {
            var service = PlayerSettingsService.Instance;
            service.CopyPendingPlayerUpgradeChoices(playerLevel, pendingChoices);
            if (pendingChoices.Count == 0)
            {
                Resolve();
                return;
            }

            activeChoiceIndex = Mathf.Clamp(activeChoiceIndex, 0, pendingChoices.Count - 1);
            var activeChoice = pendingChoices[activeChoiceIndex];
            if (activeChoice == null)
            {
                Resolve();
                return;
            }

            if (titleText != null)
            {
                titleText.text = $"PLAYER LEVEL {playerLevel} REWARDS";
            }

            if (subtitleText != null)
            {
                subtitleText.text = GetSlotHeading(activeChoice.rewardSlot);
            }

            if (progressText != null)
            {
                progressText.text = $"Choose reward {activeChoiceIndex + 1} of {pendingChoices.Count}";
            }

            ClearCards();
            BuildCards(activeChoice);
            FarmUiSkin.ApplyFromCanvas(transform.parent, transform);
        }

        private void BuildCards(PendingPlayerUpgradeChoiceData activeChoice)
        {
            if (cardsRoot == null || activeChoice?.offeredUpgradeIds == null)
            {
                return;
            }

            GameObject firstSelectable = null;
            for (var index = 0; index < activeChoice.offeredUpgradeIds.Count; index++)
            {
                var upgradeId = activeChoice.offeredUpgradeIds[index];
                if (!PlayerUpgradeProgression.TryGetDefinition(upgradeId, out var definition))
                {
                    continue;
                }

                var card = CreateUpgradeCard(cardsRoot, activeChoice, definition);
                firstSelectable ??= card;
            }

            if (firstSelectable != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(firstSelectable);
            }
        }

        private GameObject CreateUpgradeCard(
            Transform parent,
            PendingPlayerUpgradeChoiceData pending,
            PlayerUpgradeDefinition definition)
        {
            var card = CreateObject(
                definition.Id,
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            var image = card.GetComponent<Image>();
            image.color = GetCategoryTint(definition.Category);
            FarmUiThinWoodOutline.EnsureFrame(image, null);

            var layout = card.GetComponent<LayoutElement>();
            layout.minWidth = 240f;
            layout.preferredWidth = 270f;
            layout.flexibleWidth = 1f;

            var button = card.GetComponent<Button>();
            button.targetGraphic = image;
            var upgradeId = definition.Id;
            button.onClick.AddListener(() => Choose(pending.playerLevel, pending.rewardSlot, upgradeId));

            var text = CreateText(card.transform, "CardText", 25, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = $"{definition.GroupName}\n{definition.Title}\n\n{definition.Description}\n\nRANK {PlayerUpgradeProgression.GetRank(PlayerSettingsService.Instance.Profile, definition.Id)}/{definition.MaxRank}";
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 16f);
            textRect.offsetMax = new Vector2(-18f, -16f);
            return card;
        }

        private void Choose(int reachedLevel, PlayerUpgradeRewardSlot rewardSlot, string upgradeId)
        {
            if (!PlayerSettingsService.Instance.TryChoosePlayerUpgrade(reachedLevel, rewardSlot, upgradeId))
            {
                return;
            }

            activeChoiceIndex = 0;
            Refresh();
        }

        private void Resolve()
        {
            var callback = resolvedCallback;
            resolvedCallback = null;
            gameObject.SetActive(false);
            if (selectionAfterClose != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectionAfterClose);
            }

            callback?.Invoke();
        }

        private void ClearCards()
        {
            if (cardsRoot == null)
            {
                return;
            }

            for (var index = cardsRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(cardsRoot.GetChild(index).gameObject);
            }
        }

        private static string GetSlotHeading(PlayerUpgradeRewardSlot slot)
        {
            return slot switch
            {
                PlayerUpgradeRewardSlot.Attacker => "Choose one ATTACKER upgrade.",
                PlayerUpgradeRewardSlot.Defender => "Choose one DEFENDER upgrade.",
                PlayerUpgradeRewardSlot.Random => "Choose one RANDOM bonus upgrade.",
                PlayerUpgradeRewardSlot.General => "Choose one milestone GENERAL upgrade.",
                _ => "Choose an upgrade."
            };
        }

        private static Color GetCategoryTint(PlayerUpgradeCategory category)
        {
            return category switch
            {
                PlayerUpgradeCategory.Attacker => AttackerTint,
                PlayerUpgradeCategory.Defender => DefenderTint,
                _ => GeneralTint
            };
        }

        private Text CreateText(Transform parent, string name, int fontSize, FontStyle style, TextAnchor alignment)
        {
            var textObject = CreateObject(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = InkBrown;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 13;
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateObject(string name, Transform parent, params Type[] components)
        {
            var created = new GameObject(name, components);
            created.layer = parent.gameObject.layer;
            created.transform.SetParent(parent, false);
            return created;
        }

        private static Font ResolveFont(Transform canvasRoot, UnityEngine.UI.Button template)
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
}
