using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using TAD.Environment;
using TAD.Match;
using TAD.Progression;
using UnityEngine;

namespace TAD.Waves
{
    /// <summary>
    /// Records attacker-card selections as FIFO groups. At the end of preparation, a snapshot of
    /// those groups begins entering the map in order; selections made during combat remain queued for the next day.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    public sealed class AttackerWaveQueue : MonoBehaviour
    {
        private sealed class QueuedAttackerGroup
        {
            private readonly HashSet<long> activeAttackerIds = new();
            private readonly HashSet<long> foodArrivalIds = new();
            private int remainingToSpawn;
            private int spawnedCount;
            private int resolvedCount;
            private bool hasSpawnedAnyAttacker;

            public QueuedAttackerGroup(long id, AttackerDefinition definition, int cost, int income)
            {
                Id = id;
                Definition = definition;
                remainingToSpawn = definition != null ? definition.SendSize : 0;
                Cost = Mathf.Max(0, cost);
                Income = Mathf.Max(0, income);
            }

            public long Id { get; }
            public AttackerDefinition Definition { get; }
            /// <summary>
            /// The number of attackers that still need to enter the map. A partially failed
            /// spawn retries only its unsent animals, so it cannot pay arrival rewards twice.
            /// </summary>
            public int Count => remainingToSpawn;
            public int Cost { get; }
            public int Income { get; }
            public bool IsSpawnSequenceFinished { get; private set; }
            public bool SpawnSequenceSucceeded { get; private set; }
            public bool HasReceivedFirstArrivalRefund { get; private set; }
            public bool HasSpawnedAnyAttacker => hasSpawnedAnyAttacker;
            public int RemainingAlive => Mathf.Max(
                0,
                (IsSpawnSequenceFinished ? spawnedCount : Count) - resolvedCount);
            public bool HasResolvedAllSpawnedAttackers => IsSpawnSequenceFinished &&
                                                        resolvedCount >= spawnedCount;

            public AttackerQueuedGroupInfo CreateInfo(bool isDispatching, bool isCurrentGroup)
            {
                return new AttackerQueuedGroupInfo(
                    Id,
                    Definition,
                    Count,
                    RemainingAlive,
                    isDispatching,
                    isCurrentGroup);
            }

            public bool RegisterSpawned(BasicAttacker attacker)
            {
                if (ReferenceEquals(attacker, null) || !activeAttackerIds.Add(attacker.InstanceId))
                {
                    return false;
                }

                spawnedCount++;
                hasSpawnedAnyAttacker = true;
                return true;
            }

            public bool RegisterResolved(BasicAttacker attacker)
            {
                if (ReferenceEquals(attacker, null) || !activeAttackerIds.Remove(attacker.InstanceId))
                {
                    return false;
                }

                resolvedCount++;
                return true;
            }

            public bool RegisterFoodStoreArrival(BasicAttacker attacker, out bool refundDue)
            {
                refundDue = false;
                if (attacker == null || !activeAttackerIds.Contains(attacker.InstanceId) ||
                    !foodArrivalIds.Add(attacker.InstanceId))
                {
                    return false;
                }

                if (!HasReceivedFirstArrivalRefund)
                {
                    HasReceivedFirstArrivalRefund = true;
                    refundDue = true;
                }

                return true;
            }

            public void CompleteSpawnSequence(bool completed)
            {
                IsSpawnSequenceFinished = true;
                SpawnSequenceSucceeded = completed;
            }

            public void ResetForRetry()
            {
                // A route/pool failure can occur after part of a group has already entered the
                // map. Those animals are already authoritative gameplay entities, so retry only
                // the ones that never spawned and keep the one-time cost-refund state intact.
                remainingToSpawn = Mathf.Max(0, remainingToSpawn - spawnedCount);
                activeAttackerIds.Clear();
                foodArrivalIds.Clear();
                spawnedCount = 0;
                resolvedCount = 0;
                IsSpawnSequenceFinished = false;
                SpawnSequenceSucceeded = false;
            }
        }

        [SerializeField, Min(0f)]
        [Tooltip("Seconds between attackers in each player-selected group.")]
        private float spawnInterval = 0.2f;

        [SerializeField]
        [Tooltip("The player bank that pays for attacker groups and earns persistent income when cards are selected and animals reach the Food Store.")]
        private ResourceBank attackerBank;

        private readonly Queue<QueuedAttackerGroup> queuedGroups = new();
        // Frozen at the start of an attack period. New player choices always remain in queuedGroups
        // for the following day/night cycle.
        private readonly Queue<QueuedAttackerGroup> dispatchedGroups = new();
        private readonly List<QueuedAttackerGroup> activeGroups = new();
        private readonly Dictionary<long, QueuedAttackerGroup> groupsByActiveAttackerId = new();
        private readonly List<AttackerDefinition> spawnBuffer = new();
        private WaveController waveController;
        private DayCycleController dayCycle;
        private FoodStore foodStore;
        private Action<bool> sequenceCompletion;
        private QueuedAttackerGroup currentSpawningGroup;
        private QueuedAttackerGroup failedDispatchedGroup;
        // Snapshot the persistent income that existed when this day's groups were frozen. Cards
        // selected during combat update the HUD immediately but belong to the next day's payout.
        private int dispatchedIncome;
        private int arrivalIncomeDuringDispatch;
        private long nextGroupId;
        private bool isSending;
        private bool isSubscribedToFoodStore;

        public bool HasQueuedAttackers => queuedGroups.Count > 0;
        public int QueuedWaveCount => queuedGroups.Count;
        public int QueuedAttackerCount => CountAttackers(queuedGroups);
        public bool IsSending => isSending;
        public bool CanQueueAttackers => waveController != null &&
                                         (dayCycle == null || dayCycle.CanAcceptAttackerOrders) &&
                                         waveController.CanQueuePlayerWave;
        public bool CanSendQueuedWave => HasQueuedAttackers &&
                                          !isSending &&
                                          waveController != null &&
                                          waveController.CanStartPlayerWave;

        /// <summary>Raised whenever displayed group data or its ready state changes.</summary>
        public event Action QueueChanged;

        /// <summary>
        /// Raised once for each attacker successfully dispatched into the level. Match progression
        /// uses this rather than queue intent, so refunded, removed, and failed-to-dispatch groups
        /// never contribute attacker-side experience.
        /// </summary>
        public event Action<AttackerDefinition> AttackerDispatched;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            dayCycle = GetComponent<DayCycleController>();
            ResolveAttackerBank();
            ResolveFoodStore();
        }

