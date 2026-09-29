using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Plays short, presentation-only ground-impact sprites whenever a bouncing roller reaches
    /// one of its authored bounce points. Projectile damage and pooling remain owned by
    /// <see cref="RollingProjectile"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RollingProjectile), typeof(SpriteRenderer))]
    public sealed class RollingProjectileVisualController : MonoBehaviour, IPoolLifecycle
    {
        [SerializeField] private RollingProjectile rollingProjectile;
        [SerializeField] private SpriteRenderer impactRenderer;
        [SerializeField] private Sprite[] bounceSprites;
        [SerializeField, Min(0.01f)] private float bounceFrameDuration = 0.06f;

        private bool isSubscribed;
        private bool isPlayingBounce;
        private float bounceElapsed;

        private void Awake()
        {
            ResolveReferences();
            ResetPresentation();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ResetPresentation();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!isPlayingBounce || impactRenderer == null || bounceSprites == null || bounceSprites.Length == 0)
            {
                return;
            }

            bounceElapsed += Time.deltaTime;
            var frameIndex = Mathf.FloorToInt(bounceElapsed / Mathf.Max(0.01f, bounceFrameDuration));
            if (frameIndex >= bounceSprites.Length)
            {
                ResetPresentation();
                return;
            }

            var sprite = bounceSprites[frameIndex];
            impactRenderer.enabled = sprite != null;
            if (sprite != null && impactRenderer.sprite != sprite)
            {
                impactRenderer.sprite = sprite;
            }
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveReferences();
            ResetPresentation();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            ResetPresentation();
        }

        private void Subscribe()
        {
            if (isSubscribed || rollingProjectile == null)
            {
                return;
            }

            rollingProjectile.Bounced += HandleBounced;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (rollingProjectile != null)
            {
                rollingProjectile.Bounced -= HandleBounced;
            }

            isSubscribed = false;
        }

        private void HandleBounced(Vector2 _, int __)
        {
            if (impactRenderer == null || bounceSprites == null || bounceSprites.Length == 0 || bounceSprites[0] == null)
            {
                return;
            }

            isPlayingBounce = true;
            bounceElapsed = 0f;
            impactRenderer.sprite = bounceSprites[0];
            impactRenderer.enabled = true;
        }

        private void ResetPresentation()
        {
            isPlayingBounce = false;
            bounceElapsed = 0f;
            if (impactRenderer != null)
            {
                impactRenderer.enabled = false;
            }
        }

        private void ResolveReferences()
        {
            rollingProjectile ??= GetComponent<RollingProjectile>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bounceFrameDuration = Mathf.Max(0.01f, bounceFrameDuration);
            ResolveReferences();
        }
#endif
    }
}
