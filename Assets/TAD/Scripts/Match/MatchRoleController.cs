using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Navigation;
using TAD.Pooling;
using TAD.UI;
using TAD.Waves;
using UnityEngine;

namespace TAD.Match
{
    /// <summary>
    /// Activates a lightweight opponent for the side not selected by the player.
    /// It intentionally uses the same queue, bank, and pool APIs as the human controls so the
    /// shared HUD and wave cue always represent authoritative gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    [RequireComponent(typeof(AttackerWaveQueue))]
    [RequireComponent(typeof(DayCycleController))]
    public sealed class MatchRoleController : MonoBehaviour
    {
        [Header("Attacker AI")]
        [SerializeField, Min(1)]
        [Tooltip("Minimum number of attacker groups queued by the AI each preparation phase.")]
        private int baseAttackerGroupsPerDay = 1;

        [SerializeField, Min(1)]
        [Tooltip("The AI queues one additional group after each many completed days.")]
        private int attackerAiAdditionalGroupEveryDays = 3;

        [Header("Defender AI")]
        [SerializeField, Min(1)]
        [Tooltip("Maximum number of farmhands placed by the defending AI in one level.")]
        private int defenderAiMaximumFarmhands = 4;

        [SerializeField, Min(1)]
        [Tooltip("The AI adds another farmhand after each many completed days, while below its cap.")]
        private int defenderAiAdditionalFarmHandEveryDays = 2;

        [SerializeField, Min(0f)]
        [Tooltip("How far the AI begins positioning farmhands beside the attacker route.")]
        private float defenderAiSideOffset = 1.4f;

        private const int RecentAttackerAiSelectionLimit = 4;
        private const int MaximumDefenderAiPlacementCandidates = 128;
        private const float DefenderAiCandidateSpacing = 0.4f;

        private readonly List<AttackerDefinition> attackerDefinitions = new();
        private readonly List<AttackerAiCandidate> attackerAiCandidates = new();
        private readonly List<AttackerAiDefenderThreat> attackerAiDefenderThreats = new();
        private readonly List<AttackerDefinition> recentAttackerAiSelections = new();
        private readonly List<Vector3> attackerAiRoutePoints = new();

        private readonly List<AttackerQueuedGroupInfo> defenderAiQueuedGroups = new();
        private readonly List<DefenderAiRoutePlan> defenderAiRoutePlans = new();
        private readonly List<Vector3> defenderAiRoutePoints = new();
        private readonly List<Vector2> defenderAiPlacementCandidates = new();
        private readonly List<DefenderAiCoverage> defenderAiExistingCoverage = new();
        private readonly List<WeaponDefinition> defenderAiAffordableUpgradeOptions = new();

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private DayCycleController dayCycle;
        private SpawnPool spawnPool;
        private FarmHandPlacementController farmHandPlacement;
        private ResourceBank attackerBank;
        private PlayerRole playerRole;
        private int lastAttackerAiQueuedDay = int.MinValue;
        private int lastDefenderAiPreparedDay = int.MinValue;
        private bool defenderAiPlanDirty = true;
        private bool isSubscribedToAttackerQueue;
        private int roleUiStabilizationFrames = 3;

        /// <summary>The side controlled by the human for this level instance.</summary>
        public PlayerRole SelectedPlayerRole => playerRole;

        public bool IsPlayerAttacker => playerRole == PlayerRole.Attacker;
        public bool IsPlayerDefender => playerRole == PlayerRole.Defender;

        private void OnEnable()
        {
            defenderAiPlanDirty = true;
            SubscribeToAttackerQueue();
        }

        private void OnDisable()
        {
            UnsubscribeFromAttackerQueue();
        }

        private void OnDestroy()
        {
            UnsubscribeFromAttackerQueue();
        }

        private void Awake()
        {
            playerRole = MatchRoleSelection.SelectedRole;
            waveController = GetComponent<WaveController>();
            attackerWaveQueue = GetComponent<AttackerWaveQueue>();
            dayCycle = GetComponent<DayCycleController>();
            ResolveDependencies();
            SubscribeToAttackerQueue();
        }

