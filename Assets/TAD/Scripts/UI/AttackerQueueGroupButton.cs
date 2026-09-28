using System;
using TAD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>One circular, clickable summary of a distinct attacker group in the ordered upcoming-wave list.</summary>
    [DisallowMultipleComponent]
    public sealed class AttackerQueueGroupButton : MonoBehaviour
    {
        [SerializeField] private Button selectionButton;
        [SerializeField] private Image attackerImage;
        [SerializeField] private Text countText;
        [SerializeField] private Text stateText;
        [SerializeField] private Outline selectionOutline;

        private long groupId;
        private Action<long> selected;

        public long GroupId => groupId;

        private void Awake()
        {
            selectionButton ??= GetComponent<Button>();
            attackerImage ??= transform.Find("AttackerImage")?.GetComponent<Image>();
            countText ??= transform.Find("Count")?.GetComponent<Text>();
            stateText ??= transform.Find("State")?.GetComponent<Text>();
            selectionOutline ??= GetComponent<Outline>();
        }

        private void OnEnable()
        {
            if (selectionButton != null)
            {
                selectionButton.onClick.AddListener(HandlePressed);
            }
        }

        private void OnDisable()
        {
            if (selectionButton != null)
            {
                selectionButton.onClick.RemoveListener(HandlePressed);
            }
        }

        /// <summary>Updates this reusable circle from an immutable queue snapshot.</summary>
        public void Bind(AttackerQueuedGroupInfo group, Sprite rightFacingSprite, Action<long> onSelected, bool isSelected)
        {
            groupId = group.Id;
            selected = onSelected;

            if (attackerImage != null)
            {
                attackerImage.sprite = rightFacingSprite;
                attackerImage.enabled = rightFacingSprite != null;
                AttackerCombatRatingBadge.ShowFor(attackerImage, group.Definition);
            }

            if (countText != null)
            {
                countText.text = group.RemainingAlive + " / " + group.TotalCount;
            }

            if (stateText != null)
            {
                stateText.text = group.IsDispatching
                    ? group.IsCurrentGroup ? "NOW" : "UP NEXT"
                    : "NEXT WAVE";
                stateText.color = group.IsCurrentGroup
                    ? new Color(1f, 0.84f, 0.26f, 1f)
                    : group.IsDispatching
                        ? new Color(0.78f, 0.9f, 1f, 1f)
                        : new Color(0.72f, 1f, 0.76f, 1f);
            }

            if (selectionButton != null)
            {
                selectionButton.interactable = group.Definition != null;
            }

            if (selectionOutline != null)
            {
                selectionOutline.enabled = isSelected;
            }
        }

        private void HandlePressed()
        {
            if (groupId != 0)
            {
                selected?.Invoke(groupId);
            }
        }
    }
}
