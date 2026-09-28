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
        private static Sprite arrowSprite;

        private FarmHand farmHand;
        private SpriteRenderer farmHandVisual;
        private SpriteRenderer arrowRenderer;
        private bool isSubscribed;

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
            if (arrowRenderer != null)
            {
                arrowRenderer.enabled = false;
            }
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
            if (arrowRenderer != null || !Application.isPlaying)
            {
                return;
            }

            var arrowObject = new GameObject("Upgrade Available Arrow", typeof(SpriteRenderer));
            arrowObject.layer = gameObject.layer;
            arrowObject.transform.SetParent(transform, worldPositionStays: false);
            arrowObject.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            arrowRenderer = arrowObject.GetComponent<SpriteRenderer>();
            arrowRenderer.sprite = GetArrowSprite();
            arrowRenderer.color = new Color(0.24f, 1f, 0.28f, 0.96f);
            arrowRenderer.enabled = false;
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
                name = "FarmHandUpgradeArrow",
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
            arrowSprite.name = "FarmHandUpgradeArrow";
            arrowSprite.hideFlags = HideFlags.HideAndDontSave;
            return arrowSprite;
        }
    }
}
