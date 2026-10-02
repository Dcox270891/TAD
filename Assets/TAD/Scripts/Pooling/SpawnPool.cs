using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Navigation;
using UnityEngine;

namespace TAD.Pooling
{
    /// <summary>
    /// Reuses attacker, defender, and projectile prefabs. Each configured gameplay prefab is prewarmed.
    /// Additional attacker prefabs are addressed by their shared Attacker Definition.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmRouteMapper))]
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

        [Header("Additional Projectiles")]
        [SerializeField]
        [Tooltip("One reusable projectile prefab for each specialised defender weapon. The default Projectile pool remains the Unarmed hand.")]
        private GameObject[] additionalProjectilePrefabs = Array.Empty<GameObject>();

        [SerializeField, Min(0)]
        [Tooltip("Objects prewarmed for every additional projectile prefab.")]
        private int additionalProjectileInitialCapacity = 5;

        [SerializeField]
        [Tooltip("The route map owned by this Spawn Pool. It is required so pooled attackers always share the same map.")]
        private FarmRouteMapper routeMapper;

        // Shared by every pool so IDs stay unique across all pooled gameplay types for one play session.
        private static long nextRuntimeInstanceId;

        private readonly Dictionary<PooledObjectType, PoolBucket> pools = new();
        private readonly Dictionary<AttackerDefinition, PoolBucket> attackerPoolsByDefinition = new();
        private readonly Dictionary<GameObject, PoolBucket> projectilePoolsByPrefab = new();
        private readonly Dictionary<PooledInstance, PoolBucket> instanceBuckets = new();
        private bool isInitialized;

        public FarmRouteMapper RouteMapper
        {
            get
            {
                routeMapper ??= GetComponent<FarmRouteMapper>();
                return routeMapper;
            }
        }

        /// <summary>Copies every attacker definition configured by this pool, preserving roster order.</summary>
        public void CopyAvailableAttackerDefinitions(List<AttackerDefinition> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            if (poolSettings != null)
            {
                foreach (var settings in poolSettings)
                {
                    if (settings == null || settings.ObjectType != PooledObjectType.Attacker)
                    {
                        continue;
                    }

                    AddAttackerDefinition(settings.Prefab, destination);
                }
            }

            if (additionalAttackerPrefabs == null)
            {
                return;
            }

            foreach (var attackerPrefab in additionalAttackerPrefabs)
            {
                AddAttackerDefinition(attackerPrefab, destination);
            }
        }

        /// <summary>
        /// Copies every projectile prefab configured by this pool, including the default projectile and
        /// all specialised weapon variants. This provides a safe way for validation and diagnostics to
        /// exercise the same buckets gameplay uses.
        /// </summary>
        public void CopyAvailableProjectilePrefabs(List<GameObject> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            if (poolSettings != null)
            {
                foreach (var settings in poolSettings)
                {
                    if (settings != null && settings.ObjectType == PooledObjectType.Projectile)
                    {
                        AddUniquePrefab(settings.Prefab, destination);
                    }
                }
            }

            if (additionalProjectilePrefabs == null)
            {
                return;
            }

            foreach (var projectilePrefab in additionalProjectilePrefabs)
            {
                AddUniquePrefab(projectilePrefab, destination);
            }
        }