        private void Start()
        {
            ResolveDependencies();
            SubscribeToAttackerQueue();
            // FarmHandPlacementController itself still handles shared defender inspection when this
            // is false. This call only gates its Buy/confirm/cancel controls.
            farmHandPlacement?.SetPlacementInputEnabled(IsPlayerDefender);
        }

        private void LateUpdate()
        {
            if (roleUiStabilizationFrames <= 0)
            {
                return;
            }

            HideOpposingPlayerControls();
            roleUiStabilizationFrames--;
        }

        private void Update()
        {
            if (ModalOverlay.IsGameplayInputBlocked || dayCycle == null || !dayCycle.IsPreparing || dayCycle.IsComplete)
            {
                return;
            }

            ResolveDependencies();
            if (IsPlayerAttacker)
            {
                RunDefenderAi();
            }
            else
            {
                RunAttackerAi();
            }
        }

        private void HideOpposingPlayerControls()
        {
            if (IsPlayerAttacker)
            {
                foreach (var placementUi in Object.FindObjectsByType<FarmHandPlacementUi>(FindObjectsInactive.Include))
                {
                    if (placementUi != null)
                    {
                        placementUi.gameObject.SetActive(false);
                    }
                }

                return;
            }

            // Some UI prefabs can survive an editor or scene lifecycle transition. Run after every
            // Start method so the selected side, rather than Unity execution order, owns visibility.
            foreach (var attackerPanel in Object.FindObjectsByType<AttackerSelectionPanel>(FindObjectsInactive.Include))
            {
                if (attackerPanel != null)
                {
                    attackerPanel.gameObject.SetActive(false);
                }
            }

            foreach (var dispatchButton in Object.FindObjectsByType<WaveStartButton>(FindObjectsInactive.Include))
            {
                if (dispatchButton != null)
                {
                    dispatchButton.gameObject.SetActive(false);
                }
            }
        }

        private void RunAttackerAi()
        {
            if (attackerWaveQueue == null || spawnPool == null ||
                !attackerWaveQueue.CanQueueAttackers ||
                lastAttackerAiQueuedDay == dayCycle.CurrentDay)
            {
                return;
            }

            spawnPool.CopyAvailableAttackerDefinitions(attackerDefinitions);
            if (attackerDefinitions.Count == 0)
            {
                lastAttackerAiQueuedDay = dayCycle.CurrentDay;
                return;
            }

            var groupsToQueue = Mathf.Max(
                1,
                baseAttackerGroupsPerDay +
                (dayCycle.CurrentDay / Mathf.Max(1, attackerAiAdditionalGroupEveryDays)));

            if (!IsAttackerAiRouteMapReady())
            {
                // FarmRouteMapper builds its cached fields in Start. Do not consume this preparation
                // decision if script execution order reaches us one frame before that work completes.
                return;
            }

            BuildAttackerAiDefenderThreats();
            for (var groupIndex = 0; groupIndex < groupsToQueue; groupIndex++)
            {
                if (!TryQueueAiAttackerGroup(dayCycle.CurrentDay, groupIndex))
                {
                    break;
                }
            }

            // The group selection is a single per-preparation decision. New income is considered
            // naturally at the next day rather than repeatedly spending any balance mid-timer.
            lastAttackerAiQueuedDay = dayCycle.CurrentDay;
        }

        private bool IsAttackerAiRouteMapReady()
        {
            var routeMapper = spawnPool != null ? spawnPool.RouteMapper : null;
            return routeMapper != null && routeMapper.FoodStore != null && routeMapper.CachedRouteFieldCount > 0;
        }

        private bool TryQueueAiAttackerGroup(int day, int groupIndex)
        {
            if (!BuildAttackerAiCandidates())
            {
                return false;
            }

            if (!AttackerWavePlanner.TryChoose(
                    attackerAiCandidates,
                    attackerAiDefenderThreats,
                    recentAttackerAiSelections,
                    day,
                    groupIndex,
                    out var decision) ||
                !decision.IsValid)
            {
                return false;
            }

            if (!attackerWaveQueue.TryQueueAttackers(decision.Definition))
            {
                return false;
            }

            RememberAttackerAiSelection(decision.Definition);
            Debug.Log(
                $"Attacker AI queued {decision.Definition.DisplayName} ({decision.Definition.Archetype}, score {decision.Score:0.0}): {decision.Reason}.",
                this);
            return true;
        }

