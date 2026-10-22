using System;
using System.Collections;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Environment;
using TAD.Navigation;
using TAD.Pooling;
using TAD.Progression;
using UnityEngine;

namespace TAD.Waves
{
    [DisallowMultipleComponent]
    public sealed class WaveController : MonoBehaviour
    {
        // The mapper deliberately works only inside the camera view. Keep the spawn and Food
        // Store safely within that view before caching routes, including in level layouts that
        // use the reusable core at an offset.
        private const float RouteEndpointViewportPadding = 0.75f;

        /// <summary>
        /// Owns one player-selected group while it is spawning and resolving. Each group keeps
        /// its own callbacks and pooled-attacker subscriptions so the next group can begin as
        /// soon as the previous one has entered the map without losing the first group's result.
        /// </summary>
        private sealed class PlayerWaveRun
        {
            private readonly Dictionary<BasicAttacker, Action<BasicAttacker>> returnHandlers = new();

            public PlayerWaveRun(
                Action<BasicAttacker> attackerSpawned,
                Action<BasicAttacker> attackerResolved,
                Action<bool> spawnSequenceCompleted,
                Action<bool> completed)
            {
                AttackerSpawned = attackerSpawned;
                AttackerResolved = attackerResolved;
                SpawnSequenceCompleted = spawnSequenceCompleted;
                Completed = completed;
            }

            public readonly HashSet<BasicAttacker> ActiveAttackers = new();
            public Action<BasicAttacker> AttackerSpawned { get; }
            public Action<BasicAttacker> AttackerResolved { get; }
            public Action<bool> SpawnSequenceCompleted { get; }
            public Action<bool> Completed { get; }
            public int WaveNumber { get; set; }
            public bool SpawnSequenceFinished { get; set; }
            public Coroutine Routine { get; set; }

            public void RegisterAttacker(BasicAttacker attacker, Action<BasicAttacker> returnedHandler)
            {
                if (attacker == null || returnedHandler == null || !ActiveAttackers.Add(attacker))
                {
                    return;
                }

                returnHandlers.Add(attacker, returnedHandler);
                attacker.ReturnedToPool += returnedHandler;
            }

            public bool TryResolveAttacker(BasicAttacker attacker)
            {
                // ReferenceEquals deliberately handles a Unity object that was destroyed without
                // returning to the pool. Unity's overloaded null comparison would otherwise make
                // the stale entry impossible to remove and stall this group's completion.
                if (ReferenceEquals(attacker, null) || !ActiveAttackers.Remove(attacker))
                {
                    return false;
                }

                if (returnHandlers.TryGetValue(attacker, out var returnedHandler))
                {
                    if (attacker != null)
                    {
                        attacker.ReturnedToPool -= returnedHandler;
                    }

                    returnHandlers.Remove(attacker);
                }

                return true;
            }

            public void DetachAllAttackers()
            {
                foreach (var pair in returnHandlers)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.ReturnedToPool -= pair.Value;
                    }
                }

