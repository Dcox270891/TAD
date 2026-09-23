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
        [SerializeField, Range(1, 3)] private int challengeTier = 1;
        [SerializeField] private AttackerTraversalProfile traversalProfile = AttackerTraversalProfile.Ground;

        [Header("Visuals")]
        [SerializeField] private AttackerVisualDefinition visualDefinition;

        public string AttackerId => attackerId;
        public string DisplayName => displayName;
        public float MaximumHealth => maximumHealth;
        public float MovementSpeed => movementSpeed;
        public float Size => size;
        public int SendCost => sendCost;
        public int WaveIncome => waveIncome;
        public int ExpGiven => expGiven;
        public int KillGain => killGain;
        public int SendSize => sendSize;
        public AttackerArchetype Archetype => archetype;
        public int ChallengeTier => challengeTier;
        public AttackerTraversalProfile TraversalProfile => traversalProfile;
        public bool IgnoresHalfCover => traversalProfile == AttackerTraversalProfile.IgnoresHalfCover;
        public AttackerVisualDefinition VisualDefinition => visualDefinition;

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
            challengeTier = Mathf.Clamp(challengeTier, 1, 3);
        }
#endif
    }
}
