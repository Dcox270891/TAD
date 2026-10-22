using System.Collections;
using TAD.Pooling;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Renders an attacker from 16 directional sprite sequences without an Animator state-machine per animal.
    /// Atlas-backed roster members receive their facing, walk, and death motion procedurally so the
    /// complete animal roster remains compact and legible on mobile.
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
        private const float PoseAngleEpsilon = 0.01f;
        private const float PoseVectorEpsilonSquared = 0.000001f;

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField]
        [Tooltip("Rendered over the animal only while its death animation is playing.")]
        private SpriteRenderer deathEffectRenderer;
        [SerializeField]
        [Tooltip("Used when a legacy attacker definition does not yet reference visual data.")]
        private AttackerVisualDefinition fallbackVisualDefinition;

        private BasicAttacker attacker;
        private Coroutine deathRoutine;
        private float movementElapsed;
        private int lastDirectionIndex;
        private bool isPlayingDeath;
        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private Vector3 baseLocalScale;
        private Vector3 displayLocalScale;
        private Color baseSpriteColor = Color.white;
        private Color baseDeathEffectColor = Color.white;
        private bool hasBasePresentation;
        private int lastAppliedDirectionIndex = -1;
        private int lastAppliedMovementFrameIndex = -1;

        private void Awake()
        {
            ResolveComponents();
            CacheBasePresentation();
            ResetVisualPresentation();
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
            if (visuals == null || (!visuals.HasDeathSprites && !visuals.UsesProceduralDirectionalAnimation))
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
            CacheBasePresentation();
            ResetVisualPresentation();
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
            ResetVisualPresentation();
        }

        private IEnumerator PlayDeathSequence(BasicAttacker source, AttackerVisualDefinition visuals)
        {
            for (var frameIndex = 0; frameIndex < AttackerVisualDefinition.DeathFrameCount; frameIndex++)
            {
                var deathSprite = visuals.GetDeathSprite(lastDirectionIndex, frameIndex);
                if (deathSprite != null)
                {
                    SetSprite(deathSprite);
                }

                if (visuals.UsesProceduralDirectionalAnimation)
                {
                    ApplyProceduralDeathPose(visuals, frameIndex);
                }

                UpdateDeathEffect(visuals, frameIndex);
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
            var poseChanged = visuals.UsesProceduralDirectionalAnimation
                ? ApplyProceduralMovementPose(visuals, frameIndex)
                : RestoreSpriteSheetMovementPose();
            if (SetSprite(visuals.GetMovementSprite(lastDirectionIndex, frameIndex)) || poseChanged)
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
            CacheBasePresentation();
            StopDeathRoutine();
            isPlayingDeath = false;
            ResetVisualPresentation();
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
            if (deathEffectRenderer == null)
            {
                deathEffectRenderer = transform.Find("DeathPuff")?.GetComponent<SpriteRenderer>();
            }

            attacker ??= GetComponentInParent<BasicAttacker>();
        }

        private void RefreshDisplayScale()
        {
            if (attacker == null)
            {
                return;
            }

            CacheBasePresentation();
            displayLocalScale = baseLocalScale *
                                (VisualScaleMultiplier * Mathf.Max(MinimumDisplaySize, attacker.Size));
            transform.localScale = displayLocalScale;
            lastAppliedDirectionIndex = -1;
            lastAppliedMovementFrameIndex = -1;
            SynchronizeHitColliderToVisibleSprite();
        }

        private void CacheBasePresentation()
        {
            if (hasBasePresentation)
            {
                return;
            }

            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            baseLocalScale = transform.localScale;
            if (spriteRenderer != null)
            {
                baseSpriteColor = spriteRenderer.color;
            }

            if (deathEffectRenderer != null)
            {
                baseDeathEffectColor = deathEffectRenderer.color;
            }

            displayLocalScale = baseLocalScale;
            hasBasePresentation = true;
        }

        private void ResetVisualPresentation()
        {
            if (!hasBasePresentation)
            {
                return;
            }

            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
            transform.localScale = baseLocalScale;
            if (spriteRenderer != null)
            {
                spriteRenderer.color = baseSpriteColor;
                spriteRenderer.enabled = true;
            }

            ClearDeathEffect();
            lastAppliedDirectionIndex = -1;
            lastAppliedMovementFrameIndex = -1;
        }

        private bool ApplyProceduralMovementPose(AttackerVisualDefinition visuals, int frameIndex)
        {
            if (lastAppliedDirectionIndex == lastDirectionIndex &&
                lastAppliedMovementFrameIndex == frameIndex)
            {
                return false;
            }

            lastAppliedDirectionIndex = lastDirectionIndex;
            lastAppliedMovementFrameIndex = frameIndex;
            var isLiftedStep = frameIndex == 0;
            var stepScale = isLiftedStep
                ? new Vector3(1.035f, 0.965f, 1f)
                : new Vector3(0.97f, 1.04f, 1f);
            var stepOffset = isLiftedStep ? 0.018f : -0.012f;
            return ApplyVisualTransform(
                baseLocalPosition + Vector3.up * stepOffset,
                GetDirectionalRotation(visuals),
                Vector3.Scale(displayLocalScale, stepScale));
        }

        private bool RestoreSpriteSheetMovementPose()
        {
            lastAppliedDirectionIndex = -1;
            lastAppliedMovementFrameIndex = -1;
            if (spriteRenderer != null && spriteRenderer.color != baseSpriteColor)
            {
                spriteRenderer.color = baseSpriteColor;
            }

            ClearDeathEffect();
            return ApplyVisualTransform(baseLocalPosition, baseLocalRotation, displayLocalScale);
        }

        private void ApplyProceduralDeathPose(AttackerVisualDefinition visuals, int frameIndex)
        {
            var progress = (frameIndex + 1f) / AttackerVisualDefinition.DeathFrameCount;
            var fallenScale = new Vector3(
                Mathf.Lerp(1f, 1.22f, progress),
                Mathf.Lerp(1f, 0.18f, progress),
                1f);
            ApplyVisualTransform(
                baseLocalPosition + Vector3.down * Mathf.Lerp(0f, 0.055f, progress),
                Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 82f, progress)) * GetDirectionalRotation(visuals),
                Vector3.Scale(displayLocalScale, fallenScale));

            if (spriteRenderer != null)
            {
                var color = baseSpriteColor;
                color.a *= Mathf.Lerp(1f, 0.08f, progress);
                spriteRenderer.color = color;
            }
        }

        private Quaternion GetDirectionalRotation(AttackerVisualDefinition visuals)
        {
            var degrees = lastDirectionIndex * (360f / AttackerVisualDefinition.DirectionCount) +
                          visuals.AtlasFacingOffsetDegrees;
            return Quaternion.Euler(0f, 0f, degrees) * baseLocalRotation;
        }

        private bool ApplyVisualTransform(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var changed = (transform.localPosition - localPosition).sqrMagnitude > PoseVectorEpsilonSquared ||
                          Quaternion.Angle(transform.localRotation, localRotation) > PoseAngleEpsilon ||
                          (transform.localScale - localScale).sqrMagnitude > PoseVectorEpsilonSquared;
            if (!changed)
            {
                return false;
            }

            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;
            return true;
        }

        private void UpdateDeathEffect(AttackerVisualDefinition visuals, int frameIndex)
        {
            if (deathEffectRenderer == null || !visuals.HasDeathEffectSprites)
            {
                return;
            }

            var sprite = visuals.GetDeathEffectSprite(frameIndex);
            if (sprite == null)
            {
                ClearDeathEffect();
                return;
            }

            deathEffectRenderer.sprite = sprite;
            var color = baseDeathEffectColor;
            color.a *= Mathf.Lerp(1f, 0.18f, (frameIndex + 1f) / AttackerVisualDefinition.DeathFrameCount);
            deathEffectRenderer.color = color;
            deathEffectRenderer.enabled = true;
        }

        private void ClearDeathEffect()
        {
            if (deathEffectRenderer == null)
            {
                return;
            }

            deathEffectRenderer.color = baseDeathEffectColor;
            deathEffectRenderer.enabled = false;
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