        private bool BuildAttackerAiCandidates()
        {
            attackerAiCandidates.Clear();
            var routeMapper = spawnPool != null ? spawnPool.RouteMapper : null;
            if (routeMapper == null || routeMapper.FoodStore == null || waveController == null)
            {
                return false;
            }

            foreach (var definition in attackerDefinitions)
            {
                if (definition == null || definition.SendSize <= 0 || definition.MaximumHealth <= 0f ||
                    definition.MovementSpeed <= 0f ||
                    (attackerBank != null && !attackerBank.CanAfford(definition.SendCost)))
                {
                    continue;
                }

                attackerAiRoutePoints.Clear();
                if (!routeMapper.TryGetFastestRoute(
                        waveController.SpawnPosition,
                        definition.Size,
                        definition.TraversalProfile,
                        attackerAiRoutePoints))
                {
                    continue;
                }

                var candidate = new AttackerAiCandidate(definition, attackerAiRoutePoints);
                if (candidate.HasUsableRoute)
                {
                    attackerAiCandidates.Add(candidate);
                }
            }

            return attackerAiCandidates.Count > 0;
        }

        private void BuildAttackerAiDefenderThreats()
        {
            attackerAiDefenderThreats.Clear();
            foreach (var farmHand in Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude))
            {
                if (farmHand == null || !farmHand.isActiveAndEnabled || farmHand.IsAwaitingLevelThreeUpgrade)
                {
                    continue;
                }

                var weapon = farmHand.WeaponDefinition;
                var projectile = weapon != null ? weapon.ProjectileDefinition : null;
                if (weapon == null || projectile == null || farmHand.Range <= 0f || farmHand.Damage <= 0f ||
                    weapon.AttackSpeed <= 0f)
                {
                    continue;
                }

                attackerAiDefenderThreats.Add(new AttackerAiDefenderThreat(
                    farmHand.transform.position,
                    farmHand.Range,
                    farmHand.Damage * weapon.AttackSpeed,
                    projectile.ProjectileType,
                    projectile.ProjectileBehavior));
            }
        }

        private void RememberAttackerAiSelection(AttackerDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            recentAttackerAiSelections.Insert(0, definition);
            if (recentAttackerAiSelections.Count > RecentAttackerAiSelectionLimit)
            {
                recentAttackerAiSelections.RemoveRange(
                    RecentAttackerAiSelectionLimit,
                    recentAttackerAiSelections.Count - RecentAttackerAiSelectionLimit);
            }
        }

        /// <summary>
        /// Chooses and purchases a legal level-three weapon for an AI farmhand. The UI bootstrap
        /// keeps ownership of the upgrade request while this controller owns defender-side policy.
        /// </summary>
        public bool TryChooseDefenderAiUpgrade(
            FarmHand farmHand,
            IReadOnlyList<WeaponDefinition> upgradeOptions)
        {
            if (!IsPlayerAttacker ||
                farmHand == null ||
                !farmHand.IsAwaitingLevelThreeUpgrade ||
                upgradeOptions == null)
            {
                return false;
            }

            ResolveDependencies();
            if (!RefreshDefenderAiRoutePlans() || defenderAiRoutePlans.Count == 0)
            {
                // Keep the request pending until the attacking player has committed a next-wave group.
                return false;
            }

            defenderAiAffordableUpgradeOptions.Clear();
            for (var index = 0; index < upgradeOptions.Count; index++)
            {
                var option = upgradeOptions[index];
                if (option != null && farmHand.CanAffordLevelThreeUpgrade(option))
                {
                    defenderAiAffordableUpgradeOptions.Add(option);
                }
            }

            if (defenderAiAffordableUpgradeOptions.Count == 0)
            {
                return false;
            }

            var currentWeapon = farmHand.WeaponDefinition;
            var progressionDamageMultiplier = currentWeapon != null && currentWeapon.Damage > 0.001f
                ? farmHand.Damage / currentWeapon.Damage
                : 1f;
            if (!DefenderAiPlanner.TryChooseWeapon(
                    farmHand.transform.position,
                    progressionDamageMultiplier,
                    defenderAiAffordableUpgradeOptions,
                    defenderAiRoutePlans,
                    out var decision) ||
                !decision.IsValid ||
                !farmHand.TryChooseLevelThreeUpgrade(decision.Weapon))
            {
                return false;
            }

            defenderAiPlanDirty = true;
            Debug.Log(
                $"Defender AI upgraded a farmhand to {decision.Weapon.DisplayName} (score {decision.Score:0.0}): {decision.Reason}.",
                this);
            return true;
        }

