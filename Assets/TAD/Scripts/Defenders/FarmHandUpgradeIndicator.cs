using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Runtime-only presentation for a farmhand that has reached an upgrade level. It deliberately
    /// owns no gameplay state: FarmHand remains the authority for whether an upgrade is waiting.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmHand))]
    public sealed class FarmHandUpgradeIndicator : MonoBehaviour
    {
        private const float ArrowPixelsPerUnit = 32f;
        private const float ArrowSizeMultiplier = 1.2f;
        private const float PulseAmount = 0.055f;
        private const float PulseSpeed = 4.5f;
        private const float BobAmount = 0.025f;
        private const string ArrowObjectName = "Upgrade Available Arrow";

        // Kept slightly translucent so the farmhand remains legible while the arrow sits behind it.
        private static readonly Color UpgradeReadyRed = new(0.94f, 0.10f, 0.12f, 0.94f);
        private static Sprite arrowSprite;

        [Header("Presentation")]
        [Tooltip("The authored low-poly marker used in game. The small generated arrow remains as a safe fallback for older prefabs.")]
        [SerializeField] private Sprite authoredArrowSprite;

        private FarmHand farmHand;
        private SpriteRenderer farmHandVisual;
        private SpriteRenderer arrowRenderer;
        private bool isSubscribed;
        private Vector3 baseArrowLocalScale = Vector3.one;
        private Vector3 baseArrowLocalPosition;
        private bool hasBaseArrowPresentation;

        private void Awake()
        {
            ResolveReferences();
            EnsureArrowRenderer();
            Refresh();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureArrowRenderer();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            hasBaseArrowPresentation = false;
            if (arrowRenderer != null)
            {
                arrowRenderer.enabled = false;
            }
        }

        private void Update()
        {
            if (arrowRenderer == null || !arrowRenderer.enabled || !hasBaseArrowPresentation)
            {
                return;
            }

            // The restrained bob and pulse keeps a waiting upgrade obvious at mobile scale while
            // the farmhand still renders clearly over the marker.
            var phase = Time.unscaledTime * PulseSpeed;
            var scaleMultiplier = 1f + Mathf.Sin(phase) * PulseAmount;
            arrowRenderer.transform.localScale = baseArrowLocalScale * scaleMultiplier;
            arrowRenderer.transform.localPosition = baseArrowLocalPosition +
                (Vector3.up * (Mathf.Sin(phase * 0.75f) * BobAmount));
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary>Refreshes the visual from FarmHand's authoritative pending-upgrade state.</summary>
        public void Refresh()
        {
            ResolveReferences();
            EnsureArrowRenderer();
            if (arrowRenderer == null)
            {
                return;
            }

            if (farmHandVisual != null)
            {
                arrowRenderer.sortingLayerID = farmHandVisual.sortingLayerID;
                arrowRenderer.sortingOrder = farmHandVisual.sortingOrder - 1;
            }

            ConfigureArrowPresentation();
            arrowRenderer.enabled = farmHand != null && farmHand.isActiveAndEnabled &&
                                    farmHand.IsAwaitingWeaponUpgrade;
        }

        private void ResolveReferences()
        {
            farmHand ??= GetComponent<FarmHand>();
            farmHandVisual ??= farmHand != null ? farmHand.Visual : GetComponent<SpriteRenderer>();
        }

        private void Subscribe()
        {
            if (isSubscribed || farmHand == null)
            {
                return;
            }

            farmHand.ProgressionChanged += Refresh;
            farmHand.WeaponChanged += HandleWeaponChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || farmHand == null)
            {
                isSubscribed = false;
                return;
            }

            farmHand.ProgressionChanged -= Refresh;
            farmHand.WeaponChanged -= HandleWeaponChanged;
            isSubscribed = false;
        }

        private void HandleWeaponChanged(WeaponDefinition _)
        {
            Refresh();
        }

        private void EnsureArrowRenderer()
        {
            if (arrowRenderer == null)
            {
                var existingArrow = transform.Find(ArrowObjectName);
                arrowRenderer = existingArrow != null ? existingArrow.GetComponent<SpriteRenderer>() : null;
            }

            if (arrowRenderer != null || !Application.isPlaying)
            {
                return;
            }

            var arrowObject = new GameObject(ArrowObjectName, typeof(SpriteRenderer));
            arrowObject.layer = gameObject.layer;
            arrowObject.transform.SetParent(transform, worldPositionStays: false);
            arrowObject.transform.localPosition = Vector3.zero;
            arrowRenderer = arrowObject.GetComponent<SpriteRenderer>();
            arrowRenderer.enabled = false;
        }

        /// <summary>
        /// Fits the arrow to the live body artwork instead of relying on the farmhand prefab's root scale.
        /// The default prefab is intentionally very small, so a unit-scaled child arrow would otherwise be
        /// almost invisible. Keeping the indicator at the visual centre lets the defender render over it.
        /// </summary>
        private void ConfigureArrowPresentation()
        {
            if (arrowRenderer == null)
            {
                return;
            }

            arrowRenderer.sprite = authoredArrowSprite != null ? authoredArrowSprite : GetArrowSprite();
            arrowRenderer.color = UpgradeReadyRed;

            var arrowTransform = arrowRenderer.transform;
            if (farmHandVisual == null || farmHandVisual.sprite == null || arrowRenderer.sprite == null)
            {
                arrowTransform.localPosition = Vector3.zero;
                baseArrowLocalPosition = arrowTransform.localPosition;
                baseArrowLocalScale = arrowTransform.localScale;
                hasBaseArrowPresentation = true;
                return;
            }

            var arrowSize = arrowRenderer.sprite.bounds.size;
            var parentScale = transform.lossyScale;
            var parentScaleX = Mathf.Abs(parentScale.x);
            var parentScaleY = Mathf.Abs(parentScale.y);
            if (arrowSize.x <= Mathf.Epsilon || arrowSize.y <= Mathf.Epsilon ||
                parentScaleX <= Mathf.Epsilon || parentScaleY <= Mathf.Epsilon)
            {
                return;
            }

            var visualSize = farmHandVisual.bounds.size;
            var targetWorldSize = Mathf.Max(visualSize.x, visualSize.y) * ArrowSizeMultiplier;
            arrowTransform.localScale = new Vector3(
                targetWorldSize / (arrowSize.x * parentScaleX),
                targetWorldSize / (arrowSize.y * parentScaleY),
                1f);

            var localVisualCentre = transform.InverseTransformPoint(farmHandVisual.bounds.center);
            arrowTransform.localPosition = new Vector3(localVisualCentre.x, localVisualCentre.y, 0f);
            baseArrowLocalScale = arrowTransform.localScale;
            baseArrowLocalPosition = arrowTransform.localPosition;
            hasBaseArrowPresentation = true;
        }

        private static Sprite GetArrowSprite()
        {
            if (arrowSprite != null)
            {
                return arrowSprite;
            }

            const int width = 16;
            const int height = 20;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "FarmHandUpgradeReadyArrow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var clear = new Color(1f, 1f, 1f, 0f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            var white = Color.white;
            for (var y = 0; y < 11; y++)
            {
                for (var x = 6; x <= 9; x++)
                {
                    texture.SetPixel(x, y, white);
                }
            }

            for (var y = 9; y < height; y++)
            {
                var halfWidth = Mathf.Max(1, Mathf.CeilToInt((height - y) * 0.55f));
                for (var x = 8 - halfWidth; x <= 8 + halfWidth; x++)
                {
                    if (x >= 0 && x < width)
                    {
                        texture.SetPixel(x, y, white);
                    }
                }
            }

            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            arrowSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                ArrowPixelsPerUnit);
            arrowSprite.name = "FarmHandUpgradeReadyArrow";
            arrowSprite.hideFlags = HideFlags.HideAndDontSave;
            return arrowSprite;
        }
    }
}
