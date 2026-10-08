using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Chooses a complete, data-authored appearance and a friendly name whenever a farmhand is
    /// taken from the defender pool. The choice is intentionally automatic: player placement
    /// never opens a cosmetic selection screen and moving a placed farmhand retains its identity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmHand), typeof(FarmHandVisualController))]
    public sealed class FarmHandAppearanceRandomizer : MonoBehaviour, IPoolLifecycle
    {
        [Header("Automatic Appearance Choices")]
        [Tooltip("Complete body, hair/hat, and outfit combinations that can be assigned on placement.")]
        [SerializeField] private FarmHandAppearanceDefinition[] appearances;
        [Tooltip("Friendly farmhand names chosen automatically alongside the appearance.")]
        [SerializeField] private string[] displayNames;

        [SerializeField] private FarmHand farmHand;
        [SerializeField] private FarmHandVisualController visualController;
        private FarmHandAppearanceDefinition assignedAppearance;

        /// <summary>The appearance chosen for this active placement, if the pool has configured choices.</summary>
        public FarmHandAppearanceDefinition AssignedAppearance => assignedAppearance;

        /// <summary>Number of valid configured appearances. Exposed for validation and authoring diagnostics.</summary>
        public int AppearanceCount => CountValidAppearances();

        /// <summary>Number of valid configured names. Exposed for validation and authoring diagnostics.</summary>
        public int NameCount => CountValidNames();

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// Assigns a fresh identity to this pooled lifetime. This is public so future modes can
        /// deliberately reroll a newly created defender without exposing a player cosmetic choice.
        /// </summary>
        public void AssignRandomIdentity()
        {
            ResolveReferences();

            var appearance = GetRandomAppearance();
            if (appearance != null)
            {
                assignedAppearance = appearance;
                visualController?.SetAppearance(appearance);
            }

            farmHand?.SetDisplayName(GetRandomDisplayName());
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            AssignRandomIdentity();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            // A name and look belong to the current placement. They are replaced next time this
            // reusable object is taken from the pool; no state needs to persist while inactive.
            assignedAppearance = null;
        }

        private FarmHandAppearanceDefinition GetRandomAppearance()
        {
            var validCount = CountValidAppearances();
            if (validCount == 0)
            {
                return null;
            }

            var targetIndex = Random.Range(0, validCount);
            for (var index = 0; index < appearances.Length; index++)
            {
                if (appearances[index] == null)
                {
                    continue;
                }

                if (targetIndex == 0)
                {
                    return appearances[index];
                }

                targetIndex--;
            }

            return null;
        }

        private string GetRandomDisplayName()
        {
            var validCount = CountValidNames();
            if (validCount == 0)
            {
                return "Farmhand";
            }

            var targetIndex = Random.Range(0, validCount);
            for (var index = 0; index < displayNames.Length; index++)
            {
                var candidate = displayNames[index];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (targetIndex == 0)
                {
                    return candidate.Trim();
                }

                targetIndex--;
            }

            return "Farmhand";
        }

        private int CountValidAppearances()
        {
            if (appearances == null)
            {
                return 0;
            }

            var count = 0;
            for (var index = 0; index < appearances.Length; index++)
            {
                if (appearances[index] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountValidNames()
        {
            if (displayNames == null)
            {
                return 0;
            }

            var count = 0;
            for (var index = 0; index < displayNames.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(displayNames[index]))
                {
                    count++;
                }
            }

            return count;
        }

        private void ResolveReferences()
        {
            farmHand ??= GetComponent<FarmHand>();
            visualController ??= GetComponent<FarmHandVisualController>();
        }
    }
}
