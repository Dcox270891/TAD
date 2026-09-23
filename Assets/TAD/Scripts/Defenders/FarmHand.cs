using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// The initial reusable defender token. Weapon selection and combat behaviour can be added to this
    /// component later without changing the placement or pooling contract.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class FarmHand : MonoBehaviour, IPoolLifecycle
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private WeaponDefinition weaponDefinition;

        /// <summary>Visual used by the placement system to create a non-interactive preview.</summary>
        public SpriteRenderer Visual => visual;

        /// <summary>Shared weapon data currently assigned to this farmhand.</summary>
        public WeaponDefinition WeaponDefinition => weaponDefinition;

        private void Awake()
        {
            visual ??= GetComponent<SpriteRenderer>();
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            if (visual != null)
            {
                visual.enabled = true;
            }
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            visual ??= GetComponent<SpriteRenderer>();
        }
#endif
    }
}
