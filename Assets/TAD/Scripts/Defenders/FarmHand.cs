using System.Collections.Generic;
using TAD.Attackers;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Reusable defender token that owns its assigned weapon, range presentation, and attackers currently in attack range.
    /// Weapon selection and target-priority behaviour can be layered on without changing placement or pooling.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D))]
    public sealed class FarmHand : MonoBehaviour, IPoolLifecycle
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private WeaponDefinition weaponDefinition;
        [SerializeField] private CircleCollider2D rangeCollider;
        [SerializeField] private Rigidbody2D rangeBody;
        [SerializeField] private DefenderRangeVisual rangeVisual;

        private PooledInstance pooledInstance;
        private SpawnPool projectilePool;
        private readonly List<BasicAttacker> attackersInRange = new();
        private float nextFireTime;
        private bool isRangeIndicatorVisible;

        /// <summary>Visual used by the placement system to create a non-interactive preview.</summary>
        public SpriteRenderer Visual => visual;

        /// <summary>Shared weapon data currently assigned to this farmhand.</summary>
        public WeaponDefinition WeaponDefinition => weaponDefinition;

        /// <summary>
        /// Session-unique identity assigned by the pool when this defender is created.
        /// The value remains the same if this farmhand is later reused from the pool.
        /// </summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        /// <summary>World-space attack range supplied by the currently assigned weapon.</summary>
        public float Range => weaponDefinition != null ? weaponDefinition.Range : 0f;

        /// <summary>The non-blocking 2D trigger used to maintain the nearby attacker collection.</summary>
        public CircleCollider2D RangeCollider => rangeCollider;

        /// <summary>True while this defender's translucent range indicator is visible.</summary>
        public bool IsRangeIndicatorVisible => isRangeIndicatorVisible;

        /// <summary>
        /// Attackers currently inside this farmhand's range. The collection is pruned before every read
        /// so pooled or defeated animals cannot remain valid target candidates.
        /// </summary>
        public IReadOnlyList<BasicAttacker> AttackersInRange
        {
            get
            {
                PruneInvalidAttackers();
                return attackersInRange;
            }
        }

        /// <summary>Number of valid attackers available for a future target-selection policy.</summary>
        public int AttackersInRangeCount
        {
            get
            {
                PruneInvalidAttackers();
                return attackersInRange.Count;
            }
        }

        private void Awake()
        {
            ResolveComponents();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureRangeVisual();
        }

        private void OnEnable()
        {
            ClearAttackersInRange();
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureRangeVisual();
        }

        private void Update()
        {
            TryFireAtFirstAttackerInRange();
        }

        private void OnDisable()
        {
            ClearAttackersInRange();
            SetRangeIndicatorVisible(false);
        }

        /// <summary>Assigns a weapon and immediately updates this defender's detection and visible ranges.</summary>
        public void SetWeapon(WeaponDefinition definition)
        {
            weaponDefinition = definition;
            ClearAttackersInRange();
            nextFireTime = Time.time;
            ConfigureRangeCollider();
            ConfigureRangeVisual();
        }

        /// <summary>Shows or hides the red attack-range circle used when this defender is selected or targeted.</summary>
        public void SetRangeIndicatorVisible(bool visible)
        {
            isRangeIndicatorVisible = visible;
            ConfigureRangeVisual();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TrackAttacker(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Also catches an animal activated inside an already-active defender range.
            TrackAttacker(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var attacker = other.GetComponentInParent<BasicAttacker>();
            if (attacker != null)
            {
                attackersInRange.Remove(attacker);
            }
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveComponents();

            if (visual != null)
            {
                visual.enabled = true;
            }

            ClearAttackersInRange();
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureRangeVisual();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            ClearAttackersInRange();
            nextFireTime = 0f;
            SetRangeIndicatorVisible(false);
        }

        private void ResolveComponents()
        {
            visual ??= GetComponent<SpriteRenderer>();
            rangeCollider ??= GetComponent<CircleCollider2D>();
            rangeBody ??= GetComponent<Rigidbody2D>();
            rangeVisual ??= GetComponentInChildren<DefenderRangeVisual>(true);
            pooledInstance ??= GetComponent<PooledInstance>();
        }

        private void TrackAttacker(Collider2D other)
        {
            var attacker = other.GetComponentInParent<BasicAttacker>();
            if (!IsValidAttacker(attacker) || attackersInRange.Contains(attacker))
            {
                return;
            }

            attackersInRange.Add(attacker);
            TryFireAtFirstAttackerInRange();
        }

        private void TryFireAtFirstAttackerInRange()
        {
            if (Time.time < nextFireTime || !TryGetFirstAttackerInRange(out var target))
            {
                return;
            }

            var weapon = weaponDefinition;
            if (weapon == null || weapon.ProjectilePrefab == null || weapon.ProjectileSpeed <= 0f ||
                weapon.Damage <= 0f || weapon.ProjectileLifetime <= 0f)
            {
                return;
            }

            var pool = ResolveProjectilePool();
            if (pool == null)
            {
                return;
            }

            var origin = (Vector2)transform.position;
            var projectileObject = pool.Spawn(PooledObjectType.Projectile, origin, Quaternion.identity);
            if (projectileObject == null)
            {
                nextFireTime = Time.time + weapon.FireInterval;
                return;
            }

            var projectile = projectileObject.GetComponent<FarmHandProjectile>();
            if (projectile == null || !projectile.Launch(
                    origin,
                    target,
                    weapon.ProjectileSpeed,
                    weapon.Damage,
                    weapon.ProjectileLifetime))
            {
                ReturnProjectileToPool(projectileObject);
                nextFireTime = Time.time + weapon.FireInterval;
                return;
            }

            nextFireTime = Time.time + weapon.FireInterval;
        }

        private bool TryGetFirstAttackerInRange(out BasicAttacker attacker)
        {
            PruneInvalidAttackers();
            if (attackersInRange.Count > 0)
            {
                attacker = attackersInRange[0];
                return true;
            }

            attacker = null;
            return false;
        }

        private SpawnPool ResolveProjectilePool()
        {
            if (projectilePool != null)
            {
                return projectilePool;
            }

            pooledInstance ??= GetComponent<PooledInstance>();
            projectilePool = pooledInstance != null ? pooledInstance.Owner : null;
            projectilePool ??= Object.FindAnyObjectByType<SpawnPool>();
            return projectilePool;
        }

        private static void ReturnProjectileToPool(GameObject projectileObject)
        {
            if (projectileObject == null)
            {
                return;
            }

            var pooledProjectile = projectileObject.GetComponent<PooledInstance>();
            if (pooledProjectile != null)
            {
                pooledProjectile.ReturnToPool();
                return;
            }

            projectileObject.SetActive(false);
        }

        private void ConfigureRangeBody()
        {
            if (rangeBody == null)
            {
                return;
            }

            rangeBody.bodyType = RigidbodyType2D.Kinematic;
            rangeBody.gravityScale = 0f;
            rangeBody.useFullKinematicContacts = true;
            rangeBody.constraints = RigidbodyConstraints2D.FreezeAll;
            rangeBody.simulated = true;
        }

        private void ConfigureRangeCollider()
        {
            if (rangeCollider == null)
            {
                return;
            }

            var worldRange = Range;
            var largestWorldScale = Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Abs(transform.lossyScale.y));

            rangeCollider.isTrigger = true;
            rangeCollider.radius = largestWorldScale > Mathf.Epsilon
                ? worldRange / largestWorldScale
                : 0f;
            rangeCollider.enabled = worldRange > 0f && largestWorldScale > Mathf.Epsilon;
        }

        private void ConfigureRangeVisual()
        {
            if (rangeVisual == null)
            {
                return;
            }

            var sortingLayerId = visual != null ? visual.sortingLayerID : 0;
            var sortingOrder = visual != null ? visual.sortingOrder - 1 : 0;
            rangeVisual.Configure(Range, sortingLayerId, sortingOrder, isRangeIndicatorVisible);
        }

        private void ClearAttackersInRange()
        {
            attackersInRange.Clear();
        }

        private void PruneInvalidAttackers()
        {
            for (var index = attackersInRange.Count - 1; index >= 0; index--)
            {
                if (!IsValidAttacker(attackersInRange[index]))
                {
                    attackersInRange.RemoveAt(index);
                }
            }
        }

        private static bool IsValidAttacker(BasicAttacker attacker)
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
            ResolveComponents();
            ConfigureRangeBody();
            ConfigureRangeCollider();
        }

        private void OnDrawGizmosSelected()
        {
            if (Range <= 0f)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, Range);
        }
#endif
    }
}
