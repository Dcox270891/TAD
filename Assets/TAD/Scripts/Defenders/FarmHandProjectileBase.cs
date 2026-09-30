using System.Collections.Generic;
using TAD.Attackers;
using TAD.Environment;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Shared pooled-projectile lifecycle, physics setup, and defender damage bridge.
    /// Concrete projectile prefabs own only their movement and contact persistence rules.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public abstract class FarmHandProjectileBase : MonoBehaviour, IPoolLifecycle
    {
        private const int WallHitCapacity = 16;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Collider2D hitCollider;
        [SerializeField] private Rigidbody2D body;
        [SerializeField, Min(0.01f)]
        [Tooltip("World-space hit radius used by circular projectile prefabs.")]
        private float hitRadius = 0.15f;

        private PooledInstance pooledInstance;
        private Vector3 baseLocalScale;
        private bool hasBaseLocalScale;
        private readonly Collider2D[] impactOverlapResults = new Collider2D[32];
        private readonly HashSet<BasicAttacker> areaImpactTargets = new();
        private static readonly RaycastHit2D[] wallHitBuffer = new RaycastHit2D[WallHitCapacity];
        private static int environmentLayerMask;
        private static bool hasResolvedEnvironmentLayerMask;

        protected FarmHand SourceFarmHand { get; private set; }
        protected long SourceActivationSessionId { get; private set; }
        protected BasicAttacker Target { get; private set; }
        protected ProjectileDefinition Definition { get; private set; }
        protected float AttackRange { get; private set; }
        protected float Damage { get; private set; }
        protected Rigidbody2D Body => body;
        protected Collider2D HitCollider => hitCollider;
        protected Vector3 BaseLocalScale => baseLocalScale;
        public bool IsLaunched { get; private set; }
        public FarmHand Source => SourceFarmHand;
        /// <summary>Enabled lifetime captured from <see cref="Source"/> when this projectile was launched.</summary>
        public long SourceActivationSession => SourceActivationSessionId;
        public ProjectileDefinition ProjectileDefinition => Definition;
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        protected abstract ProjectileBehavior ExpectedProjectileBehavior { get; }

        /// <summary>
        /// Most prefabs implement one behaviour. A reusable prefab can explicitly opt into a closely
        /// related behaviour (for example a rolling visual that also performs ground bounces).
        /// </summary>
        protected virtual bool SupportsProjectileBehavior(ProjectileBehavior projectileBehavior)
        {
            return projectileBehavior == ExpectedProjectileBehavior;
        }

        protected virtual void Awake()
        {
            ResolveComponents();
            CacheBaseLocalScale();
            ConfigurePhysics();
            ConfigureHitCollider();
            ResetRuntimeState();
        }

        protected virtual void OnEnable()
        {
            ResolveComponents();
            CacheBaseLocalScale();
            ConfigurePhysics();
            ConfigureHitCollider();
            ResetRuntimeState();
        }

        /// <summary>Initializes this projectile from immutable projectile data immediately after the pool activates it.</summary>
        public bool Launch(
            FarmHand sourceFarmHand,
            Vector2 origin,
            BasicAttacker target,
            ProjectileDefinition definition,
            float attackRange,
            float damageAmount)
        {
            if (sourceFarmHand == null || definition == null || !SupportsProjectileBehavior(definition.ProjectileBehavior) ||
                !IsValidTarget(target) || !definition.CanTarget(target) || definition.ProjectileSpeed <= 0f || damageAmount <= 0f ||
                (definition.UsesTimedLifetime && definition.ProjectileLifetime <= 0f))
            {
                return false;
            }

            var sourceActivationSessionId = sourceFarmHand.ActivationSessionId;
            if (!sourceFarmHand.IsActivationSessionCurrent(sourceActivationSessionId))
            {
                return false;
            }

            ResolveComponents();
            CacheBaseLocalScale();
            ConfigurePhysics();
            ConfigureHitCollider();
            if (body == null || hitCollider == null)
            {
                return false;
            }

            SourceFarmHand = sourceFarmHand;
            SourceActivationSessionId = sourceActivationSessionId;
            Target = target;
            Definition = definition;
            AttackRange = Mathf.Max(0f, attackRange);
            Damage = damageAmount;
            body.position = origin;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            transform.position = new Vector3(origin.x, origin.y, transform.position.z);
            transform.localScale = Vector3.Scale(
                baseLocalScale,
                Vector3.one * definition.ProjectileScaleMultiplier);

            if (!OnLaunched(origin, target))
            {
                ResetRuntimeState();
                return false;
            }

            IsLaunched = true;
            if (visual != null)
            {
                visual.enabled = true;
            }

            return true;
        }

        /// <summary>Ends the active projectile safely when its owner is disabled, recycled, or re-equipped.</summary>
        public void Cancel()
        {
            if (!IsLaunched)
            {
                return;
            }

            ReturnToPool();
        }

        protected abstract bool OnLaunched(Vector2 origin, BasicAttacker target);

        protected void SetFacing(Vector2 direction)
        {
            if (body == null || direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            body.rotation = (Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg) - 90f;
        }

        protected void SetHitColliderEnabled(bool enabled)
        {
            if (hitCollider != null)
            {
                hitCollider.enabled = enabled;
            }
        }

        protected BasicAttacker GetValidAttacker(Collider2D other)
        {
            var attacker = other != null ? other.GetComponentInParent<BasicAttacker>() : null;
            return IsValidTarget(attacker) && Definition != null && Definition.CanTarget(attacker)
                ? attacker
                : null;
        }

        protected bool TryDamageAttacker(BasicAttacker attacker)
        {
            return TryDamageAttacker(attacker, Damage);
        }

        /// <summary>Applies an explicit area-damage amount through the same XP and kill-reward path as the main hit.</summary>
        protected bool TryDamageAttacker(BasicAttacker attacker, float damageAmount)
        {
            return IsValidTarget(attacker) && Definition != null && Definition.CanTarget(attacker) &&
                   SourceFarmHand != null && damageAmount > 0f &&
                   SourceFarmHand.TryDamageAttacker(attacker, damageAmount, SourceActivationSessionId);
        }

        /// <summary>
        /// Returns whether the projectile's next movement segment crosses cover that blocks this
        /// projectile type. Movement scripts use this to keep rolls and bullets from passing a
        /// Full-cover wall after their original target has moved.
        /// </summary>
        protected bool IsAttackPathBlockedByWall(Vector2 origin, Vector2 destination)
        {
            return Definition != null && IsProjectilePathBlocked(
                origin,
                destination,
                Definition.ProjectileType);
        }

        /// <summary>
        /// Applies a direct hit, then optional splash damage and status effects around the impact point.
        /// The direct target receives the weapon's normal damage exactly once; nearby attackers receive
        /// only <see cref="ProjectileDefinition.ImpactAreaDamage"/> while every target in the radius can
        /// receive the configured bleed, poison, stun, or slow effect.
        /// </summary>
        protected bool TryApplyImpactEffects(
            BasicAttacker primaryTarget,
            Vector2 impactPosition,
            bool applyPrimaryDamage)
        {
            if (Definition == null || !HasCurrentSourceActivation())
            {
                return false;
            }

            var affected = false;
            var primaryAttackOrigin = GetPrimaryAttackOrigin(impactPosition);
            if (IsImpactTarget(primaryTarget, primaryAttackOrigin))
            {
                affected |= ApplyConfiguredStatusEffects(primaryTarget, primaryAttackOrigin);
                if (applyPrimaryDamage)
                {
                    affected |= TryDamageAttacker(primaryTarget);
                }
            }

            if (!Definition.HasImpactArea)
            {
                return affected;
            }

            areaImpactTargets.Clear();
            var resultCount = Physics2D.OverlapCircle(
                impactPosition,
                Definition.ImpactAreaRadius,
                default,
                impactOverlapResults);
            for (var index = 0; index < resultCount; index++)
            {
                var collider = impactOverlapResults[index];
                impactOverlapResults[index] = null;
                var attacker = collider != null ? collider.GetComponentInParent<BasicAttacker>() : null;
                if (attacker == null || attacker == primaryTarget || !areaImpactTargets.Add(attacker) ||
                    !IsImpactTarget(attacker, impactPosition))
                {
                    continue;
                }

                affected |= ApplyConfiguredStatusEffects(attacker, impactPosition);
                if (Definition.ImpactAreaDamage > 0f)
                {
                    affected |= TryDamageAttacker(attacker, Definition.ImpactAreaDamage);
                }
            }

            return affected;
        }

        /// <summary>Applies the configured area effect at a ground impact without a direct target.</summary>
        protected bool TryApplyAreaImpact(Vector2 impactPosition)
        {
            return TryApplyImpactEffects(null, impactPosition, applyPrimaryDamage: false);
        }

        private Vector2 GetPrimaryAttackOrigin(Vector2 impactPosition)
        {
            // A grounded trap and a bounce deal their effect from their current ground impact. A
            // direct, pole, or rolling strike still originates from the farmhand, matching target
            // selection and ensuring Full cover protects an attacker that moves behind it mid-shot.
            return Definition != null &&
                   (Definition.ProjectileBehavior == ProjectileBehavior.Trap ||
                    Definition.ProjectileBehavior == ProjectileBehavior.Bouncing)
                ? impactPosition
                : SourceFarmHand != null
                    ? (Vector2)SourceFarmHand.transform.position
                    : impactPosition;
        }

        private bool IsImpactTarget(BasicAttacker attacker, Vector2 attackOrigin)
        {
            return IsValidTarget(attacker) &&
                   Definition != null &&
                   Definition.CanTarget(attacker) &&
                   !IsProjectilePathBlocked(
                       attackOrigin,
                       attacker.CurrentPosition,
                       Definition.ProjectileType);
        }

        private bool ApplyConfiguredStatusEffects(BasicAttacker attacker, Vector2 attackOrigin)
        {
            if (!IsImpactTarget(attacker, attackOrigin) || !HasCurrentSourceActivation())
            {
                return false;
            }

            var applied = false;
            if (Definition.BleedDamagePerSecond > 0f && Definition.BleedDuration > 0f)
            {
                attacker.ApplyBleed(
                    SourceFarmHand,
                    SourceActivationSessionId,
                    Definition.BleedDamagePerSecond,
                    Definition.BleedDuration);
                applied = true;
            }

            if (Definition.PoisonDamagePerSecond > 0f && Definition.PoisonDuration > 0f)
            {
                attacker.ApplyPoison(
                    SourceFarmHand,
                    SourceActivationSessionId,
                    Definition.PoisonDamagePerSecond,
                    Definition.PoisonDuration);
                applied = true;
            }

            if (Definition.StunDuration > 0f)
            {
                attacker.ApplyStun(Definition.StunDuration);
                applied = true;
            }

            if (Definition.SlowDuration > 0f && Definition.SlowMovementMultiplier < 0.999f)
            {
                attacker.ApplySlow(Definition.SlowMovementMultiplier, Definition.SlowDuration);
                applied = true;
            }

            return applied;
        }

        private bool HasCurrentSourceActivation()
        {
            return SourceFarmHand != null &&
                   SourceFarmHand.IsActivationSessionCurrent(SourceActivationSessionId);
        }

        /// <summary>
        /// Mirrors FarmHand's live targeting rule for projectiles that are already in flight and
        /// for splash targets: melee cannot cross any cover; ranged can cross Half cover; Full
        /// cover always blocks. The fixed buffer is deliberately conservative if a saturated query
        /// might otherwise hide a blocking wall.
        /// </summary>
        private static bool IsProjectilePathBlocked(
            Vector2 origin,
            Vector2 target,
            ProjectileType projectileType)
        {
            var delta = target - origin;
            var wallLayerMask = GetEnvironmentLayerMask();
            if (delta.sqrMagnitude <= 0.0001f || wallLayerMask == 0)
            {
                return false;
            }

            var distance = delta.magnitude;
            var hitCount = Physics2D.RaycastNonAlloc(
                origin,
                delta / distance,
                wallHitBuffer,
                distance,
                wallLayerMask);
            if (hitCount >= wallHitBuffer.Length)
            {
                ClearWallHitBuffer(hitCount);
                return true;
            }

            var isBlocked = false;
            for (var index = 0; index < hitCount; index++)
            {
                var hitCollider = wallHitBuffer[index].collider;
                wallHitBuffer[index] = default;
                var wall = hitCollider != null ? hitCollider.GetComponentInParent<FarmWall>() : null;
                if (wall != null && (projectileType != ProjectileType.Ranged || wall.BlocksAllAttacks))
                {
                    isBlocked = true;
                    break;
                }
            }

            ClearWallHitBuffer(hitCount);
            return isBlocked;
        }

        /// <summary>
        /// Resolves the Environment layer only when a projectile actually evaluates an attack path.
        /// Unity can construct a MonoBehaviour while creating an Inspector preview, where layer-name
        /// APIs are forbidden; resolving it from a static field initializer caused those previews to
        /// throw before the projectile was even launched.
        /// </summary>
        private static int GetEnvironmentLayerMask()
        {
            if (hasResolvedEnvironmentLayerMask)
            {
                return environmentLayerMask;
            }

            hasResolvedEnvironmentLayerMask = true;
            var environmentLayer = LayerMask.NameToLayer("Environment");
            environmentLayerMask = environmentLayer >= 0 ? 1 << environmentLayer : 0;
            return environmentLayerMask;
        }

        private static void ClearWallHitBuffer(int hitCount)
        {
            var clearCount = Mathf.Min(hitCount, wallHitBuffer.Length);
            for (var index = 0; index < clearCount; index++)
            {
                wallHitBuffer[index] = default;
            }
        }

        protected void ReturnToPool()
        {
            IsLaunched = false;
            pooledInstance ??= GetComponent<PooledInstance>();
            if (pooledInstance != null)
            {
                pooledInstance.ReturnToPool();
                return;
            }

            gameObject.SetActive(false);
        }

        protected static bool IsValidTarget(BasicAttacker attacker)
        {
            return attacker != null &&
                   attacker.isActiveAndEnabled &&
                   attacker.gameObject.activeInHierarchy &&
                   attacker.RemainingHealth > 0f &&
                   !attacker.HasReachedFoodStore;
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveComponents();
            CacheBaseLocalScale();
            ConfigurePhysics();
            ConfigureHitCollider();
            ResetRuntimeState();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            ResetRuntimeState();
        }

        private void ResolveComponents()
        {
            visual ??= GetComponent<SpriteRenderer>();
            hitCollider ??= GetComponent<Collider2D>();
            body ??= GetComponent<Rigidbody2D>();
            pooledInstance ??= GetComponent<PooledInstance>();
        }

        private void CacheBaseLocalScale()
        {
            if (!hasBaseLocalScale)
            {
                baseLocalScale = transform.localScale;
                hasBaseLocalScale = true;
            }
        }

        private void ConfigurePhysics()
        {
            if (body == null)
            {
                return;
            }

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.simulated = true;
        }

        private void ConfigureHitCollider()
        {
            if (hitCollider == null)
            {
                return;
            }

            hitCollider.isTrigger = true;
            if (hitCollider is not CircleCollider2D circleCollider)
            {
                return;
            }

            var largestWorldScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y));
            circleCollider.radius = largestWorldScale > Mathf.Epsilon
                ? hitRadius / largestWorldScale
                : hitRadius;
        }

        private void ResetRuntimeState()
        {
            IsLaunched = false;
            SourceFarmHand = null;
            SourceActivationSessionId = 0L;
            Target = null;
            Definition = null;
            AttackRange = 0f;
            Damage = 0f;
            areaImpactTargets.Clear();

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            if (hitCollider != null)
            {
                hitCollider.enabled = false;
            }

            if (visual != null)
            {
                visual.enabled = false;
            }

            if (hasBaseLocalScale)
            {
                transform.localScale = baseLocalScale;
            }

            ResetSpecializedState();
        }

        protected virtual void ResetSpecializedState()
        {
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            hitRadius = Mathf.Max(0.01f, hitRadius);
            ResolveComponents();
            CacheBaseLocalScale();
            ConfigurePhysics();
            ConfigureHitCollider();
        }
#endif
    }
}
