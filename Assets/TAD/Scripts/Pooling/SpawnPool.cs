using System;
using System.Collections.Generic;
using UnityEngine;

namespace TAD.Pooling
{
    /// <summary>
    /// Reuses attacker, defender, and projectile prefabs. Each configured type prewarms five objects.
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
            public readonly List<PooledInstance> Instances = new();
            public readonly GameObject Prefab;
            public readonly PooledObjectType ObjectType;
            public readonly Transform InactiveContainer;

            public PoolBucket(GameObject prefab, PooledObjectType objectType, Transform inactiveContainer)
            {
                Prefab = prefab;
                ObjectType = objectType;
                InactiveContainer = inactiveContainer;
            }
        }

        [SerializeField] private PoolSettings[] poolSettings =
        {
            new PoolSettings(PooledObjectType.Attacker),
            new PoolSettings(PooledObjectType.Defender),
            new PoolSettings(PooledObjectType.Projectile)
        };

        private readonly Dictionary<PooledObjectType, PoolBucket> pools = new();
        private bool isInitialized;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Activates an available object of the requested type, creating one if the pool is exhausted.
        /// </summary>
        public GameObject Spawn(PooledObjectType objectType, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            Initialize();

            if (!pools.TryGetValue(objectType, out var pool))
            {
                Debug.LogError($"{name} has no configured {objectType} prefab.", this);
                return null;
            }

            var instance = GetAvailableInstance(pool);
            var instanceTransform = instance.transform;
            instanceTransform.SetParent(parent, false);
            instanceTransform.SetPositionAndRotation(position, rotation);
            instance.NotifyTakenFromPool();
            instance.gameObject.SetActive(true);
            return instance.gameObject;
        }

        /// <summary>Returns an object created by this pool to its inactive storage.</summary>
        public void Release(PooledInstance instance)
        {
            if (instance == null || instance.Owner != this)
            {
                return;
            }

            if (!pools.TryGetValue(instance.ObjectType, out var pool))
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

                var inactiveContainer = new GameObject($"{settings.ObjectType} Pool").transform;
                inactiveContainer.SetParent(transform, false);

                var pool = new PoolBucket(settings.Prefab, settings.ObjectType, inactiveContainer);
                pools.Add(settings.ObjectType, pool);

                for (var index = 0; index < settings.InitialCapacity; index++)
                {
                    CreateInstance(pool);
                }
            }
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

            instance.Configure(this, pool.ObjectType);
            instanceObject.SetActive(false);
            pool.Instances.Add(instance);
            return instance;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            foreach (var settings in poolSettings)
            {
                settings?.Validate();
            }
        }
#endif
    }
}
