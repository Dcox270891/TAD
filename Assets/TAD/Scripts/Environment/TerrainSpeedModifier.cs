using UnityEngine;

namespace TAD.Environment
{
    /// <summary>Identifies the floor surface that changes a ground attacker's movement speed.</summary>
    public enum TerrainSpeedEffect
    {
        Mud,
        Ice
    }

    /// <summary>
    /// Configures a trigger-only floor area that changes the speed of ground attackers standing on it.
    /// The moving attacker owns the transient overlap state, which keeps this component reusable and
    /// ensures pooled attackers always reset cleanly. Flying attackers deliberately ignore this surface.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PolygonCollider2D))]
    public sealed class TerrainSpeedModifier : MonoBehaviour
    {
        public const float DefaultMudMovementMultiplier = 0.75f;
        public const float IceMovementMultiplier = 1.1f;

        [SerializeField]
        [Tooltip("Mud slows ground attackers. Ice speeds ground attackers up by 10 percent.")]
        private TerrainSpeedEffect terrainType = TerrainSpeedEffect.Mud;

        [SerializeField, Range(0.05f, 0.99f)]
        [Tooltip("Ground movement multiplier for this mud tile. A value of 0.75 is 25 percent slower.")]
        private float mudMovementMultiplier = DefaultMudMovementMultiplier;

        [SerializeField]
        [Tooltip("The visible ground patch used to build this surface's gameplay footprint.")]
        private SpriteRenderer footprintVisual;

        private PolygonCollider2D terrainCollider;

        public TerrainSpeedEffect TerrainType => terrainType;
        public float MudMovementMultiplier => mudMovementMultiplier;
        public float MovementSpeedMultiplier => terrainType == TerrainSpeedEffect.Ice
            ? IceMovementMultiplier
            : Mathf.Clamp(mudMovementMultiplier, 0.05f, 0.99f);

        /// <summary>
        /// World-space footprint used by the route mapper when it evaluates terrain-aware paths.
        /// It intentionally matches the same trigger collider used by moving attackers.
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                EnsureTriggerCollider();
                return terrainCollider != null ? terrainCollider.bounds : new Bounds(transform.position, Vector3.zero);
            }
        }

        /// <summary>
        /// Returns whether this active terrain trigger affects the supplied world position. Route mapping
        /// calls this only while rebuilding its cached fields, never from a per-frame AI loop.
        /// </summary>
        public bool ContainsWorldPoint(Vector2 worldPosition)
        {
            EnsureTriggerCollider();
            return isActiveAndEnabled && terrainCollider != null && terrainCollider.enabled &&
                   terrainCollider.OverlapPoint(worldPosition);
        }

        private void Awake()
        {
            EnsureTriggerCollider();
            SynchronizeTriggerToVisibleFootprint();
        }

        /// <summary>Configures a generated or runtime-authored terrain tile without changing its authored footprint.</summary>
        public void Configure(TerrainSpeedEffect type, float mudMultiplier = DefaultMudMovementMultiplier)
        {
            terrainType = type;
            mudMovementMultiplier = Mathf.Clamp(mudMultiplier, 0.05f, 0.99f);
            EnsureTriggerCollider();
        }

        /// <summary>
        /// Copies the visible sprite's generated physics outline into the terrain trigger. This makes
        /// the gameplay effect follow the painted ice or mud shape rather than an oversized rectangle.
        /// </summary>
        [ContextMenu("Synchronize Trigger To Visible Footprint")]
        public void SynchronizeTriggerToVisibleFootprint()
        {
            EnsureTriggerCollider();
            footprintVisual ??= FindFootprintVisual();
            if (terrainCollider == null || footprintVisual == null || footprintVisual.sprite == null)
            {
                return;
            }

            if (SpritePhysicsFootprint2D.SynchronizePolygonCollider(terrainCollider, footprintVisual))
            {
                terrainCollider.isTrigger = true;
            }
        }

        private void EnsureTriggerCollider()
        {
            terrainCollider ??= GetComponent<PolygonCollider2D>();
            if (terrainCollider != null)
            {
                terrainCollider.isTrigger = true;
            }
        }

        private SpriteRenderer FindFootprintVisual()
        {
            foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer != null && renderer.enabled && renderer.sprite != null)
                {
                    return renderer;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            mudMovementMultiplier = Mathf.Clamp(mudMovementMultiplier, 0.05f, 0.99f);
            terrainCollider = GetComponent<PolygonCollider2D>();
            footprintVisual ??= FindFootprintVisual();
            SynchronizeTriggerToVisibleFootprint();
        }
#endif
    }
}
