using UnityEngine;
using UnityEngine.Serialization;

namespace TAD.Defenders
{
    /// <summary>
    /// Shared defender weapon data. Projectile movement, lifetime, collision rules, and projectile prefab
    /// are supplied by the referenced ProjectileDefinition so they can be reused independently of a weapon.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Unarmed";
        [SerializeField, TextArea] private string description = "A basic unarmed farmhand attack.";

        [Header("Cost")]
        [SerializeField, Min(0)] private int cost;

        [Header("Attack Range")]
        [SerializeField, Min(0f)] private float range = 1f;

        [Header("Attack Speed")]
        [FormerlySerializedAs("fireInterval")]
        [SerializeField, Min(0.01f)] private float attackSpeed = 1f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float damage = 1f;

        [Header("Projectile")]
        [SerializeField] private ProjectileDefinition projectileDefinition;

        public int Cost => cost;
        public float Range => range;
        public float AttackSpeed => attackSpeed;
        public float FireInterval => 1f / Mathf.Max(0.01f, attackSpeed);
        public float Damage => damage;
        public ProjectileDefinition ProjectileDefinition => projectileDefinition;
        public string DisplayName => displayName;
        public string Description => description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            cost = Mathf.Max(0, cost);
            range = Mathf.Max(0f, range);
            attackSpeed = Mathf.Max(0.01f, attackSpeed);
            damage = Mathf.Max(0f, damage);
        }
#endif
    }
}