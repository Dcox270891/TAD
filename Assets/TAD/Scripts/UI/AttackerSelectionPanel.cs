using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using TAD.Match;
using TAD.Pooling;
using TAD.Progression;
using TAD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>Builds a scrollable, data-driven collection of reusable attacker selection cards.</summary>
    [DisallowMultipleComponent]
    public sealed class AttackerSelectionPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private AttackerSelectionButton buttonPrefab;
        [SerializeField] private CanvasGroup inputCanvasGroup;

        private RectTransform legacyContent;
        private RectTransform railContent;
        private ScrollRect scrollRect;
        private readonly List<AttackerDefinition> definitions = new();
        private readonly List<AttackerSelectionButton> spawnedButtons = new();
        private bool isOrderInputEnabled = true;
        private bool isPresentationVisible = true;

        /// <summary>Whether this panel is currently allowed to accept additional attacker orders.</summary>
        public bool IsOrderInputEnabled => isOrderInputEnabled;

        private void Awake()
        {
            EnsureRailContent();
            ApplyOrderInputState();
        }

        private void OnEnable()
        {
            ConfigureRailPresentation(MobileUiLayoutController.UsesPortraitLayout(transform));
        }

        /// <summary>Populates one card for each attacker definition configured on the level Spawn Pool.</summary>
        public void Bind(SpawnPool spawnPool, ResourceBank attackerBank, AttackerWaveQueue waveQueue)
        {
            EnsureRailContent();
            ClearButtons();
            if (spawnPool == null || attackerBank == null || waveQueue == null ||
                content == null || buttonPrefab == null)
            {
                return;
            }

            spawnPool.CopyAvailableAttackerDefinitions(definitions);
            definitions.Sort(CompareForDisplay);
            var unlockedChallengeTier = MatchRoleSelection.IsPlayerAttacker
                ? PlayerUpgradeProgression.GetUnlockedChallengeTier(PlayerSettingsService.Instance.Profile.playerLevel)
                : int.MaxValue;
            foreach (var definition in definitions)
            {
                if (definition == null || definition.ChallengeTier > unlockedChallengeTier ||
                    !spawnPool.TryGetAttackerPrefab(definition, out var attackerPrefab))
                {
                    continue;
                }

                var button = Instantiate(buttonPrefab, content);
                button.name = definition.DisplayName + "Button";
                button.Bind(definition, ResolveRightFacingSprite(definition, attackerPrefab), attackerBank, waveQueue);
                spawnedButtons.Add(button);
            }

            ApplyOrderInputState();
        }

        /// <summary>
        /// Replaces the former bottom horizontal card tray with a scrollable side-rail grid while
        /// leaving the original serialized content object intact for old prefab instances.
        /// </summary>
        internal void ConfigureRailPresentation(bool portrait)
        {
            EnsureRailContent();
            if (railContent == null)
            {
                return;
            }

            var grid = railContent.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.cellSize = portrait ? new Vector2(160f, 205f) : new Vector2(140f, 188f);
                grid.spacing = new Vector2(12f, 14f);
                grid.padding = new RectOffset(12, 12, 12, 12);
                grid.childAlignment = TextAnchor.UpperCenter;
                grid.startAxis = GridLayoutGroup.Axis.Vertical;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = portrait ? 1 : 2;
            }

            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }
        }

        /// <summary>
        /// Keeps the choice tray predictable: every challenge-one attacker is shown before
        /// challenge two, then challenge three. Within a tier, roles follow the authored
        /// Basic/Fast/Strong/Flying order and then their display names.
        /// </summary>
        private static int CompareForDisplay(AttackerDefinition left, AttackerDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var tierComparison = left.ChallengeTier.CompareTo(right.ChallengeTier);
            if (tierComparison != 0)
            {
                return tierComparison;
            }

            var archetypeComparison = left.Archetype.CompareTo(right.Archetype);
            if (archetypeComparison != 0)
            {
                return archetypeComparison;
            }

            var nameComparison = string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            return nameComparison != 0
                ? nameComparison
                : string.Compare(left.AttackerId, right.AttackerId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Locally suspends this panel's input without using a modal gameplay overlay, allowing defender
        /// inspection to continue while attacker group details are open.
        /// </summary>
        public void SetOrderInputEnabled(bool enabled)
        {
            if (isOrderInputEnabled == enabled)
            {
                return;
            }

            isOrderInputEnabled = enabled;
            ApplyOrderInputState();
        }

        /// <summary>Temporarily hides the card tray while a selected wave's information card is open.</summary>
        public void SetPresentationVisible(bool visible)
        {
            if (isPresentationVisible == visible)
            {
                return;
            }

            isPresentationVisible = visible;
            ApplyOrderInputState();
        }

        private void OnDestroy()
        {
            ClearButtons();
        }

        private void ClearButtons()
        {
            foreach (var button in spawnedButtons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            spawnedButtons.Clear();
            definitions.Clear();
        }

        private void ApplyOrderInputState()
        {
            if (!ResolveInputCanvasGroup())
            {
                return;
            }

            inputCanvasGroup.interactable = isPresentationVisible && isOrderInputEnabled;
            inputCanvasGroup.blocksRaycasts = isPresentationVisible && isOrderInputEnabled;
            inputCanvasGroup.alpha = isPresentationVisible
                ? isOrderInputEnabled ? 1f : 0.5f
                : 0f;
        }

        private void EnsureRailContent()
        {
            scrollRect ??= transform.Find("ScrollView")?.GetComponent<ScrollRect>();
            var viewport = scrollRect != null
                ? scrollRect.viewport ?? transform.Find("ScrollView/Viewport") as RectTransform
                : null;
            if (viewport == null)
            {
                return;
            }

            legacyContent ??= viewport.Find("Content") as RectTransform;
            railContent ??= viewport.Find("RailContent") as RectTransform;
            if (railContent == null)
            {
                var railObject = new GameObject(
                    "RailContent",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(GridLayoutGroup),
                    typeof(ContentSizeFitter));
                railObject.layer = gameObject.layer;
                railObject.transform.SetParent(viewport, false);
                railContent = railObject.GetComponent<RectTransform>();
                railContent.anchorMin = new Vector2(0f, 1f);
                railContent.anchorMax = new Vector2(1f, 1f);
                railContent.pivot = new Vector2(0.5f, 1f);
                railContent.anchoredPosition = Vector2.zero;
                railContent.sizeDelta = Vector2.zero;

                var fitter = railObject.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Retain any runtime-created cards if a presentation refresh happened after the
                // selection panel had already been bound during a retained play session.
                if (legacyContent != null)
                {
                    while (legacyContent.childCount > 0)
                    {
                        legacyContent.GetChild(0).SetParent(railContent, false);
                    }
                }
            }

            if (legacyContent != null && legacyContent != railContent)
            {
                var oldLayout = legacyContent.GetComponent<HorizontalLayoutGroup>();
                if (oldLayout != null)
                {
                    oldLayout.enabled = false;
                }

                legacyContent.gameObject.SetActive(false);
            }

            content = railContent;
            if (scrollRect != null)
            {
                scrollRect.content = railContent;
            }
        }

        private bool ResolveInputCanvasGroup()
        {
            // A CanvasGroup left over from an unloaded scene can look non-null to C# while Unity
            // treats it as destroyed. Use Unity's null semantics before accessing it.
            if (inputCanvasGroup == null)
            {
                inputCanvasGroup = GetComponent<CanvasGroup>();
            }

            if (inputCanvasGroup == null)
            {
                inputCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            return inputCanvasGroup != null;
        }

        /// <summary>Resolves the exact right-facing image used by attacker selection cards and group indicators.</summary>
        public static Sprite ResolveRightFacingSprite(AttackerDefinition definition, GameObject attackerPrefab)
        {
            if (definition != null && definition.VisualDefinition != null)
            {
                var visualSprite = definition.VisualDefinition.GetMovementSprite(0, 0);
                if (visualSprite != null)
                {
                    return visualSprite;
                }
            }

            var spriteRenderer = attackerPrefab != null
                ? attackerPrefab.GetComponentInChildren<SpriteRenderer>(true)
                : null;
            return spriteRenderer != null ? spriteRenderer.sprite : null;
        }
    }
}