        private void RunDefenderAi()
        {
            if (farmHandPlacement == null || spawnPool == null ||
                (!defenderAiPlanDirty && lastDefenderAiPreparedDay == dayCycle.CurrentDay))
            {
                return;
            }

            if (!RefreshDefenderAiRoutePlans())
            {
                // FarmRouteMapper builds during level startup. Do not consume this preparation
                // decision until the actual cached animal routes are available.
                return;
            }

            var maximumFarmHands = Mathf.Max(1, defenderAiMaximumFarmhands);
            var dailyFarmHandTarget = Mathf.Min(
                maximumFarmHands,
                1 + (dayCycle.CurrentDay / Mathf.Max(1, defenderAiAdditionalFarmHandEveryDays)));
            var routeAwareFarmHandTarget = DefenderAiPlanner.GetSuggestedFarmHandCount(
                defenderAiRoutePlans,
                maximumFarmHands);
            var desiredFarmHandCount = Mathf.Max(dailyFarmHandTarget, routeAwareFarmHandTarget);

            var activeFarmHandCount = CountActiveFarmHands();
            while (activeFarmHandCount < desiredFarmHandCount)
            {
                if (!TryPlaceAiFarmHand(activeFarmHandCount, defenderAiRoutePlans))
                {
                    break;
                }

                activeFarmHandCount++;
            }

            lastDefenderAiPreparedDay = dayCycle.CurrentDay;
        }

        private bool RefreshDefenderAiRoutePlans()
        {
            if (!defenderAiPlanDirty)
            {
                return true;
            }

            if (!IsAttackerAiRouteMapReady() || attackerWaveQueue == null || waveController == null)
            {
                return false;
            }

            defenderAiQueuedGroups.Clear();
            defenderAiRoutePlans.Clear();
            attackerWaveQueue.CopyPendingGroups(defenderAiQueuedGroups);

            var routeMapper = spawnPool.RouteMapper;
            for (var index = 0; index < defenderAiQueuedGroups.Count; index++)
            {
                var group = defenderAiQueuedGroups[index];
                var definition = group.Definition;
                if (definition == null || group.TotalCount <= 0)
                {
                    continue;
                }

                defenderAiRoutePoints.Clear();
                if (!routeMapper.TryGetFastestRoute(
                        waveController.SpawnPosition,
                        definition.Size,
                        definition.TraversalProfile,
                        defenderAiRoutePoints))
                {
                    continue;
                }

                var routePlan = new DefenderAiRoutePlan(
                    definition,
                    group.TotalCount,
                    defenderAiRoutePoints);
                if (routePlan.HasUsableRoute)
                {
                    defenderAiRoutePlans.Add(routePlan);
                }
            }

            defenderAiPlanDirty = false;
            return true;
        }

        private bool TryPlaceAiFarmHand(
            int farmHandIndex,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes)
        {
            var routeMapper = spawnPool != null ? spawnPool.RouteMapper : null;
            if (routeMapper == null)
            {
                return false;
            }

            if (plannedRoutes != null && plannedRoutes.Count > 0)
            {
                BuildDefenderAiPlacementCandidates(routeMapper, plannedRoutes);
                BuildDefenderAiExistingCoverage();
                if (DefenderAiPlanner.TryChoosePlacement(
                        plannedRoutes,
                        defenderAiPlacementCandidates,
                        defenderAiExistingCoverage,
                        GetDefaultAiFarmHandRange(),
                        out var decision) &&
                    decision.IsValid &&
                    farmHandPlacement.TryPlaceFarmHandAt(decision.Position))
                {
                    return true;
                }
            }

            // Retain a route-independent fallback for a fresh map before the human attacker has
            // selected a group, or for an unusually dense environment with no valid route samples.
            return TryPlaceFallbackFarmHand(farmHandIndex);
        }

