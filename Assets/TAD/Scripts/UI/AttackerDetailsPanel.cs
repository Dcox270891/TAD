using System;
using TAD.Attackers;
using TAD.Match;
using TAD.Progression;
using TAD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>Non-modal details card for one selected attacker group in the upcoming or active wave.</summary>
    [DisallowMultipleComponent]
    public sealed class AttackerDetailsPanel : MonoBehaviour
    {
        // The two left HUD plaques occupy the top-left corner. Keep this card below their final
        // stacked bounds so it remains clear at the compact mobile Game View sizes.
        private const float SafeTopOffset = 188f;

        [SerializeField] private Text headingText;
        [SerializeField] private Image attackerImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text statsText;
        [SerializeField] private Text routeText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button removeFromQueueButton;

        private AttackerWaveQueue attackerWaveQueue;
        private Sprite rightFacingSprite;
        private long groupId;
        private bool isSubscribed;

        public event Action Closed;
        public bool IsShowing => gameObject.activeSelf && groupId != 0;
        public long GroupId => groupId;

        private void Awake()
        {
            ResolveReferences();
            ConfigureSafePlacement();
            if (groupId == 0)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Hide);
            }

            if (removeFromQueueButton != null)
            {
                removeFromQueueButton.onClick.AddListener(HandleRemoveFromQueuePressed);
            }

            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Hide);
            }

            if (removeFromQueueButton != null)
            {
                removeFromQueueButton.onClick.RemoveListener(HandleRemoveFromQueuePressed);
            }

            var wasShowing = groupId != 0;
            Unsubscribe();
            if (!wasShowing)
            {
                return;
            }

            attackerWaveQueue = null;
            groupId = 0;
            rightFacingSprite = null;
            Closed?.Invoke();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary>Shows the information for a stable group ID and keeps it current through the queue event.</summary>
        public void Show(AttackerWaveQueue waveQueue, AttackerQueuedGroupInfo group, Sprite sprite)
        {
            ResolveReferences();
            ConfigureSafePlacement();
            if (attackerWaveQueue != waveQueue || groupId != group.Id)
            {
                Unsubscribe();
                attackerWaveQueue = waveQueue;
                groupId = group.Id;
            }

            rightFacingSprite = sprite;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            Subscribe();
            Refresh();
        }

        /// <summary>Closes the card and returns control of attacker ordering to its presenter.</summary>
        public void Hide()
        {
            var wasShowing = IsShowing;
            Unsubscribe();
            attackerWaveQueue = null;
            groupId = 0;
            rightFacingSprite = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            if (wasShowing)
            {
                Closed?.Invoke();
            }
        }

        private void ResolveReferences()
        {
            headingText ??= FindText("Card/Heading");
            attackerImage ??= transform.Find("Card/AttackerImage")?.GetComponent<Image>();
            statusText ??= FindText("Card/Status");
            statsText ??= FindText("Card/Stats");
            routeText ??= FindText("Card/Route");
            closeButton ??= transform.Find("Card/Close")?.GetComponent<Button>();
            removeFromQueueButton ??= transform.Find("Card/RemoveFromQueue")?.GetComponent<Button>();
            ConfigureWrapping(headingText);
            ConfigureWrapping(statusText);
            ConfigureWrapping(statsText);
            ConfigureWrapping(routeText);
        }

        private void ConfigureSafePlacement()
        {
            var card = transform.Find("Card") as RectTransform;
            if (card == null)
            {
                return;
            }

            if (MobileUiLayoutController.UsesPortraitLayout(transform))
            {
                card.anchorMin = new Vector2(1f, 1f);
                card.anchorMax = new Vector2(1f, 1f);
                card.pivot = new Vector2(1f, 1f);
                card.anchoredPosition = new Vector2(-16f, -208f);
                card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 470f);
                return;
            }

            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(0f, 1f);
            card.pivot = new Vector2(0f, 1f);
            card.anchoredPosition = new Vector2(card.anchoredPosition.x, -SafeTopOffset);
        }

        private Text FindText(string path)
        {
            var child = transform.Find(path);
            return child != null ? child.GetComponent<Text>() : null;
        }

        private static void ConfigureWrapping(Text text)
        {
            if (text == null)
            {
                return;
            }

            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Details cards can grow through FarmUiCardAutoSizer; copy must never render into
            // an adjacent card while that responsive pass is settling on a compact phone.
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void Subscribe()
        {
            if (isSubscribed || attackerWaveQueue == null || groupId == 0)
            {
                return;
            }

            attackerWaveQueue.QueueChanged += Refresh;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (attackerWaveQueue != null)
            {
                attackerWaveQueue.QueueChanged -= Refresh;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
            if (attackerWaveQueue == null || groupId == 0 ||
                !attackerWaveQueue.TryGetDisplayGroup(groupId, out var group) || group.Definition == null)
            {
                if (groupId != 0)
                {
                    Hide();
                }

                return;
            }

            UpdateRemoveFromQueueButton(group);

            var definition = group.Definition;
            if (headingText != null)
            {
                headingText.text = definition.DisplayName;
            }

            if (attackerImage != null)
            {
                attackerImage.sprite = rightFacingSprite;
                attackerImage.enabled = rightFacingSprite != null;
                AttackerCombatRatingBadge.ShowFor(attackerImage, definition);
                AttackerArchetypeBadge.ShowFor(attackerImage, definition);
            }

            if (statusText != null)
            {
                var state = group.IsDispatching
                    ? group.IsCurrentGroup ? "Wave in progress" : "Waiting in this wave"
                    : "Queued for the next wave";
                statusText.text = state + "  •  Alive: " + group.RemainingAlive + "/" + group.TotalCount;
            }

            if (statsText != null)
            {
                var routeRule = definition.TraversalProfile == AttackerTraversalProfile.IgnoresHalfCover
                    ? "Ignores half cover"
                    : "Ground route";
                var profile = MatchRoleSelection.IsPlayerAttacker
                    ? PlayerSettingsService.Instance.Profile
                    : null;
                var health = PlayerUpgradeProgression.GetEffectiveAttackerHealth(profile, definition);
                var speed = PlayerUpgradeProgression.GetEffectiveAttackerSpeed(profile, definition);
                var cost = PlayerUpgradeProgression.GetEffectiveAttackerCost(profile, definition);
                var income = PlayerUpgradeProgression.GetEffectiveAttackerIncome(profile, definition);
                statsText.text =
                    "Health: " + health.ToString("0.#") +
                    "    Speed: " + speed.ToString("0.#") + System.Environment.NewLine +
                    "Size: " + definition.Size.ToString("0.#") +
                    "    Send: " + definition.SendSize + System.Environment.NewLine +
                    "Cost: " + cost +
                    "    Income: +" + income + System.Environment.NewLine +
                    "Type: " + definition.Archetype +
                    "    Tier: " + definition.ChallengeTier + System.Environment.NewLine +
                    "Route: " + routeRule + System.Environment.NewLine +
                    "Attacker ID: " + definition.AttackerId;
            }

            if (routeText != null)
            {
                routeText.text = "Route highlighted on the farm";
            }
        }

        private void HandleRemoveFromQueuePressed()
        {
            if (attackerWaveQueue == null ||
                groupId == 0 ||
                !attackerWaveQueue.TryGetDisplayGroup(groupId, out var group) ||
                !CanRemoveFromQueue(group))
            {
                return;
            }

            attackerWaveQueue.TryRemoveQueuedGroup(groupId);
        }

        private void UpdateRemoveFromQueueButton(AttackerQueuedGroupInfo group)
        {
            if (removeFromQueueButton == null)
            {
                return;
            }

            var canRemove = CanRemoveFromQueue(group);
            if (removeFromQueueButton.gameObject.activeSelf != canRemove)
            {
                removeFromQueueButton.gameObject.SetActive(canRemove);
            }

            removeFromQueueButton.interactable = canRemove;
        }

        private static bool CanRemoveFromQueue(AttackerQueuedGroupInfo group)
        {
            return MatchRoleSelection.IsPlayerAttacker && !group.IsDispatching;
        }
    }
}