        /// <summary>Gets the configured attacker prefab for a definition, including the legacy default attacker.</summary>
        public bool TryGetAttackerPrefab(AttackerDefinition definition, out GameObject attackerPrefab)
        {
            attackerPrefab = null;
            if (definition == null)
            {
                return false;
            }

            if (poolSettings != null)
            {
                foreach (var settings in poolSettings)
                {
                    if (settings != null && settings.ObjectType == PooledObjectType.Attacker &&
                        MatchesDefinition(settings.Prefab, definition))
                    {
                        attackerPrefab = settings.Prefab;
                        return true;
                    }
                }
            }

            if (additionalAttackerPrefabs == null)
            {
                return TryFindEquivalentAttackerPrefab(definition, out attackerPrefab);
            }

            foreach (var candidate in additionalAttackerPrefabs)
            {
                if (MatchesDefinition(candidate, definition))
                {
                    attackerPrefab = candidate;
                    return true;
                }
            }

            // A pool normally receives the canonical asset reference. In editor hot reload or
            // deserialization edge cases Unity can provide an equivalent ScriptableObject copy.
            // Accept that copy only when its entire authored identity matches exactly; an ID by itself
            // is deliberately not enough to redirect a potentially different attacker to a prefab.
            return TryFindEquivalentAttackerPrefab(definition, out attackerPrefab);
        }

        private static void AddAttackerDefinition(GameObject attackerPrefab, List<AttackerDefinition> destination)
        {
            if (attackerPrefab == null ||
                !attackerPrefab.TryGetComponent<BasicAttacker>(out var attacker) ||
                attacker.Definition == null ||
                destination.Contains(attacker.Definition))
            {
                return;
            }

            destination.Add(attacker.Definition);
        }

        private static void AddUniquePrefab(GameObject prefab, List<GameObject> destination)
        {
            if (prefab != null && !destination.Contains(prefab))
            {
                destination.Add(prefab);
            }
        }

        private static bool MatchesDefinition(GameObject attackerPrefab, AttackerDefinition definition)
        {
            return attackerPrefab != null &&
                   attackerPrefab.TryGetComponent<BasicAttacker>(out var attacker) &&
                   attacker.Definition == definition;
        }

