using TAD.Pooling;
using TAD.Progression;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Presents a pooled attacker's live health with a lightweight world-space bar.
    /// The bar is a sibling of the animated visual, so directional and death sprites never alter it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackerHealthBar : MonoBehaviour, IPoolLifecycle
    {
        private const float MinimumBarWorldWidth = 0.32f;
        private const float BarWidthMultiplier = 1.12f;
        private const float BarWorldHeight = 0.055f;
        private const float BarWorldPadding = 0.045f;
        private const float FrameHorizontalInset = 0.022f;
        private const float FrameVerticalInset = 0.012f;
        private const int BackgroundOrderOffset = 8;
        private const int FillOrderOffset = 9;

        private static Sprite sharedBarSprite;

        [SerializeField] private BasicAttacker attacker;
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private Sprite barFrameSprite;
        [SerializeField] private Sprite barFillSprite;
        [SerializeField] private Color backgroundColor = Color.black;
        [SerializeField] private Color depletedColor = new(0.88f, 0.18f, 0.16f, 1f);
        [SerializeField] private Color fullColor = new(0.18f, 0.78f, 0.28f, 1f);

        private PolygonCollider2D hitBox;
        private SpriteRenderer visualRenderer;
        private float barWidth = MinimumBarWorldWidth;
        private bool hasVisibleFill;
        private bool isSubscribed;
        private bool isSettingsSubscribed;
        private PlayerSettingsService settingsService;

        private void Awake()
        {
            ResolveComponents();
            EnsureRenderers();
            Refresh();
        }

        private void OnEnable()
        {
            ResolveComponents();
            EnsureRenderers();
            Subscribe();
            SubscribeToSettings();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnsubscribeFromSettings();
            SetVisible(false);
        }

        /// <summary>Refreshes the bar from the attacker's authoritative current and maximum health values.</summary>
        public void Refresh()
        {
            ResolveComponents();
            EnsureRenderers();
            if (Application.isPlaying && !PlayerSettingsService.Instance.AreAttackerHealthBarsVisible)
            {
                SetVisible(false);
                return;
            }

            if (attacker == null || !attacker.isActiveAndEnabled || !attacker.gameObject.activeInHierarchy ||
                attacker.IsDefeated || attacker.RemainingHealth <= 0f || attacker.MaximumHealth <= 0f)
            {
                SetVisible(false);
                return;
            }

            var normalizedHealth = Mathf.Clamp01(attacker.RemainingHealth / attacker.MaximumHealth);
            ConfigureTransform();
            ConfigureRenderers(normalizedHealth);
            SetVisible(true);
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            // Pool notifications happen before activation, so OnEnable performs the final visible refresh.
            Refresh();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            Unsubscribe();
            SetVisible(false);
        }

        /// <summary>Called by the UI art builder to keep pooled world bars in the same visual family as the HUD.</summary>
        public void ConfigureArtwork(Sprite frameSprite, Sprite fillSprite)
        {
            barFrameSprite = frameSprite;
            barFillSprite = fillSprite;
            Refresh();
        }

        private void ResolveComponents()
        {
            attacker ??= GetComponentInParent<BasicAttacker>(true);
            hitBox ??= attacker != null ? attacker.GetComponent<PolygonCollider2D>() : null;

            if (visualRenderer == null && attacker != null)
            {
                var visualController = attacker.GetComponentInChildren<AttackerVisualController>(true);
                visualRenderer = visualController != null
                    ? visualController.GetComponent<SpriteRenderer>()
                    : null;
            }

            backgroundRenderer ??= FindRenderer("Background");
            fillRenderer ??= FindRenderer("Fill");
        }

        private void EnsureRenderers()
        {
            backgroundRenderer ??= CreateRenderer("Background");
            fillRenderer ??= CreateRenderer("Fill");
        }

        private SpriteRenderer FindRenderer(string childName)
        {
            var child = transform.Find(childName);
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        private SpriteRenderer CreateRenderer(string childName)
        {
            var part = new GameObject(childName, typeof(SpriteRenderer));
            part.layer = gameObject.layer;
            part.transform.SetParent(transform, worldPositionStays: false);
            return part.GetComponent<SpriteRenderer>();
        }

        private void Subscribe()
        {
            if (isSubscribed || attacker == null)
            {
                return;
            }

            attacker.HealthChanged += HandleHealthChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || attacker == null)
            {
                return;
            }

            attacker.HealthChanged -= HandleHealthChanged;
            isSubscribed = false;
        }

        private void SubscribeToSettings()
        {
            if (!Application.isPlaying || isSettingsSubscribed)
            {
                return;
            }

            settingsService = PlayerSettingsService.Instance;
            settingsService.SettingsChanged += HandleSettingsChanged;
            isSettingsSubscribed = true;
        }

        private void UnsubscribeFromSettings()
        {
            if (!isSettingsSubscribed)
            {
                return;
            }

            if (settingsService != null)
            {
                settingsService.SettingsChanged -= HandleSettingsChanged;
                settingsService = null;
            }

            isSettingsSubscribed = false;
        }

        private void HandleHealthChanged(float currentHealth, float maximumHealth)
        {
            Refresh();
        }

        private void HandleSettingsChanged(PlayerProfileData profile)
        {
            Refresh();
        }

        private void ConfigureTransform()
        {
            var parentScale = attacker.transform.lossyScale;
            var horizontalScale = Mathf.Abs(parentScale.x);
            var verticalScale = Mathf.Abs(parentScale.y);
            if (horizontalScale <= Mathf.Epsilon || verticalScale <= Mathf.Epsilon)
            {
                return;
            }

            var bounds = GetAttackerBounds();
            barWidth = Mathf.Max(MinimumBarWorldWidth, bounds.size.x * BarWidthMultiplier);

            transform.localScale = new Vector3(1f / horizontalScale, 1f / verticalScale, 1f);
            transform.localRotation = Quaternion.identity;
            transform.position = new Vector3(
                attacker.transform.position.x,
                bounds.max.y + BarWorldPadding + (BarWorldHeight * 0.5f),
                attacker.transform.position.z);
        }

        private Bounds GetAttackerBounds()
        {
            var bounds = hitBox != null
                ? hitBox.bounds
                : new Bounds(attacker.transform.position, Vector3.zero);

            if (visualRenderer != null && visualRenderer.sprite != null)
            {
                bounds.Encapsulate(visualRenderer.bounds);
            }

            return bounds;
        }

        private void ConfigureRenderers(float normalizedHealth)
        {
            var visual = visualRenderer;
            var sortingLayerId = visual != null ? visual.sortingLayerID : 0;
            var sortingOrder = visual != null ? visual.sortingOrder : 0;
            var frameSprite = barFrameSprite != null ? barFrameSprite : GetSharedBarSprite();
            var fillSprite = barFillSprite != null ? barFillSprite : GetSharedBarSprite();

            if (backgroundRenderer != null)
            {
                backgroundRenderer.sprite = frameSprite;
                backgroundRenderer.color = barFrameSprite != null ? Color.white : backgroundColor;
                backgroundRenderer.sortingLayerID = sortingLayerId;
                backgroundRenderer.sortingOrder = sortingOrder + BackgroundOrderOffset;
                backgroundRenderer.transform.localPosition = Vector3.zero;
                backgroundRenderer.transform.localRotation = Quaternion.identity;
                if (barFrameSprite != null)
                {
                    ScaleSpriteRenderer(backgroundRenderer, frameSprite, barWidth, BarWorldHeight);
                }
                else
                {
                    backgroundRenderer.drawMode = SpriteDrawMode.Simple;
                    backgroundRenderer.transform.localScale = new Vector3(barWidth, BarWorldHeight, 1f);
                }
            }

            var availableFillWidth = Mathf.Max(0f, barWidth - FrameHorizontalInset * 2f);
            var fillHeight = Mathf.Max(0.005f, BarWorldHeight - FrameVerticalInset * 2f);
            var fillWidth = availableFillWidth * normalizedHealth;
            hasVisibleFill = fillWidth > 0f;
            if (fillRenderer != null)
            {
                fillRenderer.sprite = fillSprite;
                fillRenderer.color = Color.Lerp(depletedColor, fullColor, normalizedHealth);
                fillRenderer.sortingLayerID = sortingLayerId;
                fillRenderer.sortingOrder = sortingOrder + FillOrderOffset;
                fillRenderer.transform.localPosition = new Vector3(
                    -availableFillWidth * 0.5f + fillWidth * 0.5f,
                    0f,
                    -0.01f);
                fillRenderer.transform.localRotation = Quaternion.identity;
                if (barFillSprite != null)
                {
                    ScaleSpriteRenderer(fillRenderer, fillSprite, fillWidth, fillHeight);
                }
                else
                {
                    fillRenderer.drawMode = SpriteDrawMode.Simple;
                    fillRenderer.transform.localScale = new Vector3(fillWidth, fillHeight, 1f);
                }
            }
        }

        private void SetVisible(bool visible)
        {
            if (backgroundRenderer != null)
            {
                backgroundRenderer.enabled = visible;
            }

            if (fillRenderer != null)
            {
                fillRenderer.enabled = visible && hasVisibleFill;
            }
        }

        private static void ScaleSpriteRenderer(SpriteRenderer renderer, Sprite sprite, float width, float height)
        {
            if (renderer == null || sprite == null)
            {
                return;
            }

            var sourceSize = sprite.bounds.size;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.transform.localScale = new Vector3(
                width / Mathf.Max(0.0001f, sourceSize.x),
                height / Mathf.Max(0.0001f, sourceSize.y),
                1f);
        }

        private static Sprite GetSharedBarSprite()
        {
            if (sharedBarSprite != null)
            {
                return sharedBarSprite;
            }

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                name = "TAD Attacker Health Bar",
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
            sharedBarSprite.name = "TAD Attacker Health Bar";
            sharedBarSprite.hideFlags = HideFlags.DontSave;
            return sharedBarSprite;
        }
    }
}
