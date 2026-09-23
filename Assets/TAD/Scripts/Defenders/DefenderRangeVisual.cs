using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Draws a reusable, translucent world-space circle for a defender's current attack range.
    /// The generated sprite is shared by all range indicators to avoid per-defender textures.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DefenderRangeVisual : MonoBehaviour
    {
        private const int CircleTextureResolution = 64;
        private const float CirclePixelsPerUnit = CircleTextureResolution * 0.5f;

        private static Sprite sharedCircleSprite;

        [SerializeField] private SpriteRenderer visual;
        [SerializeField, Min(0f)] private float worldRadius;
        [SerializeField] private Color tint = new(1f, 0.08f, 0.08f, 0.5f);
        [SerializeField] private int sortingLayerId;
        [SerializeField] private int sortingOrder;

        private bool isVisible;

        public float WorldRadius => worldRadius;
        public bool IsVisible => isVisible;

        private void Awake()
        {
            ResolveRenderer();
            RefreshVisual();
        }

        private void OnEnable()
        {
            RefreshVisual();
        }

        /// <summary>Sets the world-space radius, rendering order, and visibility for this indicator.</summary>
        public void Configure(float radius, int layerId, int order, bool shouldBeVisible)
        {
            worldRadius = Mathf.Max(0f, radius);
            sortingLayerId = layerId;
            sortingOrder = order;
            isVisible = shouldBeVisible;
            ResolveRenderer();
            RefreshVisual();
        }

        /// <summary>Shows or hides the existing range circle without changing its configured radius.</summary>
        public void SetVisible(bool shouldBeVisible)
        {
            isVisible = shouldBeVisible;
            ResolveRenderer();
            UpdateRendererVisibility();
        }

        private void ResolveRenderer()
        {
            if (visual == null)
            {
                visual = GetComponent<SpriteRenderer>();
            }
        }

        private void RefreshVisual()
        {
            if (visual == null)
            {
                return;
            }

            visual.sprite = GetSharedCircleSprite();
            visual.color = tint;
            visual.sortingLayerID = sortingLayerId;
            visual.sortingOrder = sortingOrder;

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            var parentScale = transform.parent == null
                ? 1f
                : Mathf.Max(
                    Mathf.Abs(transform.parent.lossyScale.x),
                    Mathf.Abs(transform.parent.lossyScale.y));
            transform.localScale = parentScale > Mathf.Epsilon
                ? Vector3.one * (worldRadius / parentScale)
                : Vector3.zero;

            UpdateRendererVisibility();
        }

        private void UpdateRendererVisibility()
        {
            if (visual != null)
            {
                visual.enabled = isVisible && worldRadius > 0f;
            }
        }

        private static Sprite GetSharedCircleSprite()
        {
            if (sharedCircleSprite != null)
            {
                return sharedCircleSprite;
            }

            var texture = new Texture2D(
                CircleTextureResolution,
                CircleTextureResolution,
                TextureFormat.RGBA32,
                mipChain: false)
            {
                name = "TAD Defender Range Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[CircleTextureResolution * CircleTextureResolution];
            var centre = (CircleTextureResolution - 1) * 0.5f;
            var radius = CircleTextureResolution * 0.5f;
            for (var y = 0; y < CircleTextureResolution; y++)
            {
                for (var x = 0; x < CircleTextureResolution; x++)
                {
                    var horizontal = (x - centre) / radius;
                    var vertical = (y - centre) / radius;
                    var distance = Mathf.Sqrt((horizontal * horizontal) + (vertical * vertical));
                    var alpha = Mathf.Clamp01((1f - distance) * 2f);
                    pixels[(y * CircleTextureResolution) + x] = new Color32(
                        255,
                        255,
                        255,
                        (byte)Mathf.RoundToInt(alpha * byte.MaxValue));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            sharedCircleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, CircleTextureResolution, CircleTextureResolution),
                new Vector2(0.5f, 0.5f),
                CirclePixelsPerUnit,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
            sharedCircleSprite.name = "TAD Defender Range Circle";
            sharedCircleSprite.hideFlags = HideFlags.DontSave;
            return sharedCircleSprite;
        }
    }
}