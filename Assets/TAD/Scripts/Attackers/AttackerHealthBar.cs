using TAD.Pooling;
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
        private const int BackgroundOrderOffset = 8;
        private const int FillOrderOffset = 9;

        private static Sprite sharedBarSprite;

        [SerializeField] private BasicAttacker attacker;
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private Color backgroundColor = Color.black;
        [SerializeField] private Color depletedColor = new(0.88f, 0.18f, 0.16f, 1f);
        [SerializeField] private Color fullColor = new(0.18f, 0.78f, 0.28f, 1f);

        private PolygonCollider2D hitBox;
        private SpriteRenderer visualRenderer;
        private float barWidth = MinimumBarWorldWidth;
        private bool hasVisibleFill;
        private bool isSubscribed;

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
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            SetVisible(false);
        }

        /// <summary>Refreshes the bar from the attacker's authoritative current and maximum health values.</summary>
        public void Refresh()
        {
            ResolveComponents();
            EnsureRenderers();
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

        private void HandleHealthChanged(float currentHealth, float maximumHealth)
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
            var sharedSprite = GetSharedBarSprite();

            if (backgroundRenderer != null)
            {
                backgroundRenderer.sprite = sharedSprite;
                backgroundRenderer.color = backgroundColor;
                backgroundRenderer.sortingLayerID = sortingLayerId;
                backgroundRenderer.sortingOrder = sortingOrder + BackgroundOrderOffset;
                backgroundRenderer.transform.localPosition = Vector3.zero;
                backgroundRenderer.transform.localRotation = Quaternion.identity;
                backgroundRenderer.transform.localScale = new Vector3(barWidth, BarWorldHeight, 1f);
            }

            var fillWidth = barWidth * normalizedHealth;
            hasVisibleFill = fillWidth > 0f;
            if (fillRenderer != null)
            {
                fillRenderer.sprite = sharedSprite;
                fillRenderer.color = Color.Lerp(depletedColor, fullColor, normalizedHealth);
                fillRenderer.sortingLayerID = sortingLayerId;
                fillRenderer.sortingOrder = sortingOrder + FillOrderOffset;
                fillRenderer.transform.localPosition = new Vector3(
                    (fillWidth - barWidth) * 0.5f,
                    0f,
                    -0.01f);
                fillRenderer.transform.localRotation = Quaternion.identity;
                fillRenderer.transform.localScale = new Vector3(fillWidth, BarWorldHeight, 1f);
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
