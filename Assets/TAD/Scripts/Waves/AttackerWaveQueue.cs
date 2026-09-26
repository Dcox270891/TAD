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
            public QueuedAttackerGroup(AttackerDefinition definition)
            {
                Definition = definition;
                Count = definition != null ? definition.SendSize : 0;
                Income = definition != null ? definition.WaveIncome : 0;
            }

            public AttackerDefinition Definition { get; }
            public int Count { get; }
            public int Income { get; }
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
        // Snapshot of recurring income at the start of the currently dispatched sequence.
        private int dispatchedIncome;
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

        /// <summary>Raised whenever queued groups or their ready state changes.</summary>
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
            dispatchedGroups.Clear();
            spawnBuffer.Clear();
            dispatchedIncome = 0;
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

            var group = new QueuedAttackerGroup(definition);
            queuedGroups.Enqueue(group);
            attackerBank.AddIncome(group.Income);
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
            // Income ordered during combat remains part of the next sequence payout.
            dispatchedIncome = attackerBank != null ? attackerBank.CurrentIncome : 0;
            isSending = true;
            while (queuedGroups.Count > 0)
            {
                var group = queuedGroups.Dequeue();
                dispatchedGroups.Enqueue(group);
            }

            RaiseQueueChanged();
            if (TryStartNextDispatchedGroup())
            {
                return true;
            }

            // Leave a group intact if it cannot be started, so a temporary map or pool issue never
            // silently consumes the attacker's purchased selection.
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

            return waveController.TryStartPlayerWave(spawnBuffer, spawnInterval, HandleDispatchedGroupFinished);
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
