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
        [SerializeField] private Sprite[] detonationSprites;
        [SerializeField, Min(0.01f)] private float detonationFrameDuration = 0.07f;

        private bool isSubscribed;
        private bool isDetonating;
        private float detonationElapsed;

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

        private void Update()
        {
            if (!isDetonating || visual == null || detonationSprites == null || detonationSprites.Length == 0)
            {
                return;
            }

            detonationElapsed += Time.deltaTime;
            var frameIndex = Mathf.Clamp(
                Mathf.FloorToInt(detonationElapsed / Mathf.Max(0.01f, detonationFrameDuration)),
                0,
                detonationSprites.Length - 1);
            var sprite = detonationSprites[frameIndex];
            if (sprite != null && visual.sprite != sprite)
            {
                visual.sprite = sprite;
            }
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveReferences();
            isDetonating = false;
            detonationElapsed = 0f;
            RefreshState();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            isDetonating = false;
            detonationElapsed = 0f;
            RefreshState();
        }

        private void Subscribe()
        {
            if (isSubscribed || trapProjectile == null)
            {
                return;
            }

            trapProjectile.ArmedStateChanged += HandleArmedStateChanged;
            trapProjectile.Detonated += HandleDetonated;
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
                trapProjectile.Detonated -= HandleDetonated;
            }

            isSubscribed = false;
        }

        private void HandleArmedStateChanged(bool isArmed)
        {
            isDetonating = false;
            detonationElapsed = 0f;
            SetSprite(isArmed);
        }

        private void HandleDetonated()
        {
            isDetonating = detonationSprites != null && detonationSprites.Length > 0 && detonationSprites[0] != null;
            detonationElapsed = 0f;
            if (isDetonating && visual != null)
            {
                visual.sprite = detonationSprites[0];
            }
        }

        private void RefreshState()
        {
            if (trapProjectile != null && trapProjectile.IsDetonating)
            {
                HandleDetonated();
                return;
            }

            isDetonating = false;
            detonationElapsed = 0f;
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