                returnHandlers.Clear();
                ActiveAttackers.Clear();
            }
        }

        [Serializable]
        private sealed class AttackerGroup
        {
            [SerializeField] private AttackerDefinition attackerDefinition;
            [SerializeField, Min(1)] private int count = 1;
            public AttackerDefinition AttackerDefinition => attackerDefinition;
            public int Count => count;
#if UNITY_EDITOR
            public void Validate() => count = Mathf.Max(1, count);
#endif
        }

        [Serializable]
        private sealed class WaveSettings
        {
            [SerializeField, Min(1)]
            [Tooltip("How many default attackers the AI sends when no typed composition is configured.")]
            private int attackerCount = 3;
            [SerializeField]
            [Tooltip("Optional ordered attacker groups. When supplied, their total replaces the legacy attacker count.")]
            private AttackerGroup[] attackerGroups = Array.Empty<AttackerGroup>();
            [SerializeField, Min(0f)]
            [Tooltip("Seconds between attackers in this wave.")]
            private float spawnInterval = 0.5f;
            [SerializeField, Min(0f)]
            [Tooltip("Seconds to wait after the previous wave is cleared before starting this wave.")]
            private float delayBeforeWave = 1f;

            public int AttackerCount
            {
                get
                {
                    if (attackerGroups == null || attackerGroups.Length == 0)
                    {
                        return attackerCount;
                    }

                    var total = 0;
                    foreach (var group in attackerGroups)
                    {
                        total += group != null ? group.Count : 0;
                    }

                    return total > 0 ? total : attackerCount;
                }
            }

            public float SpawnInterval => spawnInterval;
            public float DelayBeforeWave => delayBeforeWave;

            public WaveSettings(int attackerCount, float spawnInterval, float delayBeforeWave)
            {
                this.attackerCount = attackerCount;
                this.spawnInterval = spawnInterval;
                this.delayBeforeWave = delayBeforeWave;
            }

            public AttackerDefinition GetDefinitionAtSpawnIndex(int spawnIndex)
            {
                if (attackerGroups == null || attackerGroups.Length == 0)
                {
                    return null;
                }

                var remainingIndex = spawnIndex;
                foreach (var group in attackerGroups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    if (remainingIndex < group.Count)
                    {
                        return group.AttackerDefinition;
                    }

                    remainingIndex -= group.Count;
                }

                return null;
            }

#if UNITY_EDITOR
            public void Validate()
            {
                attackerCount = Mathf.Max(1, attackerCount);
                spawnInterval = Mathf.Max(0f, spawnInterval);
                delayBeforeWave = Mathf.Max(0f, delayBeforeWave);
                if (attackerGroups == null)
                {
                    return;
                }

                foreach (var group in attackerGroups)
                {
                    group?.Validate();
                }
            }
#endif
        }

        [SerializeField]
        [Tooltip("The reusable pool used to spawn attacker prefabs.")]
        private SpawnPool spawnPool;
        [SerializeField]
        [Tooltip("The map that directs each spawned attacker toward the Food Store.")]
        private FarmRouteMapper routeMapper;
        [SerializeField]
        [Tooltip("The farm objective. No additional waves are sent after its food is depleted.")]
        private FoodStore foodStore;
        [SerializeField]
        [Tooltip("Optional location for spawned animals. When empty, this object's transform is used.")]
        private Transform spawnPoint;
        [SerializeField]
        [Tooltip("Starts the configured AI wave sequence when this scene begins.")]
        private bool startOnStart;
        [SerializeField]
        private WaveSettings[] waves =
        {
            new WaveSettings(3, 0.5f, 1f),
            new WaveSettings(5, 0.45f, 2f),
            new WaveSettings(8, 0.4f, 2.5f)
        };

        private readonly List<GameObject> activeAttackers = new();
        private Coroutine waveRoutine;
        private readonly List<PlayerWaveRun> activePlayerWaveRuns = new();
        private readonly List<BasicAttacker> stalePlayerAttackers = new();
        private int currentWaveNumber;
        private int nextWaveIndex;
        private int spawnedInCurrentWave;
        private bool hasCompletedAllWaves;
        private bool isPlayerSpawnSequenceRunning;
        private bool playerWaveRunAggregateFailed;
        private bool isSubscribedToFoodStore;

        public int CurrentWaveNumber => currentWaveNumber;
        public int TotalWaveCount => waves?.Length ?? 0;
        public int SpawnedInCurrentWave => spawnedInCurrentWave;
        public int ActiveAttackerCount => CountActiveAttackers();
        public bool IsRunning => waveRoutine != null || activePlayerWaveRuns.Count > 0;
        public bool HasCompletedAllWaves => hasCompletedAllWaves;
        public bool CanStartNextWave => !IsRunning && !hasCompletedAllWaves &&
                                        waves != null && nextWaveIndex < waves.Length &&
                                        (foodStore == null || !foodStore.IsDepleted);
        public bool CanStartPlayerWave => waveRoutine == null && !isPlayerSpawnSequenceRunning && TryResolveDependencies() &&
                                           foodStore != null && !foodStore.IsDepleted && EnsureRouteMap();
        public bool CanQueuePlayerWave => TryResolveDependencies() && foodStore != null &&
                                          !foodStore.IsDepleted && EnsureRouteMap();
        public Vector3 SpawnPosition => (spawnPoint != null ? spawnPoint : transform).position;
        /// <summary>The authored spawn transform used by portrait framing and route presentation.</summary>
        public Transform SpawnPointTransform => spawnPoint != null ? spawnPoint : transform;
        public FoodStore FoodStore => foodStore;

        public event Action<int, int> WaveStarted;
        public event Action<int> WaveCompleted;
        public event Action AllWavesCompleted;
        public event Action<bool> WaveRunFinished;

        private void Start()
        {
            // Build the complete, visible-only route cache once the two gameplay endpoints
            // have been framed by the camera. This happens before either AI can queue a wave.
            if (TryResolveDependencies())
            {
                EnsureRouteMap();
            }

            if (startOnStart)
            {
                StartWaves();
            }
        }

        private void OnDisable()
        {
            StopWaves();
            UnsubscribeFromFoodStore();
        }

        public void RequireManualStart()
        {
            startOnStart = false;
        }

        public bool StartWaves()
        {
            if (IsRunning)
            {
                return false;
            }

            if (waves == null || waves.Length == 0)
            {
                Debug.LogWarning("Wave Controller has no configured waves to send.", this);
                return false;
            }

            if (!TryResolveDependencies(logMissingDependencies: true) || !EnsureRouteMap())
            {
                Debug.LogError("Wave Controller could not build a route to the Food Store before sending attackers.", this);
                return false;
            }

            currentWaveNumber = 0;
            nextWaveIndex = 0;
            spawnedInCurrentWave = 0;
            hasCompletedAllWaves = false;
            activeAttackers.Clear();
            SubscribeToFoodStore();
            waveRoutine = StartCoroutine(RunWaves());
            return true;
        }

        public bool StartNextWave()
        {
            if (!CanStartNextWave || !TryResolveDependencies(logMissingDependencies: true) || foodStore.IsDepleted)
            {
                return false;
            }

            if (!EnsureRouteMap())
            {
                Debug.LogError("Wave Controller could not build a route to the Food Store before sending attackers.", this);
                return false;
            }

            var waveIndex = nextWaveIndex;
            if (waveIndex < 0 || waveIndex >= waves.Length || waves[waveIndex] == null)
            {
                Debug.LogWarning("Wave Controller cannot start the next wave because its settings are missing.", this);
                return false;
            }

            spawnedInCurrentWave = 0;
            activeAttackers.Clear();
            SubscribeToFoodStore();
            waveRoutine = StartCoroutine(RunSingleWave(waveIndex));
            return true;
        }

        public bool TryStartPlayerWave(
            IReadOnlyList<AttackerDefinition> attackerDefinitions,
            float spawnInterval,
            Action<bool> completion)
        {
            return TryStartPlayerWave(attackerDefinitions, spawnInterval, null, null, null, completion);
        }

        /// <summary>
        /// Sends a player-composed attacker wave and reports successful spawns and returns to its caller.
        /// Presentation can therefore observe a group without becoming the owner of combat state.
        /// </summary>
        public bool TryStartPlayerWave(
            IReadOnlyList<AttackerDefinition> attackerDefinitions,
            float spawnInterval,
            Action<BasicAttacker> onAttackerSpawned,
            Action<BasicAttacker> onAttackerResolved,
            Action<bool> completion)
        {
            return TryStartPlayerWave(
                attackerDefinitions,
                spawnInterval,
                onAttackerSpawned,
                onAttackerResolved,
                null,
                completion);
        }

        /// <summary>
        /// Sends one selected attacker group. <paramref name="onSpawnSequenceCompleted"/> is
        /// raised as soon as this group has finished entering the playable map, while
        /// <paramref name="completion"/> waits until this group's own attackers have returned
        /// to the pool. This permits visible, overlapping groups without combining their rewards.
        /// </summary>
        public bool TryStartPlayerWave(
            IReadOnlyList<AttackerDefinition> attackerDefinitions,
            float spawnInterval,
            Action<BasicAttacker> onAttackerSpawned,
            Action<BasicAttacker> onAttackerResolved,
            Action<bool> onSpawnSequenceCompleted,
            Action<bool> completion)
        {
            if (!CanStartPlayerWave || attackerDefinitions == null || attackerDefinitions.Count == 0)
            {
                return false;
            }

            spawnedInCurrentWave = 0;
            SubscribeToFoodStore();
            if (activePlayerWaveRuns.Count == 0)
            {
                playerWaveRunAggregateFailed = false;
            }

            var playerWaveRun = new PlayerWaveRun(
                onAttackerSpawned,
                onAttackerResolved,
                onSpawnSequenceCompleted,
                completion);
            activePlayerWaveRuns.Add(playerWaveRun);
            isPlayerSpawnSequenceRunning = true;
            playerWaveRun.Routine = StartCoroutine(
                RunPlayerWave(playerWaveRun, attackerDefinitions, Mathf.Max(0f, spawnInterval)));
            return true;
        }

        public void ResetForNewLevelSession()
        {
            if (waveRoutine != null)
            {
                StopCoroutine(waveRoutine);
                waveRoutine = null;
            }

            StopPlayerWaveRuns(notifyCompletion: false);
            for (var index = activeAttackers.Count - 1; index >= 0; index--)
            {
                var attackerObject = activeAttackers[index];
                if (attackerObject != null && attackerObject.activeSelf)
                {
                    ReturnSpawnedAttacker(attackerObject);
                }
            }

            activeAttackers.Clear();
            currentWaveNumber = 0;
            nextWaveIndex = 0;
            spawnedInCurrentWave = 0;
            hasCompletedAllWaves = false;
            UnsubscribeFromFoodStore();
        }

        public void StopWaves()
        {
            var wasRunning = IsRunning;
            if (wasRunning)
            {
                if (waveRoutine != null)
                {
                    StopCoroutine(waveRoutine);
                    waveRoutine = null;
                }

                StopPlayerWaveRuns(notifyCompletion: true);
            }

            activeAttackers.Clear();
            if (wasRunning)
            {
                WaveRunFinished?.Invoke(false);
            }
        }

        private IEnumerator RunWaves()
        {
            var completedSequence = true;
            for (var waveIndex = 0; waveIndex < waves.Length; waveIndex++)
            {
                if (foodStore.IsDepleted)
                {
                    completedSequence = false;
                    break;
                }

                var wave = waves[waveIndex];
                if (wave == null)
                {
                    continue;
                }

                if (wave.DelayBeforeWave > 0f)
                {
                    yield return new WaitForSeconds(wave.DelayBeforeWave);
                }

                if (foodStore.IsDepleted)
                {
                    completedSequence = false;
                    break;
                }

                currentWaveNumber = waveIndex + 1;
                nextWaveIndex = currentWaveNumber;
                spawnedInCurrentWave = 0;
                activeAttackers.Clear();
                WaveStarted?.Invoke(currentWaveNumber, wave.AttackerCount);

                var failedToSpawn = false;
                for (var attackerIndex = 0; attackerIndex < wave.AttackerCount; attackerIndex++)
                {
                    if (foodStore.IsDepleted)
                    {
                        completedSequence = false;
                        break;
                    }

                    if (!SpawnAttacker(wave.GetDefinitionAtSpawnIndex(attackerIndex)))
                    {
                        completedSequence = false;
                        failedToSpawn = true;
                        break;
                    }

                    spawnedInCurrentWave++;
                    if (attackerIndex < wave.AttackerCount - 1 && wave.SpawnInterval > 0f)
                    {
                        yield return new WaitForSeconds(wave.SpawnInterval);
                    }
                }

                yield return WaitForActiveAttackers();
                if (foodStore.IsDepleted || failedToSpawn)
                {
                    completedSequence = false;
                    break;
                }

                WaveCompleted?.Invoke(currentWaveNumber);
            }

            hasCompletedAllWaves = completedSequence && !foodStore.IsDepleted;
            waveRoutine = null;
            WaveRunFinished?.Invoke(hasCompletedAllWaves);
            if (hasCompletedAllWaves)
            {
                AllWavesCompleted?.Invoke();
            }
        }

        private IEnumerator RunSingleWave(int waveIndex)
        {
            var wave = waves[waveIndex];
            if (foodStore.IsDepleted)
            {
                CompleteSingleWave(false);
                yield break;
            }

            if (wave.DelayBeforeWave > 0f)
            {
                yield return new WaitForSeconds(wave.DelayBeforeWave);
            }

            if (foodStore.IsDepleted)
            {
                CompleteSingleWave(false);
                yield break;
            }

            currentWaveNumber = waveIndex + 1;
            spawnedInCurrentWave = 0;
            activeAttackers.Clear();
            WaveStarted?.Invoke(currentWaveNumber, wave.AttackerCount);

            var failedToSpawn = false;
            for (var attackerIndex = 0; attackerIndex < wave.AttackerCount; attackerIndex++)
            {
                if (foodStore.IsDepleted)
                {
                    break;
                }

                if (!SpawnAttacker(wave.GetDefinitionAtSpawnIndex(attackerIndex)))
                {
                    failedToSpawn = true;
                    break;
                }

                spawnedInCurrentWave++;
                if (attackerIndex < wave.AttackerCount - 1 && wave.SpawnInterval > 0f)
                {
                    yield return new WaitForSeconds(wave.SpawnInterval);
                }
            }

            yield return WaitForActiveAttackers();
            var completed = !foodStore.IsDepleted && !failedToSpawn;
            if (completed)
            {
                nextWaveIndex = waveIndex + 1;
                hasCompletedAllWaves = nextWaveIndex >= waves.Length;
                WaveCompleted?.Invoke(currentWaveNumber);
            }

            CompleteSingleWave(completed);
        }

        private IEnumerator RunPlayerWave(
            PlayerWaveRun playerWaveRun,
            IReadOnlyList<AttackerDefinition> attackerDefinitions,
            float spawnInterval)
        {
            if (foodStore.IsDepleted)
            {
                CompletePlayerWaveSpawnSequence(playerWaveRun, completed: false);
                CompletePlayerWaveRun(playerWaveRun, completed: false);
                yield break;
            }

            currentWaveNumber = Mathf.Max(1, currentWaveNumber + 1);
            playerWaveRun.WaveNumber = currentWaveNumber;
            spawnedInCurrentWave = 0;
            WaveStarted?.Invoke(playerWaveRun.WaveNumber, attackerDefinitions.Count);

            var failedToSpawn = false;
            for (var attackerIndex = 0; attackerIndex < attackerDefinitions.Count; attackerIndex++)
            {
                if (foodStore.IsDepleted)
                {
                    failedToSpawn = true;
                    break;
                }

                if (!SpawnAttacker(attackerDefinitions[attackerIndex], out var attacker))
                {
                    failedToSpawn = true;
                    break;
                }

                playerWaveRun.RegisterAttacker(
                    attacker,
                    returnedAttacker => HandlePlayerAttackerReturned(playerWaveRun, returnedAttacker));
                playerWaveRun.AttackerSpawned?.Invoke(attacker);
                spawnedInCurrentWave++;
                if (attackerIndex < attackerDefinitions.Count - 1 && spawnInterval > 0f)
                {
                    yield return new WaitForSeconds(spawnInterval);
                }
            }

            // The next queued group may start now. This run deliberately keeps waiting on only
            // its own tracked attackers, so earlier groups remain on screen while later groups enter.
            CompletePlayerWaveSpawnSequence(playerWaveRun, !failedToSpawn && !foodStore.IsDepleted);

            while (playerWaveRun.ActiveAttackers.Count > 0)
            {
                ResolveInactivePlayerAttackers(playerWaveRun);
                if (playerWaveRun.ActiveAttackers.Count == 0)
                {
                    break;
                }

                yield return null;
            }

            CompletePlayerWaveRun(playerWaveRun, !foodStore.IsDepleted && !failedToSpawn);
        }

        private void CompletePlayerWaveSpawnSequence(PlayerWaveRun playerWaveRun, bool completed)
        {
            if (playerWaveRun == null || playerWaveRun.SpawnSequenceFinished)
            {
                return;
            }

            playerWaveRun.SpawnSequenceFinished = true;
            isPlayerSpawnSequenceRunning = false;
            playerWaveRun.SpawnSequenceCompleted?.Invoke(completed);
        }

        private void HandlePlayerAttackerReturned(PlayerWaveRun playerWaveRun, BasicAttacker attacker)
        {
            if (playerWaveRun == null || !playerWaveRun.TryResolveAttacker(attacker))
            {
                return;
            }

            if (attacker != null)
            {
                activeAttackers.Remove(attacker.gameObject);
            }

            playerWaveRun.AttackerResolved?.Invoke(attacker);
        }

        /// <summary>
        /// Pool return notifications are the normal authoritative resolution path. This small
        /// defensive sweep prevents a manually disabled or destroyed attacker from leaving an
        /// overlapping player group waiting forever if a third-party component bypasses the pool.
        /// </summary>
        private void ResolveInactivePlayerAttackers(PlayerWaveRun playerWaveRun)
        {
            if (playerWaveRun == null || playerWaveRun.ActiveAttackers.Count == 0)
            {
                return;
            }

            stalePlayerAttackers.Clear();
            foreach (var attacker in playerWaveRun.ActiveAttackers)
            {
                if (attacker == null || !attacker.gameObject.activeInHierarchy)
                {
                    stalePlayerAttackers.Add(attacker);
                }
            }

            foreach (var attacker in stalePlayerAttackers)
            {
                HandlePlayerAttackerReturned(playerWaveRun, attacker);
            }

            stalePlayerAttackers.Clear();
        }

        private void CompletePlayerWaveRun(PlayerWaveRun playerWaveRun, bool completed)
        {
            if (playerWaveRun == null || !activePlayerWaveRuns.Remove(playerWaveRun))
            {
                return;
            }

            playerWaveRun.Routine = null;
            playerWaveRun.DetachAllAttackers();
            if (!completed)
            {
                playerWaveRunAggregateFailed = true;
            }
            else
            {
                WaveCompleted?.Invoke(playerWaveRun.WaveNumber);
            }

            playerWaveRun.Completed?.Invoke(completed);
            if (activePlayerWaveRuns.Count != 0)
            {
                return;
            }

            var allPlayerRunsCompleted = !playerWaveRunAggregateFailed;
            playerWaveRunAggregateFailed = false;
            WaveRunFinished?.Invoke(allPlayerRunsCompleted);
        }

        private void CompleteSingleWave(bool completed)
        {
            waveRoutine = null;
            WaveRunFinished?.Invoke(completed);
            if (completed && hasCompletedAllWaves)
            {
                AllWavesCompleted?.Invoke();
            }
        }

        private bool SpawnAttacker(AttackerDefinition requestedDefinition)
        {
            return SpawnAttacker(requestedDefinition, out _);
        }

        private bool SpawnAttacker(AttackerDefinition requestedDefinition, out BasicAttacker spawnedAttacker)
        {
            spawnedAttacker = null;
            var source = spawnPoint != null ? spawnPoint : transform;
            var attackerObject = requestedDefinition != null
                ? spawnPool.SpawnAttacker(requestedDefinition, source.position, source.rotation)
                : spawnPool.Spawn(PooledObjectType.Attacker, source.position, source.rotation);
            if (attackerObject == null)
            {
                return false;
            }

            if (!attackerObject.TryGetComponent<BasicAttacker>(out var attacker))
            {
                Debug.LogWarning(attackerObject.name + " does not have a Basic Attacker component, so this wave cannot assign it a route.", attackerObject);
                ReturnSpawnedAttacker(attackerObject);
                return false;
            }

            if (attacker.Definition == null)
            {
                Debug.LogWarning(attackerObject.name + " was returned to the pool because it has no attacker definition.", this);
                ReturnSpawnedAttacker(attackerObject);
                return false;
            }

            attacker.SetRouteMapper(routeMapper);
            if (!attacker.TryClampToVisibleMap() ||
                !routeMapper.TryGetNextDirection(attacker.CurrentPosition, attacker.Definition, out _))
            {
                Debug.LogWarning(attackerObject.name + " was returned to the pool because the visible spawn area has no route to the Food Store for size " + attacker.Size + " and profile " + attacker.TraversalProfile + ".", this);
                ReturnSpawnedAttacker(attackerObject);
                return false;
            }

            activeAttackers.Add(attackerObject);
            TadSfxPlayer.Play(attacker.Definition.AnimalCrySound, TadAudioChannelType.Attacker, attacker.CurrentPosition);
            spawnedAttacker = attacker;
            return true;
        }

        private static void ReturnSpawnedAttacker(GameObject attackerObject)
        {
            if (attackerObject.TryGetComponent<PooledInstance>(out var pooledInstance))
            {
                pooledInstance.ReturnToPool();
                return;
            }

            attackerObject.SetActive(false);
        }

        private IEnumerator WaitForActiveAttackers()
        {
            while (CountActiveAttackers() > 0)
            {
                yield return null;
            }
        }

        private int CountActiveAttackers()
        {
            for (var index = activeAttackers.Count - 1; index >= 0; index--)
            {
                var attackerObject = activeAttackers[index];
                var isStillActive = attackerObject != null && attackerObject.activeSelf;
                if (isStillActive)
                {
                    continue;
                }

                activeAttackers.RemoveAt(index);
            }

            return activeAttackers.Count;
        }

        private void StopPlayerWaveRuns(bool notifyCompletion)
        {
            isPlayerSpawnSequenceRunning = false;
            if (activePlayerWaveRuns.Count == 0)
            {
                playerWaveRunAggregateFailed = false;
                return;
            }

            var runs = activePlayerWaveRuns.ToArray();
            activePlayerWaveRuns.Clear();
            foreach (var playerWaveRun in runs)
            {
                if (playerWaveRun.Routine != null)
                {
                    StopCoroutine(playerWaveRun.Routine);
                    playerWaveRun.Routine = null;
                }

                playerWaveRun.DetachAllAttackers();
                if (notifyCompletion)
                {
                    playerWaveRun.Completed?.Invoke(false);
                }
            }

            playerWaveRunAggregateFailed = false;
        }

        private bool EnsureRouteMap()
        {
            if (routeMapper == null)
            {
                return false;
            }

            // When the camera has to widen, all flow fields are based on stale visible bounds.
            // Rebuild them after physics has caught up with the current level geometry.
            var cameraFramingChanged = EnsureGameplayCameraFramesRouteEndpoints();
            if (cameraFramingChanged || routeMapper.CachedRouteFieldCount == 0)
            {
                Physics2D.SyncTransforms();
                return routeMapper.RebuildRoutes();
            }

            return true;
        }

        /// <summary>
        /// Rebuilds the camera-bounded route cache after a presentation-only camera rotation or
        /// viewport resize. Gameplay callers retain the normal startup path through <see cref="EnsureRouteMap"/>.
        /// </summary>
        public bool RefreshRouteMapForCurrentViewport()
        {
            return TryResolveDependencies() && EnsureRouteMap();
        }

        /// <summary>
        /// Ensures the visible play area contains the two endpoints required by every attacker
        /// route. This keeps a translated level core from producing an empty flow field or
        /// returning pooled animals before they reach the visible map.
        /// </summary>
        private bool EnsureGameplayCameraFramesRouteEndpoints()
        {
            var gameplayCamera = Camera.main;
            if (gameplayCamera == null || !gameplayCamera.orthographic || foodStore == null)
            {
                return false;
            }

            // Use the camera's local axes rather than raw world X/Y. Portrait presentation turns
            // only the camera so the authored farm remains intact while the spawn is at the top of
            // a phone screen and the Food Store is at the bottom.
            var spawnPosition = gameplayCamera.transform.InverseTransformPoint(SpawnPosition);
            var foodPosition = gameplayCamera.transform.InverseTransformPoint(foodStore.transform.position);
            var requiredHalfHeight = Mathf.Max(
                Mathf.Abs(spawnPosition.y),
                Mathf.Abs(foodPosition.y)) + RouteEndpointViewportPadding;
            var requiredHalfWidth = Mathf.Max(
                Mathf.Abs(spawnPosition.x),
                Mathf.Abs(foodPosition.x)) + RouteEndpointViewportPadding;
            var requiredSize = Mathf.Max(
                requiredHalfHeight,
                requiredHalfWidth / Mathf.Max(0.01f, gameplayCamera.aspect));

            if (requiredSize <= gameplayCamera.orthographicSize + 0.01f)
            {
                return false;
            }

            gameplayCamera.orthographicSize = requiredSize;
            return true;
        }

        /// <summary>
        /// Resolves scene dependencies without treating passive availability checks as runtime
        /// errors. A minimal scene can legitimately own a day-cycle controller before it owns a
        /// wave-spawning setup; only an explicit attempt to send a wave should report that
        /// configuration problem to the developer.
        /// </summary>
        private bool TryResolveDependencies(bool logMissingDependencies = false)
        {
            spawnPool ??= UnityEngine.Object.FindAnyObjectByType<SpawnPool>();
            if (spawnPool != null)
            {
                routeMapper = spawnPool.RouteMapper;
            }
            else
            {
                routeMapper ??= UnityEngine.Object.FindAnyObjectByType<FarmRouteMapper>();
            }

            foodStore ??= UnityEngine.Object.FindAnyObjectByType<FoodStore>();
            if (spawnPool != null && routeMapper != null && foodStore != null)
            {
                return true;
            }

            if (logMissingDependencies)
            {
                Debug.LogError("Wave Controller needs a Spawn Pool, Farm Route Mapper, and Food Store before it can send waves.", this);
            }

            return false;
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

        private void HandleFoodDepleted()
        {
            StopWaves();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (waves == null)
            {
                return;
            }

            foreach (var wave in waves)
            {
                wave?.Validate();
            }
        }
#endif
    }
}
