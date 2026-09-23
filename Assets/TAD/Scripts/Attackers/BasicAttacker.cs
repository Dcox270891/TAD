using System.Collections.Generic;
using TAD.Defenders;
using TAD.Environment;
using TAD.Navigation;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Moves a basic animal attacker and reports its remaining health to the Food Store on arrival.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PolygonCollider2D), typeof(Rigidbody2D))]
    public sealed class BasicAttacker : MonoBehaviour, IFoodStoreRaider, IPoolLifecycle
    {
        private const float WaypointArrivalDistance = 0.04f;

        [SerializeField] private AttackerDefinition definition;
        [SerializeField] private Vector2 startingMoveDirection = Vector2.right;

        private Rigidbody2D body;
        private PooledInstance pooledInstance;
        private FarmRouteMapper routeMapper;
        private Vector2 moveDirection;
        private float currentHealth;
        private bool hasReachedFoodStore;
        private readonly List<Vector3> routePoints = new();
        private readonly HashSet<FarmHand> helpedToKillDefenders = new();
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
        public bool HasReachedFoodStore => hasReachedFoodStore;
        public float Size => definition != null ? definition.Size : 0f;
        public int SendCost => definition != null ? definition.SendCost : 0;
        public int WaveIncome => definition != null ? definition.WaveIncome : 0;
        public int ExpGiven => definition != null ? definition.ExpGiven : 0;
        public int KillGain => definition != null ? definition.KillGain : 0;
        public int SendSize => definition != null ? definition.SendSize : 0;

        /// <summary>Unique defenders that have dealt a non-lethal hit during this attacker activation.</summary>
        public IReadOnlyCollection<FarmHand> HelpedToKillDefenders => helpedToKillDefenders;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            pooledInstance = GetComponent<PooledInstance>();
            ResetRuntimeState();
        }

        private void FixedUpdate()
        {
            if (definition == null || hasReachedFoodStore || currentHealth <= 0f)
            {
                return;
            }

            routeMapper ??= Object.FindAnyObjectByType<FarmRouteMapper>();
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

            var nextPosition = body.position + (moveDirection * definition.MovementSpeed * Time.fixedDeltaTime);
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
        /// Applies damage from a defender and reports whether damage was accepted, whether it was lethal,
        /// and whether this defender earned a new helped-to-kill credit.
        /// </summary>
        public bool TryTakeDamage(float damage, FarmHand defender, out bool wasDefeated, out bool helpedToKillAdded)
        {
            wasDefeated = false;
            helpedToKillAdded = false;
            if (damage <= 0f || currentHealth <= 0f)
            {
                return false;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            if (currentHealth > 0f)
            {
                helpedToKillAdded = defender != null && helpedToKillDefenders.Add(defender);
                return true;
            }

            wasDefeated = true;
            if (defender != null)
            {
                // A defender with an existing assisted-kill credit receives the killing-blow reward instead.
                helpedToKillDefenders.Remove(defender);
            }

            ReturnToPool();
            return true;
        }

        public void OnFoodStoreReached(FoodStore foodStore, float foodConsumed)
        {
            if (hasReachedFoodStore)
            {
                return;
            }

            hasReachedFoodStore = true;
            ReturnToPool();
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
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }

        private void ResetRuntimeState()
        {
            currentHealth = definition != null ? definition.MaximumHealth : 0f;
            hasReachedFoodStore = false;
            moveDirection = startingMoveDirection.sqrMagnitude > 0f ? startingMoveDirection.normalized : Vector2.right;
            routePoints.Clear();
            helpedToKillDefenders.Clear();
            nextRoutePointIndex = 0;

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
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
                !routeMapper.TryGetFastestRoute(body.position, Size, routePoints) ||
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
            return routeMapper != null && routeMapper.TryGetNextDirection(currentPosition, Size, out direction);
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
