using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using TAD.Pooling;
using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Builds a scrollable, data-driven collection of reusable attacker selection cards.</summary>
    [DisallowMultipleComponent]
    public sealed class AttackerSelectionPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private AttackerSelectionButton buttonPrefab;
        [SerializeField] private CanvasGroup inputCanvasGroup;

        private readonly List<AttackerDefinition> definitions = new();
        private readonly List<AttackerSelectionButton> spawnedButtons = new();
        private bool isOrderInputEnabled = true;

        /// <summary>Whether this panel is currently allowed to accept additional attacker orders.</summary>
        public bool IsOrderInputEnabled => isOrderInputEnabled;

        private void Awake()
        {
            ApplyOrderInputState();
        }

        /// <summary>Populates one card for each attacker definition configured on the level Spawn Pool.</summary>
        public void Bind(SpawnPool spawnPool, ResourceBank attackerBank, AttackerWaveQueue waveQueue)
        {
            ClearButtons();
            if (spawnPool == null || attackerBank == null || waveQueue == null ||
                content == null || buttonPrefab == null)
            {
                return;
            }

            spawnPool.CopyAvailableAttackerDefinitions(definitions);
            foreach (var definition in definitions)
            {
                if (definition == null || !spawnPool.TryGetAttackerPrefab(definition, out var attackerPrefab))
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
            inputCanvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            inputCanvasGroup.interactable = isOrderInputEnabled;
            inputCanvasGroup.blocksRaycasts = isOrderInputEnabled;
            inputCanvasGroup.alpha = isOrderInputEnabled ? 1f : 0.5f;
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