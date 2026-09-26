using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Swaps a trap's low-poly sprite between its travelling/closed and armed/open states.
    /// It follows the pooled projectile lifecycle and has no authority over collision or damage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrapProjectile), typeof(SpriteRenderer))]
    public sealed class TrapProjectileVisualController : MonoBehaviour, IPoolLifecycle
    {
        [SerializeField] private TrapProjectile trapProjectile;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite closedSprite;
        [SerializeField] private Sprite armedSprite;

        private bool isSubscribed;

        private void Awake()
        {
            ResolveReferences();
            RefreshState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            RefreshState();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveReferences();
            RefreshState();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            RefreshState();
        }

        private void Subscribe()
        {
            if (isSubscribed || trapProjectile == null)
            {
                return;
            }

            trapProjectile.ArmedStateChanged += HandleArmedStateChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (trapProjectile != null)
            {
                trapProjectile.ArmedStateChanged -= HandleArmedStateChanged;
            }

            isSubscribed = false;
        }

        private void HandleArmedStateChanged(bool isArmed)
        {
            SetSprite(isArmed);
        }

        private void RefreshState()
        {
            SetSprite(trapProjectile != null && trapProjectile.IsArmed);
        }

        private void SetSprite(bool isArmed)
        {
            if (visual == null)
            {
                return;
            }

            var sprite = isArmed ? armedSprite : closedSprite;
            if (sprite != null && visual.sprite != sprite)
            {
                visual.sprite = sprite;
            }
        }

        private void ResolveReferences()
        {
            trapProjectile ??= GetComponent<TrapProjectile>();
            visual ??= GetComponent<SpriteRenderer>();
        }
    }
}
