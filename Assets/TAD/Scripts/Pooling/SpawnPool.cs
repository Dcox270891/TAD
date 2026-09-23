using System;
using System.Collections.Generic;
using TAD.Attackers;
using UnityEngine;

namespace TAD.Pooling
{
    /// <summary>
    /// Reuses attacker, defender, and projectile prefabs. Each configured gameplay prefab is prewarmed.
    /// Additional attacker prefabs are addressed by their shared Attacker Definition.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPool : MonoBehaviour
    {
        [Serializable]
        private sealed class PoolSettings
        {
            [SerializeField] private PooledObjectType objectType;
            [SerializeField] private GameObject prefab;
            [SerializeField, Min(0)] private int initialCapacity = 5;

            public PooledObjectType ObjectType => objectType;
            public GameObject Prefab => prefab;
            public int InitialCapacity => initialCapacity;

            public PoolSettings(PooledObjectType objectType)
            {
                this.objectType = objectType;
            }

#if UNITY_EDITOR
            public void Validate()
            {
                initialCapacity = Mathf.Max(0, initialCapacity);
            }
#endif
        }

        private sealed class PoolBucket
        {
            public PoolBucket(GameObject prefab, PooledObjectType objectType, Transform inactiveContainer)
            {
                Prefab = prefab;
                ObjectType = objectType;
                InactiveContainer = inactiveContainer;
            }

            public readonly List<PooledInstance> Instances = new();
            public readonly GameObject Prefab;
            public readonly PooledObjectType ObjectType;
            public readonly Transform InactiveContainer;
        }

        [SerializeField] private PoolSettings[] poolSettings =
        {
            new PoolSettings(PooledObjectType.Attacker),
            new PoolSettings(PooledObjectType.Defender),
            new PoolSettings(PooledObjectType.Projectile)
        };

        [Header("Additional Attackers")]
        [SerializeField]
        [Tooltip("One reusable prefab per attacker definition. The original Attacker pool remains the default for legacy waves.")]
        private GameObject[] additionalAttackerPrefabs = Array.Empty<GameObject>();

        [SerializeField, Min(0)]
        [Tooltip("Objects prewarmed for every additional attacker prefab.")]
        private int additionalAttackerInitialCapacity = 5;

        // Shared by every pool so IDs stay unique across all pooled gameplay types for one play session.
        private static long nextRuntimeInstanceId;

        private readonly Dictionary<PooledObjectType, PoolBucket> pools = new();
        private readonly Dictionary<AttackerDefinition, PoolBucket> attackerPoolsByDefinition = new();
        private readonly Dictionary<PooledInstance, PoolBucket> instanceBuckets = new();
        private bool isInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeInstanceIdSequence()
        {
            // SubsystemRegistration also runs when Enter Play Mode Options disable domain reload.
            nextRuntimeInstanceId = 0L;
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>Activates an available default object of the requested type.</summary>
        public GameObject Spawn(PooledObjectType objectType, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            Initialize();
            if (!pools.TryGetValue(objectType, out var pool))
            {
                Debug.LogError($"{name} has no configured {objectType} prefab.", this);
                return null;
            }

            return SpawnFromBucket(pool, position, rotation, parent);
        }

        /// <summary>Activates a reusable attacker prefab matched to a shared attacker definition.</summary>
        public GameObject SpawnAttacker(
            AttackerDefinition definition,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            if (definition == null)
            {
                return Spawn(PooledObjectType.Attacker, position, rotation, parent);
            }

            Initialize();
            if (!attackerPoolsByDefinition.TryGetValue(definition, out var pool))
            {
                Debug.LogError($"{name} has no configured pooled prefab for attacker definition {definition.DisplayName} ({definition.AttackerId}).", this);
                return null;
            }

            return SpawnFromBucket(pool, position, rotation, parent);
        }

        /// <summary>Returns an object created by this pool to the storage bucket that created it.</summary>
        public void Release(PooledInstance instance)
        {
            if (instance == null || instance.Owner != this || !instanceBuckets.TryGetValue(instance, out var pool))
            {
                return;
            }

            instance.NotifyReturnedToPool();
            instance.transform.SetParent(pool.InactiveContainer, false);
            instance.gameObject.SetActive(false);
        }

        private void Initialize()
        {
            if (isInitialized)
            {
                return;
            }

            isInitialized = true;
            foreach (var settings in poolSettings)
            {
                if (settings == null || settings.Prefab == null)
                {
                    continue;
                }

                if (pools.ContainsKey(settings.ObjectType))
                {
                    Debug.LogWarning($"{name} has more than one {settings.ObjectType} pool configuration. The later one is ignored.", this);
                    continue;
                }

                var pool = CreateBucket(settings.Prefab, settings.ObjectType, settings.ObjectType + " Pool");
                pools.Add(settings.ObjectType, pool);
                RegisterAttackerPool(settings.Prefab, pool);
                Prewarm(pool, settings.InitialCapacity);
            }

            if (additionalAttackerPrefabs == null)
            {
                return;
            }

            foreach (var attackerPrefab in additionalAttackerPrefabs)
            {
                if (attackerPrefab == null)
                {
                    continue;
                }

                if (!attackerPrefab.TryGetComponent<BasicAttacker>(out var attacker) || attacker.Definition == null)
                {
                    Debug.LogWarning($"{name} ignored additional attacker prefab {attackerPrefab.name} because it has no Basic Attacker definition.", this);
                    continue;
                }

                if (attackerPoolsByDefinition.ContainsKey(attacker.Definition))
                {
                    Debug.LogWarning($"{name} already has a pooled prefab for attacker definition {attacker.Definition.DisplayName}; {attackerPrefab.name} was ignored.", this);
                    continue;
                }

                var pool = CreateBucket(attackerPrefab, PooledObjectType.Attacker, $"Attacker Pool ({attackerPrefab.name})");
                attackerPoolsByDefinition.Add(attacker.Definition, pool);
                Prewarm(pool, additionalAttackerInitialCapacity);
            }
        }

        private PoolBucket CreateBucket(GameObject prefab, PooledObjectType objectType, string containerName)
        {
            var inactiveContainer = new GameObject(containerName).transform;
            // Containers are runtime infrastructure, so they inherit this Background-layer pool's layer.
            inactiveContainer.gameObject.layer = gameObject.layer;
            inactiveContainer.SetParent(transform, false);
            return new PoolBucket(prefab, objectType, inactiveContainer);
        }

        private void RegisterAttackerPool(GameObject prefab, PoolBucket pool)
        {
            if (pool.ObjectType != PooledObjectType.Attacker ||
                !prefab.TryGetComponent<BasicAttacker>(out var attacker) ||
                attacker.Definition == null)
            {
                return;
            }

            if (!attackerPoolsByDefinition.TryAdd(attacker.Definition, pool))
            {
                Debug.LogWarning($"{name} has more than one pooled prefab for attacker definition {attacker.Definition.DisplayName}. The later prefab is ignored.", this);
            }
        }

        private void Prewarm(PoolBucket pool, int count)
        {
            for (var index = 0; index < Mathf.Max(0, count); index++)
            {
                CreateInstance(pool);
            }
        }

        private GameObject SpawnFromBucket(PoolBucket pool, Vector3 position, Quaternion rotation, Transform parent)
        {
            var instance = GetAvailableInstance(pool);
            var instanceTransform = instance.transform;
            instanceTransform.SetParent(parent, false);
            instanceTransform.SetPositionAndRotation(position, rotation);
            instance.NotifyTakenFromPool();
            instance.gameObject.SetActive(true);
            return instance.gameObject;
        }

        private PooledInstance GetAvailableInstance(PoolBucket pool)
        {
            foreach (var instance in pool.Instances)
            {
                if (instance != null && !instance.gameObject.activeSelf)
                {
                    return instance;
                }
            }

            return CreateInstance(pool);
        }

        private PooledInstance CreateInstance(PoolBucket pool)
        {
            var instanceObject = Instantiate(pool.Prefab, pool.InactiveContainer);
            instanceObject.name = pool.Prefab.name;
            var instance = instanceObject.GetComponent<PooledInstance>();
            if (instance == null)
            {
                instance = instanceObject.AddComponent<PooledInstance>();
            }

            instance.Configure(this, pool.ObjectType, AllocateRuntimeInstanceId());
            instanceObject.SetActive(false);
            pool.Instances.Add(instance);
            instanceBuckets.Add(instance, pool);
            return instance;
        }

        private static long AllocateRuntimeInstanceId()
        {
            if (nextRuntimeInstanceId == long.MaxValue)
            {
                throw new InvalidOperationException("The pooled runtime instance ID sequence has been exhausted.");
            }

            return ++nextRuntimeInstanceId;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            foreach (var settings in poolSettings)
            {
                settings?.Validate();
            }

            additionalAttackerInitialCapacity = Mathf.Max(0, additionalAttackerInitialCapacity);
        }
#endif
    }
}
