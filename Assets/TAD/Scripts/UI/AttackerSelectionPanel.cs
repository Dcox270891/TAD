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

        private readonly List<AttackerDefinition> definitions = new();
        private readonly List<AttackerSelectionButton> spawnedButtons = new();

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
                button.name = $"{definition.DisplayName}Button";
                button.Bind(definition, ResolveRightFacingSprite(definition, attackerPrefab), attackerBank, waveQueue);
                spawnedButtons.Add(button);
            }
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

        private static Sprite ResolveRightFacingSprite(AttackerDefinition definition, GameObject attackerPrefab)
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
