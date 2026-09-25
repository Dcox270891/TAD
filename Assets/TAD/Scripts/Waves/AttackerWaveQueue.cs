using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Economy;
using UnityEngine;

namespace TAD.Waves
{
    /// <summary>
    /// Collects attacker groups selected by the player and sends them as one pooled, route-validated wave.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    public sealed class AttackerWaveQueue : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("Seconds between attackers in a player-composed wave.")]
        private float spawnInterval = 0.2f;

        [SerializeField]
        [Tooltip("The player bank that pays for attacker groups and receives their queued wave income.")]
        private ResourceBank attackerBank;

        private readonly List<AttackerDefinition> queuedAttackers = new();
        private WaveController waveController;
        private bool isSubscribed;

        public bool HasQueuedAttackers => queuedAttackers.Count > 0;
        public int QueuedAttackerCount => queuedAttackers.Count;
        public bool CanQueueAttackers => waveController != null && !waveController.IsRunning;
        public bool CanSendQueuedWave => HasQueuedAttackers && CanQueueAttackers;

        /// <summary>Raised whenever the group queue or its ready state changes.</summary>
        public event Action QueueChanged;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            ResolveAttackerBank();
        }

        private void OnEnable()
        {
            SubscribeToWaveController();
        }

        private void Start()
        {
            RaiseQueueChanged();
        }

        private void OnDisable()
        {
            UnsubscribeFromWaveController();
        }

        /// <summary>
        /// Pays once for a selected attacker card and queues that definition for each unit in its Send Size.
        /// The card's Wave Income is added once per selected group and paid after the queued wave succeeds.
        /// </summary>
        public bool TryQueueAttackers(AttackerDefinition definition)
        {
            if (definition == null ||
                definition.SendSize <= 0 ||
                attackerBank == null ||
                !CanQueueAttackers ||
                !waveController.CanStartPlayerWave ||
                !attackerBank.TrySpendGold(definition.SendCost))
            {
                return false;
            }

            for (var index = 0; index < definition.SendSize; index++)
            {
                queuedAttackers.Add(definition);
            }

            attackerBank.AddIncome(definition.WaveIncome);
            RaiseQueueChanged();
            return true;
        }

        /// <summary>Sends every selected attacker using the Wave Controller's existing pooled spawn pipeline.</summary>
        public bool TrySendQueuedWave()
        {
            if (!CanSendQueuedWave)
            {
                return false;
            }

            var attackersToSend = queuedAttackers.ToArray();
            if (!waveController.TryStartPlayerWave(attackersToSend, spawnInterval, HandlePlayerWaveFinished))
            {
                return false;
            }

            queuedAttackers.Clear();
            RaiseQueueChanged();
            return true;
        }

        private void HandlePlayerWaveFinished(bool completed)
        {
            if (attackerBank != null)
            {
                if (completed)
                {
                    attackerBank.CollectIncome();
                }
                else
                {
                    attackerBank.ClearIncome();
                }
            }

            RaiseQueueChanged();
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

        private void SubscribeToWaveController()
        {
            if (isSubscribed || waveController == null)
            {
                return;
            }

            waveController.WaveStarted += HandleWaveStateChanged;
            waveController.WaveRunFinished += HandleWaveRunFinished;
            isSubscribed = true;
        }

        private void UnsubscribeFromWaveController()
        {
            if (!isSubscribed || waveController == null)
            {
                return;
            }

            waveController.WaveStarted -= HandleWaveStateChanged;
            waveController.WaveRunFinished -= HandleWaveRunFinished;
            isSubscribed = false;
        }

        private void HandleWaveStateChanged(int waveNumber, int attackerCount)
        {
            RaiseQueueChanged();
        }

        private void HandleWaveRunFinished(bool completed)
        {
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
