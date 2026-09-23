using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Displays a farmhand's level and current experience with a lightweight world-space bar.
    /// It derives all state from the owning FarmHand and does not receive player input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmHandExperienceBar : MonoBehaviour
    {
        private const float BarWorldWidth = 0.9f;
        private const float BarWorldHeight = 0.13f;
        private const float BarWorldVerticalOffset = -0.65f;
        private const int BackgroundOrderOffset = 1;
        private const int FillOrderOffset = 2;
        private const int LabelOrderOffset = 3;

        private static Sprite sharedBarSprite;

        [SerializeField] private FarmHand farmHand;
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private TextMesh valueText;
        [SerializeField] private Color backgroundColor = new(0f, 0f, 0f, 0.8f);
        [SerializeField] private Color fillColor = new(0.24f, 0.85f, 0.28f, 0.95f);
        [SerializeField] private Color textColor = Color.white;

        private MeshRenderer textRenderer;
        private bool isSubscribed;

        private void Awake()
        {
            ResolveComponents();
            Refresh();
        }

        private void OnEnable()
        {
            ResolveComponents();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>Assigns the prefab-owned parts used to render this farmhand's progression.</summary>
        public void Configure(
            FarmHand owner,
            SpriteRenderer background,
            SpriteRenderer fill,
            TextMesh text)
        {
            Unsubscribe();
            farmHand = owner;
            backgroundRenderer = background;
            fillRenderer = fill;
            valueText = text;
            textRenderer = valueText != null ? valueText.GetComponent<MeshRenderer>() : null;
            if (Application.isPlaying)
            {
                Subscribe();
                Refresh();
            }
        }

        /// <summary>Refreshes the bar from the farmhand's current level and experience.</summary>
        public void Refresh()
        {
            ResolveComponents();
            if (farmHand == null)
            {
                return;
            }

            var expMax = Mathf.Max(1, farmHand.ExpMax);
            var normalizedExperience = Mathf.Clamp01(farmHand.CurrentExp / expMax);
            ConfigureTransform();
            ConfigureBarRenderers(normalizedExperience);
            ConfigureTextRenderer();
        }

        private void ResolveComponents()
        {
            farmHand ??= GetComponentInParent<FarmHand>(true);
            if (valueText == null)
            {
                var labelTransform = transform.Find("Label");
                valueText = labelTransform != null
                    ? labelTransform.GetComponent<TextMesh>()
                    : GetComponentInChildren<TextMesh>(true);
            }

            textRenderer ??= valueText != null ? valueText.GetComponent<MeshRenderer>() : null;

            if (backgroundRenderer == null)
            {
                var backgroundTransform = transform.Find("Background");
                backgroundRenderer = backgroundTransform != null
                    ? backgroundTransform.GetComponent<SpriteRenderer>()
                    : null;
            }

            if (fillRenderer == null)
            {
                var fillTransform = transform.Find("Fill");
                fillRenderer = fillTransform != null
                    ? fillTransform.GetComponent<SpriteRenderer>()
                    : null;
            }
        }

        private void Subscribe()
        {
            if (!Application.isPlaying || isSubscribed || farmHand == null)
            {
                return;
            }

            farmHand.ProgressionChanged += Refresh;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || farmHand == null)
            {
                return;
            }

            farmHand.ProgressionChanged -= Refresh;
            isSubscribed = false;
        }

        private void ConfigureTransform()
        {
            var parentScale = farmHand.transform.lossyScale;
            var horizontalScale = Mathf.Abs(parentScale.x);
            var verticalScale = Mathf.Abs(parentScale.y);
            if (horizontalScale <= Mathf.Epsilon || verticalScale <= Mathf.Epsilon)
            {
                return;
            }

            transform.localPosition = new Vector3(0f, BarWorldVerticalOffset / verticalScale, 0f);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(1f / horizontalScale, 1f / verticalScale, 1f);
        }

        private void ConfigureBarRenderers(float normalizedExperience)
        {
            var visual = farmHand.Visual;
            var sortingLayerId = visual != null ? visual.sortingLayerID : 0;
            var sortingOrder = visual != null ? visual.sortingOrder : 0;

            if (backgroundRenderer != null)
            {
                backgroundRenderer.sprite = GetSharedBarSprite();
                backgroundRenderer.color = backgroundColor;
                backgroundRenderer.sortingLayerID = sortingLayerId;
                backgroundRenderer.sortingOrder = sortingOrder + BackgroundOrderOffset;
                backgroundRenderer.transform.localPosition = Vector3.zero;
                backgroundRenderer.transform.localRotation = Quaternion.identity;
                backgroundRenderer.transform.localScale = new Vector3(BarWorldWidth, BarWorldHeight, 1f);
                backgroundRenderer.enabled = true;
            }

            if (fillRenderer != null)
            {
                var fillWidth = BarWorldWidth * normalizedExperience;
                fillRenderer.sprite = GetSharedBarSprite();
                fillRenderer.color = fillColor;
                fillRenderer.sortingLayerID = sortingLayerId;
                fillRenderer.sortingOrder = sortingOrder + FillOrderOffset;
                fillRenderer.transform.localPosition = new Vector3(
                    (fillWidth - BarWorldWidth) * 0.5f,
                    0f,
                    -0.01f);
                fillRenderer.transform.localRotation = Quaternion.identity;
                fillRenderer.transform.localScale = new Vector3(fillWidth, BarWorldHeight, 1f);
                fillRenderer.enabled = fillWidth > 0f;
            }
        }

        private void ConfigureTextRenderer()
        {
            if (valueText == null)
            {
                return;
            }

            valueText.font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            valueText.text = $"Lv {farmHand.CurrentLevel}  {farmHand.CurrentExp:0.#} / {farmHand.ExpMax}";
            valueText.anchor = TextAnchor.MiddleCenter;
            valueText.alignment = TextAlignment.Center;
            valueText.fontSize = 22;
            valueText.characterSize = 0.016f;
            valueText.color = textColor;
            valueText.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            valueText.transform.localRotation = Quaternion.identity;
            valueText.transform.localScale = Vector3.one;

            if (textRenderer == null)
            {
                return;
            }

            if (valueText.font != null)
            {
                textRenderer.sharedMaterial = valueText.font.material;
            }

            var visual = farmHand.Visual;
            textRenderer.sortingLayerID = visual != null ? visual.sortingLayerID : 0;
            textRenderer.sortingOrder = (visual != null ? visual.sortingOrder : 0) + LabelOrderOffset;
            textRenderer.enabled = true;
        }

        private static Sprite GetSharedBarSprite()
        {
            if (sharedBarSprite != null)
            {
                return sharedBarSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                name = "TAD FarmHand Experience Bar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            sharedBarSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
            sharedBarSprite.name = "TAD FarmHand Experience Bar";
            sharedBarSprite.hideFlags = HideFlags.DontSave;
            return sharedBarSprite;
        }
    }
}