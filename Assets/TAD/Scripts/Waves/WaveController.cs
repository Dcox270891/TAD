using System;
using System.Collections;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Environment;
using TAD.Navigation;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Waves
{
    [DisallowMultipleComponent]
    public sealed class WaveController : MonoBehaviour
    {
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
        private Action<bool> playerWaveCompletion;
        private Action<BasicAttacker> playerAttackerSpawned;
        private Action<BasicAttacker> playerAttackerResolved;
        private int currentWaveNumber;
        private int nextWaveIndex;
        private int spawnedInCurrentWave;
        private bool hasCompletedAllWaves;
        private bool isPlayerWave;
        private bool isSubscribedToFoodStore;

        public int CurrentWaveNumber => currentWaveNumber;
        public int TotalWaveCount => waves?.Length ?? 0;
        public int SpawnedInCurrentWave => spawnedInCurrentWave;
        public int ActiveAttackerCount => CountActiveAttackers();
        public bool IsRunning => waveRoutine != null;
        public bool HasCompletedAllWaves => hasCompletedAllWaves;
        public bool CanStartNextWave => waveRoutine == null && !hasCompletedAllWaves &&
                                        waves != null && nextWaveIndex < waves.Length &&
                                        (foodStore == null || !foodStore.IsDepleted);
        public bool CanStartPlayerWave => waveRoutine == null && TryResolveDependencies() &&
                                           foodStore != null && !foodStore.IsDepleted && EnsureRouteMap();
        public bool CanQueuePlayerWave => TryResolveDependencies() && foodStore != null &&
                                          !foodStore.IsDepleted && EnsureRouteMap();
        public Vector3 SpawnPosition => (spawnPoint != null ? spawnPoint : transform).position;

        public event Action<int, int> WaveStarted;
        public event Action<int> WaveCompleted;
        public event Action AllWavesCompleted;
        public event Action<bool> WaveRunFinished;

        private void Start()
        {
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
            if (waveRoutine != null)
            {
                return false;
            }

            if (waves == null || waves.Length == 0)
            {
                Debug.LogWarning("Wave Controller has no configured waves to send.", this);
                return false;
            }

            if (!TryResolveDependencies() || !EnsureRouteMap())
            {
                Debug.LogError("Wave Controller could not build a route to the Food Store before sending attackers.", this);
                return false;
            }

            ClearPlayerWaveCallbacks();
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
            if (!CanStartNextWave || !TryResolveDependencies() || foodStore.IsDepleted)
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

            ClearPlayerWaveCallbacks();
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
            return TryStartPlayerWave(attackerDefinitions, spawnInterval, null, null, completion);
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
            if (!CanStartPlayerWave || attackerDefinitions == null || attackerDefinitions.Count == 0)
            {
                return false;
            }

            spawnedInCurrentWave = 0;
            activeAttackers.Clear();
            SubscribeToFoodStore();
            playerWaveCompletion = completion;
            playerAttackerSpawned = onAttackerSpawned;
            playerAttackerResolved = onAttackerResolved;
            isPlayerWave = true;
            waveRoutine = StartCoroutine(RunPlayerWave(attackerDefinitions, Mathf.Max(0f, spawnInterval)));
            return true;
        }

        public void ResetForNewLevelSession()
        {
            if (waveRoutine != null)
            {
                StopCoroutine(waveRoutine);
                waveRoutine = null;
            }

            ClearPlayerWaveCallbacks();
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
            var wasRunning = waveRoutine != null;
            var completion = playerWaveCompletion;
            if (wasRunning)
            {
                StopCoroutine(waveRoutine);
                waveRoutine = null;
            }

            ClearPlayerWaveCallbacks();
            activeAttackers.Clear();
            if (wasRunning)
            {
                completion?.Invoke(false);
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

        private IEnumerator RunPlayerWave(IReadOnlyList<AttackerDefinition> attackerDefinitions, float spawnInterval)
        {
            if (foodStore.IsDepleted)
            {
                CompletePlayerWave(false);
                yield break;
            }

            currentWaveNumber = Mathf.Max(1, currentWaveNumber + 1);
            spawnedInCurrentWave = 0;
            activeAttackers.Clear();
            WaveStarted?.Invoke(currentWaveNumber, attackerDefinitions.Count);

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

                playerAttackerSpawned?.Invoke(attacker);
                spawnedInCurrentWave++;
                if (attackerIndex < attackerDefinitions.Count - 1 && spawnInterval > 0f)
                {
                    yield return new WaitForSeconds(spawnInterval);
                }
            }

            yield return WaitForActiveAttackers();
            CompletePlayerWave(!foodStore.IsDepleted && !failedToSpawn);
        }

        private void CompletePlayerWave(bool completed)
        {
            waveRoutine = null;
            var completion = playerWaveCompletion;
            ClearPlayerWaveCallbacks();
            if (completed)
            {
                WaveCompleted?.Invoke(currentWaveNumber);
            }

            completion?.Invoke(completed);
            WaveRunFinished?.Invoke(completed);
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
                if (isPlayerWave && attackerObject != null &&
                    attackerObject.TryGetComponent<BasicAttacker>(out var resolvedAttacker) &&
                    (!isStillActive || resolvedAttacker.IsDefeated))
                {
                    // A defeated animal can remain visible briefly for its death animation, but it is no
                    // longer alive for the queued-group counter. The queue ignores duplicate instance IDs.
                    playerAttackerResolved?.Invoke(resolvedAttacker);
                }

                if (isStillActive)
                {
                    continue;
                }

                activeAttackers.RemoveAt(index);
            }

            return activeAttackers.Count;
        }

        private void ClearPlayerWaveCallbacks()
        {
            playerWaveCompletion = null;
            playerAttackerSpawned = null;
            playerAttackerResolved = null;
            isPlayerWave = false;
        }

        private bool EnsureRouteMap()
        {
            return routeMapper != null && (routeMapper.CachedRouteFieldCount > 0 || routeMapper.RebuildRoutes());
        }

        private bool TryResolveDependencies()
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

            Debug.LogError("Wave Controller needs a Spawn Pool, Farm Route Mapper, and Food Store before it can send waves.", this);
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
