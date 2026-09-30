using System.Collections;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Renders an attacker from 16 directional sprite sequences without an Animator state-machine per animal.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class AttackerVisualController : MonoBehaviour, IPoolLifecycle
    {
        private const float MovementDirectionThreshold = 0.0001f;
        private const float MinimumDisplaySize = 0.01f;
        // The map is intentionally broad; display scale follows attacker data while navigation
        // remains route-driven and the hit outline is rebuilt alongside the visible sprite.
        private const float VisualScaleMultiplier = 4f;

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField]
        [Tooltip("Used when a legacy attacker definition does not yet reference visual data.")]
        private AttackerVisualDefinition fallbackVisualDefinition;

        private BasicAttacker attacker;
        private Coroutine deathRoutine;
        private float movementElapsed;
        private int lastDirectionIndex;
        private bool isPlayingDeath;
        private Vector3 baseLocalScale;
        private bool hasBaseLocalScale;

        private void Awake()
        {
            CacheBaseLocalScale();
            ResolveComponents();
            RefreshDisplayScale();
            RefreshMovementSprite();
        }

        private void LateUpdate()
        {
            if (isPlayingDeath || attacker == null || attacker.IsDefeated)
            {
                return;
            }

            movementElapsed += Time.deltaTime;
            RefreshMovementSprite();
        }

        /// <summary>Starts the matching directional death sequence and returns whether one could be played.</summary>
        public bool TryPlayDeathAnimation(BasicAttacker source)
        {
            ResolveComponents();
            if (source == null || source != attacker)
            {
                return false;
            }

            var visuals = GetVisualDefinition();
            if (visuals == null || !visuals.HasDeathSprites)
            {
                return false;
            }

            if (deathRoutine != null)
            {
                StopCoroutine(deathRoutine);
            }

            isPlayingDeath = true;
            deathRoutine = StartCoroutine(PlayDeathSequence(source, visuals));
            return true;
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            StopDeathRoutine();
            isPlayingDeath = false;
            movementElapsed = 0f;
            ResolveComponents();
            RefreshDisplayScale();

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = true;
            }

            if (attacker != null)
            {
                lastDirectionIndex = ToDirectionIndex(attacker.MoveDirection);
            }

            RefreshMovementSprite();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            StopDeathRoutine();
            isPlayingDeath = false;
        }

        private IEnumerator PlayDeathSequence(BasicAttacker source, AttackerVisualDefinition visuals)
        {
            for (var frameIndex = 0; frameIndex < AttackerVisualDefinition.DeathFrameCount; frameIndex++)
            {
                SetSprite(visuals.GetDeathSprite(lastDirectionIndex, frameIndex));
                yield return new WaitForSeconds(visuals.DeathFrameDuration);
            }

            deathRoutine = null;
            if (source != null && source.IsDefeated && source.gameObject.activeInHierarchy)
            {
                source.CompleteDeathAnimation();
            }
        }

        private void RefreshMovementSprite()
        {
            ResolveComponents();
            var visuals = GetVisualDefinition();
            if (visuals == null || !visuals.HasMovementSprites || attacker == null || attacker.IsDefeated)
            {
                return;
            }

            var moveDirection = attacker.MoveDirection;
            if (moveDirection.sqrMagnitude > MovementDirectionThreshold)
            {
                lastDirectionIndex = ToDirectionIndex(moveDirection);
            }

            var frameIndex = Mathf.FloorToInt(movementElapsed / visuals.MovementFrameDuration) %
                             AttackerVisualDefinition.MovementFrameCount;
            if (SetSprite(visuals.GetMovementSprite(lastDirectionIndex, frameIndex)))
            {
                SynchronizeHitColliderToVisibleSprite();
            }
        }

        /// <summary>
        /// Refreshes this attacker's visual presentation after its shared definition changes.
        /// The pool can assign a different animal definition after this child component has been
        /// reactivated, so its scale, sprite, and collision footprint must be refreshed together.
        /// </summary>
        public void RefreshFromAttackerDefinition()
        {
            ResolveComponents();
            RefreshDisplayScale();
            RefreshMovementSprite();
            SynchronizeHitColliderToVisibleSprite();
        }

        private AttackerVisualDefinition GetVisualDefinition()
        {
            return attacker != null && attacker.Definition != null && attacker.Definition.VisualDefinition != null
                ? attacker.Definition.VisualDefinition
                : fallbackVisualDefinition;
        }

        private void ResolveComponents()
        {
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            attacker ??= GetComponentInParent<BasicAttacker>();
        }

        private void RefreshDisplayScale()
        {
            if (attacker == null)
            {
                return;
            }

            CacheBaseLocalScale();
            var displayScale = baseLocalScale *
                               (VisualScaleMultiplier * Mathf.Max(MinimumDisplaySize, attacker.Size));
            if (transform.localScale == displayScale)
            {
                return;
            }

            transform.localScale = displayScale;
            SynchronizeHitColliderToVisibleSprite();
        }

        private void CacheBaseLocalScale()
        {
            if (hasBaseLocalScale)
            {
                return;
            }

            baseLocalScale = transform.localScale;
            hasBaseLocalScale = true;
        }

        private void StopDeathRoutine()
        {
            if (deathRoutine != null)
            {
                StopCoroutine(deathRoutine);
                deathRoutine = null;
            }
        }

        private void SynchronizeHitColliderToVisibleSprite()
        {
            if (isPlayingDeath || attacker == null || attacker.IsDefeated || spriteRenderer == null ||
                spriteRenderer.sprite == null)
            {
                return;
            }

            attacker.SynchronizeHitColliderToVisual(spriteRenderer);
        }

        private bool SetSprite(Sprite sprite)
        {
            if (spriteRenderer != null && sprite != null && spriteRenderer.sprite != sprite)
            {
                spriteRenderer.sprite = sprite;
                return true;
            }

            return false;
        }

        private static int ToDirectionIndex(Vector2 direction)
        {
            if (direction.sqrMagnitude <= MovementDirectionThreshold)
            {
                return 0;
            }

            var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var index = Mathf.RoundToInt(angle / (360f / AttackerVisualDefinition.DirectionCount));
            return (index % AttackerVisualDefinition.DirectionCount + AttackerVisualDefinition.DirectionCount) %
                   AttackerVisualDefinition.DirectionCount;
        }
    }
}