        private void BuildDefenderAiPlacementCandidates(
            FarmRouteMapper routeMapper,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes)
        {
            defenderAiPlacementCandidates.Clear();
            if (routeMapper == null || plannedRoutes == null)
            {
                return;
            }

            var maximumSegmentsPerRoute = Mathf.Max(
                1,
                MaximumDefenderAiPlacementCandidates / Mathf.Max(1, plannedRoutes.Count * 4));
            for (var routeIndex = 0;
                 routeIndex < plannedRoutes.Count &&
                 defenderAiPlacementCandidates.Count < MaximumDefenderAiPlacementCandidates;
                 routeIndex++)
            {
                var route = plannedRoutes[routeIndex];
                if (!route.HasUsableRoute)
                {
                    continue;
                }

                var routeSegmentCount = route.RoutePoints.Count - 1;
                var sampleStep = Mathf.Max(
                    1,
                    Mathf.CeilToInt((float)routeSegmentCount / maximumSegmentsPerRoute));
                for (var pointIndex = 1;
                     pointIndex < route.RoutePoints.Count &&
                     defenderAiPlacementCandidates.Count < MaximumDefenderAiPlacementCandidates;
                     pointIndex += sampleStep)
                {
                    var start = route.RoutePoints[pointIndex - 1];
                    var end = route.RoutePoints[pointIndex];
                    var midpoint = Vector2.Lerp(start, end, 0.5f);
                    TryAddDefenderAiPlacementCandidate(routeMapper, start);
                    TryAddDefenderAiPlacementCandidate(routeMapper, midpoint);

                    var segment = end - start;
                    if (segment.sqrMagnitude <= 0.0001f)
                    {
                        continue;
                    }

                    var normal = new Vector2(-segment.y, segment.x).normalized;
                    var sideOffset = Mathf.Min(
                        0.4f,
                        Mathf.Max(0.15f, defenderAiSideOffset * 0.30f));
                    TryAddDefenderAiPlacementCandidate(routeMapper, midpoint + (normal * sideOffset));
                    TryAddDefenderAiPlacementCandidate(routeMapper, midpoint - (normal * sideOffset));
                }
            }
        }

        private void TryAddDefenderAiPlacementCandidate(
            FarmRouteMapper routeMapper,
            Vector2 requestedPosition)
        {
            if (routeMapper == null ||
                defenderAiPlacementCandidates.Count >= MaximumDefenderAiPlacementCandidates ||
                !routeMapper.TryClampToVisibleMap(requestedPosition, out var candidate) ||
                IsEnvironmentOccupied(candidate))
            {
                return;
            }

            var minimumCandidateSpacingSquared = DefenderAiCandidateSpacing * DefenderAiCandidateSpacing;
            for (var index = 0; index < defenderAiPlacementCandidates.Count; index++)
            {
                if ((defenderAiPlacementCandidates[index] - candidate).sqrMagnitude <
                    minimumCandidateSpacingSquared)
                {
                    return;
                }
            }

            defenderAiPlacementCandidates.Add(candidate);
        }

