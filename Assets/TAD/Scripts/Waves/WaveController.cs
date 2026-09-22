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
    /// <summary>
    /// Sends configured, numbered attacker waves from this object's position and waits for every
    /// attacker in a wave to return to the pool before starting the next one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveController : MonoBehaviour
    {
        [Serializable]
        private sealed class WaveSettings
        {
            [SerializeField, Min(1)]
            [Tooltip("How many attackers the AI sends in this numbered wave.")]
            private int attackerCount = 3;

            [SerializeField, Min(0f)]
            [Tooltip("Seconds between attackers in this wave.")]
            private float spawnInterval = 0.5f;

            [SerializeField, Min(0f)]
            [Tooltip("Seconds to wait after the previous wave is cleared before starting this wave.")]
            private float delayBeforeWave = 1f;

            public int AttackerCount => attackerCount;
            public float SpawnInterval => spawnInterval;
            public float DelayBeforeWave => delayBeforeWave;

            public WaveSettings(int attackerCount, float spawnInterval, float delayBeforeWave)
            {
                this.attackerCount = attackerCount;
                this.spawnInterval = spawnInterval;
                this.delayBeforeWave = delayBeforeWave;
            }

#if UNITY_EDITOR
            public void Validate()
            {
                attackerCount = Mathf.Max(1, attackerCount);
                spawnInterval = Mathf.Max(0f, spawnInterval);
                delayBeforeWave = Mathf.Max(0f, delayBeforeWave);
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
        private int currentWaveNumber;
        private int spawnedInCurrentWave;
        private bool hasCompletedAllWaves;
        private bool isSubscribedToFoodStore;

        /// <summary>The currently deploying or resolving wave, numbered from one. Zero means no wave has started.</summary>
        public int CurrentWaveNumber => currentWaveNumber;

        public int TotalWaveCount => waves?.Length ?? 0;

        public int SpawnedInCurrentWave => spawnedInCurrentWave;

        public int ActiveAttackerCount => CountActiveAttackers();

        public bool IsRunning => waveRoutine != null;

        public bool HasCompletedAllWaves => hasCompletedAllWaves;

        /// <summary>Prevents automatic startup so a player-facing control can start the sequence instead.</summary>
        public void RequireManualStart()
        {
            startOnStart = false;
        }

        /// <summary>The exact source position used when this controller releases an attacker.</summary>
        public Vector3 SpawnPosition => (spawnPoint != null ? spawnPoint : transform).position;

        /// <summary>Raised with the one-based wave number and its requested attacker count.</summary>
        public event Action<int, int> WaveStarted;

        /// <summary>Raised after every attacker sent in the wave has returned to the pool.</summary>
        public event Action<int> WaveCompleted;

        /// <summary>Raised after the final configured wave has been cleared while food remains.</summary>
        public event Action AllWavesCompleted;

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

        /// <summary>Begins the configured sequence from wave one. Returns false if it is already running or incomplete.</summary>
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

            if (!TryResolveDependencies())
            {
                return false;
            }

            if (!routeMapper.RebuildRoutes())
            {
                Debug.LogError("Wave Controller could not build a route to the Food Store before sending attackers.", this);
                return false;
            }

            currentWaveNumber = 0;
            spawnedInCurrentWave = 0;
            hasCompletedAllWaves = false;
            activeAttackers.Clear();
            SubscribeToFoodStore();
            waveRoutine = StartCoroutine(RunWaves());
            return true;
        }

        /// <summary>Stops scheduling future attackers. Attackers already released by the pool are left unchanged.</summary>
        public void StopWaves()
        {
            if (waveRoutine != null)
            {
                StopCoroutine(waveRoutine);
                waveRoutine = null;
            }

            activeAttackers.Clear();
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

                    if (!SpawnAttacker())
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

            if (hasCompletedAllWaves)
            {
                AllWavesCompleted?.Invoke();
            }
        }

        private bool SpawnAttacker()
        {
            var source = spawnPoint != null ? spawnPoint : transform;
            var attackerObject = spawnPool.Spawn(PooledObjectType.Attacker, source.position, source.rotation);
            if (attackerObject == null)
            {
                return false;
            }

            if (attackerObject.TryGetComponent<BasicAttacker>(out var attacker))
            {
                if (attacker.Definition == null)
                {
                    Debug.LogWarning($"{attackerObject.name} was returned to the pool because it has no attacker definition.", this);
                    ReturnSpawnedAttacker(attackerObject);
                    return false;
                }

                attacker.SetRouteMapper(routeMapper);

                if (!attacker.TryClampToVisibleMap() ||
                    !routeMapper.TryGetNextDirection(attacker.CurrentPosition, attacker.Size, out _))
                {
                    Debug.LogWarning($"{attackerObject.name} was returned to the pool because the visible spawn area has no route to the Food Store for size {attacker.Size}.", this);
                    ReturnSpawnedAttacker(attackerObject);
                    return false;
                }
            }
            else
            {
                Debug.LogWarning($"{attackerObject.name} does not have a Basic Attacker component, so this wave cannot assign it a route.", attackerObject);
                ReturnSpawnedAttacker(attackerObject);
                return false;
            }

            activeAttackers.Add(attackerObject);
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
                var attacker = activeAttackers[index];
                if (attacker == null || !attacker.activeSelf)
                {
                    activeAttackers.RemoveAt(index);
                }
            }

            return activeAttackers.Count;
        }

        private bool TryResolveDependencies()
        {
            spawnPool ??= UnityEngine.Object.FindAnyObjectByType<SpawnPool>();
            routeMapper ??= UnityEngine.Object.FindAnyObjectByType<FarmRouteMapper>();
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
            foreach (var wave in waves)
            {
                wave?.Validate();
            }
        }
#endif
    }
}
