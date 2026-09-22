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

        public string AttackerId => attackerId;
        public string DisplayName => displayName;
        public float MaximumHealth => maximumHealth;
        public float MovementSpeed => movementSpeed;
        public float Size => size;
        public int SendCost => sendCost;
        public int WaveIncome => waveIncome;

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumHealth = Mathf.Max(0f, maximumHealth);
            movementSpeed = Mathf.Max(0f, movementSpeed);
            size = Mathf.Max(0f, size);
            sendCost = Mathf.Max(0, sendCost);
            waveIncome = Mathf.Max(0, waveIncome);
        }
#endif
    }
}
