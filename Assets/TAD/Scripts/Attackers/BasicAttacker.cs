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
    [RequireComponent(typeof(BoxCollider2D), typeof(Rigidbody2D))]
    public sealed class BasicAttacker : MonoBehaviour, IFoodStoreRaider, IPoolLifecycle
    {
        [SerializeField] private AttackerDefinition definition;
        [SerializeField] private Vector2 startingMoveDirection = Vector2.right;

        private Rigidbody2D body;
        private PooledInstance pooledInstance;
        private FarmRouteMapper routeMapper;
        private Vector2 moveDirection;
        private float currentHealth;
        private bool hasReachedFoodStore;

        public AttackerDefinition Definition => definition;
        public string AttackerId => definition != null ? definition.AttackerId : string.Empty;
        public string DisplayName => definition != null ? definition.DisplayName : string.Empty;
        public float RemainingHealth => currentHealth;
        public bool HasReachedFoodStore => hasReachedFoodStore;
        public float Size => definition != null ? definition.Size : 0f;
        public int SendCost => definition != null ? definition.SendCost : 0;
        public int WaveIncome => definition != null ? definition.WaveIncome : 0;

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
            if (routeMapper != null)
            {
                if (!routeMapper.TryGetNextDirection(body.position, Size, out var mappedDirection))
                {
                    return;
                }

                moveDirection = mappedDirection;
            }

            if (moveDirection == Vector2.zero)
            {
                return;
            }

            body.MovePosition(body.position + (moveDirection * definition.MovementSpeed * Time.fixedDeltaTime));
        }

        /// <summary>Assigns the map used to navigate this attacker toward the Food Store.</summary>
        public void SetRouteMapper(FarmRouteMapper mapper)
        {
            routeMapper = mapper;
        }

        /// <summary>Sets the current path direction. A zero vector stops the attacker.</summary>
        public void SetMoveDirection(Vector2 direction)
        {
            moveDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.zero;
        }

        /// <summary>Applies damage and returns true when this attacker is defeated.</summary>
        public bool TakeDamage(float damage)
        {
            if (damage <= 0f || currentHealth <= 0f)
            {
                return false;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            if (currentHealth > 0f)
            {
                return false;
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

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
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
