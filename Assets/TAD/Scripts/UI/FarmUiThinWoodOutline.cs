using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Preserves a fine, brass-free timber outline on a uGUI image. The source sprite is
    /// nine-sliced so the trim stays narrow on compact controls and does not thicken on
    /// larger information cards.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class FarmUiThinWoodOutline : MonoBehaviour
    {
        public const string FrameObjectName = "ThinWoodOutlineFrame";

        [SerializeField] private Sprite outlineSprite;

        public Sprite OutlineSprite => outlineSprite;

        /// <summary>
        /// Configures the reusable outline from an editor asset builder. The same component is
        /// also safe to add to a runtime-created control when a sprite has been assigned.
        /// </summary>
        public void ConfigureForAuthoring(Sprite sprite)
        {
            Configure(sprite);
        }

        /// <summary>
        /// Assigns the shared frame artwork and immediately refreshes this image. This is safe
        /// for both editor-authored controls and UI created while a match is running.
        /// </summary>
        public void Configure(Sprite sprite)
        {
            outlineSprite = sprite;
            Apply();
        }

        /// <summary>
        /// Adds or updates a non-interactive child frame on a readable panel surface. Keeping
        /// the outline separate means panels remain able to receive clicks while the artwork
        /// itself never blocks labels, buttons, or scroll content.
        /// </summary>
        public static FarmUiThinWoodOutline EnsureFrame(Image surface, Sprite sprite)
        {
            if (surface == null || sprite == null)
            {
                return null;
            }

            var frameTransform = surface.transform.Find(FrameObjectName);
            if (frameTransform == null)
            {
                var frameObject = new GameObject(
                    FrameObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(LayoutElement));
                frameObject.layer = surface.gameObject.layer;
                frameObject.transform.SetParent(surface.transform, false);
                frameTransform = frameObject.transform;

                // Create the layout marker before adding the outline behaviour.  Awake on the
                // latter refreshes the image, and Unity does not permit AddComponent calls from
                // that lifecycle callback.
                MarkFrameLayoutNeutral(frameTransform, createIfMissing: false);
                frameObject.AddComponent<FarmUiThinWoodOutline>();
            }

            var frameRect = frameTransform as RectTransform;
            if (frameRect != null)
            {
                frameRect.anchorMin = Vector2.zero;
                frameRect.anchorMax = Vector2.one;
                frameRect.pivot = new Vector2(0.5f, 0.5f);
                frameRect.anchoredPosition = Vector2.zero;
                frameRect.sizeDelta = Vector2.zero;
                frameRect.localScale = Vector3.one;
            }

            var frameImage = frameTransform.GetComponent<Image>();
            if (frameImage == null)
            {
                frameImage = frameTransform.gameObject.AddComponent<Image>();
            }

            frameImage.raycastTarget = false;

            // Existing author-time frames are normalised by FarmUiSkin after the canvas has
            // started.  Do not create components here: this helper is also used by UI that is
            // constructed from Awake callbacks.
            KeepFrameOutOfLayouts(frameTransform);
            var outline = frameTransform.GetComponent<FarmUiThinWoodOutline>();
            if (outline == null)
            {
                outline = frameTransform.gameObject.AddComponent<FarmUiThinWoodOutline>();
            }

            outline.Configure(sprite);
            frameTransform.SetAsLastSibling();
            return outline;
        }

        public void Apply()
        {
            // Existing authored frames can have their sprite refreshed through the outline
            // component directly, without passing through EnsureFrame. Keep those frames
            // layout-neutral as well so the guarantee applies to every menu and HUD panel.
            KeepFrameOutOfLayouts(transform);
            ApplyTo(GetComponent<Image>());
        }

        /// <summary>
        /// Makes an existing outline layout-neutral after its canvas has started. This is kept
        /// separate from Apply so OnValidate/Awake never try to add a component.
        /// </summary>
        public static void EnsureLayoutNeutral(Transform frameTransform)
        {
            MarkFrameLayoutNeutral(frameTransform, createIfMissing: true);
        }

        private static void KeepFrameOutOfLayouts(Transform frameTransform)
        {
            MarkFrameLayoutNeutral(frameTransform, createIfMissing: false);
        }

        private static void MarkFrameLayoutNeutral(Transform frameTransform, bool createIfMissing)
        {
            if (frameTransform == null || frameTransform.name != FrameObjectName)
            {
                return;
            }

            // Decorative frames are children of their panel. Mark the frame as layout-neutral so
            // a Vertical/HorizontalLayoutGroup never gives it a content slot (which previously
            // made a frame push labels outside compact HUD and menu panels).
            var frameLayout = frameTransform.GetComponent<LayoutElement>();
            if (frameLayout == null && createIfMissing)
            {
                frameLayout = frameTransform.gameObject.AddComponent<LayoutElement>();
            }

            if (frameLayout != null)
            {
                frameLayout.ignoreLayout = true;
            }
        }

        public void ApplyTo(Image targetImage)
        {
            if (targetImage == null || outlineSprite == null)
            {
                return;
            }

            targetImage.sprite = outlineSprite;
            targetImage.type = Image.Type.Sliced;
            targetImage.fillCenter = false;
            targetImage.preserveAspect = false;
            targetImage.color = Color.white;
        }

        private void Awake()
        {
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Apply();
        }
#endif
    }
}
