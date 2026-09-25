using System;
using TAD.Economy;
using TAD.Environment;
using TAD.UI;
using UnityEngine;

namespace TAD.Waves
{
    /// <summary>
    /// Owns the level's preparation-and-attack rhythm. Attacker orders are collected during both
    /// preparation and combat, then the orders present at the end of preparation are dispatched in FIFO order.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController), typeof(AttackerWaveQueue))]
    public sealed class DayCycleController : MonoBehaviour
    {
        private static int playSessionGeneration;

        private enum CyclePhase
        {
            Inactive,
            Preparation,
            EmptyDayTimer,
            AttackerWaves,
            DefendersWon,
            AttackersWon
        }

        [SerializeField, Min(0.1f)]
        [Tooltip("Seconds available to prepare and queue attacker groups before they are sent.")]
        private float preparationDuration = 5f;

        [SerializeField, Min(0f)]
        [Tooltip("Minimum length of a day when no attacker groups are sent, including preparation.")]
        private float minimumDayDuration = 10f;

        [SerializeField, Min(1)]
        [Tooltip("The defenders win after this many completed days while food remains.")]
        private int defenderVictoryDay = 10;

        [SerializeField]
        [Tooltip("Optional reusable bottom-centre display for the day and preparation timer.")]
        private DayCycleHud dayCycleHudPrefab;

        [SerializeField]
        [Tooltip("Optional reusable terminal overlay shown when the defenders survive the final day.")]
        private DefenderWinOverlay defenderWinOverlayPrefab;

        [SerializeField]
        [Tooltip("The Food Store this cycle protects. Left empty to find the active level store.")]
        private FoodStore foodStore;

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private DayCycleHud spawnedHud;
        private DefenderWinOverlay spawnedDefenderWinOverlay;
        private bool ownsSpawnedHud;
        private bool ownsSpawnedDefenderWinOverlay;
        private bool isSubscribedToFoodStore;
        private CyclePhase phase;
        private float preparationRemaining;
        private float emptyDayRemaining;
        private int currentDay;
        private int lastPublishedPreparationSecond = -1;
        private int lastPublishedEmptyDaySecond = -1;
        private int initializedPlaySessionGeneration = -1;

        /// <summary>The current completed-day count, beginning at zero when the level loads.</summary>
        public int CurrentDay => currentDay;

        /// <summary>Remaining scaled seconds in the current preparation period.</summary>
        public float PreparationRemaining => preparationRemaining;

        /// <summary>Remaining scaled seconds before an empty day can end.</summary>
        public float EmptyDayRemaining => emptyDayRemaining;

        public bool IsPreparing => phase == CyclePhase.Preparation;
        public bool IsWaitingForEmptyDay => phase == CyclePhase.EmptyDayTimer;
        public bool IsAttackerWavesRunning => phase == CyclePhase.AttackerWaves;
        public bool IsComplete => phase == CyclePhase.DefendersWon || phase == CyclePhase.AttackersWon;
        public bool CanPause => phase == CyclePhase.Preparation ||
                                phase == CyclePhase.EmptyDayTimer ||
                                phase == CyclePhase.AttackerWaves;
        public bool CanAcceptAttackerOrders => !IsComplete && foodStore != null && !foodStore.IsDepleted;
        public bool CanStartQueuedWavesEarly => IsPreparing &&
                                                  CanAcceptAttackerOrders &&
                                                  attackerWaveQueue != null &&
                                                  attackerWaveQueue.HasQueuedAttackers &&
                                                  !attackerWaveQueue.IsSending;

        /// <summary>Raised when day, phase, or the displayed preparation second changes.</summary>
        public event Action CycleChanged;

