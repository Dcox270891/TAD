using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
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

        private readonly List<AttackerDefinition> attackerDefinitions = new();
        private readonly List<AttackerAiCandidate> attackerAiCandidates = new();
        private readonly List<AttackerAiDefenderThreat> attackerAiDefenderThreats = new();
        private readonly List<AttackerDefinition> recentAttackerAiSelections = new();
        private readonly List<Vector3> attackerAiRoutePoints = new();

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private DayCycleController dayCycle;
        private SpawnPool spawnPool;
        private FarmHandPlacementController farmHandPlacement;
        private ResourceBank attackerBank;
        private PlayerRole playerRole;
        private int lastAttackerAiQueuedDay = int.MinValue;
        private int lastDefenderAiPreparedDay = int.MinValue;
        private int roleUiStabilizationFrames = 3;

        /// <summary>The side controlled by the human for this level instance.</summary>
        public PlayerRole SelectedPlayerRole => playerRole;

        public bool IsPlayerAttacker => playerRole == PlayerRole.Attacker;
        public bool IsPlayerDefender => playerRole == PlayerRole.Defender;

        private void Awake()
        {
            playerRole = MatchRoleSelection.SelectedRole;
            waveController = GetComponent<WaveController>();
            attackerWaveQueue = GetComponent<AttackerWaveQueue>();
            dayCycle = GetComponent<DayCycleController>();
            ResolveDependencies();
        }

        private void Start()
        {
            ResolveDependencies();
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

        private void RunDefenderAi()
        {
            if (farmHandPlacement == null || spawnPool == null ||
                lastDefenderAiPreparedDay == dayCycle.CurrentDay)
            {
                return;
            }

            var routeMapper = spawnPool.RouteMapper;
            if (routeMapper == null || routeMapper.FoodStore == null)
            {
                // The route mapper builds during level start. Try again next frame without marking
                // this preparation phase as handled.
                return;
            }

            var desiredFarmHandCount = Mathf.Min(
                Mathf.Max(1, defenderAiMaximumFarmhands),
                1 + (dayCycle.CurrentDay / Mathf.Max(1, defenderAiAdditionalFarmHandEveryDays)));

            var activeFarmHandCount = CountActiveFarmHands();
            while (activeFarmHandCount < desiredFarmHandCount)
            {
                if (!TryPlaceAiFarmHand(activeFarmHandCount))
                {
                    break;
                }

                activeFarmHandCount++;
            }

            lastDefenderAiPreparedDay = dayCycle.CurrentDay;
        }

        private bool TryPlaceAiFarmHand(int farmHandIndex)
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

            // Test several alternating lane-side candidates. This keeps defenders away from the
            // food itself and avoids fixed positions that may be occupied by a wall on one map.
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

        private void ResolveDependencies()
        {
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
