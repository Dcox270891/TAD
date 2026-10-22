using TAD.Environment;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Presents a Food Store's remaining supply as a screen-space HUD bar.
    /// The Food Store remains the sole owner of its food state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoodStoreHud : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Image backgroundImage;
        [SerializeField] private UnityEngine.UI.Image fillImage;
        [SerializeField] private UnityEngine.UI.Text amountText;
        [SerializeField] private Sprite squareFillSprite;
        [SerializeField] private Sprite statusBarFrameSprite;
        [SerializeField] private Color depletedColor = new(0.88f, 0.18f, 0.16f, 1f);
        [SerializeField] private Color fullColor = new(0.18f, 0.78f, 0.28f, 1f);

        private const string LegacyOutlineFrameName = "ThinWoodOutlineFrame";
        // The new status frame already draws its own recessed edge. The old large offsets left
        // visible black gaps at both ends of the food fill rather than reading like a health bar.
        private const float BarHorizontalInset = 6f;
        private const float BarVerticalInset = 6f;
        private const float LabelHorizontalInset = 18f;
        private const float LabelVerticalInset = 6f;

        private FoodStore foodStore;
        private bool isSubscribed;
        private static Sprite runtimeFallbackSquareFillSprite;

        private void Awake()
        {
            ConfigureSquareBarGeometry();
            ConfigureLeftToRightFill();
            ConfigureEmptyBar();
        }

        /// <summary>Binds this display to the supplied Food Store and immediately presents its current state.</summary>
        public void Bind(FoodStore source)
        {
            if (foodStore == source)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            foodStore = source;
            Subscribe();
            Refresh();
        }

        /// <summary>
        /// Assigns the opaque square sprite used by the horizontal fill. A Filled uGUI Image
        /// needs a real sprite; a sprite-less image silently renders no coloured portion.
        /// This is called by the UI asset builder so every authored HUD uses the same fill.
        /// </summary>
        public void ConfigureSquareFillSprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            squareFillSprite = sprite;
            ConfigureSquareBarGeometry();
            ConfigureLeftToRightFill();
            ConfigureEmptyBar();
        }

        /// <summary>
        /// Applies the shared recessed wooden frame and softly highlighted tintable fill. It is
        /// intentionally separate from food state so the HUD remains authoritative only for its
        /// presentation.
        /// </summary>
        public void ConfigureFarmStatusBar(Sprite frameSprite, Sprite fillSprite)
        {
            statusBarFrameSprite = frameSprite;
            if (fillSprite != null)
            {
                squareFillSprite = fillSprite;
            }

            ConfigureSquareBarGeometry();
            ConfigureLeftToRightFill();
            ConfigureEmptyBar();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodChanged += HandleFoodChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodChanged -= HandleFoodChanged;
            isSubscribed = false;
        }

        private void Refresh()
        {
            if (foodStore != null)
            {
                HandleFoodChanged(foodStore.CurrentFood, foodStore.StartingFood);
            }
        }

        private void HandleFoodChanged(float currentFood, float maximumFood)
        {
            var normalizedFood = maximumFood > 0f
                ? Mathf.Clamp01(currentFood / maximumFood)
                : 0f;

            if (fillImage != null)
            {
                ConfigureLeftToRightFill();
                fillImage.fillAmount = normalizedFood;
                fillImage.color = Color.Lerp(depletedColor, fullColor, normalizedFood);
            }

            if (amountText != null)
            {
                amountText.text = $"Food store: {currentFood:0.#} / {maximumFood:0.#} ({normalizedFood:P0})";
            }
        }

        private void ConfigureLeftToRightFill()
        {
            if (fillImage == null)
            {
                return;
            }

            fillImage.type = UnityEngine.UI.Image.Type.Filled;
            fillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            fillImage.sprite = ResolveSquareFillSprite();
            fillImage.fillCenter = true;
            fillImage.preserveAspect = false;
            fillImage.raycastTarget = false;
        }

        private void ConfigureEmptyBar()
        {
            if (backgroundImage != null)
            {
                backgroundImage.sprite = statusBarFrameSprite;
                backgroundImage.type = statusBarFrameSprite != null
                    ? UnityEngine.UI.Image.Type.Sliced
                    : UnityEngine.UI.Image.Type.Simple;
                backgroundImage.fillCenter = true;
                backgroundImage.preserveAspect = false;
                backgroundImage.color = statusBarFrameSprite != null ? Color.white : Color.black;
                backgroundImage.raycastTarget = false;
            }
        }

        private void ConfigureSquareBarGeometry()
        {
            if (backgroundImage != null)
            {
                RemoveLegacyOutlineFrame();
                backgroundImage.sprite = statusBarFrameSprite;
                backgroundImage.type = statusBarFrameSprite != null
                    ? UnityEngine.UI.Image.Type.Sliced
                    : UnityEngine.UI.Image.Type.Simple;
                backgroundImage.fillCenter = true;
                backgroundImage.preserveAspect = false;
            }

            if (fillImage != null)
            {
                var fillRect = fillImage.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.pivot = new Vector2(0.5f, 0.5f);
                fillRect.offsetMin = new Vector2(BarHorizontalInset, BarVerticalInset);
                fillRect.offsetMax = new Vector2(-BarHorizontalInset, -BarVerticalInset);
                fillRect.localScale = Vector3.one;
            }

            if (amountText != null)
            {
                var textRect = amountText.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.offsetMin = new Vector2(LabelHorizontalInset, LabelVerticalInset);
                textRect.offsetMax = new Vector2(-LabelHorizontalInset, -LabelVerticalInset);
                textRect.localScale = Vector3.one;
                amountText.alignment = UnityEngine.TextAnchor.MiddleCenter;
                amountText.resizeTextForBestFit = true;
                amountText.resizeTextMinSize = 12;
                amountText.resizeTextMaxSize = Mathf.Max(12, amountText.fontSize);
                amountText.horizontalOverflow = UnityEngine.HorizontalWrapMode.Wrap;
                amountText.verticalOverflow = UnityEngine.VerticalWrapMode.Truncate;
            }
        }

        /// <summary>
        /// Earlier HUDs carried a separate thin outline under the status-bar frame. The art
        /// family now owns that border in one sprite, so remove the legacy child through Unity's
        /// object API whenever the HUD is rebuilt or initialised.
        /// </summary>
        private void RemoveLegacyOutlineFrame()
        {
            if (backgroundImage == null)
            {
                return;
            }

            var legacyFrame = backgroundImage.transform.Find(LegacyOutlineFrameName);
            if (legacyFrame == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(legacyFrame.gameObject);
                return;
            }
#endif

            legacyFrame.gameObject.SetActive(false);
            Destroy(legacyFrame.gameObject);
        }

        private Sprite ResolveSquareFillSprite()
        {
            if (squareFillSprite != null)
            {
                return squareFillSprite;
            }

            // Old scene copies can be played before their prefab is migrated by the editor
            // builder. Retain a minimal, opaque fallback so the vital food state is never
            // invisible in that situation.
            if (runtimeFallbackSquareFillSprite == null)
            {
                var texture = Texture2D.whiteTexture;
                runtimeFallbackSquareFillSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                runtimeFallbackSquareFillSprite.name = "Runtime Square UI Fill";
            }

            return runtimeFallbackSquareFillSprite;
        }
    }
}
