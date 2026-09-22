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
        [NonSerialized] private MonoBehaviour[] lifecycleComponents;

        public SpawnPool Owner => owner;
        public PooledObjectType ObjectType => objectType;

        internal void Configure(SpawnPool pool, PooledObjectType type)
        {
            owner = pool;
            objectType = type;
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
