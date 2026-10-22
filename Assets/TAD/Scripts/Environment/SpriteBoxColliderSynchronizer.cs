using UnityEngine;
using System.Collections.Generic;

namespace TAD.Environment
{
    /// <summary>
    /// Keeps a map object's BoxCollider2D aligned to the visible sprite after an art or scale
    /// update. Walls and the food barn use it so player-visible geometry is the gameplay geometry.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SpriteBoxColliderSynchronizer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField]
        [Tooltip("Small inward inset in world units. Use zero when the full visible sprite is walk-blocking.")]
        [Min(0f)] private float inset;

        private BoxCollider2D boxCollider;
        private readonly List<Vector2> physicsPath = new();

        private void Awake()
        {
            Synchronize();
        }

        /// <summary>Recalculates the collider in this object's local space from the rendered sprite.</summary>
        public void Synchronize()
        {
            boxCollider ??= GetComponent<BoxCollider2D>();
            visual ??= transform.Find("Visual")?.GetComponent<SpriteRenderer>();
            visual ??= GetComponentInChildren<SpriteRenderer>(true);
            if (boxCollider == null || visual == null || visual.sprite == null)
            {
                return;
            }

            var minimum = Vector2.zero;
            var maximum = Vector2.zero;
            var hasPoint = false;

            // The outer texture rectangle may include transparent padding. Gameplay needs to stop
            // at the painted object itself, so derive the box from Unity's sprite physics outline
            // whenever it is available. This uses the same visual silhouette as terrain and
            // attacker collision rather than a differently-sized authoring rectangle.
            for (var pathIndex = 0; pathIndex < visual.sprite.GetPhysicsShapeCount(); pathIndex++)
            {
                physicsPath.Clear();
                visual.sprite.GetPhysicsShape(pathIndex, physicsPath);
                foreach (var spritePoint in physicsPath)
                {
                    var localPoint = (Vector2)transform.InverseTransformPoint(
                        visual.transform.TransformPoint(spritePoint));
                    if (!hasPoint)
                    {
                        minimum = maximum = localPoint;
                        hasPoint = true;
                    }
                    else
                    {
                        minimum = Vector2.Min(minimum, localPoint);
                        maximum = Vector2.Max(maximum, localPoint);
                    }
                }
            }

            if (!hasPoint)
            {
                var bounds = visual.sprite.bounds;
                var bottomLeft = transform.InverseTransformPoint(visual.transform.TransformPoint(bounds.min));
                var topRight = transform.InverseTransformPoint(visual.transform.TransformPoint(bounds.max));
                minimum = Vector2.Min(bottomLeft, topRight);
                maximum = Vector2.Max(bottomLeft, topRight);
            }
            var size = maximum - minimum;
            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon)
            {
                return;
            }

            var insetAmount = Mathf.Min(inset, Mathf.Min(size.x, size.y) * 0.49f);
            boxCollider.offset = (minimum + maximum) * 0.5f;
            boxCollider.size = new Vector2(
                Mathf.Max(0.01f, size.x - insetAmount * 2f),
                Mathf.Max(0.01f, size.y - insetAmount * 2f));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                Synchronize();
            }
        }
#endif
    }
}
