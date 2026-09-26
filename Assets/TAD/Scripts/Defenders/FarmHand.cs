using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using TAD.Environment;
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
        private const float BlockedTargetRetryInterval = 0.1f;
        private const int LineOfSightHitCapacity = 16;
        private const float HelpedToKillExperiencePercent = 0.1f;
        private const float ExperienceDecimalMultiplier = 10f;
        private const float LevelDamageMultiplier = 1.1f;
        private const int LevelThreeUpgradeLevel = 3;

        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private WeaponDefinition weaponDefinition;
        [SerializeField] private CircleCollider2D rangeCollider;
        [SerializeField] private Rigidbody2D rangeBody;
        [SerializeField] private DefenderRangeVisual rangeVisual;
        [SerializeField, Min(1)] private int expMax = 10;
        [SerializeField, Min(1)] private int maxLevel = 10;

        [System.NonSerialized] private float currentExp;
        [System.NonSerialized] private float totalExperience;
        [System.NonSerialized] private int currentLevel = 1;
        [System.NonSerialized] private WeaponDefinition baseWeaponDefinition;
        [System.NonSerialized] private bool isAwaitingLevelThreeUpgrade;
        [System.NonSerialized] private bool hasSelectedLevelThreeUpgrade;
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

        /// <summary>Raised after this farmhand's experience or level changes.</summary>
        public event System.Action ProgressionChanged;

        /// <summary>Raised after a farmhand has configured its non-blocking range sensor.</summary>
        public static event System.Action<FarmHand> FarmHandEnabled;

        /// <summary>Raised once when an active farmhand first reaches level 3 and needs a weapon choice.</summary>
        public static event System.Action<FarmHand> LevelThreeUpgradeChoiceRequested;

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
            baseWeaponDefinition ??= weaponDefinition;
            ResetProgression();
            ResolveComponents();
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
        }

        private void OnEnable()
        {
            ClearAttackersInRange();
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
            FarmHandEnabled?.Invoke(this);
        }

        private void Update()
        {
            TryFireAtFirstAttackableAttacker();
        }

        private void OnDisable()
        {
            CancelLimitedProjectiles();
            ClearAttackersInRange();
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
        }

        /// <summary>Applies the one permanent weapon reward offered when this farmhand reaches level 3.</summary>
        public bool TryChooseLevelThreeUpgrade(WeaponDefinition definition)
        {
            if (definition == null || !isAwaitingLevelThreeUpgrade || currentLevel < LevelThreeUpgradeLevel)
            {
                return false;
            }

            isAwaitingLevelThreeUpgrade = false;
            hasSelectedLevelThreeUpgrade = true;
            SetWeapon(definition);
            ProgressionChanged?.Invoke();
            return true;
        }

        /// <summary>Shows or hides the red attack-range circle used when this defender is selected or targeted.</summary>
        public void SetRangeIndicatorVisible(bool visible)
        {
            isRangeIndicatorVisible = visible;
            ConfigureRangeVisual();
        }

        /// <summary>Awards one non-lethal helped-to-kill experience credit.</summary>
        internal void AwardHelpedToKillExperience(int expGiven)
        {
            AddExperience(GetHelpedToKillExperience(expGiven));
        }

        /// <summary>
        /// Awards a killing blow, replacing this attacker's earlier assisted-kill experience when present,
        /// and adds the matching gold to the Defender bank.
        /// </summary>
        internal void AwardKillingBlowReward(int expGiven, int killGain, bool replacesHelpedToKillExperience)
        {
            var experienceAward = Mathf.Max(
                0f,
                expGiven - (replacesHelpedToKillExperience ? GetHelpedToKillExperience(expGiven) : 0f));
            AddExperience(experienceAward);
            ResolveDefenderBank()?.AddGold(killGain);
        }

        /// <summary>Applies projectile damage while preserving this defender's assisted XP, kill XP, and gold rules.</summary>
        internal bool TryDamageAttacker(BasicAttacker attacker, float damageAmount)
        {
            if (!IsValidAttacker(attacker) || damageAmount <= 0f)
            {
                return false;
            }

            var expGiven = attacker.ExpGiven;
            var killGain = attacker.KillGain;
            if (!attacker.TryTakeDamage(
                    damageAmount,
                    this,
                    out var wasDefeated,
                    out var helpedToKillAdded,
                    out var killingBlowReplacedHelpedToKill))
            {
                return false;
            }

            if (wasDefeated)
            {
                AwardKillingBlowReward(expGiven, killGain, killingBlowReplacedHelpedToKill);
            }
            else if (helpedToKillAdded)
            {
                AwardHelpedToKillExperience(expGiven);
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
            if (attacker != null)
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
            nextFireTime = Time.time;
            isRangeIndicatorVisible = false;
            ConfigureRangeBody();
            ConfigureRangeCollider();
            ConfigureNonBlockingColliders();
            ConfigureRangeVisual();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            CancelLimitedProjectiles();
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
            if (!IsValidAttacker(attacker) || attackersInRange.Contains(attacker))
            {
                return;
            }

            attackersInRange.Add(attacker);
            TryFireAtFirstAttackableAttacker();
        }

        private void TryFireAtFirstAttackableAttacker()
        {
            if (isAwaitingLevelThreeUpgrade || Time.time < nextFireTime)
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

            if (!TryGetFirstAttackableAttacker(projectileDefinition.ProjectileType, out var target))
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

            nextFireTime = Time.time + weapon.FireInterval;
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

        private bool TryGetFirstAttackableAttacker(ProjectileType projectileType, out BasicAttacker attacker)
        {
            PruneInvalidAttackers();

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

        private static float GetHelpedToKillExperience(int expGiven)
        {
            return RoundToNearestTenth(expGiven * HelpedToKillExperiencePercent);
        }

        private void AddExperience(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            // Results can credit every earned point even after no further level can be gained.
            totalExperience = RoundToNearestTenth(totalExperience + amount);
            var shouldRequestLevelThreeUpgrade = false;

            if (!IsAtMaxLevel)
            {
                var expThreshold = Mathf.Max(1, ExpMax);
                currentExp = RoundToNearestTenth(currentExp + amount);
                while (currentLevel < MaxLevel && currentExp >= expThreshold)
                {
                    currentExp = RoundToNearestTenth(currentExp - expThreshold);
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
        }
        private static float RoundToNearestTenth(float amount)
        {
            return Mathf.Round(amount * ExperienceDecimalMultiplier) / ExperienceDecimalMultiplier;
        }

        private void ResetProgression()
        {
            currentExp = 0f;
            totalExperience = 0f;
            currentLevel = 1;
            isAwaitingLevelThreeUpgrade = false;
            hasSelectedLevelThreeUpgrade = false;
            ProgressionChanged?.Invoke();
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
            expMax = Mathf.Max(1, expMax);
            maxLevel = Mathf.Max(1, maxLevel);
            ResolveComponents();
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