        /// <summary>Raised after the tenth completed day when the Food Store remains intact.</summary>
        public event Action DefendersWon;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            attackerWaveQueue = GetComponent<AttackerWaveQueue>();
            ResolveFoodStore();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                SubscribeToFoodStore();
            }
        }

        private void Start()
        {
            InitializeForNewLevelSession();
        }

        private void Update()
        {
            if (initializedPlaySessionGeneration != playSessionGeneration)
            {
                InitializeForNewLevelSession();
            }

            if (IsComplete)
            {
                return;
            }

            if (IsPreparing)
            {
                preparationRemaining = Mathf.Max(0f, preparationRemaining - Time.deltaTime);
                PublishPreparationChangeIfNeeded();
                if (preparationRemaining <= 0f)
                {
                    StartQueuedWaves();
                }

                return;
            }

            if (!IsWaitingForEmptyDay)
            {
                return;
            }

            // A late card selection during the empty-day countdown becomes this day's wave instead
            // of being silently deferred or skipped.
            if (attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers)
            {
                StartQueuedWaves();
                return;
            }

            emptyDayRemaining = Mathf.Max(0f, emptyDayRemaining - Time.deltaTime);
            PublishEmptyDayChangeIfNeeded();
            if (emptyDayRemaining <= 0f)
            {
                CompleteCurrentDay();
            }
        }

        /// <summary>Restores all level-owned runtime state before beginning a new Day 0 preparation period.</summary>
        private void InitializeForNewLevelSession()
        {
            initializedPlaySessionGeneration = playSessionGeneration;
            currentDay = 0;
            phase = CyclePhase.Inactive;
            preparationRemaining = 0f;
            emptyDayRemaining = 0f;
            lastPublishedPreparationSecond = -1;
            lastPublishedEmptyDaySecond = -1;

            // Clear the queue before stopping its wave, so a cancellation callback from an old session
            // cannot advance a newly reset day cycle.
            attackerWaveQueue?.ResetForNewLevelSession();
            waveController?.ResetForNewLevelSession();
            ResolveFoodStore();
            if (waveController == null || attackerWaveQueue == null || foodStore == null)
            {
                Debug.LogError("Day Cycle needs a Wave Controller, Attacker Wave Queue, and Food Store.", this);
                return;
            }

            foodStore.ResetFood();
            ResetLevelResourceBanks();
            SubscribeToFoodStore();
            CreateAndBindViews();
            HideExistingModalOverlays();
            Time.timeScale = 1f;
            BeginPreparation();
        }

        private static void ResetLevelResourceBanks()
        {
            foreach (var bank in FindObjectsByType<ResourceBank>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                bank?.ResetGold();
            }
        }

        private static void HideExistingModalOverlays()
        {
            foreach (var overlay in FindObjectsByType<ModalOverlay>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                overlay?.Hide();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromFoodStore();
        }

        private void OnDestroy()
        {
            UnsubscribeFromFoodStore();

            if (ownsSpawnedHud && spawnedHud != null)
            {
                Destroy(spawnedHud.gameObject);
            }

            if (ownsSpawnedDefenderWinOverlay && spawnedDefenderWinOverlay != null)
            {
                Destroy(spawnedDefenderWinOverlay.gameObject);
            }
        }

        /// <summary>Starts the currently queued attacker groups before the timer reaches zero.</summary>
        public bool TryStartQueuedWavesEarly()
        {
            return CanStartQueuedWavesEarly && StartQueuedWaves();
        }

        private bool StartQueuedWaves()
        {
            if (!CanAcceptAttackerOrders || (!IsPreparing && !IsWaitingForEmptyDay))
            {
                return false;
            }

            if (attackerWaveQueue == null || !attackerWaveQueue.HasQueuedAttackers)
            {
                BeginEmptyDayTimer();
                return true;
            }

            phase = CyclePhase.AttackerWaves;
            PublishCycleChanged();
            if (attackerWaveQueue.TrySendQueuedWaves(HandleQueuedWavesFinished))
            {
                return true;
            }

            // A temporary route or pool failure leaves the orders queued. Give the player another
            // preparation window instead of silently taking their gold or advancing the day.
            if (!IsComplete)
            {
                BeginPreparation();
            }

            return false;
        }

        private void HandleQueuedWavesFinished(bool completed)
        {
            if (IsComplete)
            {
                return;
            }

            if (!completed)
            {
                BeginPreparation();
                return;
            }

            CompleteCurrentDay();
        }

        private void CompleteCurrentDay()
        {
            if (IsComplete)
            {
                return;
            }

            currentDay++;
            if (currentDay >= defenderVictoryDay)
            {
                phase = CyclePhase.DefendersWon;
                preparationRemaining = 0f;
                emptyDayRemaining = 0f;
                PublishCycleChanged();
                DefendersWon?.Invoke();
                return;
            }

            BeginPreparation();
        }

        private void BeginPreparation()
        {
            if (IsComplete || foodStore == null || foodStore.IsDepleted)
            {
                return;
            }

            phase = CyclePhase.Preparation;
            preparationRemaining = preparationDuration;
            emptyDayRemaining = 0f;
            lastPublishedPreparationSecond = -1;
            lastPublishedEmptyDaySecond = -1;
            PublishCycleChanged();
        }

        private void BeginEmptyDayTimer()
        {
            if (IsComplete || foodStore == null || foodStore.IsDepleted)
            {
                return;
            }

            phase = CyclePhase.EmptyDayTimer;
            emptyDayRemaining = Mathf.Max(0f, minimumDayDuration - preparationDuration);
            lastPublishedEmptyDaySecond = -1;
            PublishCycleChanged();
            if (emptyDayRemaining <= 0f)
            {
                CompleteCurrentDay();
            }
        }

        private void PublishPreparationChangeIfNeeded()
        {
            var displayedSeconds = Mathf.CeilToInt(preparationRemaining);
            if (displayedSeconds == lastPublishedPreparationSecond)
            {
                return;
            }

            lastPublishedPreparationSecond = displayedSeconds;
            PublishCycleChanged();
        }

        private void PublishEmptyDayChangeIfNeeded()
        {
            var displayedSeconds = Mathf.CeilToInt(emptyDayRemaining);
            if (displayedSeconds == lastPublishedEmptyDaySecond)
            {
                return;
            }

            lastPublishedEmptyDaySecond = displayedSeconds;
            PublishCycleChanged();
        }

        private void HandleFoodDepleted()
        {
            if (IsComplete)
            {
                return;
            }

            phase = CyclePhase.AttackersWon;
            preparationRemaining = 0f;
            emptyDayRemaining = 0f;
            PublishCycleChanged();
            attackerWaveQueue?.CancelAllOrders();
        }

        private void CreateAndBindViews()
        {
            spawnedHud = FindAnyObjectByType<DayCycleHud>(FindObjectsInactive.Include);
            if (spawnedHud == null && dayCycleHudPrefab != null)
            {
                spawnedHud = Instantiate(dayCycleHudPrefab);
                ownsSpawnedHud = true;
            }

            spawnedHud?.Bind(this);

            spawnedDefenderWinOverlay = FindAnyObjectByType<DefenderWinOverlay>(FindObjectsInactive.Include);
            if (spawnedDefenderWinOverlay == null && defenderWinOverlayPrefab != null)
            {
                spawnedDefenderWinOverlay = Instantiate(defenderWinOverlayPrefab);
                ownsSpawnedDefenderWinOverlay = true;
            }

            spawnedDefenderWinOverlay?.Bind(this);
        }

        private void ResolveFoodStore()
        {
            foodStore ??= FindAnyObjectByType<FoodStore>();
        }

        private void SubscribeToFoodStore()
        {
            if (isSubscribedToFoodStore || foodStore == null)
            {
                return;
            }

            foodStore.FoodDepleted += HandleFoodDepleted;
            isSubscribedToFoodStore = true;
        }

        private void UnsubscribeFromFoodStore()
        {
            if (!isSubscribedToFoodStore || foodStore == null)
            {
                return;
            }

            foodStore.FoodDepleted -= HandleFoodDepleted;
            isSubscribedToFoodStore = false;
        }

        private void PublishCycleChanged()
        {
            CycleChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void AdvancePlaySessionGeneration()
        {
            // SubsystemRegistration also runs when Enter Play Mode Options disable domain reload.
            playSessionGeneration++;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            preparationDuration = Mathf.Max(0.1f, preparationDuration);
            minimumDayDuration = Mathf.Max(0f, minimumDayDuration);
            defenderVictoryDay = Mathf.Max(1, defenderVictoryDay);
        }
#endif
    }
}