        private void BuildDefenderAiExistingCoverage()
        {
            defenderAiExistingCoverage.Clear();
            foreach (var farmHand in Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude))
            {
                if (farmHand != null && farmHand.isActiveAndEnabled && farmHand.Range > 0f)
                {
                    defenderAiExistingCoverage.Add(
                        new DefenderAiCoverage(farmHand.transform.position, farmHand.Range));
                }
            }
        }

        private static float GetDefaultAiFarmHandRange()
        {
            var smallestConfiguredRange = float.PositiveInfinity;
            foreach (var farmHand in Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Include))
            {
                if (farmHand != null && farmHand.Range > 0f)
                {
                    // The base unarmed farmhand is the shortest-range authored weapon. Selecting
                    // the smallest value prevents an upgraded active farmhand from making a newly
                    // placed unarmed farmhand overestimate its initial coverage.
                    smallestConfiguredRange = Mathf.Min(smallestConfiguredRange, farmHand.Range);
                }
            }

            // The authored Unarmed weapon begins at range one. This fallback keeps the policy safe
            // until a pool has finished prewarming its first reusable defender instance.
            return float.IsPositiveInfinity(smallestConfiguredRange) ? 1f : smallestConfiguredRange;
        }

        private bool TryPlaceFallbackFarmHand(int farmHandIndex)
        {
            var routeMapper = spawnPool != null ? spawnPool.RouteMapper : null;
            var foodStore = routeMapper != null ? routeMapper.FoodStore : null;
            if (routeMapper == null || foodStore == null || waveController == null)
            {
                return false;
            }

            var spawnPosition = (Vector2)waveController.SpawnPosition;
            var foodPosition = (Vector2)foodStore.transform.position;
            var routeVector = foodPosition - spawnPosition;
            var routeLength = routeVector.magnitude;
            var routeDirection = routeLength > 0.001f ? routeVector / routeLength : Vector2.right;
            var sideDirection = new Vector2(-routeDirection.y, routeDirection.x);

            // Test several alternating lane-side candidates. This retains compatibility with maps
            // that have no selected attacker group yet.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var alternatingIndex = farmHandIndex + attempt;
                var laneFraction = Mathf.Lerp(0.38f, 0.68f, (alternatingIndex % 3) * 0.5f);
                var sideSign = (alternatingIndex & 1) == 0 ? 1f : -1f;
                var sideDistance = defenderAiSideOffset + (attempt / 2) * 0.7f;
                var candidate = spawnPosition +
                                (routeDirection * (routeLength * laneFraction)) +
                                (sideDirection * sideSign * sideDistance);

                if (!routeMapper.TryClampToVisibleMap(candidate, out var clampedCandidate) ||
                    IsEnvironmentOccupied(clampedCandidate))
                {
                    continue;
                }

                if (farmHandPlacement.TryPlaceFarmHandAt(clampedCandidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEnvironmentOccupied(Vector2 position)
        {
            var environmentLayerMask = LayerMask.GetMask("Environment");
            return environmentLayerMask != 0 &&
                   Physics2D.OverlapCircle(position, 0.18f, environmentLayerMask) != null;
        }

        private static int CountActiveFarmHands()
        {
            return Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude).Length;
        }

        private void SubscribeToAttackerQueue()
        {
            if (isSubscribedToAttackerQueue)
            {
                return;
            }

            attackerWaveQueue ??= GetComponent<AttackerWaveQueue>();
            if (attackerWaveQueue == null)
            {
                return;
            }

            attackerWaveQueue.QueueChanged += HandleAttackerQueueChanged;
            isSubscribedToAttackerQueue = true;
        }

        private void UnsubscribeFromAttackerQueue()
        {
            if (!isSubscribedToAttackerQueue)
            {
                return;
            }

            if (attackerWaveQueue != null)
            {
                attackerWaveQueue.QueueChanged -= HandleAttackerQueueChanged;
            }

            isSubscribedToAttackerQueue = false;
        }

        private void HandleAttackerQueueChanged()
        {
            defenderAiPlanDirty = true;
        }

        private void ResolveDependencies()
        {
            attackerWaveQueue ??= GetComponent<AttackerWaveQueue>();
            spawnPool ??= Object.FindAnyObjectByType<SpawnPool>();
            farmHandPlacement ??= spawnPool != null
                ? spawnPool.GetComponent<FarmHandPlacementController>()
                : Object.FindAnyObjectByType<FarmHandPlacementController>();

            if (attackerBank == null)
            {
                foreach (var bank in Object.FindObjectsByType<ResourceBank>(FindObjectsInactive.Include))
                {
                    if (bank != null && bank.BankSide == BankSide.Attacker)
                    {
                        attackerBank = bank;
                        break;
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            baseAttackerGroupsPerDay = Mathf.Max(1, baseAttackerGroupsPerDay);
            attackerAiAdditionalGroupEveryDays = Mathf.Max(1, attackerAiAdditionalGroupEveryDays);
            defenderAiMaximumFarmhands = Mathf.Max(1, defenderAiMaximumFarmhands);
            defenderAiAdditionalFarmHandEveryDays = Mathf.Max(1, defenderAiAdditionalFarmHandEveryDays);
            defenderAiSideOffset = Mathf.Max(0f, defenderAiSideOffset);
        }
#endif
    }
}
