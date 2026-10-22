using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>Authoring data shared by each instance of one attacker type.</summary>
    [CreateAssetMenu(menuName = "TAD/Attackers/Attacker Definition", fileName = "AttackerDefinition")]
    public sealed class AttackerDefinition : ScriptableObject
    {
        [SerializeField] private string attackerId = "001";
        [SerializeField] private string displayName = "Chicken";
        [SerializeField, Min(0f)] private float maximumHealth = 10f;
        [SerializeField, Min(0f)] private float movementSpeed = 3f;
        [SerializeField, Min(0f)] private float size = 1f;
        [SerializeField, Min(0)] private int sendCost = 5;
        [SerializeField, Min(0)] private int waveIncome = 1;
        [SerializeField, Min(0)] private int expGiven = 10;
        [SerializeField, Min(0)] private int killGain = 10;
        [SerializeField, Min(0)] private int sendSize = 5;

        [Header("Roster")]
        [SerializeField] private AttackerArchetype archetype = AttackerArchetype.Basic;
        [SerializeField, Range(1, 10)] private int challengeTier = 1;
        [SerializeField] private AttackerTraversalProfile traversalProfile = AttackerTraversalProfile.Ground;

        [Header("Visuals")]
        [SerializeField] private AttackerVisualDefinition visualDefinition;
        [SerializeField, Min(0f)]
        [Tooltip("Optional world-space route diameter override. Leave at zero to use the standardized prefab footprint derived from Size.")]
        private float navigationDiameterOverride;

        [Header("Audio")]
        [SerializeField]
        [Tooltip("The animal cry reused when this attacker joins a cue, leaves the spawn point, and reaches the Food Store.")]
        private AudioClip entrySound;
        [SerializeField]
        [Tooltip("Played once when this attacker is defeated, before its pooled death animation begins.")]
        private AudioClip deathSound;

        public string AttackerId => attackerId;
        public string DisplayName => displayName;
        public float MaximumHealth => maximumHealth;
        public float MovementSpeed => movementSpeed;
        public float Size => size;
        /// <summary>
        /// The world-space diameter needed to keep this attacker's visible hit outline on the farm
        /// grass and clear of walls. <see cref="Size"/> remains the player-facing display scale;
        /// the standard multiplier matches the generated attacker prefab/collider scale.
        /// </summary>
        public float NavigationDiameter
        {
            get
            {
                if (cachedNavigationDiameter < 0f)
                {
                    cachedNavigationDiameter = navigationDiameterOverride > 0f
                        ? navigationDiameterOverride
                        : Mathf.Max(0.01f, size * DefaultNavigationDiameterPerDisplaySize);
                }

                return cachedNavigationDiameter;
            }
        }
        public int SendCost => sendCost;
        public int WaveIncome => waveIncome;
        public int ExpGiven => expGiven;
        public int KillGain => killGain;
        public int SendSize => sendSize;
        /// <summary>Player-facing per-animal combat rating derived from this definition's authored values.</summary>
        public float CombatRating => AttackerCombatRating.Calculate(this);
        public AttackerArchetype Archetype => archetype;
        public int ChallengeTier => challengeTier;
        public AttackerTraversalProfile TraversalProfile => traversalProfile;
        public bool IgnoresHalfCover => traversalProfile == AttackerTraversalProfile.IgnoresHalfCover;
        public AttackerVisualDefinition VisualDefinition => visualDefinition;
        /// <summary>Reusable animal vocalisation for all successful gameplay arrival/entry cues.</summary>
        public AudioClip AnimalCrySound => entrySound;
        /// <summary>Legacy name retained for existing scene and gameplay integrations.</summary>
        public AudioClip EntrySound => entrySound;
        public AudioClip DeathSound => deathSound;

        // Root attacker prefabs are uniformly authored at 0.1 scale and the visual controller
        // renders them at 4 x Size. The resulting conservative navigation diameter is 0.3 x Size.
        private const float DefaultNavigationDiameterPerDisplaySize = 0.3f;
        [System.NonSerialized] private float cachedNavigationDiameter = -1f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumHealth = Mathf.Max(0f, maximumHealth);
            movementSpeed = Mathf.Max(0f, movementSpeed);
            size = Mathf.Max(0f, size);
            sendCost = Mathf.Max(0, sendCost);
            waveIncome = Mathf.Max(0, waveIncome);
            expGiven = Mathf.Max(0, expGiven);
            killGain = Mathf.Max(0, killGain);
            sendSize = Mathf.Max(0, sendSize);
            challengeTier = Mathf.Clamp(challengeTier, 1, 10);
            navigationDiameterOverride = Mathf.Max(0f, navigationDiameterOverride);
            cachedNavigationDiameter = -1f;
        }
#endif
    }
}
