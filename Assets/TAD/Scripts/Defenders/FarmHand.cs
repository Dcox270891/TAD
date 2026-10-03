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
        FirstEnemyInRange = 0,
        HighestHealth = 1,
        Fastest = 2,
        LastEnemyInRange = 3,
        HighestKillGain = 4
    }

    /// <summary>
    /// Reusable defender token that owns its assigned weapon, range presentation, and attackers currently in attack range.
    /// Weapon selection and target-priority behaviour are retained by the placed farmhand and reset only when it is reused by the pool.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(Rigidbody2D))]
    public sealed class FarmHand : MonoBehaviour, IPoolLifecycle
    {
        private static readonly IReadOnlyList<DefenderTargetingMode> AvailableTargetingModesInternal =
            System.Array.AsReadOnly(new[]
            {
                DefenderTargetingMode.FirstEnemyInRange,
                DefenderTargetingMode.HighestHealth,
                DefenderTargetingMode.Fastest,
                DefenderTargetingMode.LastEnemyInRange,
                DefenderTargetingMode.HighestKillGain
            });

        private const float BlockedTargetRetryInterval = 0.1f;
        private const int LineOfSightHitCapacity = 16;
        // A farmhand must feel stronger at every level without its individual scaling eclipsing
        // the increasingly frequent attacker groups in the final days of a match.
        private const float LevelDamageMultiplier = 1.07f;
        private const float LevelRangeMultiplier = 1.035f;
        private const float LevelVisualScaleMultiplier = 1.05f;
        private const int FirstWeaponUpgradeLevel = 2;
        private const int WeaponEvolutionUpgradeLevel = 5;
        private const int FinalWeaponEvolutionUpgradeLevel = 8;
        private const float RelocationArrivalDistance = 0.001f;
        private const float RelocationCostMultiplier = 0.25f;

        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private FarmHandVisualController visualController;
        [SerializeField] private WeaponDefinition weaponDefinition;
        [SerializeField] private CircleCollider2D rangeCollider;
        [SerializeField] private Rigidbody2D rangeBody;
        [SerializeField] private PolygonCollider2D placementCollider;
        [SerializeField, Min(0.01f)] private float relocationSpeed = 1f;
        [SerializeField] private DefenderRangeVisual rangeVisual;
        [SerializeField, Min(1)] private int expMax = 10;
        [SerializeField, Min(1)] private int maxLevel = 10;

        [System.NonSerialized] private float currentExp;
        [System.NonSerialized] private float totalExperience;
        [System.NonSerialized] private int currentLevel = 1;
        [System.NonSerialized] private WeaponDefinition baseWeaponDefinition;
        [System.NonSerialized] private int pendingWeaponUpgradeLevel;
        [System.NonSerialized] private bool hasSelectedFirstWeaponUpgrade;
        [System.NonSerialized] private bool hasSelectedWeaponEvolution;
        [System.NonSerialized] private bool hasSelectedFinalWeaponEvolution;
        [System.NonSerialized] private Vector3 baseLocalScale;
        [System.NonSerialized] private bool hasCapturedBaseLocalScale;
        [System.NonSerialized] private int investedGold;
        [System.NonSerialized] private bool isRelocating;
        [System.NonSerialized] private Vector2 relocationDestination;
        [System.NonSerialized] private int relocationRouteIndex;
        [System.NonSerialized] private Sprite placementFootprintSprite;
        [System.NonSerialized] private Vector3 placementFootprintVisualLocalScale;
        [System.NonSerialized] private bool hasPlacementFootprintVisualScale;
        [System.NonSerialized] private Vector2 facingDirection = Vector2.right;
        [System.NonSerialized] private DefenderTargetingMode targetingMode = DefenderTargetingMode.FirstEnemyInRange;
        [System.NonSerialized] private FarmHandUpgradeIndicator upgradeIndicator;
        [System.NonSerialized] private bool isValidating;
        private PooledInstance pooledInstance;
        private SpawnPool projectilePool;
        private ResourceBank defenderBank;
        private readonly List<BasicAttacker> attackersInRange = new();
        private readonly List<Vector3> relocationRoute = new();
        private readonly RaycastHit2D[] lineOfSightHits = new RaycastHit2D[LineOfSightHitCapacity];
        private ContactFilter2D lineOfSightFilter;
        private float nextFireTime;
        private float reloadCompleteTime;
        private int shotsRemainingInMagazine;
        private long activationSessionId;
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

        /// <summary>
        /// Diameter of a circle which fully contains the farmhand's visible placement footprint.
        /// The movement route uses it as clearance, so the sprite itself cannot clip a wall while
        /// the root transform follows a grid waypoint.
        /// </summary>
        public float RelocationPathDiameter => Mathf.Max(
            0.02f,
            Mathf.Max(PlacementFootprintSize.x, PlacementFootprintSize.y) +
            (PlacementFootprintCenterOffset.magnitude * 2f));

        /// <summary>World-space size of this farmhand's sprite-sized placement footprint.</summary>
        public Vector2 PlacementFootprintSize => placementCollider != null && placementCollider.enabled
            ? placementCollider.bounds.size
            : GetSpriteFootprintSize();

        /// <summary>World-space offset from the farmhand origin to its footprint centre.</summary>
        public Vector2 PlacementFootprintCenterOffset => placementCollider != null && placementCollider.enabled
            ? (Vector2)(placementCollider.bounds.center - transform.position)
            : GetSpriteFootprintCenterOffset();

        /// <summary>
        /// Dedicated trigger used only to prevent overlapping farmhand placement. Its paths mirror
        /// the visible body sprite, while the combat range remains on the root circle collider.
        /// </summary>
        public PolygonCollider2D PlacementCollider => placementCollider;

        /// <summary>
        /// Rebuilds the dedicated placement trigger from the current body artwork. Cosmetic and
        /// level-scale changes can call this safely; it never changes the combat range or turns a
        /// farmhand into a physical obstacle for animals or projectiles.
        /// </summary>
        public void SynchronizePlacementFootprint()
        {
            ResolveComponents();
            EnsurePlacementCollider();
        }

        /// <summary>
        /// Returns whether the Defender can apply the supplied pending weapon choice. The level-two
        /// branch is deliberately free; the level-five evolution still charges its weapon cost.
        /// </summary>
        public bool CanAffordWeaponUpgrade(WeaponDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (IsFreeWeaponUpgradePending)
            {
                return true;
            }

            var bank = ResolveDefenderBank();
            return bank != null && bank.CanAfford(definition.Cost);
        }

        /// <summary>True while this farmhand has reached an upgrade level and is waiting for a player or AI choice.</summary>
        public bool IsAwaitingWeaponUpgrade => pendingWeaponUpgradeLevel > 0;

        /// <summary>The level that owns the current pending weapon selection, or zero when no selection is waiting.</summary>
        public int PendingWeaponUpgradeLevel => pendingWeaponUpgradeLevel;

        /// <summary>True while the free level-two Stick-or-Stone selection is waiting.</summary>
        public bool IsFreeWeaponUpgradePending => pendingWeaponUpgradeLevel == FirstWeaponUpgradeLevel;

        /// <summary>True while either paid weapon-evolution selection is waiting.</summary>
        public bool IsPaidWeaponUpgradePending =>
            pendingWeaponUpgradeLevel == WeaponEvolutionUpgradeLevel ||
            pendingWeaponUpgradeLevel == FinalWeaponEvolutionUpgradeLevel;

        /// <summary>
        /// Pending farmhands remain in combat, but their rewards are paused until their upgrade choice has been made.
        /// </summary>
        public bool CanEarnExperience => !IsAwaitingWeaponUpgrade;

        /// <summary>True after this farmhand has completed its free level-two Stick-or-Stone weapon choice.</summary>
        public bool HasSelectedWeaponUpgrade => hasSelectedFirstWeaponUpgrade;

        /// <summary>True after this farmhand has completed its paid level-five weapon evolution.</summary>
        public bool HasSelectedWeaponEvolution => hasSelectedWeaponEvolution;

        /// <summary>True after this farmhand has completed its paid level-eight final evolution.</summary>
        public bool HasSelectedFinalWeaponEvolution => hasSelectedFinalWeaponEvolution;

        /// <summary>True while the current weapon magazine is reloading.</summary>
        public bool IsReloading => weaponDefinition != null && weaponDefinition.UsesReload && Time.time < reloadCompleteTime;

        /// <summary>
        /// Normalized presentation progress for the active magazine reload. Combat timing remains
        /// authoritative here; visual layers only use this value to select a reload sprite frame.
        /// </summary>
        public float ReloadProgress
        {
            get
            {
                if (!IsReloading || weaponDefinition == null || weaponDefinition.ReloadDuration <= 0f)
                {
                    return 0f;
                }

                return 1f - Mathf.Clamp01((reloadCompleteTime - Time.time) / weaponDefinition.ReloadDuration);
            }
        }

        /// <summary>Shots that can be fired before the current weapon next reloads.</summary>
        public int ShotsRemainingInMagazine => Mathf.Max(0, shotsRemainingInMagazine);

        /// <summary>
        /// Session-unique identity assigned by the pool when this defender is created.
        /// The value remains the same if this farmhand is later reused from the pool.
        /// </summary>
        public long InstanceId => pooledInstance != null ? pooledInstance.RuntimeInstanceId : 0L;

        /// <summary>
        /// Identity of this specific enabled lifetime. Unlike <see cref="InstanceId"/>, this value changes
        /// whenever a pooled farmhand is activated, allowing delayed combat work to reject a later reuse.
        /// </summary>
        public long ActivationSessionId => activationSessionId;

        /// <summary>Configured level-one experience threshold for this farmhand.</summary>
        public int BaseExperienceRequirement => expMax;

        /// <summary>Experience required to reach this farmhand's next available level.</summary>
        public int ExpMax => DefenderLevelProgression.GetExperienceRequiredForNextLevel(
            IsAtMaxLevel ? Mathf.Max(1, MaxLevel - 1) : currentLevel,
            expMax);

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

        /// <summary>Current visual scale relative to the authored farmhand size.</summary>
        public float VisualScaleMultiplier => DefenderLevelProgression.GetLevelMultiplier(
            currentLevel,
            LevelVisualScaleMultiplier);

        /// <summary>Per-farmhand damage after this farmhand's level multiplier is applied.</summary>
        public float Damage => weaponDefinition != null
            ? weaponDefinition.Damage * DefenderLevelProgression.GetLevelMultiplier(
                currentLevel,
                LevelDamageMultiplier)
            : 0f;

        /// <summary>Persistent target-order policy for this placed farmhand.</summary>
        public DefenderTargetingMode TargetingMode => targetingMode;

        /// <summary>Player-facing name of the target-order policy currently used by this farmhand.</summary>
        public string TargetingModeDisplayName => GetTargetingModeDisplayName(targetingMode);

        /// <summary>All target-order policies currently supported by the farmhand and its selector UI.</summary>
        public static IReadOnlyList<DefenderTargetingMode> AvailableTargetingModes => AvailableTargetingModesInternal;

        /// <summary>
        /// The live highest-priority animal that this farmhand can legally attack with its current weapon, if any.
        /// It is resolved on demand so entries, exits, defeats, health changes, and target-mode changes take effect immediately.
        /// </summary>
        public BasicAttacker CurrentTarget
        {
            get
            {
                var projectileDefinition = weaponDefinition != null ? weaponDefinition.ProjectileDefinition : null;
                return TryGetSelectedAttackableAttacker(projectileDefinition, out var attacker) ? attacker : null;
            }
        }

        /// <summary>Raised after this farmhand's experience or level changes.</summary>
        public event System.Action ProgressionChanged;

        /// <summary>Raised once after one or more defender levels have been earned.</summary>
        public event System.Action LeveledUp;

        /// <summary>Raised after this farmhand begins an authorised relocation.</summary>
        public event System.Action RelocationStarted;

        /// <summary>Raised after a successfully launched projectile so presentation can play its matching attack animation.</summary>
        public event System.Action<Vector2, WeaponDefinition> Fired;

        /// <summary>Raised after this farmhand's permanent gameplay weapon changes.</summary>
        public event System.Action<WeaponDefinition> WeaponChanged;

        /// <summary>Raised when this farmhand's selected target-order policy changes.</summary>
        public event System.Action<DefenderTargetingMode> TargetingModeChanged;

        /// <summary>Raised after a farmhand has configured its non-blocking range sensor.</summary>
        public static event System.Action<FarmHand> FarmHandEnabled;

        /// <summary>Raised when an active farmhand reaches a weapon-upgrade level and needs a choice.</summary>
        public static event System.Action<FarmHand> WeaponUpgradeChoiceRequested;

        /// <summary>World-space attack range supplied by the currently assigned weapon.</summary>
        public float Range => weaponDefinition != null
            ? weaponDefinition.Range * DefenderLevelProgression.GetLevelMultiplier(
                currentLevel,
                LevelRangeMultiplier)
            : 0f;

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
                DefenderTargetingMode.HighestHealth => "Highest health",
                DefenderTargetingMode.Fastest => "Fastest",
                DefenderTargetingMode.LastEnemyInRange => "Last enemy in range",
                DefenderTargetingMode.HighestKillGain => "Highest resources",
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
            CaptureBaseLocalScale();
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
            BeginActivationSession();
            ClearAttackersInRange();
            ResetRelocation();
            ResetWeaponFiringState();
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
            ResetWeaponFiringState();
            ConfigureRangeCollider();
            ConfigureRangeVisual();
            WeaponChanged?.Invoke(weaponDefinition);
        }

        /// <summary>
        /// Applies the currently pending weapon choice. The Stick-or-Stone branch at level 2 is free;
        /// level-five and level-eight evolutions charge the selected weapon's normal Defender-bank cost.
        /// </summary>
        public bool TryChooseWeaponUpgrade(WeaponDefinition definition)
        {
            if (definition == null || !IsAwaitingWeaponUpgrade || !IsWeaponCompatibleWithPendingUpgrade(definition))
            {
                return false;
            }

            var selectedUpgradeLevel = pendingWeaponUpgradeLevel;
            var isFreeChoice = selectedUpgradeLevel == FirstWeaponUpgradeLevel;
            if (!isFreeChoice)
            {
                var bank = ResolveDefenderBank();
                if (bank == null || !bank.TrySpendGold(definition.Cost))
                {
                    return false;
                }

                investedGold += Mathf.Max(0, definition.Cost);
            }

            pendingWeaponUpgradeLevel = 0;
            if (selectedUpgradeLevel == FirstWeaponUpgradeLevel)
            {
                hasSelectedFirstWeaponUpgrade = true;
            }
            else if (selectedUpgradeLevel == WeaponEvolutionUpgradeLevel)
            {
                hasSelectedWeaponEvolution = true;
            }
            else if (selectedUpgradeLevel == FinalWeaponEvolutionUpgradeLevel)
            {
                hasSelectedFinalWeaponEvolution = true;
            }

            SetWeapon(definition);
            ProgressionChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Validates the progression branch without duplicating the UI's configured option lists.
        /// At level five an evolution must retain the Projectile Type selected at level two. At level
        /// eight the chosen asset must name this farmhand's exact level-five parent weapon.
        /// </summary>
        public bool IsWeaponCompatibleWithPendingUpgrade(WeaponDefinition definition)
        {
            if (definition == null || !IsAwaitingWeaponUpgrade)
            {
                return false;
            }

            if (pendingWeaponUpgradeLevel == FirstWeaponUpgradeLevel)
            {
                // Final evolutions name a required level-five parent. They must never be accepted
                // merely because an external script bypassed the curated two-option upgrade UI.
                return definition.RequiredPreviousWeapon == null;
            }

            if (pendingWeaponUpgradeLevel == WeaponEvolutionUpgradeLevel)
            {
                var currentProjectile = weaponDefinition != null ? weaponDefinition.ProjectileDefinition : null;
                var nextProjectile = definition.ProjectileDefinition;
                // A level-eight asset may share the same projectile category, but it is not a
                // level-five evolution. Its exact parent is validated by the level-eight branch.
                return definition.RequiredPreviousWeapon == null &&
                       currentProjectile != null && nextProjectile != null &&
                       currentProjectile.ProjectileType == nextProjectile.ProjectileType;
            }

            if (pendingWeaponUpgradeLevel != FinalWeaponEvolutionUpgradeLevel)
            {
                return false;
            }

            return weaponDefinition != null &&
                   definition.RequiredPreviousWeapon != null &&
                   definition.RequiredPreviousWeapon == weaponDefinition;
        }

        /// <summary>
        /// Starts an already-authorised relocation along a route supplied by FarmRouteMapper.
        /// Placement validation and the relocation fee belong to FarmHandPlacementController so this
        /// component remains reusable by pooling and AI code.
        /// </summary>
        public bool TryBeginRelocation(Vector2 destination, IReadOnlyList<Vector3> plannedRoute)
        {
            if (!isActiveAndEnabled || isRelocating || plannedRoute == null || plannedRoute.Count < 2)
            {
                return false;
            }

            ResolveComponents();
            if (rangeBody == null || (destination - (Vector2)transform.position).sqrMagnitude <=
                RelocationArrivalDistance * RelocationArrivalDistance)
            {
                return false;
            }

            relocationRoute.Clear();
            for (var routeIndex = 0; routeIndex < plannedRoute.Count; routeIndex++)
            {
                var routePoint = plannedRoute[routeIndex];
                relocationRoute.Add(new Vector3(routePoint.x, routePoint.y, transform.position.z));
            }

            var finalRoutePoint = (Vector2)relocationRoute[^1];
            if ((finalRoutePoint - destination).sqrMagnitude > RelocationArrivalDistance * RelocationArrivalDistance)
            {
                relocationRoute.Clear();
                return false;
            }

            relocationRouteIndex = 0;
            var currentPosition = rangeBody.position;
            while (relocationRouteIndex < relocationRoute.Count &&
                   ((Vector2)relocationRoute[relocationRouteIndex] - currentPosition).sqrMagnitude <=
                   RelocationArrivalDistance * RelocationArrivalDistance)
            {
                relocationRouteIndex++;
            }

            if (relocationRouteIndex >= relocationRoute.Count)
            {
                relocationRoute.Clear();
                return false;
            }

            relocationDestination = destination;
            isRelocating = true;
            ClearAttackersInRange();
            nextFireTime = float.PositiveInfinity;

            var direction = (Vector2)relocationRoute[relocationRouteIndex] - currentPosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                facingDirection = direction.normalized;
            }

            rangeBody.linearVelocity = Vector2.zero;
            rangeBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            RelocationStarted?.Invoke();
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

        /// <summary>
        /// Applies delayed damage only when it still belongs to this exact enabled farmhand lifetime.
        /// Projectiles and damage-over-time effects capture the token at launch so pooling cannot award
        /// their rewards to a later occupant of the same farmhand instance.
        /// </summary>
        public bool TryDamageAttacker(
            BasicAttacker attacker,
            float damageAmount,
            long sourceActivationSessionId)
        {
            return IsActivationSessionCurrent(sourceActivationSessionId) &&
                   TryDamageAttacker(attacker, damageAmount);
        }

        /// <summary>Returns whether a delayed combat action still belongs to this enabled farmhand lifetime.</summary>
        public bool IsActivationSessionCurrent(long sessionId)
        {
            return sessionId != 0L &&
                   activationSessionId == sessionId &&
                   isActiveAndEnabled &&
                   gameObject.activeInHierarchy;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TrackAttacker(other);
        }

        private void BeginActivationSession()
        {
            // Reaching this limit would require more than nine quintillion enable cycles, but retaining a
            // non-zero token preserves the "zero means no valid source" contract if it ever occurs.
            activationSessionId = activationSessionId == long.MaxValue
                ? 1L
                : activationSessionId + 1L;
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
            CaptureBaseLocalScale();
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
            ResetWeaponFiringState();
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
            reloadCompleteTime = 0f;
            shotsRemainingInMagazine = 0;
            SetRangeIndicatorVisible(false);
        }

        private void ResolveComponents()
        {
            visual ??= GetComponent<SpriteRenderer>();
            visualController ??= GetComponent<FarmHandVisualController>();
            rangeCollider ??= GetComponent<CircleCollider2D>();
            rangeBody ??= GetComponent<Rigidbody2D>();
            placementCollider ??= transform.Find("PlacementHitbox")?.GetComponent<PolygonCollider2D>();
            rangeVisual ??= GetComponentInChildren<DefenderRangeVisual>(true);
            pooledInstance ??= GetComponent<PooledInstance>();
            upgradeIndicator ??= GetComponent<FarmHandUpgradeIndicator>();
            if (upgradeIndicator == null && Application.isPlaying && !isValidating)
            {
                upgradeIndicator = gameObject.AddComponent<FarmHandUpgradeIndicator>();
            }

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
            if (isRelocating || Time.time < nextFireTime)
            {
                return;
            }

            if (IsReloading)
            {
                nextFireTime = Mathf.Max(nextFireTime, reloadCompleteTime);
                return;
            }

            CompleteReloadIfNeeded();

            var weapon = weaponDefinition;
            var projectileDefinition = weapon != null ? weapon.ProjectileDefinition : null;
            var damage = Damage;
            if (weapon == null || projectileDefinition == null || projectileDefinition.ProjectilePrefab == null ||
                projectileDefinition.ProjectileSpeed <= 0f || damage <= 0f ||
                (projectileDefinition.UsesTimedLifetime && projectileDefinition.ProjectileLifetime <= 0f))
            {
                return;
            }

            if (!TryGetSelectedAttackableAttacker(projectileDefinition, out var target))
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
                    Range,
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

            ScheduleNextShot(weapon);
            Fired?.Invoke(FacingDirection, weapon);
        }

        /// <summary>Restores the authored magazine whenever a pooled defender is activated or re-equipped.</summary>
        private void ResetWeaponFiringState()
        {
            shotsRemainingInMagazine = weaponDefinition != null
                ? weaponDefinition.ShotsPerMagazine
                : 0;
            reloadCompleteTime = 0f;
            nextFireTime = Time.time;
        }

        private void CompleteReloadIfNeeded()
        {
            if (weaponDefinition == null || !weaponDefinition.UsesReload ||
                shotsRemainingInMagazine > 0 || Time.time < reloadCompleteTime)
            {
                return;
            }

            shotsRemainingInMagazine = weaponDefinition.ShotsPerMagazine;
            reloadCompleteTime = 0f;
        }

        /// <summary>
        /// Applies normal fire cadence first, then starts the shared weapon reload only after a successful shot.
        /// Failed pool launches never consume ammunition.
        /// </summary>
        private void ScheduleNextShot(WeaponDefinition weapon)
        {
            if (weapon == null)
            {
                return;
            }

            nextFireTime = Time.time + weapon.FireInterval;
            if (!weapon.UsesReload)
            {
                return;
            }

            shotsRemainingInMagazine = Mathf.Max(0, shotsRemainingInMagazine - 1);
            if (shotsRemainingInMagazine > 0)
            {
                return;
            }

            reloadCompleteTime = Time.time + weapon.ReloadDuration;
            nextFireTime = Mathf.Max(nextFireTime, reloadCompleteTime);
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

        private bool TryGetSelectedAttackableAttacker(ProjectileDefinition projectileDefinition, out BasicAttacker attacker)
        {
            PruneInvalidAttackers();
            var projectileType = projectileDefinition != null
                ? projectileDefinition.ProjectileType
                : ProjectileType.Melee;

            switch (targetingMode)
            {
                case DefenderTargetingMode.HighestHealth:
                case DefenderTargetingMode.Fastest:
                case DefenderTargetingMode.HighestKillGain:
                    return TryGetHighestValueAttackableAttacker(projectileDefinition, projectileType, targetingMode, out attacker);
                case DefenderTargetingMode.LastEnemyInRange:
                    return TryGetLastEnemyInRange(projectileDefinition, projectileType, out attacker);
                case DefenderTargetingMode.FirstEnemyInRange:
                default:
                    return TryGetFirstEnemyInRange(projectileDefinition, projectileType, out attacker);
            }
        }

        /// <summary>
        /// The sensor appends attackers in entry order and prunes defeated, pooled, reached, and departed animals before every search.
        /// This automatically advances to the next eligible animal when the current first enemy is no longer valid.
        /// </summary>
        private bool TryGetFirstEnemyInRange(
            ProjectileDefinition projectileDefinition,
            ProjectileType projectileType,
            out BasicAttacker attacker)
        {
            for (var index = 0; index < attackersInRange.Count; index++)
            {
                var candidate = attackersInRange[index];
                if (CanTargetCandidate(candidate, projectileDefinition, projectileType) &&
                    !IsCandidateBlockedByWall(candidate, projectileType))
                {
                    attacker = candidate;
                    return true;
                }
            }

            attacker = null;
            return false;
        }

        /// <summary>
        /// Selects the newest eligible attacker. The range list is appended in entry order, so scanning it backwards
        /// naturally selects the latest animal to enter while still respecting wall and cover rules.
        /// </summary>
        private bool TryGetLastEnemyInRange(
            ProjectileDefinition projectileDefinition,
            ProjectileType projectileType,
            out BasicAttacker attacker)
        {
            for (var index = attackersInRange.Count - 1; index >= 0; index--)
            {
                var candidate = attackersInRange[index];
                if (CanTargetCandidate(candidate, projectileDefinition, projectileType) &&
                    !IsCandidateBlockedByWall(candidate, projectileType))
                {
                    attacker = candidate;
                    return true;
                }
            }

            attacker = null;
            return false;
        }

        /// <summary>
        /// Selects the highest live value amongst targets this weapon can attack. Strict comparison deliberately
        /// leaves equal values with the first animal that entered range.
        /// </summary>
        private bool TryGetHighestValueAttackableAttacker(
            ProjectileDefinition projectileDefinition,
            ProjectileType projectileType,
            DefenderTargetingMode mode,
            out BasicAttacker attacker)
        {
            attacker = null;
            var highestValue = float.NegativeInfinity;

            for (var index = 0; index < attackersInRange.Count; index++)
            {
                var candidate = attackersInRange[index];
                if (!CanTargetCandidate(candidate, projectileDefinition, projectileType) ||
                    IsCandidateBlockedByWall(candidate, projectileType))
                {
                    continue;
                }

                var candidateValue = GetTargetingValue(candidate, mode);
                if (attacker == null || candidateValue > highestValue)
                {
                    attacker = candidate;
                    highestValue = candidateValue;
                }
            }

            return attacker != null;
        }

        /// <summary>
        /// Keeps every target-order policy aligned with the projectile's combat rules. Flying animals remain
        /// in the range sensor so a later airborne-capable weapon can acquire them immediately.
        /// </summary>
        private static bool CanTargetCandidate(
            BasicAttacker candidate,
            ProjectileDefinition projectileDefinition,
            ProjectileType fallbackProjectileType)
        {
            if (!IsValidAttacker(candidate))
            {
                return false;
            }

            return projectileDefinition != null
                ? projectileDefinition.CanTarget(candidate)
                : ProjectileDefinition.CanTarget(fallbackProjectileType, candidate);
        }

        private static float GetTargetingValue(BasicAttacker attacker, DefenderTargetingMode mode)
        {
            return mode switch
            {
                DefenderTargetingMode.HighestHealth => attacker.RemainingHealth,
                DefenderTargetingMode.Fastest => attacker.MovementSpeed,
                DefenderTargetingMode.HighestKillGain => attacker.KillGain,
                _ => 0f
            };
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

                placementCollider = footprintTransform.GetComponent<PolygonCollider2D>();
                if (placementCollider == null)
                {
                    // Older pooled instances can carry the former rectangular placement trigger.
                    // Leave it disabled rather than allowing it to widen the defender-only overlap
                    // test alongside the generated body outline.
                    var legacyBoxCollider = footprintTransform.GetComponent<BoxCollider2D>();
                    if (legacyBoxCollider != null)
                    {
                        legacyBoxCollider.enabled = false;
                    }

                    placementCollider = footprintTransform.gameObject.AddComponent<PolygonCollider2D>();
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
                hasPlacementFootprintVisualScale = false;
                return;
            }

            // Keep the child transform neutral so each point from Unity's generated sprite shape
            // is stored directly in the placement trigger's local space. The helper handles all
            // parent scale, so level growth and preview scale stay visually exact.
            placementCollider.transform.localPosition = Vector3.zero;
            placementCollider.transform.localRotation = Quaternion.identity;
            placementCollider.transform.localScale = Vector3.one;
            placementCollider.offset = Vector2.zero;
            placementCollider.enabled = SpritePhysicsFootprint2D.SynchronizePolygonCollider(placementCollider, visual);
            placementFootprintSprite = visual.sprite;
            placementFootprintVisualLocalScale = visual.transform.localScale;
            hasPlacementFootprintVisualScale = true;
        }

        private void UpdatePlacementColliderIfNeeded()
        {
            if (placementCollider != null && visual != null &&
                (visual.sprite != placementFootprintSprite ||
                 !hasPlacementFootprintVisualScale ||
                 visual.transform.localScale != placementFootprintVisualLocalScale))
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
            if (rangeBody == null || relocationRouteIndex >= relocationRoute.Count)
            {
                ResetRelocation();
                return;
            }

            var currentPosition = rangeBody.position;
            var nextWaypoint = (Vector2)relocationRoute[relocationRouteIndex];
            var remaining = nextWaypoint - currentPosition;
            var step = Mathf.Max(0.01f, relocationSpeed) * Time.fixedDeltaTime;
            if (remaining.sqrMagnitude <= RelocationArrivalDistance * RelocationArrivalDistance || remaining.magnitude <= step)
            {
                rangeBody.position = nextWaypoint;
                relocationRouteIndex++;
                if (relocationRouteIndex >= relocationRoute.Count)
                {
                    // MovePosition is normally applied at the end of the physics tick. The final
                    // waypoint is an exact, already-validated destination, so commit it to the
                    // visual Transform as well before releasing the defender to fire again.
                    rangeBody.position = relocationDestination;
                    rangeBody.transform.position = new Vector3(
                        relocationDestination.x,
                        relocationDestination.y,
                        rangeBody.transform.position.z);
                    CompleteRelocation();
                }

                return;
            }

            var nextPosition = Vector2.MoveTowards(currentPosition, nextWaypoint, step);
            rangeBody.MovePosition(nextPosition);
            facingDirection = remaining.normalized;
        }

        private void CompleteRelocation()
        {
            isRelocating = false;
            relocationDestination = transform.position;
            relocationRouteIndex = relocationRoute.Count;
            ConfigureRangeBody();
            nextFireTime = Time.time;
        }

        private void ResetRelocation()
        {
            isRelocating = false;
            relocationDestination = transform.position;
            relocationRoute.Clear();
            relocationRouteIndex = 0;
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
                    assistingDefender.gameObject.activeInHierarchy &&
                    assistingDefender.CanEarnExperience)
                {
                    eligibleRecipients.Add(assistingDefender);
                }
            }

            return eligibleRecipients;
        }

        private float AddExperience(float awardedAmount)
        {
            awardedAmount = DefenderRewardCalculator.RoundToNearestTenth(awardedAmount);
            if (awardedAmount <= 0f || !CanEarnExperience)
            {
                return 0f;
            }

            var shouldRequestWeaponUpgrade = false;
            var levelChanged = false;
            var creditedAmount = 0f;

            // Results can credit every earned point after the level cap. At an upgrade level, however,
            // progression intentionally pauses as soon as the choice becomes available. This keeps an
            // oversized kill award from silently skipping a player decision.
            if (IsAtMaxLevel)
            {
                creditedAmount = awardedAmount;
            }
            else
            {
                var remainingAmount = awardedAmount;
                while (remainingAmount > 0f && currentLevel < MaxLevel && !IsAwaitingWeaponUpgrade)
                {
                    var expThreshold = Mathf.Max(1, ExpMax);
                    var amountNeeded = Mathf.Max(0f, expThreshold - currentExp);
                    var appliedAmount = Mathf.Min(remainingAmount, amountNeeded);
                    currentExp = DefenderRewardCalculator.RoundToNearestTenth(currentExp + appliedAmount);
                    creditedAmount = DefenderRewardCalculator.RoundToNearestTenth(creditedAmount + appliedAmount);
                    remainingAmount = DefenderRewardCalculator.RoundToNearestTenth(remainingAmount - appliedAmount);
                    if (currentExp + 0.0001f < expThreshold)
                    {
                        break;
                    }

                    currentExp = DefenderRewardCalculator.RoundToNearestTenth(currentExp - expThreshold);
                    currentLevel++;
                    levelChanged = true;

                    if (TryBeginPendingWeaponUpgrade())
                    {
                        shouldRequestWeaponUpgrade = true;
                        break;
                    }
                }

                if (IsAtMaxLevel)
                {
                    // Current XP only represents progress toward an obtainable next level.
                    currentExp = 0f;
                    creditedAmount = DefenderRewardCalculator.RoundToNearestTenth(creditedAmount + remainingAmount);
                }
            }

            totalExperience = DefenderRewardCalculator.RoundToNearestTenth(totalExperience + creditedAmount);

            if (levelChanged)
            {
                ApplyLevelPresentation();
                ConfigurePlacementCollider();
                ConfigureRangeCollider();
                ConfigureRangeVisual();
                LeveledUp?.Invoke();
            }

            ProgressionChanged?.Invoke();
            if (shouldRequestWeaponUpgrade)
            {
                WeaponUpgradeChoiceRequested?.Invoke(this);
            }

            return creditedAmount;
        }

        private bool TryBeginPendingWeaponUpgrade()
        {
            if (IsAwaitingWeaponUpgrade)
            {
                return false;
            }

            if (currentLevel == FirstWeaponUpgradeLevel && !hasSelectedFirstWeaponUpgrade)
            {
                pendingWeaponUpgradeLevel = FirstWeaponUpgradeLevel;
                return true;
            }

            if (currentLevel == WeaponEvolutionUpgradeLevel && !hasSelectedWeaponEvolution)
            {
                pendingWeaponUpgradeLevel = WeaponEvolutionUpgradeLevel;
                return true;
            }

            if (currentLevel == FinalWeaponEvolutionUpgradeLevel &&
                hasSelectedWeaponEvolution &&
                !hasSelectedFinalWeaponEvolution &&
                weaponDefinition != null &&
                weaponDefinition.UnlocksFinalEvolution)
            {
                pendingWeaponUpgradeLevel = FinalWeaponEvolutionUpgradeLevel;
                return true;
            }

            return false;
        }

        private void ResetProgression()
        {
            currentExp = 0f;
            totalExperience = 0f;
            currentLevel = 1;
            pendingWeaponUpgradeLevel = 0;
            hasSelectedFirstWeaponUpgrade = false;
            hasSelectedWeaponEvolution = false;
            hasSelectedFinalWeaponEvolution = false;
            ResetWeaponFiringState();
            ApplyLevelPresentation();
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

        private void CaptureBaseLocalScale()
        {
            if (hasCapturedBaseLocalScale)
            {
                return;
            }

            baseLocalScale = transform.localScale;
            hasCapturedBaseLocalScale = true;
        }

        private void ApplyLevelPresentation()
        {
            CaptureBaseLocalScale();
            transform.localScale = Vector3.Scale(
                baseLocalScale,
                Vector3.one * VisualScaleMultiplier);
        }

        private static DefenderTargetingMode NormalizeTargetingMode(DefenderTargetingMode mode)
        {
            return mode switch
            {
                DefenderTargetingMode.FirstEnemyInRange => mode,
                DefenderTargetingMode.HighestHealth => mode,
                DefenderTargetingMode.Fastest => mode,
                DefenderTargetingMode.LastEnemyInRange => mode,
                DefenderTargetingMode.HighestKillGain => mode,
                _ => DefenderTargetingMode.FirstEnemyInRange
            };
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
            isValidating = true;
            try
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
            finally
            {
                isValidating = false;
            }
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
