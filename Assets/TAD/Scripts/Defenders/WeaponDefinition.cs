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
    /// Chooses the runtime movement and impact behaviour implemented by a weapon's projectile prefab.
    /// </summary>
    public enum WeaponProjectileBehavior
    {
        Direct = 0,
        PoleSweep = 1,
        Rolling = 2,
        Trap = 3
    }

    /// <summary>
    /// Describes whether a projectile is removed after a configured duration or only after its gameplay effect occurs.
    /// </summary>
    public enum ProjectileLifetimeMode
    {
        Timed = 0,
        UntilActivated = 1
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
        [SerializeField] private WeaponProjectileBehavior projectileBehavior = WeaponProjectileBehavior.Direct;
        [SerializeField] private ProjectileLifetimeMode projectileLifetimeMode = ProjectileLifetimeMode.Timed;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField, Min(0f)] private float damage = 1f;
        [SerializeField, Min(0f)] private float projectileLifetime;

        [Header("Special Projectile Behaviour")]
        [SerializeField, Min(0)]
        [Tooltip("Zero permits unlimited active projectiles. Pole weapons use one so a farmhand cannot begin a second sweep before the first has finished.")]
        private int maximumActiveProjectiles;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Starting fraction of maximum speed for rolling projectiles.")]
        private float initialSpeedMultiplier = 1f;
        [SerializeField, Min(0f)]
        [Tooltip("World units per second squared added to rolling projectiles until they reach projectile speed.")]
        private float rollingAcceleration;
        [SerializeField, Range(1f, 360f)]
        [Tooltip("Degrees swept by a Pole Sweep projectile around its farmhand.")]
        private float poleSweepAngle = 180f;

        public int Cost => cost;
        public float Range => range;
        public float AttackSpeed => attackSpeed;
        public float FireInterval => 1f / Mathf.Max(0.01f, attackSpeed);
        public WeaponProjectileType ProjectileType => projectileType;
        public WeaponProjectileBehavior ProjectileBehavior => projectileBehavior;
        public ProjectileLifetimeMode ProjectileLifetimeMode => projectileLifetimeMode;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float Damage => damage;
        public float ProjectileLifetime => projectileLifetime;
        public int MaximumActiveProjectiles => maximumActiveProjectiles;
        public float InitialSpeedMultiplier => initialSpeedMultiplier;
        public float RollingAcceleration => rollingAcceleration;
        public float PoleSweepAngle => poleSweepAngle;
        public bool UsesTimedLifetime => projectileLifetimeMode == ProjectileLifetimeMode.Timed;
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
            maximumActiveProjectiles = Mathf.Max(0, maximumActiveProjectiles);
            initialSpeedMultiplier = Mathf.Clamp01(initialSpeedMultiplier);
            rollingAcceleration = Mathf.Max(0f, rollingAcceleration);
            poleSweepAngle = Mathf.Clamp(poleSweepAngle, 1f, 360f);

            if (!System.Enum.IsDefined(typeof(WeaponProjectileType), projectileType))
            {
                projectileType = WeaponProjectileType.Melee;
            }

            if (!System.Enum.IsDefined(typeof(WeaponProjectileBehavior), projectileBehavior))
            {
                projectileBehavior = WeaponProjectileBehavior.Direct;
            }

            if (!System.Enum.IsDefined(typeof(ProjectileLifetimeMode), projectileLifetimeMode))
            {
                projectileLifetimeMode = ProjectileLifetimeMode.Timed;
            }
        }
#endif
    }
}