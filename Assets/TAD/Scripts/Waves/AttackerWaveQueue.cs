using System;
using System.Collections;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using UnityEngine;

namespace TAD.Waves
{
    /// <summary>
    /// Records attacker-card selections as FIFO groups. At the end of preparation, a snapshot of
    /// those groups is sent one after another; selections made during combat remain queued for the next day.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    public sealed class AttackerWaveQueue : MonoBehaviour
    {
        private sealed class QueuedAttackerGroup
        {
            private readonly HashSet<long> spawnedAttackerIds = new();

            public QueuedAttackerGroup(long id, AttackerDefinition definition)
            {
                Id = id;
                Definition = definition;
                Count = definition != null ? definition.SendSize : 0;
                Cost = definition != null ? definition.SendCost : 0;
                Income = definition != null ? definition.WaveIncome : 0;
                RemainingAlive = Count;
            }

            public long Id { get; }
            public AttackerDefinition Definition { get; }
            public int Count { get; }
            public int Cost { get; }
            public int Income { get; }
            public int RemainingAlive { get; private set; }

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
                return attacker != null && spawnedAttackerIds.Add(attacker.InstanceId);
            }

            public bool RegisterResolved(BasicAttacker attacker)
            {
                if (attacker == null || !spawnedAttackerIds.Remove(attacker.InstanceId))
                {
                    return false;
                }

                RemainingAlive = Mathf.Max(0, RemainingAlive - 1);
                return true;
            }

            public void ResetForRetry()
            {
                spawnedAttackerIds.Clear();
                RemainingAlive = Count;
            }
        }

        [SerializeField, Min(0f)]
        [Tooltip("Seconds between attackers in each player-selected group.")]
        private float spawnInterval = 0.2f;

        [SerializeField]
        [Tooltip("The player bank that pays for attacker groups and receives its persistent income after sent groups resolve.")]
        private ResourceBank attackerBank;

        private readonly Queue<QueuedAttackerGroup> queuedGroups = new();
        private readonly Queue<QueuedAttackerGroup> dispatchedGroups = new();
        private readonly List<AttackerDefinition> spawnBuffer = new();
        private WaveController waveController;
        private DayCycleController dayCycle;
        private Coroutine nextGroupRoutine;
        private Action<bool> sequenceCompletion;
        private int dispatchedIncome;
        private long nextGroupId;
        private bool isSending;

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
                                          !waveController.IsRunning &&
                                          waveController.CanStartPlayerWave;

