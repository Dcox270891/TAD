using System;
using UnityEngine;

namespace TAD.Pooling
{
    /// <summary>
    /// Added to pooled instances so gameplay components can return themselves to their owner.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PooledInstance : MonoBehaviour
    {
        [NonSerialized] private SpawnPool owner;
        [NonSerialized] private PooledObjectType objectType;
        [NonSerialized] private long runtimeInstanceId;
        [NonSerialized] private MonoBehaviour[] lifecycleComponents;

        public SpawnPool Owner => owner;
        public PooledObjectType ObjectType => objectType;

        /// <summary>
        /// Unique session-only identity assigned when this pooled object is created.
        /// The value is retained while the object is recycled by its pool.
        /// </summary>
        public long RuntimeInstanceId => runtimeInstanceId;

        internal void Configure(SpawnPool pool, PooledObjectType type, long instanceId)
        {
            owner = pool;
            objectType = type;
            runtimeInstanceId = instanceId;
            CacheLifecycleComponents();
        }

        internal void NotifyTakenFromPool()
        {
            NotifyLifecycleTakenFromPool();
        }

        internal void NotifyReturnedToPool()
        {
            NotifyLifecycleReturnedToPool();
        }

        /// <summary>Returns this object to the pool that created it.</summary>
        public void ReturnToPool()
        {
            if (owner != null)
            {
                owner.Release(this);
                return;
            }

            gameObject.SetActive(false);
        }

        private void CacheLifecycleComponents()
        {
            lifecycleComponents = GetComponentsInChildren<MonoBehaviour>(true);
        }

        private void NotifyLifecycleTakenFromPool()
        {
            if (lifecycleComponents == null)
            {
                CacheLifecycleComponents();
            }

            foreach (var component in lifecycleComponents)
            {
                if (component is IPoolLifecycle lifecycle)
                {
                    lifecycle.OnTakenFromPool();
                }
            }
        }

        private void NotifyLifecycleReturnedToPool()
        {
            if (lifecycleComponents == null)
            {
                CacheLifecycleComponents();
            }

            foreach (var component in lifecycleComponents)
            {
                if (component is IPoolLifecycle lifecycle)
                {
                    lifecycle.OnReturnedToPool();
                }
            }
        }
    }
}
