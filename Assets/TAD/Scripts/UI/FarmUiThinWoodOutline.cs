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
        [SerializeField] private Sprite outlineSprite;

        public Sprite OutlineSprite => outlineSprite;

        /// <summary>
        /// Configures the reusable outline from an editor asset builder. The same component is
        /// also safe to add to a runtime-created control when a sprite has been assigned.
        /// </summary>
        public void ConfigureForAuthoring(Sprite sprite)
        {
            outlineSprite = sprite;
            Apply();
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