        private void Start()
        {
            SubscribeToFoodStore();
            RaiseQueueChanged();
        }

        private void OnEnable()
        {
            SubscribeToFoodStore();
        }

        private void OnDisable()
        {
            isSending = false;
            sequenceCompletion = null;
            ResetGroupsForRetry(dispatchedGroups);
            ResetGroupsForRetry(activeGroups);
            dispatchedGroups.Clear();
            activeGroups.Clear();
            groupsByActiveAttackerId.Clear();
            currentSpawningGroup = null;
            failedDispatchedGroup = null;
            spawnBuffer.Clear();
            dispatchedIncome = 0;
            arrivalIncomeDuringDispatch = 0;
            UnsubscribeFromFoodStore();
        }

        /// <summary>
        /// Copies every group that should remain visible to the player. Groups already dispatched
        /// appear first, followed by later orders that belong to the next preparation period.
        /// </summary>
        public int CopyDisplayGroups(List<AttackerQueuedGroupInfo> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            foreach (var group in activeGroups)
            {
                if (group != null)
                {
                    destination.Add(group.CreateInfo(true, false));
                }
            }

            var currentGroup = currentSpawningGroup;
            foreach (var group in dispatchedGroups)
            {
                if (group != null)
                {
                    destination.Add(group.CreateInfo(true, ReferenceEquals(group, currentGroup)));
                }
            }

            foreach (var group in queuedGroups)
            {
                if (group != null)
                {
                    destination.Add(group.CreateInfo(false, false));
                }
            }

            return destination.Count;
        }

