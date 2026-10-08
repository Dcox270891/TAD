using System.Collections.Generic;
using TAD.Defenders;
using TAD.Environment;
using TAD.Match;
using TAD.Navigation;
using TAD.Pooling;
using TAD.Progression;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Identifies the persistent damage effect currently applied by a defender projectile.
    /// Bleed and poison share the same pooled attacker implementation but remain distinct for
    /// presentation, balancing, and future resistance rules.
    /// </summary>
    public enum AttackerDamageOverTimeType
    {
        None = 0,
        Bleed = 1,
        Poison = 2
    }

    /// <summary>
    /// Moves a reusable animal attacker toward the Food Store and reports its remaining health on arrival.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PolygonCollider2D), typeof(Rigidbody2D))]
    public sealed class BasicAttacker : MonoBehaviour, IFoodStoreRaider, IPoolLifecycle
    {
        private const float WaypointArrivalDistance = 0.04f;
        private const float DamageOverTimeTickInterval = 0.25f;

        private struct ActiveDamageOverTime
        {
            public FarmHand Source;
            public long SourceActivationSessionId;
            public float DamagePerSecond;
            public float RemainingDuration;
            public float TickAccumulator;

            public bool IsActive => DamagePerSecond > 0f && RemainingDuration > 0f;

            public void Clear()
            {
                Source = null;
                SourceActivationSessionId = 0L;
                DamagePerSecond = 0f;
                RemainingDuration = 0f;
                TickAccumulator = 0f;
            }
        }

        private struct ActiveSlow
        {
            public float Multiplier;
            public float RemainingDuration;
        }

        [SerializeField] private AttackerDefinition definition;
        [SerializeField] private Vector2 startingMoveDirection = Vector2.right;

        private readonly List<Vector3> routePoints = new();
        private readonly HashSet<FarmHand> helpedToKillDefenders = new();
        private readonly Dictionary<FarmHand, long> helpedToKillActivationSessions = new();
        private readonly List<Collider2D> ignoredHalfCoverColliders = new();
        // Physical farmhand colliders are never navigation obstacles. The actual range and projectile colliders
        // remain triggers, so they are deliberately left alone to preserve targeting and damage callbacks.
        private readonly List<Collider2D> ignoredFarmHandColliders = new();
        // Animals follow shared routes and should never physically queue or push one another.
        private readonly List<Collider2D> ignoredAttackerColliders = new();
        // Terrain owns its authored values; the attacker owns the transient overlap state so pooled
        // instances cannot retain a modifier from the location where they were last used.
        private readonly List<TerrainSpeedModifier> activeTerrainSpeedModifiers = new();

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
        private ActiveDamageOverTime bleedEffect;
        private ActiveDamageOverTime poisonEffect;
        private float stunRemainingDuration;
        private readonly List<ActiveSlow> activeSlows = new();

        public AttackerDefinition Definition => definition;
        public string AttackerId => definition != null ? definition.AttackerId : string.Empty;
        public string DisplayName => definition != null ? definition.DisplayName : string.Empty;

        /// <summary>
        /// Session-unique identity assigned by the pool when this attacker is created.
        /// This is separate from <see cref="AttackerId"/>, which identifies the attacker type.
        /// </summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        public float RemainingHealth => currentHealth;
        public float MaximumHealth => definition != null
            ? PlayerUpgradeProgression.GetEffectiveAttackerHealth(GetPlayerAttackerUpgradeProfile(), definition)
            : 0f;
        public float MovementSpeed => definition != null
            ? PlayerUpgradeProgression.GetEffectiveAttackerSpeed(GetPlayerAttackerUpgradeProfile(), definition)
            : 0f;
        /// <summary>Current movement speed after active stun and slow effects are applied.</summary>
        public float EffectiveMovementSpeed => IsStunned
            ? 0f
            : MovementSpeed * GetSlowMultiplier() * TerrainMovementMultiplier;
        /// <summary>Combined multiplier from the floor terrain currently beneath this ground attacker.</summary>
        public float TerrainMovementMultiplier => GetTerrainMovementMultiplier();
        public bool HasReachedFoodStore => hasReachedFoodStore;

        /// <summary>Raised whenever this pooled attacker's health is reset or changed.</summary>
        public event System.Action<float, float> HealthChanged;
        public bool IsDefeated => isDefeated;
        public float Size => definition != null ? definition.Size : 0f;
        public int SendCost => definition != null
            ? PlayerUpgradeProgression.GetEffectiveAttackerCost(GetPlayerAttackerUpgradeProfile(), definition)
            : 0;
        public int WaveIncome => definition != null
            ? PlayerUpgradeProgression.GetEffectiveAttackerIncome(GetPlayerAttackerUpgradeProfile(), definition)
            : 0;
        public float CombatRating => definition != null
            ? AttackerCombatRating.Calculate(definition, GetPlayerAttackerUpgradeProfile())
            : 0f;
        public int ExpGiven => definition != null
            ? PlayerUpgradeProgression.GetEffectiveAttackerExperienceGiven(GetPlayerAttackerUpgradeProfile(), definition)
            : 0;
        public int KillGain => definition != null ? definition.KillGain : 0;
        public int SendSize => definition != null ? definition.SendSize : 0;
        /// <summary>True when this animal is airborne and therefore requires a defender projectile that can target airborne animals.</summary>
        public bool IsFlying => definition != null && definition.Archetype == AttackerArchetype.Flying;
        public AttackerTraversalProfile TraversalProfile => definition != null
            ? definition.TraversalProfile
            : AttackerTraversalProfile.Ground;

        /// <summary>The latest normalized route direction, used by directional visual presentation.</summary>
        public Vector2 MoveDirection => moveDirection;

        /// <summary>Unique defenders that have dealt a non-lethal hit during this attacker activation.</summary>
        public IReadOnlyCollection<FarmHand> HelpedToKillDefenders => helpedToKillDefenders;

        /// <summary>True while a shovel or other stun source has halted this attacker.</summary>
        public bool IsStunned => stunRemainingDuration > 0f;

        /// <summary>True while a rake's bleed effect is active.</summary>
        public bool IsBleeding => bleedEffect.IsActive;

        /// <summary>True while a poison trap's poison effect is active.</summary>
        public bool IsPoisoned => poisonEffect.IsActive;

        /// <summary>True while one or more combat or terrain effects reduce this attacker's movement speed.</summary>
        public bool IsSlowed => GetSlowMultiplier() < 0.999f || TerrainMovementMultiplier < 0.999f;

        /// <summary>True while an ice or future terrain effect increases this attacker's movement speed.</summary>
        public bool IsSpeedBoosted => TerrainMovementMultiplier > 1.001f;

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
            activeTerrainSpeedModifiers.Clear();
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

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterTerrainSpeedModifier(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // A pooled attacker can be activated while already overlapping terrain. Staying in the
            // trigger therefore re-registers the terrain after the pool lifecycle clears old state.
            RegisterTerrainSpeedModifier(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other == null)
            {
                return;
            }

            var terrainModifier = other.GetComponentInParent<TerrainSpeedModifier>();
            if (terrainModifier != null)
            {
                activeTerrainSpeedModifiers.Remove(terrainModifier);
            }
        }

        private void FixedUpdate()
        {
            if (definition == null || hasReachedFoodStore || isDefeated || currentHealth <= 0f)
            {
                return;
            }

            TickStatusEffects();
            if (hasReachedFoodStore || isDefeated || currentHealth <= 0f)
            {
                return;
            }

            if (IsStunned)
            {
                if (body != null)
                {
                    body.linearVelocity = Vector2.zero;
                }

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
            var maximumMoveDistance = Mathf.Max(0f, EffectiveMovementSpeed) * Time.fixedDeltaTime;
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
            visualController ??= GetComponentInChildren<AttackerVisualController>(true);
            visualController?.RefreshFromAttackerDefinition();
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

        /// <summary>
        /// Fits this attacker's hit collider to its currently displayed sprite outline. The visual
        /// controller calls this when the animal changes facing or display scale, so the collision
        /// footprint follows the visible body rather than the old shared egg placeholder.
        /// </summary>
        public void SynchronizeHitColliderToVisual(SpriteRenderer visual)
        {
            bodyCollider ??= GetComponent<PolygonCollider2D>();
            if (bodyCollider == null || visual == null || visual.sprite == null)
            {
                return;
            }

            SpritePhysicsFootprint2D.SynchronizePolygonCollider(bodyCollider, visual);
            bodyCollider.isTrigger = false;
        }

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
        /// Applies or refreshes a non-stacking bleed. If a stronger bleed replaces a weaker one,
        /// its defender remains credited for the periodic damage and any killing blow.
        /// </summary>
        public void ApplyBleed(FarmHand source, float damagePerSecond, float duration)
        {
            ApplyBleed(
                source,
                source != null ? source.ActivationSessionId : 0L,
                damagePerSecond,
                duration);
        }

        /// <summary>
        /// Applies bleed from a projectile that captured the source farmhand's enabled lifetime at launch.
        /// A source that has since been reused cannot attach a new bleed to this attacker.
        /// </summary>
        public void ApplyBleed(
            FarmHand source,
            long sourceActivationSessionId,
            float damagePerSecond,
            float duration)
        {
            ApplyDamageOverTime(
                ref bleedEffect,
                source,
                sourceActivationSessionId,
                damagePerSecond,
                duration);
        }

        /// <summary>Applies or refreshes a non-stacking poison effect.</summary>
        public void ApplyPoison(FarmHand source, float damagePerSecond, float duration)
        {
            ApplyPoison(
                source,
                source != null ? source.ActivationSessionId : 0L,
                damagePerSecond,
                duration);
        }

        /// <summary>Equivalent token-aware poison entry point used by pooled defender projectiles.</summary>
        public void ApplyPoison(
            FarmHand source,
            long sourceActivationSessionId,
            float damagePerSecond,
            float duration)
        {
            ApplyDamageOverTime(
                ref poisonEffect,
                source,
                sourceActivationSessionId,
                damagePerSecond,
                duration);
        }

        /// <summary>Stops the attacker for the supplied real gameplay duration.</summary>
        public void ApplyStun(float duration)
        {
            if (duration > 0f && !isDefeated && !hasReachedFoodStore)
            {
                stunRemainingDuration = Mathf.Max(stunRemainingDuration, duration);
            }
        }

        /// <summary>
        /// Applies a movement multiplier for a duration. Separate slow sources coexist so a brief
        /// strong slow cannot erase a longer, weaker one when it expires.
        /// </summary>
        public void ApplySlow(float movementMultiplier, float duration)
        {
            if (duration <= 0f || isDefeated || hasReachedFoodStore)
            {
                return;
            }

            movementMultiplier = Mathf.Clamp(movementMultiplier, 0.05f, 1f);
            if (movementMultiplier >= 0.999f)
            {
                return;
            }

            for (var index = 0; index < activeSlows.Count; index++)
            {
                var existing = activeSlows[index];
                if (Mathf.Abs(existing.Multiplier - movementMultiplier) > 0.001f)
                {
                    continue;
                }

                existing.RemainingDuration = Mathf.Max(existing.RemainingDuration, duration);
                activeSlows[index] = existing;
                return;
            }

            activeSlows.Add(new ActiveSlow
            {
                Multiplier = movementMultiplier,
                RemainingDuration = duration
            });
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
                    helpedToKillActivationSessions[defender] = defender.ActivationSessionId;
                }

                return true;
            }

            wasDefeated = true;
            if (defender != null)
            {
                // A killing defender receives the remaining kill reward rather than a second assist allocation.
                helpedToKillDefenders.Remove(defender);
                helpedToKillActivationSessions.Remove(defender);
            }

            if (helpedToKillDefenders.Count > 0)
            {
                var eligibleRecipients = new List<FarmHand>(helpedToKillDefenders.Count);
                foreach (var assistedDefender in helpedToKillDefenders)
                {
                    if (assistedDefender != null &&
                        helpedToKillActivationSessions.TryGetValue(
                            assistedDefender,
                            out var assistedActivationSessionId) &&
                        assistedDefender.IsActivationSessionCurrent(assistedActivationSessionId))
                    {
                        eligibleRecipients.Add(assistedDefender);
                    }
                }

                assistRecipients = eligibleRecipients.ToArray();
            }

            helpedToKillDefenders.Clear();
            helpedToKillActivationSessions.Clear();
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
            currentHealth = MaximumHealth;
            hasReachedFoodStore = false;
            isDefeated = false;
            moveDirection = startingMoveDirection.sqrMagnitude > 0f ? startingMoveDirection.normalized : Vector2.right;
            routePoints.Clear();
            helpedToKillDefenders.Clear();
            helpedToKillActivationSessions.Clear();
            activeTerrainSpeedModifiers.Clear();
            ResetStatusEffects();
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

        /// <summary>
        /// Account attacker bonuses belong only to the human attacker. The same pooled creature
        /// prefab is used by the defender-side AI, which must continue to use authored values.
        /// </summary>
        private static PlayerProfileData GetPlayerAttackerUpgradeProfile()
        {
            return MatchRoleSelection.IsPlayerAttacker
                ? PlayerSettingsService.Instance.Profile
                : null;
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
                !routeMapper.TryGetFastestRoute(body.position, definition, routePoints) ||
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
                definition,
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
            ResetStatusEffects();
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

        private void ApplyDamageOverTime(
            ref ActiveDamageOverTime effect,
            FarmHand source,
            long sourceActivationSessionId,
            float damagePerSecond,
            float duration)
        {
            if (damagePerSecond <= 0f || duration <= 0f || isDefeated || hasReachedFoodStore)
            {
                return;
            }

            // A late contact from an old projectile must not seed a status effect on behalf of the
            // farmhand currently occupying that pooled instance.
            if (source != null && !source.IsActivationSessionCurrent(sourceActivationSessionId))
            {
                return;
            }

            if (!effect.IsActive || damagePerSecond >= effect.DamagePerSecond ||
                (effect.Source != null && !effect.Source.IsActivationSessionCurrent(effect.SourceActivationSessionId)))
            {
                effect.Source = source;
                effect.SourceActivationSessionId = sourceActivationSessionId;
                effect.DamagePerSecond = damagePerSecond;
            }

            effect.RemainingDuration = Mathf.Max(effect.RemainingDuration, duration);
            // A refreshed effect should not wait through an arbitrary old tick delay.
            effect.TickAccumulator = Mathf.Min(effect.TickAccumulator, DamageOverTimeTickInterval);
        }

        private void TickStatusEffects()
        {
            var deltaTime = Time.fixedDeltaTime;
            stunRemainingDuration = Mathf.Max(0f, stunRemainingDuration - deltaTime);
            TickDamageOverTime(ref bleedEffect, deltaTime);
            if (isDefeated || currentHealth <= 0f)
            {
                return;
            }

            TickDamageOverTime(ref poisonEffect, deltaTime);
            if (isDefeated || currentHealth <= 0f)
            {
                return;
            }

            for (var index = activeSlows.Count - 1; index >= 0; index--)
            {
                var slow = activeSlows[index];
                slow.RemainingDuration -= deltaTime;
                if (slow.RemainingDuration <= 0f)
                {
                    activeSlows.RemoveAt(index);
                    continue;
                }

                activeSlows[index] = slow;
            }
        }

        private void TickDamageOverTime(ref ActiveDamageOverTime effect, float deltaTime)
        {
            if (!effect.IsActive)
            {
                return;
            }

            var activeTime = Mathf.Min(deltaTime, effect.RemainingDuration);
            effect.RemainingDuration -= deltaTime;
            effect.TickAccumulator += activeTime;
            if (effect.TickAccumulator + 0.0001f < DamageOverTimeTickInterval && effect.RemainingDuration > 0f)
            {
                return;
            }

            var damage = effect.DamagePerSecond * effect.TickAccumulator;
            effect.TickAccumulator = 0f;
            if (damage > 0f)
            {
                var source = effect.Source;
                if (source != null && source.IsActivationSessionCurrent(effect.SourceActivationSessionId))
                {
                    source.TryDamageAttacker(this, damage, effect.SourceActivationSessionId);
                }
                else if (source != null && source.isActiveAndEnabled && source.gameObject.activeInHierarchy)
                {
                    // The same farmhand instance has been reused. Its new activation did not inflict this
                    // effect, so discard the stale status instead of awarding its XP or gold.
                    effect.Clear();
                    return;
                }
                else
                {
                    TakeDamage(damage);
                }
            }

            if (effect.RemainingDuration <= 0f || isDefeated || currentHealth <= 0f)
            {
                effect.Clear();
            }
        }

        private float GetSlowMultiplier()
        {
            var multiplier = 1f;
            for (var index = 0; index < activeSlows.Count; index++)
            {
                multiplier = Mathf.Min(multiplier, activeSlows[index].Multiplier);
            }

            return multiplier;
        }

        private void RegisterTerrainSpeedModifier(Collider2D other)
        {
            if (other == null || IsFlying)
            {
                return;
            }

            var terrainModifier = other.GetComponentInParent<TerrainSpeedModifier>();
            if (terrainModifier != null && terrainModifier.isActiveAndEnabled &&
                !activeTerrainSpeedModifiers.Contains(terrainModifier))
            {
                activeTerrainSpeedModifiers.Add(terrainModifier);
            }
        }

        private float GetTerrainMovementMultiplier()
        {
            if (IsFlying)
            {
                return 1f;
            }

            var multiplier = 1f;
            for (var index = activeTerrainSpeedModifiers.Count - 1; index >= 0; index--)
            {
                var terrainModifier = activeTerrainSpeedModifiers[index];
                if (terrainModifier == null || !terrainModifier.isActiveAndEnabled)
                {
                    activeTerrainSpeedModifiers.RemoveAt(index);
                    continue;
                }

                multiplier *= terrainModifier.MovementSpeedMultiplier;
            }

            // Stacked floor patches are unusual but supported. Clamp only the extreme authoring cases
            // so a misconfigured tile cannot permanently halt or launch an attacker off its route.
            return Mathf.Clamp(multiplier, 0.05f, 3f);
        }

        private void ResetStatusEffects()
        {
            bleedEffect.Clear();
            poisonEffect.Clear();
            stunRemainingDuration = 0f;
            activeSlows.Clear();
        }
    }
}
