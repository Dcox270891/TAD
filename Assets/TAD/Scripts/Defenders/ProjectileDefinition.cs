using TAD.Attackers;
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
        Trap = 3,
        Bouncing = 4
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
        [SerializeField]
        [Tooltip("Allows this individual projectile to target airborne attackers even when it is melee. Reserve this for deliberate exceptions such as Unarmed.")]
        private bool canTargetFlying;
        [SerializeField] private ProjectileBehavior projectileBehavior = ProjectileBehavior.Direct;
        [SerializeField] private ProjectileLifetimeMode projectileLifetimeMode = ProjectileLifetimeMode.Timed;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField, Min(0f)] private float projectileLifetime;

        [Header("Audio")]
        [SerializeField]
        [Tooltip("Played once when this projectile has launched successfully. Every projectile definition can use its own cue.")]
        private AudioClip firingSound;

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

        [Header("Impact and Status Effects")]
        [SerializeField, Min(0f)]
        [Tooltip("Radius around an impact that receives the area damage and configured status effects. Zero affects only the direct target.")]
        private float impactAreaRadius;
        [SerializeField, Min(0f)]
        [Tooltip("Extra damage dealt to every other attacker inside Impact Area Radius. The triggering target still receives the weapon's normal damage.")]
        private float impactAreaDamage;
        [SerializeField, Min(0f)] private float bleedDamagePerSecond;
        [SerializeField, Min(0f)] private float bleedDuration;
        [SerializeField, Min(0f)] private float poisonDamagePerSecond;
        [SerializeField, Min(0f)] private float poisonDuration;
        [SerializeField, Min(0f)] private float stunDuration;
        [SerializeField, Range(0.05f, 1f)] private float slowMovementMultiplier = 1f;
        [SerializeField, Min(0f)] private float slowDuration;

        [Header("Projectile Presentation")]
        [SerializeField, Min(0.01f)]
        [Tooltip("Multiplier applied to the authored projectile visual and hitbox for this reusable definition.")]
        private float projectileScaleMultiplier = 1f;

        [Header("Bounce Behaviour")]
        [SerializeField, Min(0.05f)]
        [Tooltip("Distance travelled between each ground impact by a bouncing projectile.")]
        private float bounceDistance = 1f;
        [SerializeField, Range(0f, 89f)]
        [Tooltip("Alternating deflection from the intended travel direction after each bounce.")]
        private float bounceAngle = 20f;
        [SerializeField, Min(1)]
        [Tooltip("Number of ground impacts made before a bouncing projectile returns to its pool.")]
        private int maximumBounceCount = 3;

        public ProjectileType ProjectileType => projectileType;
        /// <summary>True when this projectile is allowed to select and damage airborne attackers.</summary>
        public bool CanTargetFlying => projectileType == ProjectileType.Ranged || canTargetFlying;
        public ProjectileBehavior ProjectileBehavior => projectileBehavior;
        public ProjectileLifetimeMode ProjectileLifetimeMode => projectileLifetimeMode;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float ProjectileLifetime => projectileLifetime;
        public AudioClip FiringSound => firingSound;
        public int MaximumActiveProjectiles => maximumActiveProjectiles;
        public float InitialSpeedMultiplier => initialSpeedMultiplier;
        public float RollingAcceleration => rollingAcceleration;
        public float PoleSweepAngle => poleSweepAngle;
        public float ImpactAreaRadius => impactAreaRadius;
        public float ImpactAreaDamage => impactAreaDamage;
        public float BleedDamagePerSecond => bleedDamagePerSecond;
        public float BleedDuration => bleedDuration;
        public float PoisonDamagePerSecond => poisonDamagePerSecond;
        public float PoisonDuration => poisonDuration;
        public float StunDuration => stunDuration;
        public float SlowMovementMultiplier => slowMovementMultiplier;
        public float SlowDuration => slowDuration;
        public float ProjectileScaleMultiplier => projectileScaleMultiplier;
        public float BounceDistance => bounceDistance;
        public float BounceAngle => bounceAngle;
        public int MaximumBounceCount => maximumBounceCount;
        public bool HasImpactArea => impactAreaRadius > 0f;
        public bool UsesTimedLifetime => projectileLifetimeMode == ProjectileLifetimeMode.Timed;

        /// <summary>
        /// Returns whether this projectile category can legally attack the supplied animal.
        /// Flying animals are airborne, so they require a projectile explicitly configured to target them.
        /// </summary>
        public bool CanTarget(BasicAttacker attacker)
        {
            return attacker != null && (!attacker.IsFlying || CanTargetFlying);
        }

        /// <summary>
        /// Shared default compatibility rule used where only a projectile category is available.
        /// Melee remains grounded-only unless a concrete projectile definition opts into the exception.
        /// </summary>
        public static bool CanTarget(ProjectileType projectileType, BasicAttacker attacker)
        {
            return CanTarget(projectileType, canTargetFlying: false, attacker);
        }

        /// <summary>Shared compatibility rule for an explicit airborne-targeting exception.</summary>
        public static bool CanTarget(
            ProjectileType projectileType,
            bool canTargetFlying,
            BasicAttacker attacker)
        {
            return attacker != null &&
                   (!attacker.IsFlying || projectileType == ProjectileType.Ranged || canTargetFlying);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);
            maximumActiveProjectiles = Mathf.Max(0, maximumActiveProjectiles);
            initialSpeedMultiplier = Mathf.Clamp01(initialSpeedMultiplier);
            rollingAcceleration = Mathf.Max(0f, rollingAcceleration);
            poleSweepAngle = Mathf.Clamp(poleSweepAngle, 1f, 360f);
            impactAreaRadius = Mathf.Max(0f, impactAreaRadius);
            impactAreaDamage = Mathf.Max(0f, impactAreaDamage);
            bleedDamagePerSecond = Mathf.Max(0f, bleedDamagePerSecond);
            bleedDuration = Mathf.Max(0f, bleedDuration);
            poisonDamagePerSecond = Mathf.Max(0f, poisonDamagePerSecond);
            poisonDuration = Mathf.Max(0f, poisonDuration);
            stunDuration = Mathf.Max(0f, stunDuration);
            slowMovementMultiplier = Mathf.Clamp(slowMovementMultiplier, 0.05f, 1f);
            slowDuration = Mathf.Max(0f, slowDuration);
            projectileScaleMultiplier = Mathf.Max(0.01f, projectileScaleMultiplier);
            bounceDistance = Mathf.Max(0.05f, bounceDistance);
            bounceAngle = Mathf.Clamp(bounceAngle, 0f, 89f);
            maximumBounceCount = Mathf.Max(1, maximumBounceCount);

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
