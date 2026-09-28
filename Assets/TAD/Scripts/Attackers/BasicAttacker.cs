using System.Collections.Generic;
using TAD.Defenders;
using TAD.Environment;
using TAD.Navigation;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Moves a reusable animal attacker toward the Food Store and reports its remaining health on arrival.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PolygonCollider2D), typeof(Rigidbody2D))]
    public sealed class BasicAttacker : MonoBehaviour, IFoodStoreRaider, IPoolLifecycle
    {
        private const float WaypointArrivalDistance = 0.04f;

        [SerializeField] private AttackerDefinition definition;
        [SerializeField] private Vector2 startingMoveDirection = Vector2.right;

        private readonly List<Vector3> routePoints = new();
        private readonly HashSet<FarmHand> helpedToKillDefenders = new();
        private readonly List<Collider2D> ignoredHalfCoverColliders = new();
        // Physical farmhand colliders are never navigation obstacles. The actual range and projectile colliders
        // remain triggers, so they are deliberately left alone to preserve targeting and damage callbacks.
        private readonly List<Collider2D> ignoredFarmHandColliders = new();
        // Animals follow shared routes and should never physically queue or push one another.
        private readonly List<Collider2D> ignoredAttackerColliders = new();

        private Rigidbody2D body;
        private PolygonCollider2D bodyCollider;
        private PooledInstance pooledInstance;
        private FarmRouteMapper routeMapper;
        private AttackerVisualController visualController;
        private Vector2 moveDirection;
        private float currentHealth;
        private bool hasReachedFoodStore;
        private bool isDefeated;
        private int nextRoutePointIndex;

        public AttackerDefinition Definition => definition;
        public string AttackerId => definition != null ? definition.AttackerId : string.Empty;
        public string DisplayName => definition != null ? definition.DisplayName : string.Empty;

        /// <summary>
        /// Session-unique identity assigned by the pool when this attacker is created.
        /// This is separate from <see cref="AttackerId"/>, which identifies the attacker type.
        /// </summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        public float RemainingHealth => currentHealth;
        public float MaximumHealth => definition != null ? definition.MaximumHealth : 0f;
        public float MovementSpeed => definition != null ? definition.MovementSpeed : 0f;
        public bool HasReachedFoodStore => hasReachedFoodStore;

        /// <summary>Raised whenever this pooled attacker's health is reset or changed.</summary>
        public event System.Action<float, float> HealthChanged;
        public bool IsDefeated => isDefeated;
        public float Size => definition != null ? definition.Size : 0f;
        public int SendCost => definition != null ? definition.SendCost : 0;
        public int WaveIncome => definition != null ? definition.WaveIncome : 0;
        public float CombatRating => definition != null ? definition.CombatRating : 0f;
        public int ExpGiven => definition != null ? definition.ExpGiven : 0;
        public int KillGain => definition != null ? definition.KillGain : 0;
        public int SendSize => definition != null ? definition.SendSize : 0;
        public AttackerTraversalProfile TraversalProfile => definition != null
            ? definition.TraversalProfile
            : AttackerTraversalProfile.Ground;

        /// <summary>The latest normalized route direction, used by directional visual presentation.</summary>
        public Vector2 MoveDirection => moveDirection;

        /// <summary>Unique defenders that have dealt a non-lethal hit during this attacker activation.</summary>
        public IReadOnlyCollection<FarmHand> HelpedToKillDefenders => helpedToKillDefenders;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<PolygonCollider2D>();
            pooledInstance = GetComponent<PooledInstance>();
            visualController = GetComponentInChildren<AttackerVisualController>(true);
            ResetRuntimeState();
        }

        private void OnEnable()
        {
            FarmHand.FarmHandEnabled += HandleFarmHandEnabled;
            // IgnoreCollision state is reset by Unity when a pooled object is reactivated.
            ConfigureTraversalCollisions();
        }

        private void OnDisable()
        {
            FarmHand.FarmHandEnabled -= HandleFarmHandEnabled;
            RestoreTraversalCollisions();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            IgnoreFarmHandCollision(collision);
            IgnoreAttackerCollision(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            // Covers a farmhand or another attacker created after this attacker has already begun moving.
            IgnoreFarmHandCollision(collision);
            IgnoreAttackerCollision(collision);
        }

        private void FixedUpdate()
        {
            if (definition == null || hasReachedFoodStore || isDefeated || currentHealth <= 0f)
            {
                return;
            }

            routeMapper ??= pooledInstance != null && pooledInstance.Owner != null
                ? pooledInstance.Owner.RouteMapper
                : Object.FindAnyObjectByType<FarmRouteMapper>();
            if (routeMapper == null || !TryClampToVisibleMap())
            {
                return;
            }

            if (!TryGetRouteDirection(body.position, out var mappedDirection))
            {
                return;
            }

            moveDirection = mappedDirection;
            if (moveDirection == Vector2.zero)
            {
                return;
            }

            // Clamp this physics step to the cached waypoint. Faster animals can otherwise move from one
            // side of a narrow arrival radius to the other without ever entering it, causing a permanent
            // oscillation at the waypoint.
            var waypoint = (Vector2)routePoints[nextRoutePointIndex];
            var maximumMoveDistance = Mathf.Max(0f, definition.MovementSpeed) * Time.fixedDeltaTime;
            var nextPosition = Vector2.MoveTowards(body.position, waypoint, maximumMoveDistance);
            if (routeMapper.TryClampToVisibleMap(nextPosition, out var constrainedNextPosition))
            {
                body.MovePosition(constrainedNextPosition);
            }
        }

        /// <summary>Assigns the map used to navigate this attacker toward the Food Store.</summary>
        public void SetRouteMapper(FarmRouteMapper mapper)
        {
            routeMapper = mapper;
            TryClampToVisibleMap();
            BuildCachedRoute();
        }

        /// <summary>
        /// Assigns this reusable attacker its shared data and resets all per-activation state.
        /// Pool setups normally serialize this reference; this method also supports data-driven runtime spawners.
        /// </summary>
        public void ConfigureDefinition(AttackerDefinition attackerDefinition)
        {
            definition = attackerDefinition;
            ResetRuntimeState();
            BuildCachedRoute();
        }

        /// <summary>Constrain this active animal to the mapper's visible play area.</summary>
        public bool TryClampToVisibleMap()
        {
            if (body == null || routeMapper == null || !routeMapper.TryClampToVisibleMap(body.position, out var constrainedPosition))
            {
                return false;
            }

            if (body.position != constrainedPosition)
            {
                body.position = constrainedPosition;
            }

            return true;
        }

        /// <summary>The position currently used by this attacker's physics body.</summary>
        public Vector2 CurrentPosition => body != null ? body.position : (Vector2)transform.position;

        /// <summary>Sets the current path direction. A zero vector stops the attacker.</summary>
        public void SetMoveDirection(Vector2 direction)
        {
            moveDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.zero;
        }

        /// <summary>Applies damage and returns true when this attacker is defeated.</summary>
        public bool TakeDamage(float damage)
        {
            TryTakeDamage(damage, null, out var wasDefeated, out _);
            return wasDefeated;
        }

        /// <summary>
        /// Applies damage from a defender. Every distinct defender that lands a non-lethal hit is recorded as an
        /// assist recipient. On defeat, the returned snapshot excludes the killer so assist XP can be shared fairly
        /// while the killer receives the entire unallocated remainder.
        /// </summary>
        public bool TryTakeDamage(
            float damage,
            FarmHand defender,
            out bool wasDefeated,
            out FarmHand[] assistRecipients)
        {
            wasDefeated = false;
            assistRecipients = System.Array.Empty<FarmHand>();
            if (damage <= 0f || currentHealth <= 0f || isDefeated)
            {
                return false;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            NotifyHealthChanged();
            if (currentHealth > 0f)
            {
                // A tower receives one assist share for this attacker activation, regardless of how many times it hits.
                if (defender != null && defender.CanEarnExperience)
                {
                    helpedToKillDefenders.Add(defender);
                }

                return true;
            }

            wasDefeated = true;
            if (defender != null)
            {
                // A killing defender receives the remaining kill reward rather than a second assist allocation.
                helpedToKillDefenders.Remove(defender);
            }

            if (helpedToKillDefenders.Count > 0)
            {
                assistRecipients = new FarmHand[helpedToKillDefenders.Count];
                helpedToKillDefenders.CopyTo(assistRecipients);
            }

            helpedToKillDefenders.Clear();
            BeginDefeat();
            return true;
        }

        public void OnFoodStoreReached(FoodStore foodStore, float foodConsumed)
        {
            if (hasReachedFoodStore || isDefeated)
            {
                return;
            }

            hasReachedFoodStore = true;
            ReturnToPool();
        }

        /// <summary>Called by the visual controller once the final death sprite has been displayed.</summary>
        public void CompleteDeathAnimation()
        {
            if (isDefeated)
            {
                ReturnToPool();
            }
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            if (pooledInstance == null)
            {
                pooledInstance = GetComponent<PooledInstance>();
            }

            ResetRuntimeState();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            RestoreTraversalCollisions();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }
        }

        private void ResetRuntimeState()
        {
            currentHealth = definition != null ? definition.MaximumHealth : 0f;
            hasReachedFoodStore = false;
            isDefeated = false;
            moveDirection = startingMoveDirection.sqrMagnitude > 0f ? startingMoveDirection.normalized : Vector2.right;
            routePoints.Clear();
            helpedToKillDefenders.Clear();
            nextRoutePointIndex = 0;

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = true;
            }

            if (bodyCollider != null)
            {
                bodyCollider.enabled = true;
            }

            ConfigureTraversalCollisions();
            NotifyHealthChanged();
        }

        private void NotifyHealthChanged()
        {
            HealthChanged?.Invoke(currentHealth, MaximumHealth);
        }

        /// <summary>
        /// Copies the map's already-computed fastest route once per pooled activation.
        /// Following these sparse waypoints avoids oscillating between adjacent flow-field cells at wall corners.
        /// </summary>
        private bool BuildCachedRoute()
        {
            routePoints.Clear();
            nextRoutePointIndex = 0;

            if (body == null || routeMapper == null ||
                !routeMapper.TryGetFastestRoute(body.position, Size, TraversalProfile, routePoints) ||
                routePoints.Count < 2)
            {
                return false;
            }

            nextRoutePointIndex = 1;
            return true;
        }

        private bool TryGetRouteDirection(Vector2 currentPosition, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (routePoints.Count < 2 && !BuildCachedRoute())
            {
                return false;
            }

            var arrivalDistanceSquared = WaypointArrivalDistance * WaypointArrivalDistance;
            while (nextRoutePointIndex < routePoints.Count)
            {
                var delta = (Vector2)routePoints[nextRoutePointIndex] - currentPosition;
                if (delta.sqrMagnitude <= arrivalDistanceSquared)
                {
                    nextRoutePointIndex++;
                    continue;
                }

                direction = delta.normalized;
                return true;
            }

            // The Food Store trigger normally returns an attacker before it reaches this fallback.
            // Keeping it preserves movement when a route ends immediately beside a small target collider.
            return routeMapper != null && routeMapper.TryGetNextDirection(
                currentPosition,
                Size,
                TraversalProfile,
                out direction);
        }

        private void BeginDefeat()
        {
            if (isDefeated)
            {
                return;
            }

            isDefeated = true;
            moveDirection = Vector2.zero;
            routePoints.Clear();
            nextRoutePointIndex = 0;

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }

            if (bodyCollider != null)
            {
                bodyCollider.enabled = false;
            }

            visualController ??= GetComponentInChildren<AttackerVisualController>(true);
            if (visualController == null || !visualController.TryPlayDeathAnimation(this))
            {
                CompleteDeathAnimation();
            }
        }

        private void ConfigureTraversalCollisions()
        {
            RestoreTraversalCollisions();
            ConfigureAttackerCollisionSafety();
            ConfigureFarmHandCollisionSafety();

            if (bodyCollider == null || TraversalProfile != AttackerTraversalProfile.IgnoresHalfCover)
            {
                return;
            }

            var walls = Object.FindObjectsByType<FarmWall>(FindObjectsInactive.Exclude);
            foreach (var wall in walls)
            {
                if (wall == null || wall.Cover != FarmWallCover.Half ||
                    !wall.TryGetComponent<BoxCollider2D>(out var wallCollider))
                {
                    continue;
                }

                Physics2D.IgnoreCollision(bodyCollider, wallCollider, true);
                ignoredHalfCoverColliders.Add(wallCollider);
            }
        }

        /// <summary>
        /// Preserves the rule that farmhands can target animals but cannot form a solid wall.
        /// Only unexpected non-trigger colliders on a farmhand hierarchy are ignored. Trigger colliders
        /// (the range sensor and pooled projectile hitboxes) continue to receive their normal callbacks.
        /// </summary>
        private void ConfigureFarmHandCollisionSafety()
        {
            if (bodyCollider == null)
            {
                return;
            }

            var farmHands = Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude);
            foreach (var farmHand in farmHands)
            {
                IgnoreFarmHandColliders(farmHand);
            }
        }

        private void HandleFarmHandEnabled(FarmHand farmHand)
        {
            // Farmhands can be placed while a wave is in progress, after this attacker was activated.
            IgnoreFarmHandColliders(farmHand);
        }

        private void IgnoreFarmHandColliders(FarmHand farmHand)
        {
            if (bodyCollider == null || farmHand == null)
            {
                return;
            }

            var farmHandColliders = farmHand.GetComponentsInChildren<Collider2D>(true);
            foreach (var farmHandCollider in farmHandColliders)
            {
                IgnoreFarmHandCollider(farmHandCollider);
            }
        }

        private void ConfigureAttackerCollisionSafety()
        {
            if (bodyCollider == null)
            {
                return;
            }

            var attackers = Object.FindObjectsByType<BasicAttacker>(FindObjectsInactive.Exclude);
            foreach (var attacker in attackers)
            {
                if (attacker == null || attacker == this)
                {
                    continue;
                }

                var attackerColliders = attacker.GetComponentsInChildren<Collider2D>(true);
                foreach (var attackerCollider in attackerColliders)
                {
                    IgnoreAttackerCollider(attackerCollider);
                }
            }
        }

        private void IgnoreFarmHandCollision(Collision2D collision)
        {
            if (collision == null)
            {
                return;
            }

            // Unity exposes both sides of a 2D collision. One is this attacker and the other may be a farmhand.
            IgnoreFarmHandCollider(collision.collider);
            IgnoreFarmHandCollider(collision.otherCollider);
        }

        private void IgnoreAttackerCollision(Collision2D collision)
        {
            if (collision == null)
            {
                return;
            }

            IgnoreAttackerCollider(collision.collider);
            IgnoreAttackerCollider(collision.otherCollider);
        }

        private void IgnoreFarmHandCollider(Collider2D collider)
        {
            if (bodyCollider == null || collider == null || collider == bodyCollider || collider.isTrigger ||
                ignoredFarmHandColliders.Contains(collider) || collider.GetComponentInParent<FarmHand>() == null)
            {
                return;
            }

            Physics2D.IgnoreCollision(bodyCollider, collider, true);
            ignoredFarmHandColliders.Add(collider);
        }

        private void IgnoreAttackerCollider(Collider2D collider)
        {
            if (bodyCollider == null || collider == null || collider == bodyCollider || collider.isTrigger ||
                ignoredAttackerColliders.Contains(collider) || collider.GetComponentInParent<BasicAttacker>() == null)
            {
                return;
            }

            Physics2D.IgnoreCollision(bodyCollider, collider, true);
            ignoredAttackerColliders.Add(collider);
        }

        private void RestoreTraversalCollisions()
        {
            if (bodyCollider == null)
            {
                ignoredHalfCoverColliders.Clear();
                ignoredFarmHandColliders.Clear();
                ignoredAttackerColliders.Clear();
                return;
            }

            foreach (var wallCollider in ignoredHalfCoverColliders)
            {
                if (wallCollider != null)
                {
                    Physics2D.IgnoreCollision(bodyCollider, wallCollider, false);
                }
            }

            foreach (var farmHandCollider in ignoredFarmHandColliders)
            {
                if (farmHandCollider != null)
                {
                    Physics2D.IgnoreCollision(bodyCollider, farmHandCollider, false);
                }
            }

            foreach (var attackerCollider in ignoredAttackerColliders)
            {
                if (attackerCollider != null)
                {
                    Physics2D.IgnoreCollision(bodyCollider, attackerCollider, false);
                }
            }

            ignoredHalfCoverColliders.Clear();
            ignoredFarmHandColliders.Clear();
            ignoredAttackerColliders.Clear();
        }

        private void ReturnToPool()
        {
            if (pooledInstance == null)
            {
                pooledInstance = GetComponent<PooledInstance>();
            }

            if (pooledInstance != null)
            {
                pooledInstance.ReturnToPool();
                return;
            }

            gameObject.SetActive(false);
        }
    }
}