        /// <summary>
        /// Copies only attacker groups that have not yet been dispatched. Defender AI uses this
        /// view during preparation so it plans against the next wave without double-counting
        /// animals already active on the map.
        /// </summary>
        public int CopyPendingGroups(List<AttackerQueuedGroupInfo> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            foreach (var group in queuedGroups)
            {
                if (group != null)
                {
                    destination.Add(group.CreateInfo(false, false));
                }
            }

            return destination.Count;
        }

        /// <summary>Gets the latest immutable presentation state for one stable queued-group identifier.</summary>
        public bool TryGetDisplayGroup(long groupId, out AttackerQueuedGroupInfo result)
        {
            foreach (var group in activeGroups)
            {
                if (group != null && group.Id == groupId)
                {
                    result = group.CreateInfo(true, false);
                    return true;
                }
            }

            var currentGroup = currentSpawningGroup;
            foreach (var group in dispatchedGroups)
            {
                if (group != null && group.Id == groupId)
                {
                    result = group.CreateInfo(true, ReferenceEquals(group, currentGroup));
                    return true;
                }
            }

            foreach (var group in queuedGroups)
            {
                if (group != null && group.Id == groupId)
                {
                    result = group.CreateInfo(false, false);
                    return true;
                }
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Pays once for a selected attacker card, immediately adds its authored income, and queues
        /// its configured Send Size as one group. Each animal that reaches the Food Store adds the
        /// same income again; only the first arrival refunds the group's send cost.
        /// </summary>
        public bool TryQueueAttackers(AttackerDefinition definition)
        {
            if (definition == null || definition.SendSize <= 0 || attackerBank == null || !CanQueueAttackers)
            {
                return false;
            }

            // The AI uses this same authoritative queue while the player defends. Challenge-tier
            // progression applies only to the human attacker's card tray and cannot restrict AI plans.
            if (MatchRoleSelection.IsPlayerAttacker &&
                !PlayerUpgradeProgression.IsChallengeTierUnlocked(
                    PlayerSettingsService.Instance.Profile.playerLevel,
                    definition.ChallengeTier))
            {
                return false;
            }

            var profile = MatchRoleSelection.IsPlayerAttacker
                ? PlayerSettingsService.Instance.Profile
                : null;
            var cost = PlayerUpgradeProgression.GetEffectiveAttackerCost(profile, definition);
            var income = PlayerUpgradeProgression.GetEffectiveAttackerIncome(profile, definition);
            if (!attackerBank.TrySpendGold(cost))
            {
                return false;
            }

            var group = new QueuedAttackerGroup(AllocateGroupId(), definition, cost, income);
            queuedGroups.Enqueue(group);
            attackerBank.AddIncome(group.Income);
            RaiseQueueChanged();
            TadSfxPlayer.Play(definition.AnimalCrySound, TadAudioChannelType.Attacker, transform.position);
            return true;
        }

        /// <summary>
        /// Removes one group that has not yet put an animal on the map, refunds its exact paid cost,
        /// and reverses the income awarded when it was selected. A partial dispatch cannot be removed
        /// after a retry, preventing duplicate refunds for animals that already participated in the current day.
        /// </summary>
        public bool TryRemoveQueuedGroup(long groupId)
        {
            if (groupId <= 0 || queuedGroups.Count == 0)
            {
                return false;
            }

            QueuedAttackerGroup removedGroup = null;
            var groupCount = queuedGroups.Count;
            for (var index = 0; index < groupCount; index++)
            {
                var group = queuedGroups.Dequeue();
                if (removedGroup == null && group != null && group.Id == groupId && !group.HasSpawnedAnyAttacker)
                {
                    removedGroup = group;
                    continue;
                }

                queuedGroups.Enqueue(group);
            }

            if (removedGroup == null)
            {
                return false;
            }

            attackerBank?.AddGold(removedGroup.Cost);
            attackerBank?.TryRemoveIncome(removedGroup.Income);
            RaiseQueueChanged();
            return true;
        }

        /// <summary>
        /// Sends every group currently queued in click order. New selections made while this sequence runs
        /// stay in the pending queue for the next preparation period.
        /// </summary>
        public bool TrySendQueuedWaves(Action<bool> completion)
        {
            if (!CanSendQueuedWave)
            {
                return false;
            }

            sequenceCompletion = completion;
            dispatchedIncome = attackerBank != null ? attackerBank.CurrentIncome : 0;
            arrivalIncomeDuringDispatch = 0;
            isSending = true;
            while (queuedGroups.Count > 0)
            {
                dispatchedGroups.Enqueue(queuedGroups.Dequeue());
            }

            SubscribeToFoodStore();
            RaiseQueueChanged();
            if (TryStartNextDispatchedGroup())
            {
                return true;
            }

            RestoreDispatchedGroupsToQueue();
            isSending = false;
            dispatchedIncome = 0;
            arrivalIncomeDuringDispatch = 0;
            sequenceCompletion = null;
            RaiseQueueChanged();
            return false;
        }

        /// <summary>Cancels all pending and active attacker orders when the level ends.</summary>
        public void CancelAllOrders()
        {
            var completion = sequenceCompletion;
            sequenceCompletion = null;
            isSending = false;
            ResetGroupsForRetry(queuedGroups);
            ResetGroupsForRetry(dispatchedGroups);
            ResetGroupsForRetry(activeGroups);
            queuedGroups.Clear();
            dispatchedGroups.Clear();
            activeGroups.Clear();
            groupsByActiveAttackerId.Clear();
            currentSpawningGroup = null;
            failedDispatchedGroup = null;
            spawnBuffer.Clear();
            dispatchedIncome = 0;
            arrivalIncomeDuringDispatch = 0;
            RaiseQueueChanged();
            completion?.Invoke(false);
        }

        /// <summary>Clears runtime-only orders when a fresh level session begins.</summary>
        public void ResetForNewLevelSession()
        {
            isSending = false;
            sequenceCompletion = null;
            ResetGroupsForRetry(queuedGroups);
            ResetGroupsForRetry(dispatchedGroups);
            ResetGroupsForRetry(activeGroups);
            queuedGroups.Clear();
            dispatchedGroups.Clear();
            activeGroups.Clear();
            groupsByActiveAttackerId.Clear();
            currentSpawningGroup = null;
            failedDispatchedGroup = null;
            spawnBuffer.Clear();
            dispatchedIncome = 0;
            arrivalIncomeDuringDispatch = 0;
            attackerBank?.ClearIncome();
            RaiseQueueChanged();
        }

        private bool TryStartNextDispatchedGroup()
        {
            if (!isSending || currentSpawningGroup != null || failedDispatchedGroup != null ||
                dispatchedGroups.Count == 0 || waveController == null)
            {
                return false;
            }

            var group = dispatchedGroups.Peek();
            if (group.Definition == null || group.Count <= 0)
            {
                return false;
            }

            spawnBuffer.Clear();
            for (var index = 0; index < group.Count; index++)
            {
                spawnBuffer.Add(group.Definition);
            }

            // StartCoroutine may execute through the first spawn before this call returns, so set
            // the ownership marker first. The callback then always knows which displayed group it owns.
            currentSpawningGroup = group;
            var started = waveController.TryStartPlayerWave(
                spawnBuffer,
                spawnInterval,
                attacker => HandleGroupAttackerSpawned(group, attacker),
                attacker => HandleGroupAttackerResolved(group, attacker),
                completed => HandleDispatchedGroupSpawnSequenceCompleted(group, completed),
                completion: null);
            if (!started && ReferenceEquals(currentSpawningGroup, group))
            {
                currentSpawningGroup = null;
            }

            return started;
        }

        private void HandleGroupAttackerSpawned(QueuedAttackerGroup group, BasicAttacker attacker)
        {
            if (isSending && group != null && group.RegisterSpawned(attacker))
            {
                groupsByActiveAttackerId[attacker.InstanceId] = group;
                AttackerDispatched?.Invoke(attacker != null && attacker.Definition != null
                    ? attacker.Definition
                    : group.Definition);
                RaiseQueueChanged();
            }
        }

        private void HandleGroupAttackerResolved(QueuedAttackerGroup group, BasicAttacker attacker)
        {
            if (isSending && group != null && group.RegisterResolved(attacker))
            {
                if (!ReferenceEquals(attacker, null) && groupsByActiveAttackerId.TryGetValue(attacker.InstanceId, out var owner) &&
                    ReferenceEquals(owner, group))
                {
                    groupsByActiveAttackerId.Remove(attacker.InstanceId);
                }

                if (group.HasResolvedAllSpawnedAttackers)
                {
                    activeGroups.Remove(group);
                }

                RaiseQueueChanged();
                TryFinishSendingSequenceIfReady();
            }
        }

        private void HandleDispatchedGroupSpawnSequenceCompleted(QueuedAttackerGroup group, bool completed)
        {
            if (!isSending || group == null || !ReferenceEquals(currentSpawningGroup, group))
            {
                return;
            }

            group.CompleteSpawnSequence(completed);
            if (dispatchedGroups.Count == 0 || !ReferenceEquals(dispatchedGroups.Peek(), group))
            {
                CompleteSendingSequence(false);
                return;
            }

            dispatchedGroups.Dequeue();
            currentSpawningGroup = null;
            if (!activeGroups.Contains(group))
            {
                activeGroups.Add(group);
            }

            if (group.HasResolvedAllSpawnedAttackers)
            {
                activeGroups.Remove(group);
            }

            if (!completed)
            {
                failedDispatchedGroup = group;
                RaiseQueueChanged();
                TryFinishSendingSequenceIfReady();
                return;
            }

            RaiseQueueChanged();
            if (dispatchedGroups.Count > 0)
            {
                // There is intentionally no inter-group delay. WaveController has already
                // finished the previous group's spawn sequence, so the next group may begin
                // immediately while earlier attackers remain active on the map.
                if (!TryStartNextDispatchedGroup())
                {
                    CompleteSendingSequence(false);
                }

                return;
            }

            TryFinishSendingSequenceIfReady();
        }

        private void TryFinishSendingSequenceIfReady()
        {
            if (!isSending || currentSpawningGroup != null)
            {
                return;
            }

            if (failedDispatchedGroup != null)
            {
                if (activeGroups.Count == 0)
                {
                    CompleteSendingSequence(false);
                }

                return;
            }

            if (dispatchedGroups.Count == 0 && activeGroups.Count == 0)
            {
                CompleteSendingSequence(true);
            }
        }

        private void CompleteSendingSequence(bool completed)
        {
            if (!isSending)
            {
                return;
            }

            if (completed)
            {
                // Selection income is paid from the frozen day-start snapshot. Every successful
                // barn arrival is deliberately added to this day's payout as well, while cards
                // chosen after dispatch remain for the next day's payout.
                attackerBank?.PayIncome(dispatchedIncome + arrivalIncomeDuringDispatch);
                dispatchedGroups.Clear();
                activeGroups.Clear();
                groupsByActiveAttackerId.Clear();
            }
            else
            {
                RestoreDispatchedGroupsToQueue();
            }

            isSending = false;
            currentSpawningGroup = null;
            failedDispatchedGroup = null;
            dispatchedIncome = 0;
            arrivalIncomeDuringDispatch = 0;
            spawnBuffer.Clear();
            var completion = sequenceCompletion;
            sequenceCompletion = null;
            RaiseQueueChanged();
            completion?.Invoke(completed);
        }

        private void RestoreDispatchedGroupsToQueue()
        {
            var unsentGroups = dispatchedGroups.ToArray();
            var groupsQueuedDuringCombat = queuedGroups.ToArray();
            dispatchedGroups.Clear();
            queuedGroups.Clear();

            if (failedDispatchedGroup != null)
            {
                failedDispatchedGroup.ResetForRetry();
                queuedGroups.Enqueue(failedDispatchedGroup);
            }

            foreach (var group in unsentGroups)
            {
                group?.ResetForRetry();
                queuedGroups.Enqueue(group);
            }

            foreach (var group in groupsQueuedDuringCombat)
            {
                queuedGroups.Enqueue(group);
            }

            activeGroups.Clear();
            groupsByActiveAttackerId.Clear();
        }

        private static int CountAttackers(IEnumerable<QueuedAttackerGroup> groups)
        {
            var count = 0;
            foreach (var group in groups)
            {
                count += group != null ? group.Count : 0;
            }

            return count;
        }

        private static void ResetGroupsForRetry(IEnumerable<QueuedAttackerGroup> groups)
        {
            foreach (var group in groups)
            {
                group?.ResetForRetry();
            }
        }

        private long AllocateGroupId()
        {
            if (nextGroupId == long.MaxValue)
            {
                throw new InvalidOperationException("The queued attacker group ID sequence has been exhausted.");
            }

            return ++nextGroupId;
        }

        private void ResolveAttackerBank()
        {
            if (attackerBank != null)
            {
                return;
            }

            foreach (var bank in GetComponentsInChildren<ResourceBank>(true))
            {
                if (bank != null && bank.BankSide == BankSide.Attacker)
                {
                    attackerBank = bank;
                    return;
                }
            }
        }

        private void ResolveFoodStore()
        {
            if (foodStore != null)
            {
                return;
            }

            foodStore = waveController != null ? waveController.FoodStore : null;
            foodStore ??= FindAnyObjectByType<FoodStore>();
        }

        private void SubscribeToFoodStore()
        {
            if (isSubscribedToFoodStore)
            {
                return;
            }

            ResolveFoodStore();
            if (foodStore == null)
            {
                return;
            }

            foodStore.RaiderReached += HandleFoodStoreRaiderReached;
            isSubscribedToFoodStore = true;
        }

        private void UnsubscribeFromFoodStore()
        {
            if (!isSubscribedToFoodStore || foodStore == null)
            {
                return;
            }

            foodStore.RaiderReached -= HandleFoodStoreRaiderReached;
            isSubscribedToFoodStore = false;
        }

        private void HandleFoodStoreRaiderReached(IFoodStoreRaider raider)
        {
            if (!isSending || raider is not BasicAttacker attacker ||
                !groupsByActiveAttackerId.TryGetValue(attacker.InstanceId, out var group) ||
                !group.RegisterFoodStoreArrival(attacker, out var refundDue))
            {
                return;
            }

            if (refundDue)
            {
                attackerBank?.AddGold(group.Cost);
            }

            // Every successful animal adds to the income already awarded when this card was selected.
            // The card's send cost is deliberately refunded only for the first arrival in this group.
            attackerBank?.AddIncome(group.Income);
            arrivalIncomeDuringDispatch += group.Income;
            RaiseQueueChanged();
        }

        private void RaiseQueueChanged()
        {
            QueueChanged?.Invoke();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            spawnInterval = Mathf.Max(0f, spawnInterval);
        }
#endif
    }
}
