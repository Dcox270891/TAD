using UnityEngine;

namespace TAD.Environment
{
    public enum FarmWallCover
    {
        Half,
        Full
    }

    /// <summary>
    /// Describes the tactical cover and solid collision boundary of one reusable farm wall.
    /// Pathfinding queries this component rather than inferring rules from wall art or names.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class FarmWall : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Half cover allows a farmhand's ranged weapon or trap to attack across it. Full cover blocks all attacks across it.")]
        private FarmWallCover cover = FarmWallCover.Full;

        private BoxCollider2D sourceCollider;

        public FarmWallCover Cover => cover;

        public bool BlocksAllAttacks => cover == FarmWallCover.Full;

        public bool AllowsFarmhandRangedAttack => cover == FarmWallCover.Half;

        private void Awake()
        {
            EnsureSolidCollider();
        }

        /// <summary>Returns whether a circle at the supplied position overlaps this solid wall.</summary>
        public bool BlocksCircle(Vector2 worldPosition, float radius)
        {
            var collider = EnsureSolidCollider();
            if (collider == null || !collider.gameObject.activeInHierarchy)
            {
                return false;
            }

            var safeRadius = Mathf.Max(0f, radius);
            var closestPoint = GetClosestPointOnBox(collider, worldPosition);
            return (closestPoint - worldPosition).sqrMagnitude + 0.0001f < safeRadius * safeRadius;
        }

        private BoxCollider2D EnsureSolidCollider()
        {
            sourceCollider ??= GetComponent<BoxCollider2D>();
            if (sourceCollider != null && !sourceCollider.enabled)
            {
                sourceCollider.enabled = true;
            }

            return sourceCollider;
        }

        private static Vector2 GetClosestPointOnBox(BoxCollider2D collider, Vector2 worldPosition)
        {
            var localPoint = (Vector2)collider.transform.InverseTransformPoint(worldPosition) - collider.offset;
            var halfSize = collider.size * 0.5f;
            var closestLocalPoint = collider.offset + new Vector2(
                Mathf.Clamp(localPoint.x, -halfSize.x, halfSize.x),
                Mathf.Clamp(localPoint.y, -halfSize.y, halfSize.y));
            return collider.transform.TransformPoint(closestLocalPoint);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            sourceCollider = GetComponent<BoxCollider2D>();
            if (sourceCollider != null)
            {
                sourceCollider.enabled = true;
            }
        }
#endif
    }
}
