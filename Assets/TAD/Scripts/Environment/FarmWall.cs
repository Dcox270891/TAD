using System.Collections.Generic;
using UnityEngine;

namespace TAD.Environment
{
    public enum FarmWallCover
    {
        Half,
        Full
    }

    /// <summary>
    /// Describes the tactical cover and passable opening of one reusable farm wall.
    /// Pathfinding queries this component rather than inferring rules from wall art or names.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class FarmWall : MonoBehaviour
    {
        private const string GeneratedCollisionRootName = "Runtime Hole Collision";
        private const float HoleCollisionPadding = 0.25f;

        [SerializeField]
        [Tooltip("Half cover allows a farmhand's ranged weapon or trap to attack across it. Full cover blocks all attacks across it.")]
        private FarmWallCover cover = FarmWallCover.Full;

        [SerializeField]
        [Tooltip("Whether the wall has an opening that an animal may use for navigation.")]
        private bool hasHole;

        [SerializeField, Min(0f)]
        [Tooltip("Width of the wall opening in world units. An animal can use it only when its diameter is no greater than this value.")]
        private float holeSize;

        public FarmWallCover Cover => cover;

        public bool HasHole => hasHole;

        public float HoleSize => hasHole ? GetEffectiveHoleSize() : 0f;

        public bool BlocksAllAttacks => cover == FarmWallCover.Full;

        public bool AllowsFarmhandRangedAttack => cover == FarmWallCover.Half;

        public bool CanAnimalPassThrough(float animalDiameter)
        {
            return hasHole && animalDiameter >= 0f && animalDiameter <= HoleSize + 0.0001f;
        }

        private BoxCollider2D sourceCollider;
        private Transform generatedCollisionRoot;
        private readonly List<BoxCollider2D> collisionSegments = new();
        private bool collisionGeometryPrepared;

        private void Awake()
        {
            PrepareCollisionGeometry();
        }

        /// <summary>
        /// Creates collision segments around an opening. This runs once when the scene starts,
        /// leaving a physical gap that matches the navigation rule.
        /// </summary>
        public void PrepareCollisionGeometry()
        {
            if (collisionGeometryPrepared)
            {
                return;
            }

            sourceCollider = GetComponent<BoxCollider2D>();
            collisionSegments.Clear();

            if (sourceCollider == null)
            {
                collisionGeometryPrepared = true;
                return;
            }

            if (!hasHole || holeSize <= 0f)
            {
                sourceCollider.enabled = true;
                collisionSegments.Add(sourceCollider);
                collisionGeometryPrepared = true;
                return;
            }

            sourceCollider.enabled = false;
            generatedCollisionRoot = transform.Find(GeneratedCollisionRootName);
            if (generatedCollisionRoot == null)
            {
                var root = new GameObject(GeneratedCollisionRootName);
                generatedCollisionRoot = root.transform;
                generatedCollisionRoot.SetParent(transform, false);
            }

            var worldSize = GetWorldColliderSize();
            var splitAlongX = worldSize.x >= worldSize.y;
            var localAxisLength = splitAlongX ? sourceCollider.size.x : sourceCollider.size.y;
            var worldAxisLength = splitAlongX ? worldSize.x : worldSize.y;
            // The gameplay rule still uses the authored Hole Size exactly. The small invisible collision margin
            // lets an equal-size animal follow grid cells through the opening instead of snagging on its edges.
            var openingWorldLength = Mathf.Min(holeSize + HoleCollisionPadding, worldAxisLength);
            var localOpeningLength = worldAxisLength > 0f
                ? openingWorldLength * (localAxisLength / worldAxisLength)
                : 0f;
            var remainingLength = Mathf.Max(0f, localAxisLength - localOpeningLength);

            if (remainingLength > 0.0001f)
            {
                var segmentLength = remainingLength * 0.5f;
                var offset = (localOpeningLength * 0.5f) + (segmentLength * 0.5f);
                CreateCollisionSegment("Lower Wall", splitAlongX, segmentLength, -offset);
                CreateCollisionSegment("Upper Wall", splitAlongX, segmentLength, offset);
            }

            collisionSegments.AddRange(generatedCollisionRoot.GetComponentsInChildren<BoxCollider2D>());
            collisionGeometryPrepared = true;
        }

        /// <summary>Returns whether a circle at the supplied position overlaps this wall's solid collision.</summary>
        public bool BlocksCircle(Vector2 worldPosition, float radius)
        {
            return BlocksCircleForRoute(worldPosition, radius, 0f);
        }

        /// <summary>
        /// Returns whether a circle overlaps this wall for a route whose allowed openings must be at least
        /// <paramref name="requiredHoleSize"/> wide. Passing positive infinity deliberately closes every hole,
        /// which lets the route mapper keep its all-walls baseline separate from its opening-aware routes.
        /// </summary>
        public bool BlocksCircleForRoute(Vector2 worldPosition, float radius, float requiredHoleSize)
        {
            PrepareCollisionGeometry();
            var safeRadius = Mathf.Max(0f, radius);
            var safeRadiusSquared = safeRadius * safeRadius;

            // The original collider is disabled at runtime when the wall has an opening. Navigation still needs
            // to be able to treat that same wall as solid while it builds the initial no-hole route.
            if (!CanAnimalPassThrough(requiredHoleSize))
            {
                return ColliderBlocksCircle(sourceCollider, worldPosition, safeRadiusSquared, hasHole);
            }

            foreach (var segment in collisionSegments)
            {
                if (ColliderBlocksCircle(segment, worldPosition, safeRadiusSquared))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ColliderBlocksCircle(BoxCollider2D collider, Vector2 worldPosition, float radiusSquared, bool treatDisabledColliderAsSolid = false)
        {
            if (collider == null || !collider.gameObject.activeInHierarchy || (!collider.enabled && !treatDisabledColliderAsSolid))
            {
                return false;
            }

            var closestPoint = GetClosestPointOnBox(collider, worldPosition);
            // Touching the edge of an exact-size opening is allowed; overlapping it is not.
            return (closestPoint - worldPosition).sqrMagnitude + 0.0001f < radiusSquared;
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

        private void CreateCollisionSegment(string segmentName, bool splitAlongX, float segmentLength, float axisOffset)
        {
            var segment = new GameObject(segmentName).transform;
            segment.SetParent(generatedCollisionRoot, false);
            segment.localPosition = sourceCollider.offset + (splitAlongX
                ? Vector2.right * axisOffset
                : Vector2.up * axisOffset);

            var collider = segment.gameObject.AddComponent<BoxCollider2D>();
            collider.size = splitAlongX
                ? new Vector2(segmentLength, sourceCollider.size.y)
                : new Vector2(sourceCollider.size.x, segmentLength);
            collider.isTrigger = sourceCollider.isTrigger;
            collider.sharedMaterial = sourceCollider.sharedMaterial;
        }

        private float GetEffectiveHoleSize()
        {
            sourceCollider ??= GetComponent<BoxCollider2D>();
            if (sourceCollider == null)
            {
                return holeSize;
            }

            var worldSize = GetWorldColliderSize();
            return Mathf.Min(holeSize, Mathf.Max(worldSize.x, worldSize.y));
        }

        private Vector2 GetWorldColliderSize()
        {
            var scale = transform.lossyScale;
            return new Vector2(
                Mathf.Abs(sourceCollider.size.x * scale.x),
                Mathf.Abs(sourceCollider.size.y * scale.y));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            holeSize = Mathf.Max(0f, holeSize);
            collisionGeometryPrepared = false;
        }
#endif
    }
}
