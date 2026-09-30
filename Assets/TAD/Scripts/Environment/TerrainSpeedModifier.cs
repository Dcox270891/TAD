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
    [RequireComponent(typeof(BoxCollider2D))]
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

        private BoxCollider2D terrainCollider;

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
        }

        /// <summary>Configures a generated or runtime-authored terrain tile without changing its collider size.</summary>
        public void Configure(TerrainSpeedEffect type, float mudMultiplier = DefaultMudMovementMultiplier)
        {
            terrainType = type;
            mudMovementMultiplier = Mathf.Clamp(mudMultiplier, 0.05f, 0.99f);
            EnsureTriggerCollider();
        }

        private void EnsureTriggerCollider()
        {
            terrainCollider ??= GetComponent<BoxCollider2D>();
            if (terrainCollider != null)
            {
                terrainCollider.isTrigger = true;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            mudMovementMultiplier = Mathf.Clamp(mudMovementMultiplier, 0.05f, 0.99f);
            terrainCollider = GetComponent<BoxCollider2D>();
            if (terrainCollider != null)
            {
                terrainCollider.isTrigger = true;
            }
        }
#endif
    }
}
