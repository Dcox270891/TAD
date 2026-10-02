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
                    typeof(FarmUiThinWoodOutline));
                frameObject.layer = surface.gameObject.layer;
                frameObject.transform.SetParent(surface.transform, false);
                frameTransform = frameObject.transform;
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
            ApplyTo(GetComponent<Image>());
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
