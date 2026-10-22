using System;
using System.Collections.Generic;
using UnityEngine;

namespace TAD.Navigation
{
    /// <summary>
    /// An authored, non-physical boundary for one farm's playable ground.
    ///
    /// The area deliberately does not use a collider: it constrains navigation and placement
    /// without becoming another wall that can change projectile line-of-sight or flying rules.
    /// Points are stored in this transform's local space so an area remains valid if its level
    /// root is repositioned. <see cref="FarmRouteMapper"/> combines this boundary with the
    /// current camera rectangle, so no actor can route through the unused space around a level.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmPlayableArea : MonoBehaviour
    {
        private const float BoundaryEpsilon = 0.0001f;

        [SerializeField]
        [Tooltip("Clockwise or counter-clockwise vertices of the intended playable ground, in this object's local space. The polygon must not self-intersect.")]
        private Vector2[] localBoundary = Array.Empty<Vector2>();

        private Vector2[] worldBoundary = Array.Empty<Vector2>();
        private Rect worldBounds;
        private Matrix4x4 cachedLocalToWorld;
        private bool hasCachedMatrix;
        private bool boundaryCacheDirty = true;

        /// <summary>True when this component has an authored polygon with at least three vertices.</summary>
        public bool HasBoundary
        {
            get
            {
                EnsureWorldBoundary();
                return worldBoundary.Length >= 3;
            }
        }

        /// <summary>The axis-aligned world bounds of the authored polygon.</summary>
        public Rect WorldBounds
        {
            get
            {
                EnsureWorldBoundary();
                return worldBounds;
            }
        }

        /// <summary>
        /// Replaces the authored polygon. This is intentionally explicit rather than exposing a
        /// mutable array so editor builders can update saved scenes without relying on YAML edits.
        /// </summary>
        public void SetLocalBoundary(IReadOnlyList<Vector2> boundary)
        {
            if (boundary == null || boundary.Count < 3)
            {
                localBoundary = Array.Empty<Vector2>();
                InvalidateBoundaryCache();
                return;
            }

            localBoundary = new Vector2[boundary.Count];
            for (var index = 0; index < boundary.Count; index++)
            {
                localBoundary[index] = boundary[index];
            }

            InvalidateBoundaryCache();
        }

        /// <summary>Copies the local-space polygon for diagnostics and editor validation.</summary>
        public int CopyLocalBoundary(List<Vector2> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (localBoundary != null)
            {
                results.AddRange(localBoundary);
            }

            return results.Count;
        }

        /// <summary>Copies the current world-space polygon for diagnostics and editor validation.</summary>
        public int CopyWorldBoundary(List<Vector2> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            EnsureWorldBoundary();
            results.Clear();
            results.AddRange(worldBoundary);
            return results.Count;
        }

        /// <summary>Returns whether a point lies inside (or directly on) the authored ground.</summary>
        public bool ContainsPoint(Vector2 worldPosition)
        {
            EnsureWorldBoundary();
            return ContainsPointInternal(worldPosition);
        }

        /// <summary>
        /// Returns whether a circle can fit entirely inside the authored ground. This is used for
        /// attacker sizes, defender footprints, and terrain placement so their visible footprint
        /// cannot spill outside the farm even if their centre is still inside it.
        /// </summary>
        public bool ContainsCircle(Vector2 worldPosition, float radius)
        {
            EnsureWorldBoundary();
            if (worldBoundary.Length < 3)
            {
                return false;
            }

            var safeRadius = Mathf.Max(0f, radius);
            if (!ContainsPointInternal(worldPosition))
            {
                return false;
            }

            if (safeRadius <= BoundaryEpsilon)
            {
                return true;
            }

            var expandedBounds = new Rect(
                worldBounds.xMin + safeRadius,
                worldBounds.yMin + safeRadius,
                Mathf.Max(0f, worldBounds.width - (safeRadius * 2f)),
                Mathf.Max(0f, worldBounds.height - (safeRadius * 2f)));
            if (!expandedBounds.Contains(worldPosition))
            {
                return false;
            }

            var requiredDistanceSquared = safeRadius * safeRadius;
            for (var index = 0; index < worldBoundary.Length; index++)
            {
                var nextIndex = (index + 1) % worldBoundary.Length;
                if (DistanceSquaredToSegment(worldPosition, worldBoundary[index], worldBoundary[nextIndex]) + BoundaryEpsilon < requiredDistanceSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private void Awake()
        {
            InvalidateBoundaryCache();
        }

        private void OnValidate()
        {
            InvalidateBoundaryCache();
        }

        private void InvalidateBoundaryCache()
        {
            boundaryCacheDirty = true;
            hasCachedMatrix = false;
        }

        private void EnsureWorldBoundary()
        {
            var currentMatrix = transform.localToWorldMatrix;
            if (!boundaryCacheDirty && hasCachedMatrix && MatricesEqual(cachedLocalToWorld, currentMatrix))
            {
                return;
            }

            boundaryCacheDirty = false;
            hasCachedMatrix = true;
            cachedLocalToWorld = currentMatrix;
            if (localBoundary == null || localBoundary.Length < 3)
            {
                worldBoundary = Array.Empty<Vector2>();
                worldBounds = default;
                return;
            }

            if (worldBoundary.Length != localBoundary.Length)
            {
                worldBoundary = new Vector2[localBoundary.Length];
            }

            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var index = 0; index < localBoundary.Length; index++)
            {
                var worldPoint = currentMatrix.MultiplyPoint3x4(localBoundary[index]);
                worldBoundary[index] = worldPoint;
                minimum = Vector2.Min(minimum, worldPoint);
                maximum = Vector2.Max(maximum, worldPoint);
            }

            worldBounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        private bool ContainsPointInternal(Vector2 worldPosition)
        {
            if (worldBoundary.Length < 3 || !worldBounds.Contains(worldPosition))
            {
                // Rect.Contains excludes its maximum edges, while a polygon boundary is valid.
                if (worldBoundary.Length < 3 ||
                    worldPosition.x < worldBounds.xMin - BoundaryEpsilon ||
                    worldPosition.x > worldBounds.xMax + BoundaryEpsilon ||
                    worldPosition.y < worldBounds.yMin - BoundaryEpsilon ||
                    worldPosition.y > worldBounds.yMax + BoundaryEpsilon)
                {
                    return false;
                }
            }

            var inside = false;
            for (var index = 0; index < worldBoundary.Length; index++)
            {
                var nextIndex = (index + 1) % worldBoundary.Length;
                var start = worldBoundary[index];
                var end = worldBoundary[nextIndex];
                if (DistanceSquaredToSegment(worldPosition, start, end) <= BoundaryEpsilon * BoundaryEpsilon)
                {
                    return true;
                }

                var crossesHorizontalRay = (start.y > worldPosition.y) != (end.y > worldPosition.y);
                if (!crossesHorizontalRay)
                {
                    continue;
                }

                var intersectionX = start.x + ((worldPosition.y - start.y) * (end.x - start.x) / (end.y - start.y));
                if (intersectionX > worldPosition.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= 0.0000001f)
            {
                return (point - start).sqrMagnitude;
            }

            var fraction = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSquared);
            return (point - (start + (segment * fraction))).sqrMagnitude;
        }

        private static bool MatricesEqual(Matrix4x4 left, Matrix4x4 right)
        {
            for (var index = 0; index < 16; index++)
            {
                if (!Mathf.Approximately(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            EnsureWorldBoundary();
            if (worldBoundary.Length < 3)
            {
                return;
            }

            Gizmos.color = new Color(0.12f, 0.9f, 0.35f, 0.9f);
            for (var index = 0; index < worldBoundary.Length; index++)
            {
                Gizmos.DrawLine(worldBoundary[index], worldBoundary[(index + 1) % worldBoundary.Length]);
            }
        }
    }
}
