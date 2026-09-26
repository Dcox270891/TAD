using TAD.Attackers;
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
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Collider2D hitCollider;
        [SerializeField] private Rigidbody2D body;
        [SerializeField, Min(0.01f)]
        [Tooltip("World-space hit radius used by circular projectile prefabs.")]
        private float hitRadius = 0.15f;

        private PooledInstance pooledInstance;
        private Vector3 baseLocalScale;
        private bool hasBaseLocalScale;

        protected FarmHand SourceFarmHand { get; private set; }
        protected BasicAttacker Target { get; private set; }
        protected ProjectileDefinition Definition { get; private set; }
        protected float AttackRange { get; private set; }
        protected float Damage { get; private set; }
        protected Rigidbody2D Body => body;
        protected Collider2D HitCollider => hitCollider;
        protected Vector3 BaseLocalScale => baseLocalScale;
        public bool IsLaunched { get; private set; }
        public FarmHand Source => SourceFarmHand;
        public ProjectileDefinition ProjectileDefinition => Definition;
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        protected abstract ProjectileBehavior ExpectedProjectileBehavior { get; }

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
            if (sourceFarmHand == null || definition == null || definition.ProjectileBehavior != ExpectedProjectileBehavior ||
                !IsValidTarget(target) || definition.ProjectileSpeed <= 0f || damageAmount <= 0f ||
                (definition.UsesTimedLifetime && definition.ProjectileLifetime <= 0f))
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
            Target = target;
            Definition = definition;
            AttackRange = Mathf.Max(0f, attackRange);
            Damage = damageAmount;
            body.position = origin;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            transform.position = new Vector3(origin.x, origin.y, transform.position.z);
            transform.localScale = baseLocalScale;

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
            return IsValidTarget(attacker) ? attacker : null;
        }

        protected bool TryDamageAttacker(BasicAttacker attacker)
        {
            return IsValidTarget(attacker) && SourceFarmHand != null &&
                   SourceFarmHand.TryDamageAttacker(attacker, Damage);
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
            Target = null;
            Definition = null;
            AttackRange = 0f;
            Damage = 0f;

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