using UnityEngine;
using UnityEngine.Serialization;

namespace TAD.Defenders
{
    /// <summary>
    /// Determines which kinds of farm-wall cover a defender's projectile may cross.
    /// </summary>
    public enum WeaponProjectileType
    {
        Melee = 0,
        Ranged = 1
    }

    /// <summary>
    /// Authoring data shared by every farmhand using the same defender weapon.
    /// Runtime combat code reads this asset but must not modify it.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Unarmed";
        [SerializeField, TextArea(2, 5)] private string description = "A farmhand's fists. No projectile is used.";

        [Header("Cost")]
        [SerializeField, Min(0)] private int cost;

        [Header("Attack Range")]
        [SerializeField, Min(0f)] private float range = 1f;

        [Header("Attack Speed")]
        [FormerlySerializedAs("fireInterval")]
        [SerializeField, Min(0.01f)]
        [Tooltip("Projectiles fired each second while a valid attacker remains in range.")]
        private float attackSpeed = 1f;

        [Header("Projectile")]
        [SerializeField]
        [Tooltip("Melee shots cannot cross any wall. Ranged shots can cross half cover but not full cover.")]
        private WeaponProjectileType projectileType = WeaponProjectileType.Melee;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField, Min(0f)] private float damage = 1f;
        [SerializeField, Min(0f)] private float projectileLifetime;

        public int Cost => cost;
        public float Range => range;
        public float AttackSpeed => attackSpeed;
        public float FireInterval => 1f / Mathf.Max(0.01f, attackSpeed);
        public WeaponProjectileType ProjectileType => projectileType;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float Damage => damage;
        public float ProjectileLifetime => projectileLifetime;
        public string DisplayName => displayName;
        public string Description => description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            cost = Mathf.Max(0, cost);
            range = Mathf.Max(0f, range);
            attackSpeed = Mathf.Max(0.01f, attackSpeed);
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            damage = Mathf.Max(0f, damage);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);

            if (!System.Enum.IsDefined(typeof(WeaponProjectileType), projectileType))
            {
                projectileType = WeaponProjectileType.Melee;
            }
        }
#endif
    }
}
