using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using TAD.Environment;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Determines how a farmhand orders the attackers detected by its range sensor.
    /// More policies can be added without changing the defender UI or stored farmhand state.
    /// </summary>
    public enum DefenderTargetingMode
    {
        FirstEnemyInRange = 0
    }

    /// <summary>
    /// Reusable defender token that owns its assigned weapon, range presentation, and attackers currently in attack range.
    /// Weapon selection and target-priority behaviour are retained by the placed farmhand and reset only when it is reused by the pool.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D))]
    public sealed class FarmHand : MonoBehaviour, IPoolLifecycle
    {
        private const float BlockedTargetRetryInterval = 0.1f;
        private const int LineOfSightHitCapacity = 16;
        private const float LevelDamageMultiplier = 1.1f;
        private const int LevelThreeUpgradeLevel = 3;
        private const float RelocationArrivalDistance = 0.001f;
        private const float RelocationCostMultiplier = 0.25f;

        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private FarmHandVisualController visualController;
        [SerializeField] private WeaponDefinition weaponDefinition;
        [SerializeField] private CircleCollider2D rangeCollider;
        [SerializeField] private Rigidbody2D rangeBody;
        [SerializeField] private BoxCollider2D placementCollider;
        [SerializeField, Min(0.01f)] private float relocationSpeed = 1f;
        [SerializeField] private DefenderRangeVisual rangeVisual;
        [SerializeField, Min(1)] private int expMax = 10;
        [SerializeField, Min(1)] private int maxLevel = 10;

        [System.NonSerialized] private float currentExp;
        [System.NonSerialized] private float totalExperience;
        [System.NonSerialized] private int currentLevel = 1;
        [System.NonSerialized] private WeaponDefinition baseWeaponDefinition;
        [System.NonSerialized] private bool isAwaitingLevelThreeUpgrade;
        [System.NonSerialized] private bool hasSelectedLevelThreeUpgrade;
        [System.NonSerialized] private int investedGold;
        [System.NonSerialized] private bool isRelocating;
        [System.NonSerialized] private Vector2 relocationDestination;
        [System.NonSerialized] private Sprite placementFootprintSprite;
        [System.NonSerialized] private Vector2 facingDirection = Vector2.right;
        [System.NonSerialized] private DefenderTargetingMode targetingMode = DefenderTargetingMode.FirstEnemyInRange;
        private PooledInstance pooledInstance;
        private SpawnPool projectilePool;
        private ResourceBank defenderBank;
        private readonly List<BasicAttacker> attackersInRange = new();
        private readonly RaycastHit2D[] lineOfSightHits = new RaycastHit2D[LineOfSightHitCapacity];
        private ContactFilter2D lineOfSightFilter;
        private float nextFireTime;
        private bool isRangeIndicatorVisible;
        private readonly List<FarmHandProjectileBase> limitedActiveProjectiles = new();

        /// <summary>Visual used by the placement system to create a non-interactive preview.</summary>
        public SpriteRenderer Visual => visual;

        /// <summary>Shared weapon data currently assigned to this farmhand.</summary>
        public WeaponDefinition WeaponDefinition => weaponDefinition;

        /// <summary>The authoritative Defender resource bank used for this farmhand's weapon purchases.</summary>
        public ResourceBank DefenderBank => ResolveDefenderBank();

        /// <summary>25% of the base farmhand and permanently purchased upgrade costs, rounded up to whole gold.</summary>
        public int RelocationCost => Mathf.CeilToInt(Mathf.Max(0, investedGold) * RelocationCostMultiplier);

        /// <summary>True while this farmhand is walking to a paid relocation destination.</summary>
        public bool IsRelocating => isRelocating;

        /// <summary>World-space destination of the active relocation, if any.</summary>
        public Vector2 RelocationDestination => relocationDestination;

        /// <summary>World-space size of this farmhand's sprite-sized placement footprint.</summary>
        public Vector2 PlacementFootprintSize => placementCollider != null && placementCollider.enabled
            ? placementCollider.bounds.size
            : GetSpriteFootprintSize();

        /// <summary>World-space offset from the farmhand origin to its footprint centre.</summary>
        public Vector2 PlacementFootprintCenterOffset => placementCollider != null && placementCollider.enabled
            ? (Vector2)(placementCollider.bounds.center - transform.position)
            : GetSpriteFootprintCenterOffset();

        /// <summary>Dedicated trigger used only to prevent overlapping farmhand placement.</summary>
        public BoxCollider2D PlacementCollider => placementCollider;

        /// <summary>Returns whether the Defender bank can pay for the supplied level-3 weapon choice.</summary>
        public bool CanAffordLevelThreeUpgrade(WeaponDefinition definition)
        {
            var bank = ResolveDefenderBank();
            return definition != null && bank != null && bank.CanAfford(definition.Cost);
        }

        /// <summary>True while this farmhand has reached level 3 and must choose its permanent weapon upgrade.</summary>
        public bool IsAwaitingLevelThreeUpgrade => isAwaitingLevelThreeUpgrade;

        /// <summary>True after this farmhand has completed its one level-3 weapon choice.</summary>
        public bool HasSelectedLevelThreeUpgrade => hasSelectedLevelThreeUpgrade;

        /// <summary>
        /// Session-unique identity assigned by the pool when this defender is created.
        /// The value remains the same if this farmhand is later reused from the pool.
        /// </summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        /// <summary>Configured experience threshold for this farmhand.</summary>
        public int ExpMax => expMax;

        /// <summary>Current runtime experience toward this farmhand's next level.</summary>
        public float CurrentExp => currentExp;

        /// <summary>Current runtime level for this placed farmhand.</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>Configured maximum level for this farmhand.</summary>
        public int MaxLevel => Mathf.Max(1, maxLevel);

        /// <summary>True once this farmhand can no longer gain levels.</summary>
        public bool IsAtMaxLevel => currentLevel >= MaxLevel;

        /// <summary>Total experience earned by this placed farmhand, including awards received at the level cap.</summary>
        public float TotalExperience => totalExperience;

        /// <summary>Per-farmhand damage after this farmhand's level multiplier is applied.</summary>
        public float Damage => weaponDefinition != null
            ? weaponDefinition.Damage * Mathf.Pow(LevelDamageMultiplier, currentLevel - 1)
            : 0f;

        /// <summary>Persistent target-order policy for this placed farmhand.</summary>
        public DefenderTargetingMode TargetingMode => targetingMode;

        /// <summary>Player-facing name of the target-order policy currently used by this farmhand.</summary>
        public string TargetingModeDisplayName => GetTargetingModeDisplayName(targetingMode);

        /// <summary>Raised after this farmhand's experience or level changes.</summary>
        public event System.Action ProgressionChanged;

        /// <summary>Raised after a successfully launched projectile so presentation can play its matching attack animation.</summary>
        public event System.Action<Vector2, WeaponDefinition> Fired;

        /// <summary>Raised after this farmhand's permanent gameplay weapon changes.</summary>
        public event System.Action<WeaponDefinition> WeaponChanged;

        /// <summary>Raised when this farmhand's selected target-order policy changes.</summary>
        public event System.Action<DefenderTargetingMode> TargetingModeChanged;

        /// <summary>Raised after a farmhand has configured its non-blocking range sensor.</summary>
        public static event System.Action<FarmHand> FarmHandEnabled;

        /// <summary>Raised once when an active farmhand first reaches level 3 and needs a weapon choice.</summary>
        public static event System.Action<FarmHand> LevelThreeUpgradeChoiceRequested;

        /// <summary>World-space attack range supplied by the currently assigned weapon.</summary>
        public float Range => weaponDefinition != null ? weaponDefinition.Range : 0f;

        /// <summary>Last valid aiming direction, used only by directional farmhand presentation.</summary>
        public Vector2 FacingDirection => facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Vector2.right;

        /// <summary>The non-blocking 2D trigger used to maintain the nearby attacker collection.</summary>
        public CircleCollider2D RangeCollider => rangeCollider;

        /// <summary>True while this defender's translucent range indicator is visible.</summary>
        public bool IsRangeIndicatorVisible => isRangeIndicatorVisible;

        /// <summary>
        /// Changes the policy used to order nearby target candidates. This remains on the farmhand while it relocates.
        /// </summary>
        public void SetTargetingMode(DefenderTargetingMode mode)
        {
            var normalizedMode = NormalizeTargetingMode(mode);
            if (targetingMode == normalizedMode)
            {
                return;
            }

            targetingMode = normalizedMode;
            TargetingModeChanged?.Invoke(targetingMode);
        }

        /// <summary>Returns a concise label suitable for both the defender selector and attacker read-only details card.</summary>
        public static string GetTargetingModeDisplayName(DefenderTargetingMode mode)
        {
            return NormalizeTargetingMode(mode) switch
            {
                DefenderTargetingMode.FirstEnemyInRange => "First enemy in range",
                _ => "First enemy in range"
            };
        }

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
            baseWeaponDefinition ??= weaponDefinition;
            ResetProgression();
            ResolveComponents();
            EnsurePlacementCollider();
            ResetRelocation();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
        }

        private void OnEnable()
        {
            ClearAttackersInRange();
            ResetRelocation();
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            ResolveComponents();
            EnsurePlacementCollider();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
            FarmHandEnabled?.Invoke(this);
        }

        private void Update()
        {
            UpdatePlacementColliderIfNeeded();
            if (isRelocating)
            {
                return;
            }

            TryFireAtSelectedAttackableAttacker();
        }

        private void FixedUpdate()
        {
            if (isRelocating)
            {
                UpdateRelocation();
            }
        }

        private void OnDisable()
        {
            CancelLimitedProjectiles();
            ClearAttackersInRange();
            ResetRelocation();
            SetRangeIndicatorVisible(false);
        }

        /// <summary>Assigns a weapon and immediately updates this defender's detection and visible ranges.</summary>
        public void SetWeapon(WeaponDefinition definition)
        {
            CancelLimitedProjectiles();
            weaponDefinition = definition;
            ClearAttackersInRange();
            nextFireTime = Time.time;
            ConfigureRangeCollider();
            ConfigureRangeVisual();
            WeaponChanged?.Invoke(weaponDefinition);
        }

        /// <summary>Charges the Defender bank and applies the one permanent weapon choice offered at level 3.</summary>
        public bool TryChooseLevelThreeUpgrade(WeaponDefinition definition)
        {
            if (definition == null || !isAwaitingLevelThreeUpgrade || currentLevel < LevelThreeUpgradeLevel)
            {
                return false;
            }

            var bank = ResolveDefenderBank();
            if (bank == null || !bank.TrySpendGold(definition.Cost))
            {
                return false;
            }

            isAwaitingLevelThreeUpgrade = false;
            hasSelectedLevelThreeUpgrade = true;
            investedGold += Mathf.Max(0, definition.Cost);
            SetWeapon(definition);
            ProgressionChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Starts an already-authorised relocation. Placement validation and the relocation fee belong to
        /// FarmHandPlacementController so this component remains reusable by pooling and AI code.
        /// </summary>
        public bool TryBeginRelocation(Vector2 destination)
        {
            if (!isActiveAndEnabled || isRelocating)
            {
                return false;
            }

            ResolveComponents();
            if (rangeBody == null || (destination - (Vector2)transform.position).sqrMagnitude <=
                RelocationArrivalDistance * RelocationArrivalDistance)
            {
                return false;
            }

            relocationDestination = destination;
            isRelocating = true;
            ClearAttackersInRange();
            nextFireTime = float.PositiveInfinity;

            var direction = destination - (Vector2)transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                facingDirection = direction.normalized;
            }

            rangeBody.linearVelocity = Vector2.zero;
            rangeBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            return true;
        }

        /// <summary>Shows or hides the red attack-range circle used when this defender is selected or targeted.</summary>
        public void SetRangeIndicatorVisible(bool visible)
        {
            isRangeIndicatorVisible = visible;
            ConfigureRangeVisual();
        }

        /// <summary>
        /// Awards every eligible assisting Farmhand a share of this attacker group's XP, capped at half of the
        /// distributed total. The killer receives the remaining share, which is therefore never below 50%.
        /// This is public so every combat source uses the same authoritative reward path.
        /// </summary>
        public void AwardKillingBlowReward(
            int expGiven,
            int killGain,
            int sendSize,
            IReadOnlyList<FarmHand> assistRecipients)
        {
            var eligibleAssistRecipients = GetEligibleAssistRecipients(assistRecipients);
            eligibleAssistRecipients.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));

            var distribution = DefenderRewardCalculator.Calculate(
                expGiven,
                sendSize,
                eligibleAssistRecipients.Count);
            for (var index = 0; index < eligibleAssistRecipients.Count; index++)
            {
                var assistExperience = distribution.GetAssistExperienceForRecipient(index);
                if (assistExperience > 0f)
                {
                    eligibleAssistRecipients[index].AddExperience(assistExperience);
                }
            }

            AddExperience(distribution.KillerExperience);
            ResolveDefenderBank()?.AddGold(killGain);
        }

        /// <summary>Applies damage while preserving this defender's assisted XP, kill XP, and gold rules.</summary>
        public bool TryDamageAttacker(BasicAttacker attacker, float damageAmount)
        {
            if (!IsValidAttacker(attacker) || damageAmount <= 0f)
            {
                return false;
            }

            var expGiven = attacker.ExpGiven;
            var killGain = attacker.KillGain;
            var sendSize = attacker.SendSize;
            if (!attacker.TryTakeDamage(
                    damageAmount,
                    this,
                    out var wasDefeated,
                    out var assistRecipients))
            {
                return false;
            }

            if (wasDefeated)
            {
                AwardKillingBlowReward(expGiven, killGain, sendSize, assistRecipients);
            }

            return true;
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
            if (attacker != null && !IsAttackerWithinRange(attacker))
            {
                attackersInRange.Remove(attacker);
            }
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            baseWeaponDefinition ??= weaponDefinition;
            weaponDefinition = baseWeaponDefinition;
            CancelLimitedProjectiles();
            ResetProgression();
            ResolveComponents();

            if (visual != null)
            {
                visual.enabled = true;
            }

            ClearAttackersInRange();
            ResetRelocation();
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            EnsurePlacementCollider();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
            WeaponChanged?.Invoke(weaponDefinition);
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            CancelLimitedProjectiles();
            ClearAttackersInRange();
            ResetRelocation();
            nextFireTime = 0f;
            SetRangeIndicatorVisible(false);
        }

        private void ResolveComponents()
        {
            visual ??= GetComponent<SpriteRenderer>();
            visualController ??= GetComponent<FarmHandVisualController>();
            rangeCollider ??= GetComponent<CircleCollider2D>();
            rangeBody ??= GetComponent<Rigidbody2D>();
            placementCollider ??= transform.Find("PlacementHitbox")?.GetComponent<BoxCollider2D>();
            rangeVisual ??= GetComponentInChildren<DefenderRangeVisual>(true);
            pooledInstance ??= GetComponent<PooledInstance>();
            ConfigureLineOfSightFilter();
        }

        private void ConfigureLineOfSightFilter()
        {
            lineOfSightFilter = new ContactFilter2D();
            lineOfSightFilter.useLayerMask = true;
            lineOfSightFilter.layerMask = LayerMask.GetMask("Environment");
            lineOfSightFilter.useTriggers = false;
        }

        private void TrackAttacker(Collider2D other)
        {
            var attacker = other.GetComponentInParent<BasicAttacker>();
            if (!IsValidAttacker(attacker) || !IsAttackerWithinRange(attacker) || attackersInRange.Contains(attacker))
            {
                return;
            }

            attackersInRange.Add(attacker);
            TryFireAtSelectedAttackableAttacker();
        }

        private void TryFireAtSelectedAttackableAttacker()
        {
            if (isRelocating || isAwaitingLevelThreeUpgrade || Time.time < nextFireTime)
            {
                return;
            }

            var weapon = weaponDefinition;
            var projectileDefinition = weapon != null ? weapon.ProjectileDefinition : null;
            var damage = Damage;
            if (weapon == null || projectileDefinition == null || projectileDefinition.ProjectilePrefab == null ||
                projectileDefinition.ProjectileSpeed <= 0f || damage <= 0f ||
                (projectileDefinition.UsesTimedLifetime && projectileDefinition.ProjectileLifetime <= 0f))
            {
                return;
            }

            if (!TryGetSelectedAttackableAttacker(projectileDefinition.ProjectileType, out var target))
            {
                // Avoid repeating line-of-sight physics queries every rendered frame while every nearby target is covered.
                nextFireTime = Time.time + BlockedTargetRetryInterval;
                return;
            }

            var pool = ResolveProjectilePool();
            if (pool == null)
            {
                return;
            }

            if (!CanLaunchProjectile(projectileDefinition))
            {
                return;
            }

            var origin = (Vector2)transform.position;
            var projectileObject = pool.SpawnProjectile(projectileDefinition.ProjectilePrefab, origin, Quaternion.identity);
            if (projectileObject == null)
            {
                nextFireTime = Time.time + weapon.FireInterval;
                return;
            }

            var projectile = projectileObject.GetComponent<FarmHandProjectileBase>();
            if (projectile == null || !projectile.Launch(
                    this,
                    origin,
                    target,
                    projectileDefinition,
                    weapon.Range,
                    damage))
            {
                ReturnProjectileToPool(projectileObject);
                nextFireTime = Time.time + weapon.FireInterval;
                return;
            }

            if (projectileDefinition.MaximumActiveProjectiles > 0)
            {
                limitedActiveProjectiles.Add(projectile);
            }

            var targetDirection = target.CurrentPosition - origin;
            if (targetDirection.sqrMagnitude > 0.0001f)
            {
                facingDirection = targetDirection.normalized;
            }

            nextFireTime = Time.time + weapon.FireInterval;
            Fired?.Invoke(FacingDirection, weapon);
        }

        private bool CanLaunchProjectile(ProjectileDefinition projectileDefinition)
        {
            if (projectileDefinition == null || projectileDefinition.MaximumActiveProjectiles <= 0)
            {
                return true;
            }

            PruneLimitedActiveProjectiles();
            var activeCount = 0;
            foreach (var projectile in limitedActiveProjectiles)
            {
                if (projectile != null && projectile.IsLaunched && projectile.Source == this &&
                    projectile.ProjectileDefinition == projectileDefinition)
                {
                    activeCount++;
                }
            }

            return activeCount < projectileDefinition.MaximumActiveProjectiles;
        }
        private void PruneLimitedActiveProjectiles()
        {
            for (var index = limitedActiveProjectiles.Count - 1; index >= 0; index--)
            {
                var projectile = limitedActiveProjectiles[index];
                if (projectile == null || !projectile.IsLaunched || projectile.Source != this)
                {
                    limitedActiveProjectiles.RemoveAt(index);
                }
            }
        }

        private void CancelLimitedProjectiles()
        {
            foreach (var projectile in limitedActiveProjectiles)
            {
                if (projectile != null && projectile.IsLaunched && projectile.Source == this)
                {
                    projectile.Cancel();
                }
            }

            limitedActiveProjectiles.Clear();
        }

        private bool TryGetSelectedAttackableAttacker(ProjectileType projectileType, out BasicAttacker attacker)
        {
            PruneInvalidAttackers();

            switch (targetingMode)
            {
                case DefenderTargetingMode.FirstEnemyInRange:
                default:
                    return TryGetFirstEnemyInRange(projectileType, out attacker);
            }
        }

        /// <summary>
        /// The sensor appends attackers in entry order and prunes defeated, pooled, reached, and departed animals before every search.
        /// This automatically advances to the next eligible animal when the current first enemy is no longer valid.
        /// </summary>
        private bool TryGetFirstEnemyInRange(ProjectileType projectileType, out BasicAttacker attacker)
        {
            for (var index = 0; index < attackersInRange.Count; index++)
            {
                var candidate = attackersInRange[index];
                if (!IsCandidateBlockedByWall(candidate, projectileType))
                {
                    attacker = candidate;
                    return true;
                }
            }

            attacker = null;
            return false;
        }

        private bool IsCandidateBlockedByWall(BasicAttacker candidate, ProjectileType projectileType)
        {
            var origin = (Vector2)transform.position;
            var targetPosition = candidate.CurrentPosition;
            var direction = targetPosition - origin;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            var distance = direction.magnitude;
            var hitCount = Physics2D.Raycast(
                origin,
                direction / distance,
                lineOfSightFilter,
                lineOfSightHits,
                distance);

            // A saturated buffer could conceal a later full-cover wall, so do not fire unless every hit was inspected.
            if (hitCount >= lineOfSightHits.Length)
            {
                return true;
            }

            for (var index = 0; index < hitCount; index++)
            {
                var wall = lineOfSightHits[index].collider.GetComponentInParent<FarmWall>();
                if (wall == null)
                {
                    continue;
                }

                if (projectileType != ProjectileType.Ranged || wall.BlocksAllAttacks)
                {
                    return true;
                }
            }

            return false;
        }

        private ResourceBank ResolveDefenderBank()
        {
            if (defenderBank != null && defenderBank.BankSide == BankSide.Defender)
            {
                return defenderBank;
            }

            defenderBank = null;
            foreach (var bank in Object.FindObjectsByType<ResourceBank>(FindObjectsInactive.Include))
            {
                if (bank.BankSide == BankSide.Defender)
                {
                    defenderBank = bank;
                    break;
                }
            }

            return defenderBank;
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
            rangeBody.constraints = isRelocating
                ? RigidbodyConstraints2D.FreezeRotation
                : RigidbodyConstraints2D.FreezeAll;
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

        private void EnsurePlacementCollider()
        {
            if (placementCollider == null)
            {
                var footprintTransform = transform.Find("PlacementHitbox");
                if (footprintTransform == null)
                {
                    var footprintObject = new GameObject("PlacementHitbox");
                    footprintObject.layer = gameObject.layer;
                    footprintTransform = footprintObject.transform;
                    footprintTransform.SetParent(transform, false);
                }

                placementCollider = footprintTransform.GetComponent<BoxCollider2D>();
                if (placementCollider == null)
                {
                    placementCollider = footprintTransform.gameObject.AddComponent<BoxCollider2D>();
                }
            }

            ConfigurePlacementCollider();
        }

        private void ConfigurePlacementCollider()
        {
            if (placementCollider == null)
            {
                return;
            }

            placementCollider.isTrigger = true;
            placementCollider.gameObject.layer = gameObject.layer;

            if (visual == null || visual.sprite == null)
            {
                placementCollider.enabled = false;
                placementFootprintSprite = null;
                return;
            }

            var rootScale = transform.lossyScale;
            var worldSize = visual.bounds.size;
            var localSize = new Vector2(
                Mathf.Abs(rootScale.x) > Mathf.Epsilon ? worldSize.x / Mathf.Abs(rootScale.x) : visual.sprite.bounds.size.x,
                Mathf.Abs(rootScale.y) > Mathf.Epsilon ? worldSize.y / Mathf.Abs(rootScale.y) : visual.sprite.bounds.size.y);

            placementCollider.transform.localPosition = transform.InverseTransformPoint(visual.bounds.center);
            placementCollider.transform.localRotation = Quaternion.identity;
            placementCollider.transform.localScale = Vector3.one;
            placementCollider.offset = Vector2.zero;
            placementCollider.size = localSize;
            placementCollider.enabled = localSize.x > Mathf.Epsilon && localSize.y > Mathf.Epsilon;
            placementFootprintSprite = visual.sprite;
        }

        private void UpdatePlacementColliderIfNeeded()
        {
            if (placementCollider != null && visual != null && visual.sprite != placementFootprintSprite)
            {
                ConfigurePlacementCollider();
            }
        }

        private Vector2 GetSpriteFootprintSize()
        {
            if (visual == null || visual.sprite == null)
            {
                return Vector2.zero;
            }

            var spriteSize = visual.sprite.bounds.size;
            var worldScale = visual.transform.lossyScale;
            return new Vector2(
                Mathf.Abs(spriteSize.x * worldScale.x),
                Mathf.Abs(spriteSize.y * worldScale.y));
        }

        private Vector2 GetSpriteFootprintCenterOffset()
        {
            return visual != null && visual.sprite != null
                ? (Vector2)(visual.transform.TransformPoint(visual.sprite.bounds.center) - transform.position)
                : Vector2.zero;
        }

        private void UpdateRelocation()
        {
            if (rangeBody == null)
            {
                ResetRelocation();
                return;
            }

            var currentPosition = rangeBody.position;
            var remaining = relocationDestination - currentPosition;
            var step = Mathf.Max(0.01f, relocationSpeed) * Time.fixedDeltaTime;
            if (remaining.sqrMagnitude <= RelocationArrivalDistance * RelocationArrivalDistance || remaining.magnitude <= step)
            {
                rangeBody.position = relocationDestination;
                CompleteRelocation();
                return;
            }

            var nextPosition = Vector2.MoveTowards(currentPosition, relocationDestination, step);
            rangeBody.MovePosition(nextPosition);
            facingDirection = remaining.normalized;
        }

        private void CompleteRelocation()
        {
            isRelocating = false;
            relocationDestination = transform.position;
            ConfigureRangeBody();
            nextFireTime = Time.time;
        }

        private void ResetRelocation()
        {
            isRelocating = false;
            relocationDestination = transform.position;
        }

        /// <summary>
        /// Farmhands are detection and attack sources, not map obstacles. Any collider on their hierarchy
        /// is therefore a trigger; physical blocking stays exclusively with Environment colliders such as walls.
        /// </summary>
        private void ConfigureNonBlockingColliders()
        {
            foreach (var farmHandCollider in GetComponentsInChildren<Collider2D>(true))
            {
                if (farmHandCollider != null)
                {
                    farmHandCollider.isTrigger = true;
                }
            }
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

        private static List<FarmHand> GetEligibleAssistRecipients(IReadOnlyList<FarmHand> assistRecipients)
        {
            var eligibleRecipients = new List<FarmHand>();
            if (assistRecipients == null)
            {
                return eligibleRecipients;
            }

            foreach (var assistingDefender in assistRecipients)
            {
                if (assistingDefender != null &&
                    assistingDefender.isActiveAndEnabled &&
                    assistingDefender.gameObject.activeInHierarchy)
                {
                    eligibleRecipients.Add(assistingDefender);
                }
            }

            return eligibleRecipients;
        }

        private float AddExperience(float awardedAmount)
        {
            awardedAmount = DefenderRewardCalculator.RoundToNearestTenth(awardedAmount);
            if (awardedAmount <= 0f)
            {
                return 0f;
            }

            // Results can credit every earned point even after no further level can be gained.
            totalExperience = DefenderRewardCalculator.RoundToNearestTenth(totalExperience + awardedAmount);
            var shouldRequestLevelThreeUpgrade = false;

            if (!IsAtMaxLevel)
            {
                var expThreshold = Mathf.Max(1, ExpMax);
                currentExp = DefenderRewardCalculator.RoundToNearestTenth(currentExp + awardedAmount);
                while (currentLevel < MaxLevel && currentExp >= expThreshold)
                {
                    currentExp = DefenderRewardCalculator.RoundToNearestTenth(currentExp - expThreshold);
                    currentLevel++;

                    if (!hasSelectedLevelThreeUpgrade && !isAwaitingLevelThreeUpgrade &&
                        currentLevel >= LevelThreeUpgradeLevel)
                    {
                        isAwaitingLevelThreeUpgrade = true;
                        shouldRequestLevelThreeUpgrade = true;
                    }
                }

                if (IsAtMaxLevel)
                {
                    // Current XP only represents progress toward an obtainable next level.
                    currentExp = 0f;
                }
            }

            ProgressionChanged?.Invoke();
            if (shouldRequestLevelThreeUpgrade)
            {
                LevelThreeUpgradeChoiceRequested?.Invoke(this);
            }

            return awardedAmount;
        }

        private void ResetProgression()
        {
            currentExp = 0f;
            totalExperience = 0f;
            currentLevel = 1;
            isAwaitingLevelThreeUpgrade = false;
            hasSelectedLevelThreeUpgrade = false;
            var startingWeapon = baseWeaponDefinition ?? weaponDefinition;
            investedGold = startingWeapon != null ? Mathf.Max(0, startingWeapon.Cost) : 0;
            facingDirection = Vector2.right;
            var modeChanged = targetingMode != DefenderTargetingMode.FirstEnemyInRange;
            targetingMode = DefenderTargetingMode.FirstEnemyInRange;
            if (modeChanged)
            {
                TargetingModeChanged?.Invoke(targetingMode);
            }

            ProgressionChanged?.Invoke();
        }

        private static DefenderTargetingMode NormalizeTargetingMode(DefenderTargetingMode mode)
        {
            return mode == DefenderTargetingMode.FirstEnemyInRange
                ? mode
                : DefenderTargetingMode.FirstEnemyInRange;
        }

        private void ClearAttackersInRange()
        {
            attackersInRange.Clear();
        }

        private void PruneInvalidAttackers()
        {
            for (var index = attackersInRange.Count - 1; index >= 0; index--)
            {
                if (!IsValidAttacker(attackersInRange[index]) || !IsAttackerWithinRange(attackersInRange[index]))
                {
                    attackersInRange.RemoveAt(index);
                }
            }
        }

        private bool IsAttackerWithinRange(BasicAttacker attacker)
        {
            if (attacker == null || Range <= 0f)
            {
                return false;
            }

            return (attacker.CurrentPosition - (Vector2)transform.position).sqrMagnitude <= Range * Range + 0.0001f;
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
            expMax = Mathf.Max(1, expMax);
            maxLevel = Mathf.Max(1, maxLevel);
            relocationSpeed = Mathf.Max(0.01f, relocationSpeed);
            ResolveComponents();
            ConfigurePlacementCollider();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
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
