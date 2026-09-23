using TAD.Attackers;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// A pooled hand projectile that travels on its launch direction until it hits an attacker or expires.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D))]
    public sealed class FarmHandProjectile : MonoBehaviour, IPoolLifecycle
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private CircleCollider2D hitCollider;
        [SerializeField] private Rigidbody2D body;
        [SerializeField, Min(0.01f)]
        [Tooltip("World-space hit radius used by this compact thrown hand.")]
        private float hitRadius = 0.15f;

        private PooledInstance pooledInstance;
        private FarmHand firingFarmHand;
        private Vector2 travelDirection;
        private float travelSpeed;
        private float damage;
        private float remainingLifetime;
        private bool isLaunched;

        /// <summary>True after a defender has launched this pooled projectile and before it returns to the pool.</summary>
        public bool IsLaunched => isLaunched;

        /// <summary>Session-unique identity assigned by the owning pool.</summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        private void Awake()
        {
            ResolveComponents();
            ConfigurePhysics();
            ConfigureHitCollider();
            ResetRuntimeState();
        }

        private void OnEnable()
        {
            ResolveComponents();
            ConfigurePhysics();
            ConfigureHitCollider();
            ResetRuntimeState();
        }

        private void FixedUpdate()
        {
            if (!isLaunched || body == null)
            {
                return;
            }

            body.MovePosition(body.position + (travelDirection * travelSpeed * Time.fixedDeltaTime));
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                ReturnToPool();
            }
        }

        /// <summary>Starts a straight, non-homing shot from a farmhand centre toward the selected attacker.</summary>
        public bool Launch(FarmHand sourceFarmHand, Vector2 origin, BasicAttacker target, float speed, float damageAmount, float lifetime)
        {
            if (sourceFarmHand == null || !IsValidTarget(target) || speed <= 0f || damageAmount <= 0f || lifetime <= 0f)
            {
                return false;
            }

            ResolveComponents();
            ConfigurePhysics();
            ConfigureHitCollider();
            if (body == null)
            {
                return false;
            }

            var delta = target.CurrentPosition - origin;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            firingFarmHand = sourceFarmHand;
            travelDirection = delta.normalized;
            travelSpeed = speed;
            damage = damageAmount;
            remainingLifetime = lifetime;
            isLaunched = true;

            body.position = origin;
            body.linearVelocity = Vector2.zero;
            var angle = Mathf.Atan2(travelDirection.y, travelDirection.x) * Mathf.Rad2Deg - 90f;
            body.rotation = angle;
            transform.position = new Vector3(origin.x, origin.y, transform.position.z);

            if (visual != null)
            {
                visual.enabled = true;
            }

            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!isLaunched)
            {
                return;
            }

            var attacker = other.GetComponentInParent<BasicAttacker>();
            if (!IsValidTarget(attacker))
            {
                return;
            }

            var expGiven = attacker.ExpGiven;
            var killGain = attacker.KillGain;
            if (attacker.TryTakeDamage(
                    damage,
                    firingFarmHand,
                    out var wasDefeated,
                    out var helpedToKillAdded,
                    out var killingBlowReplacedHelpedToKill))
            {
                if (wasDefeated)
                {
                    firingFarmHand?.AwardKillingBlowReward(
                        expGiven,
                        killGain,
                        killingBlowReplacedHelpedToKill);
                }
                else if (helpedToKillAdded)
                {
                    firingFarmHand?.AwardHelpedToKillExperience(expGiven);
                }
            }

            ReturnToPool();
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveComponents();
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
            hitCollider ??= GetComponent<CircleCollider2D>();
            body ??= GetComponent<Rigidbody2D>();
            pooledInstance ??= GetComponent<PooledInstance>();
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

            var largestWorldScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y));
            hitCollider.isTrigger = true;
            hitCollider.radius = largestWorldScale > Mathf.Epsilon
                ? hitRadius / largestWorldScale
                : hitRadius;
            hitCollider.enabled = true;
        }

        private void ResetRuntimeState()
        {
            isLaunched = false;
            firingFarmHand = null;
            travelDirection = Vector2.zero;
            travelSpeed = 0f;
            damage = 0f;
            remainingLifetime = 0f;

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            if (visual != null)
            {
                visual.enabled = false;
            }
        }

        private void ReturnToPool()
        {
            isLaunched = false;
            pooledInstance ??= GetComponent<PooledInstance>();
            if (pooledInstance != null)
            {
                pooledInstance.ReturnToPool();
                return;
            }

            gameObject.SetActive(false);
        }

        private static bool IsValidTarget(BasicAttacker attacker)
        {
            return attacker != null &&
                   attacker.isActiveAndEnabled &&
                   attacker.gameObject.activeInHierarchy &&
                   attacker.RemainingHealth > 0f &&
                   !attacker.HasReachedFoodStore;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            hitRadius = Mathf.Max(0.01f, hitRadius);
            ResolveComponents();
            ConfigurePhysics();
            ConfigureHitCollider();
        }
#endif
    }
}
