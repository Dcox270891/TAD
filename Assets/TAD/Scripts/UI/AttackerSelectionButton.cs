using TAD.Attackers;
using TAD.Economy;
using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Displays one attacker definition and queues that definition's configured group when selected.</summary>
    [DisallowMultipleComponent]
    public sealed class AttackerSelectionButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button selectionButton;
        [SerializeField] private UnityEngine.UI.Text headingText;
        [SerializeField] private UnityEngine.UI.Image attackerImage;
        [SerializeField] private UnityEngine.UI.Text costText;
        [SerializeField] private UnityEngine.UI.Text incomeText;

        private AttackerDefinition definition;
        private ResourceBank attackerBank;
        private AttackerWaveQueue attackerWaveQueue;
        private bool isSubscribed;

        /// <summary>Gets the attacker data represented by this card.</summary>
        public AttackerDefinition Definition => definition;

        /// <summary>Connects this reusable card to one attacker definition and the shared attacker economy.</summary>
        public void Bind(
            AttackerDefinition attackerDefinition,
            Sprite rightFacingSprite,
            ResourceBank bank,
            AttackerWaveQueue waveQueue)
        {
            Unsubscribe();
            definition = attackerDefinition;
            attackerBank = bank;
            attackerWaveQueue = waveQueue;
            ConfigureVisuals(rightFacingSprite);
            Subscribe();
            RefreshInteractableState();
        }

        private void Awake()
        {
            selectionButton ??= GetComponent<UnityEngine.UI.Button>();
            headingText ??= FindText("Heading");
            attackerImage ??= transform.Find("AttackerImage")?.GetComponent<UnityEngine.UI.Image>();
            costText ??= FindText("Cost");
            incomeText ??= FindText("Income");
        }

        private void OnEnable()
        {
            if (selectionButton != null)
            {
                selectionButton.onClick.AddListener(HandlePressed);
            }

            Subscribe();
            RefreshInteractableState();
        }

        private void OnDisable()
        {
            if (selectionButton != null)
            {
                selectionButton.onClick.RemoveListener(HandlePressed);
            }

            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private UnityEngine.UI.Text FindText(string childName)
        {
            var child = transform.Find(childName);
            return child != null ? child.GetComponent<UnityEngine.UI.Text>() : null;
        }

        private void ConfigureVisuals(Sprite rightFacingSprite)
        {
            if (headingText != null)
            {
                headingText.text = definition != null ? definition.DisplayName : string.Empty;
            }

            if (attackerImage != null)
            {
                attackerImage.sprite = rightFacingSprite;
                attackerImage.enabled = rightFacingSprite != null;
                AttackerCombatRatingBadge.ShowFor(attackerImage, definition);
            }

            if (costText != null)
            {
                costText.text = definition != null ? $"Cost: {definition.SendCost}" : string.Empty;
            }

            if (incomeText != null)
            {
                incomeText.text = definition != null ? $"Income: +{definition.WaveIncome}" : string.Empty;
            }
        }

        private void HandlePressed()
        {
            if (ModalOverlay.IsGameplayInputBlocked || attackerWaveQueue == null)
            {
                return;
            }

            attackerWaveQueue.TryQueueAttackers(definition);
            RefreshInteractableState();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            if (attackerBank != null)
            {
                attackerBank.GoldChanged += HandleGoldChanged;
            }

            if (attackerWaveQueue != null)
            {
                attackerWaveQueue.QueueChanged += HandleQueueChanged;
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (attackerBank != null)
            {
                attackerBank.GoldChanged -= HandleGoldChanged;
            }

            if (attackerWaveQueue != null)
            {
                attackerWaveQueue.QueueChanged -= HandleQueueChanged;
            }

            isSubscribed = false;
        }

        private void HandleGoldChanged(int gold)
        {
            RefreshInteractableState();
        }

        private void HandleQueueChanged()
        {
            RefreshInteractableState();
        }

        private void RefreshInteractableState()
        {
            if (selectionButton == null)
            {
                return;
            }

            selectionButton.interactable = definition != null &&
                                           attackerBank != null &&
                                           attackerWaveQueue != null &&
                                           attackerBank.CanAfford(definition.SendCost) &&
                                           attackerWaveQueue.CanQueueAttackers;
        }
    }
}