        /// <summary>Raised whenever displayed group data or its ready state changes.</summary>
        public event Action QueueChanged;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            dayCycle = GetComponent<DayCycleController>();
            ResolveAttackerBank();
        }

        private void Start()
        {
            RaiseQueueChanged();
        }

        private void OnDisable()
        {
            if (nextGroupRoutine != null)
            {
                StopCoroutine(nextGroupRoutine);
                nextGroupRoutine = null;
            }

            isSending = false;
            sequenceCompletion = null;
            ResetGroupsForRetry(dispatchedGroups);
            dispatchedGroups.Clear();
            spawnBuffer.Clear();
            dispatchedIncome = 0;
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
            var currentGroup = dispatchedGroups.Count > 0 ? dispatchedGroups.Peek() : null;
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
            var currentGroup = dispatchedGroups.Count > 0 ? dispatchedGroups.Peek() : null;
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
        /// Pays once for a selected attacker card, queues its configured Send Size as one group,
        /// and adds its Wave Income to the persistent attacker income total.
        /// </summary>
        public bool TryQueueAttackers(AttackerDefinition definition)
        {
            if (definition == null ||
                definition.SendSize <= 0 ||
                attackerBank == null ||
                !CanQueueAttackers ||
                !attackerBank.TrySpendGold(definition.SendCost))
            {
                return false;
            }

            var group = new QueuedAttackerGroup(AllocateGroupId(), definition);
            queuedGroups.Enqueue(group);
            attackerBank.AddIncome(group.Income);
            RaiseQueueChanged();
            return true;
        }

        /// <summary>
        /// Removes one still-pending attacker group, refunds the exact cost paid for that group, and removes
        /// only that group's income from the persistent total. Groups already being dispatched cannot be removed.
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
                if (removedGroup == null && group != null && group.Id == groupId)
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
        /// Sends a snapshot of all currently queued groups. This legacy entry point preserves existing callers.
        /// </summary>
        public bool TrySendQueuedWave()
        {
            return TrySendQueuedWaves(null);
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
            isSending = true;
            while (queuedGroups.Count > 0)
            {
                dispatchedGroups.Enqueue(queuedGroups.Dequeue());
            }

            RaiseQueueChanged();
            if (TryStartNextDispatchedGroup())
            {
                return true;
            }

            RestoreDispatchedGroupsToQueue();
            isSending = false;
            dispatchedIncome = 0;
            sequenceCompletion = null;
            RaiseQueueChanged();
            return false;
        }

        /// <summary>Cancels all pending and active attacker orders when the level ends.</summary>
        public void CancelAllOrders()
        {
            if (nextGroupRoutine != null)
            {
                StopCoroutine(nextGroupRoutine);
                nextGroupRoutine = null;
            }

            var completion = sequenceCompletion;
            sequenceCompletion = null;
            isSending = false;
            dispatchedIncome = 0;
            ResetGroupsForRetry(queuedGroups);
            ResetGroupsForRetry(dispatchedGroups);
            queuedGroups.Clear();
            dispatchedGroups.Clear();
            spawnBuffer.Clear();
            attackerBank?.ClearIncome();
            RaiseQueueChanged();
            completion?.Invoke(false);
        }

        /// <summary>Clears runtime-only orders when a fresh level session begins.</summary>
        public void ResetForNewLevelSession()
        {
            if (nextGroupRoutine != null)
            {
                StopCoroutine(nextGroupRoutine);
                nextGroupRoutine = null;
            }

            isSending = false;
            sequenceCompletion = null;
            dispatchedIncome = 0;
            ResetGroupsForRetry(queuedGroups);
            ResetGroupsForRetry(dispatchedGroups);
            queuedGroups.Clear();
            dispatchedGroups.Clear();
            spawnBuffer.Clear();
            attackerBank?.ClearIncome();
            RaiseQueueChanged();
        }

        private bool TryStartNextDispatchedGroup()
        {
            if (!isSending || dispatchedGroups.Count == 0 || waveController == null)
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

            return waveController.TryStartPlayerWave(
                spawnBuffer,
                spawnInterval,
                attacker => HandleGroupAttackerSpawned(group, attacker),
                attacker => HandleGroupAttackerResolved(group, attacker),
                HandleDispatchedGroupFinished);
        }

        private void HandleGroupAttackerSpawned(QueuedAttackerGroup group, BasicAttacker attacker)
        {
            if (isSending && group != null && group.RegisterSpawned(attacker))
            {
                RaiseQueueChanged();
            }
        }

        private void HandleGroupAttackerResolved(QueuedAttackerGroup group, BasicAttacker attacker)
        {
            if (isSending && group != null && group.RegisterResolved(attacker))
            {
                RaiseQueueChanged();
            }
        }

        private void HandleDispatchedGroupFinished(bool completed)
        {
            if (!isSending)
            {
                return;
            }

            if (!completed)
            {
                CompleteSendingSequence(false);
                return;
            }

            if (dispatchedGroups.Count > 0)
            {
                dispatchedGroups.Dequeue();
                RaiseQueueChanged();
            }

            if (dispatchedGroups.Count == 0)
            {
                CompleteSendingSequence(true);
                return;
            }

            nextGroupRoutine = StartCoroutine(StartNextDispatchedGroupOnNextFrame());
        }

        private IEnumerator StartNextDispatchedGroupOnNextFrame()
        {
            yield return null;
            nextGroupRoutine = null;
            if (isSending && !TryStartNextDispatchedGroup())
            {
                CompleteSendingSequence(false);
            }
        }

        private void CompleteSendingSequence(bool completed)
        {
            if (!isSending)
            {
                return;
            }

            if (nextGroupRoutine != null)
            {
                StopCoroutine(nextGroupRoutine);
                nextGroupRoutine = null;
            }

            if (completed)
            {
                attackerBank?.PayIncome(dispatchedIncome);
                dispatchedGroups.Clear();
            }
            else
            {
                RestoreDispatchedGroupsToQueue();
            }

            isSending = false;
            dispatchedIncome = 0;
            spawnBuffer.Clear();
            var completion = sequenceCompletion;
            sequenceCompletion = null;
            RaiseQueueChanged();
            completion?.Invoke(completed);
        }

        private void RestoreDispatchedGroupsToQueue()
        {
            if (dispatchedGroups.Count == 0)
            {
                return;
            }

            var unsentGroups = dispatchedGroups.ToArray();
            var groupsQueuedDuringCombat = queuedGroups.ToArray();
            dispatchedGroups.Clear();
            queuedGroups.Clear();

            foreach (var group in unsentGroups)
            {
                group?.ResetForRetry();
                queuedGroups.Enqueue(group);
            }

            foreach (var group in groupsQueuedDuringCombat)
            {
                queuedGroups.Enqueue(group);
            }
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