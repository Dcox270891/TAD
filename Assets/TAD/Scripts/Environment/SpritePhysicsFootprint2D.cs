using System.Collections.Generic;
using UnityEngine;

namespace TAD.Environment
{
    /// <summary>
    /// Copies Unity's generated sprite physics outline into the gameplay collider that owns the
    /// corresponding sprite.  This keeps sprite changes, display scale, and interaction areas in
    /// step without hand-authoring a second silhouette for each asset.
    /// </summary>
    public static class SpritePhysicsFootprint2D
    {
        // Unity collider and sprite APIs are main-thread-only. Reusing these buffers avoids garbage
        // every time an animated attacker changes its displayed movement frame.
        private static readonly List<Vector2> SourcePathBuffer = new();
        private static readonly List<Vector2> ConvertedPathBuffer = new();

        /// <summary>
        /// Rebuilds a polygon collider from every generated physics path on the supplied sprite.
        /// When an imported sprite has no generated path, its visible sprite bounds provide a safe
        /// rectangular fallback rather than leaving a stale collider behind.
        /// </summary>
        public static bool SynchronizePolygonCollider(PolygonCollider2D collider, SpriteRenderer visual)
        {
            if (collider == null || visual == null || visual.sprite == null)
            {
                return false;
            }

            var sprite = visual.sprite;
            var pathCount = sprite.GetPhysicsShapeCount();
            if (pathCount <= 0)
            {
                collider.pathCount = 1;
                collider.SetPath(0, CreateBoundsPath(collider.transform, visual));
                return true;
            }

            collider.pathCount = pathCount;
            for (var pathIndex = 0; pathIndex < pathCount; pathIndex++)
            {
                SourcePathBuffer.Clear();
                ConvertedPathBuffer.Clear();
                sprite.GetPhysicsShape(pathIndex, SourcePathBuffer);
                if (SourcePathBuffer.Count < 3)
                {
                    collider.pathCount = 1;
                    collider.SetPath(0, CreateBoundsPath(collider.transform, visual));
                    return true;
                }

                foreach (var point in SourcePathBuffer)
                {
                    var worldPoint = visual.transform.TransformPoint(point);
                    ConvertedPathBuffer.Add(collider.transform.InverseTransformPoint(worldPoint));
                }

                collider.SetPath(pathIndex, ConvertedPathBuffer);
            }

            return true;
        }

        private static Vector2[] CreateBoundsPath(Transform colliderTransform, SpriteRenderer visual)
        {
            var bounds = visual.sprite.bounds;
            return new[]
            {
                ToColliderPoint(colliderTransform, visual, new Vector2(bounds.min.x, bounds.min.y)),
                ToColliderPoint(colliderTransform, visual, new Vector2(bounds.min.x, bounds.max.y)),
                ToColliderPoint(colliderTransform, visual, new Vector2(bounds.max.x, bounds.max.y)),
                ToColliderPoint(colliderTransform, visual, new Vector2(bounds.max.x, bounds.min.y))
            };
        }

        private static Vector2 ToColliderPoint(Transform colliderTransform, SpriteRenderer visual, Vector2 spritePoint)
        {
            return colliderTransform.InverseTransformPoint(visual.transform.TransformPoint(spritePoint));
        }
    }
}
