using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Determines which kinds of farm-wall cover this projectile may cross.
    /// </summary>
    public enum ProjectileType
    {
        Melee = 0,
        Ranged = 1
    }

    /// <summary>
    /// Chooses the runtime movement and impact behaviour implemented by this projectile's prefab.
    /// </summary>
    public enum ProjectileBehavior
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
    /// Reusable immutable authoring data for a defender projectile. Weapon definitions select one of these assets
    /// and provide their own damage and attack range when launching it.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Projectile Definition", fileName = "ProjectileDefinition")]
    public sealed class ProjectileDefinition : ScriptableObject
    {
        [Header("Projectile")]
        [SerializeField]
        [Tooltip("Melee projectiles cannot cross any wall. Ranged projectiles can cross half cover but not full cover.")]
        private ProjectileType projectileType = ProjectileType.Melee;
        [SerializeField] private ProjectileBehavior projectileBehavior = ProjectileBehavior.Direct;
        [SerializeField] private ProjectileLifetimeMode projectileLifetimeMode = ProjectileLifetimeMode.Timed;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField, Min(0f)] private float projectileLifetime;

        [Header("Special Projectile Behaviour")]
        [SerializeField, Min(0)]
        [Tooltip("Zero permits unlimited active projectiles. Pole sweeps use one so a farmhand cannot begin a second sweep before the first has finished.")]
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

        public ProjectileType ProjectileType => projectileType;
        public ProjectileBehavior ProjectileBehavior => projectileBehavior;
        public ProjectileLifetimeMode ProjectileLifetimeMode => projectileLifetimeMode;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float ProjectileLifetime => projectileLifetime;
        public int MaximumActiveProjectiles => maximumActiveProjectiles;
        public float InitialSpeedMultiplier => initialSpeedMultiplier;
        public float RollingAcceleration => rollingAcceleration;
        public float PoleSweepAngle => poleSweepAngle;
        public bool UsesTimedLifetime => projectileLifetimeMode == ProjectileLifetimeMode.Timed;

#if UNITY_EDITOR
        private void OnValidate()
        {
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);
            maximumActiveProjectiles = Mathf.Max(0, maximumActiveProjectiles);
            initialSpeedMultiplier = Mathf.Clamp01(initialSpeedMultiplier);
            rollingAcceleration = Mathf.Max(0f, rollingAcceleration);
            poleSweepAngle = Mathf.Clamp(poleSweepAngle, 1f, 360f);

            if (!System.Enum.IsDefined(typeof(ProjectileType), projectileType))
            {
                projectileType = ProjectileType.Melee;
            }

            if (!System.Enum.IsDefined(typeof(ProjectileBehavior), projectileBehavior))
            {
                projectileBehavior = ProjectileBehavior.Direct;
            }

            if (!System.Enum.IsDefined(typeof(ProjectileLifetimeMode), projectileLifetimeMode))
            {
                projectileLifetimeMode = ProjectileLifetimeMode.Timed;
            }
        }
#endif
    }
}