        private static bool MatchesEquivalentDefinition(GameObject attackerPrefab, AttackerDefinition definition)
        {
            return attackerPrefab != null &&
                   definition != null &&
                   attackerPrefab.TryGetComponent<BasicAttacker>(out var attacker) &&
                   attacker.Definition != null &&
                   AreDefinitionsEquivalent(attacker.Definition, definition);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeInstanceIdSequence()
        {
            // SubsystemRegistration also runs when Enter Play Mode Options disable domain reload.
            nextRuntimeInstanceId = 0L;
        }

        private void Awake()
        {
            routeMapper ??= GetComponent<FarmRouteMapper>();
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

        /// <summary>Activates a pooled projectile matched to the exact prefab selected by a weapon definition.</summary>
        public GameObject SpawnProjectile(
            GameObject projectilePrefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            if (projectilePrefab == null)
            {
                Debug.LogError($"{name} cannot spawn a null projectile prefab.", this);
                return null;
            }

            Initialize();
            if (!projectilePoolsByPrefab.TryGetValue(projectilePrefab, out var pool))
            {
                Debug.LogError($"{name} has no configured pooled bucket for projectile prefab {projectilePrefab.name}.", this);
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
            if (!TryResolveAttackerPool(definition, out var pool))
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
                RegisterProjectilePool(settings.Prefab, pool);
                Prewarm(pool, settings.InitialCapacity);
            }

            if (additionalAttackerPrefabs == null)
            {
                additionalAttackerPrefabs = Array.Empty<GameObject>();
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
                RegisterAttackerPool(attackerPrefab, pool);
                Prewarm(pool, additionalAttackerInitialCapacity);
            }

            if (additionalProjectilePrefabs == null)
            {
                return;
            }

            foreach (var projectilePrefab in additionalProjectilePrefabs)
            {
                if (projectilePrefab == null)
                {
                    continue;
                }

                if (projectilePoolsByPrefab.ContainsKey(projectilePrefab))
                {
                    Debug.LogWarning($"{name} already has a pooled bucket for projectile prefab {projectilePrefab.name}; the duplicate was ignored.", this);
                    continue;
                }

                var projectilePool = CreateBucket(projectilePrefab, PooledObjectType.Projectile, $"Projectile Pool ({projectilePrefab.name})");
                RegisterProjectilePool(projectilePrefab, projectilePool);
                Prewarm(projectilePool, additionalProjectileInitialCapacity);
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
                return;
            }
        }

        private bool TryResolveAttackerPool(AttackerDefinition definition, out PoolBucket pool)
        {
            if (attackerPoolsByDefinition.TryGetValue(definition, out pool))
            {
                return true;
            }

            pool = null;
            foreach (var registeredPool in attackerPoolsByDefinition)
            {
                if (!AreDefinitionsEquivalent(registeredPool.Key, definition))
                {
                    continue;
                }

                if (pool != null && pool != registeredPool.Value)
                {
                    // Do not make a best guess if a malformed roster contains multiple equivalent
                    // data entries that point to different prefabs.
                    pool = null;
                    return false;
                }

                pool = registeredPool.Value;
            }

            return pool != null;
        }

        private bool TryFindEquivalentAttackerPrefab(AttackerDefinition definition, out GameObject attackerPrefab)
        {
            attackerPrefab = null;
            if (definition == null)
            {
                return false;
            }

            if (poolSettings != null)
            {
                foreach (var settings in poolSettings)
                {
                    if (settings == null || settings.ObjectType != PooledObjectType.Attacker ||
                        !MatchesEquivalentDefinition(settings.Prefab, definition))
                    {
                        continue;
                    }

                    if (attackerPrefab != null && attackerPrefab != settings.Prefab)
                    {
                        attackerPrefab = null;
                        return false;
                    }

                    attackerPrefab = settings.Prefab;
                }
            }

            if (additionalAttackerPrefabs != null)
            {
                foreach (var candidate in additionalAttackerPrefabs)
                {
                    if (!MatchesEquivalentDefinition(candidate, definition))
                    {
                        continue;
                    }

                    if (attackerPrefab != null && attackerPrefab != candidate)
                    {
                        attackerPrefab = null;
                        return false;
                    }

                    attackerPrefab = candidate;
                }
            }

            return attackerPrefab != null;
        }

        private static bool AreDefinitionsEquivalent(AttackerDefinition first, AttackerDefinition second)
        {
            return first != null &&
                   second != null &&
                   string.Equals(first.AttackerId?.Trim(), second.AttackerId?.Trim(), StringComparison.Ordinal) &&
                   string.Equals(first.DisplayName, second.DisplayName, StringComparison.Ordinal) &&
                   Mathf.Approximately(first.MaximumHealth, second.MaximumHealth) &&
                   Mathf.Approximately(first.MovementSpeed, second.MovementSpeed) &&
                   Mathf.Approximately(first.Size, second.Size) &&
                   first.SendCost == second.SendCost &&
                   first.WaveIncome == second.WaveIncome &&
                   first.ExpGiven == second.ExpGiven &&
                   first.KillGain == second.KillGain &&
                   first.SendSize == second.SendSize &&
                   first.Archetype == second.Archetype &&
                   first.ChallengeTier == second.ChallengeTier &&
                   first.TraversalProfile == second.TraversalProfile &&
                   first.VisualDefinition == second.VisualDefinition;
        }

        private void RegisterProjectilePool(GameObject prefab, PoolBucket pool)
        {
            if (pool.ObjectType != PooledObjectType.Projectile || prefab == null)
            {
                return;
            }

            if (!projectilePoolsByPrefab.TryAdd(prefab, pool))
            {
                Debug.LogWarning($"{name} has more than one pooled bucket for projectile prefab {prefab.name}. The later prefab was ignored.", this);
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
            routeMapper ??= GetComponent<FarmRouteMapper>();

            foreach (var settings in poolSettings)
            {
                settings?.Validate();
            }

            additionalAttackerInitialCapacity = Mathf.Max(0, additionalAttackerInitialCapacity);
            additionalProjectileInitialCapacity = Mathf.Max(0, additionalProjectileInitialCapacity);
        }
#endif
    }
}
