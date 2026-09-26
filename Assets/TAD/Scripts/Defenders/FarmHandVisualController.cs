using TAD.Pooling;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Keeps the root body renderer compatible with existing defender UI and selection code while
    /// rendering independent hair, outfit, and weapon layers for an editable farmhand appearance.
    /// This is presentation-only: FarmHand remains the authority for combat and target selection.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmHand), typeof(SpriteRenderer))]
    public sealed class FarmHandVisualController : MonoBehaviour, IPoolLifecycle
    {
        private const float DirectionThreshold = 0.0001f;

        [Header("Render Layers")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer hairRenderer;
        [SerializeField] private SpriteRenderer outfitRenderer;
        [SerializeField] private SpriteRenderer weaponRenderer;

        [Header("Appearance")]
        [SerializeField] private FarmHandAppearanceDefinition appearance;
        [SerializeField] private FarmHandWeaponVisualDefinition[] weaponVisuals;
        [SerializeField, Min(0.01f)] private float fallbackMovementFrameDuration = 0.16f;

        private FarmHand farmHand;
        private float movementElapsed;
        private float firingElapsed;
        private int directionIndex;
        private bool isFiring;
        private bool isSubscribed;

        /// <summary>The active modular appearance; future cosmetic systems can safely replace it at runtime.</summary>
        public FarmHandAppearanceDefinition Appearance => appearance;

        private void Awake()
        {
            ResolveReferences();
            ResetPresentation();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            ResetPresentation();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (farmHand == null)
            {
                return;
            }

            var facing = farmHand.FacingDirection;
            if (facing.sqrMagnitude > DirectionThreshold)
            {
                directionIndex = ToDirectionIndex(facing);
            }

            movementElapsed += Time.deltaTime;
            RefreshBodyAndCosmetics();

            if (isFiring)
            {
                firingElapsed += Time.deltaTime;
                var weaponVisual = GetWeaponVisual(farmHand.WeaponDefinition);
                if (weaponVisual != null && weaponVisual.HasFiringSprites)
                {
                    var frameIndex = Mathf.FloorToInt(firingElapsed / weaponVisual.FiringFrameDuration);
                    if (frameIndex < FarmHandWeaponVisualDefinition.FiringFrameCount)
                    {
                        SetSprite(weaponRenderer, weaponVisual.GetFiringSprite(directionIndex, frameIndex));
                        return;
                    }
                }

                isFiring = false;
            }

            RefreshHeldWeapon();
        }

        /// <summary>
        /// Replaces the current body, hair, and outfit profile. It intentionally does not alter
        /// the gameplay weapon, enabling a future cosmetic randomizer to use it safely.
        /// </summary>
        public void SetAppearance(FarmHandAppearanceDefinition definition)
        {
            appearance = definition;
            movementElapsed = 0f;
            RefreshAllLayers();
        }

        /// <summary>
        /// Copies this farmhand's current layered artwork to a transient world-space placement preview.
        /// The caller owns the returned hierarchy and can destroy it without affecting the real prefab.
        /// </summary>
        public bool ConfigurePlacementPreview(
            SpriteRenderer previewBody,
            Transform previewRoot,
            float opacity,
            int previewLayer,
            int bodySortingOrder)
        {
            ResolveReferences();
            if (previewBody == null || previewRoot == null)
            {
                return false;
            }

            var bodySprite = GetBodySprite();
            if (bodySprite == null)
            {
                return false;
            }

            ConfigurePreviewRenderer(
                previewBody,
                bodyRenderer,
                bodySprite,
                opacity,
                previewLayer,
                bodySortingOrder);

            CreatePreviewLayer(
                previewRoot,
                "Hair Preview",
                hairRenderer,
                GetHairSprite(),
                opacity,
                previewLayer,
                bodySortingOrder + 1);
            CreatePreviewLayer(
                previewRoot,
                "Outfit Preview",
                outfitRenderer,
                GetOutfitSprite(),
                opacity,
                previewLayer,
                bodySortingOrder + 2);
            CreatePreviewLayer(
                previewRoot,
                "Weapon Preview",
                weaponRenderer,
                GetHeldWeaponSprite(),
                opacity,
                previewLayer,
                bodySortingOrder + 3);
            return true;
        }

        void IPoolLifecycle.OnTakenFromPool()
        {
            ResolveReferences();
            ResetPresentation();
        }

        void IPoolLifecycle.OnReturnedToPool()
        {
            isFiring = false;
            firingElapsed = 0f;
        }

        private void Subscribe()
        {
            if (isSubscribed || farmHand == null)
            {
                return;
            }

            farmHand.Fired += HandleFarmHandFired;
            farmHand.WeaponChanged += HandleWeaponChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (farmHand != null)
            {
                farmHand.Fired -= HandleFarmHandFired;
                farmHand.WeaponChanged -= HandleWeaponChanged;
            }

            isSubscribed = false;
        }

        private void HandleFarmHandFired(Vector2 firingDirection, WeaponDefinition weapon)
        {
            if (firingDirection.sqrMagnitude > DirectionThreshold)
            {
                directionIndex = ToDirectionIndex(firingDirection);
            }

            movementElapsed = 0f;
            firingElapsed = 0f;
            var weaponVisual = GetWeaponVisual(weapon);
            isFiring = weaponVisual != null && weaponVisual.HasFiringSprites;
            RefreshAllLayers();
        }

        private void HandleWeaponChanged(WeaponDefinition weapon)
        {
            isFiring = false;
            firingElapsed = 0f;
            movementElapsed = 0f;
            RefreshAllLayers();
        }

        private void ResetPresentation()
        {
            movementElapsed = 0f;
            firingElapsed = 0f;
            isFiring = false;
            directionIndex = farmHand != null && farmHand.FacingDirection.sqrMagnitude > DirectionThreshold
                ? ToDirectionIndex(farmHand.FacingDirection)
                : 0;
            RefreshAllLayers();
        }

        private void RefreshAllLayers()
        {
            RefreshBodyAndCosmetics();
            RefreshHeldWeapon();
        }

        private void RefreshBodyAndCosmetics()
        {
            var frameIndex = GetMovementFrameIndex();
            SetSprite(bodyRenderer, appearance != null && appearance.Body != null
                ? appearance.Body.GetMovementSprite(directionIndex, frameIndex)
                : bodyRenderer != null ? bodyRenderer.sprite : null);
            SetSprite(hairRenderer, appearance != null && appearance.Hair != null
                ? appearance.Hair.GetMovementSprite(directionIndex, frameIndex)
                : null);
            SetSprite(outfitRenderer, appearance != null && appearance.Outfit != null
                ? appearance.Outfit.GetMovementSprite(directionIndex, frameIndex)
                : null);
        }

        private void RefreshHeldWeapon()
        {
            SetSprite(weaponRenderer, GetHeldWeaponSprite());
        }

        private Sprite GetBodySprite()
        {
            var frameIndex = GetMovementFrameIndex();
            return appearance != null && appearance.Body != null
                ? appearance.Body.GetMovementSprite(directionIndex, frameIndex)
                : bodyRenderer != null ? bodyRenderer.sprite : null;
        }

        private Sprite GetHairSprite()
        {
            return appearance != null && appearance.Hair != null
                ? appearance.Hair.GetMovementSprite(directionIndex, GetMovementFrameIndex())
                : null;
        }

        private Sprite GetOutfitSprite()
        {
            return appearance != null && appearance.Outfit != null
                ? appearance.Outfit.GetMovementSprite(directionIndex, GetMovementFrameIndex())
                : null;
        }

        private Sprite GetHeldWeaponSprite()
        {
            var weaponVisual = GetWeaponVisual(farmHand != null ? farmHand.WeaponDefinition : null);
            return weaponVisual != null
                ? weaponVisual.GetMovementSprite(directionIndex, GetMovementFrameIndex())
                : null;
        }

        private int GetMovementFrameIndex()
        {
            var duration = appearance != null && appearance.Body != null
                ? appearance.Body.MovementFrameDuration
                : fallbackMovementFrameDuration;
            return Mathf.FloorToInt(movementElapsed / Mathf.Max(0.01f, duration)) %
                   FarmHandDirectionalLayerDefinition.MovementFrameCount;
        }

        private FarmHandWeaponVisualDefinition GetWeaponVisual(WeaponDefinition weapon)
        {
            if (weaponVisuals == null || weapon == null)
            {
                return null;
            }

            foreach (var weaponVisual in weaponVisuals)
            {
                if (weaponVisual != null && weaponVisual.Weapon == weapon)
                {
                    return weaponVisual;
                }
            }

            return null;
        }

        private void ResolveReferences()
        {
            farmHand ??= GetComponent<FarmHand>();
            bodyRenderer ??= GetComponent<SpriteRenderer>();
            hairRenderer ??= transform.Find("HairVisual")?.GetComponent<SpriteRenderer>();
            outfitRenderer ??= transform.Find("OutfitVisual")?.GetComponent<SpriteRenderer>();
            weaponRenderer ??= transform.Find("WeaponVisual")?.GetComponent<SpriteRenderer>();
        }

        private static void SetSprite(SpriteRenderer renderer, Sprite sprite)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.enabled = sprite != null;
            if (sprite != null && renderer.sprite != sprite)
            {
                renderer.sprite = sprite;
            }
        }

        private static void CreatePreviewLayer(
            Transform previewRoot,
            string layerName,
            SpriteRenderer source,
            Sprite sprite,
            float opacity,
            int previewLayer,
            int sortingOrder)
        {
            if (sprite == null)
            {
                return;
            }

            var previewLayerObject = new GameObject(layerName, typeof(SpriteRenderer));
            previewLayerObject.layer = previewLayer;
            previewLayerObject.transform.SetParent(previewRoot, worldPositionStays: false);
            var renderer = previewLayerObject.GetComponent<SpriteRenderer>();
            ConfigurePreviewRenderer(renderer, source, sprite, opacity, previewLayer, sortingOrder);
        }

        private static void ConfigurePreviewRenderer(
            SpriteRenderer target,
            SpriteRenderer source,
            Sprite sprite,
            float opacity,
            int previewLayer,
            int sortingOrder)
        {
            if (target == null)
            {
                return;
            }

            target.gameObject.layer = previewLayer;
            target.sprite = sprite;
            target.sharedMaterial = source != null ? source.sharedMaterial : null;
            target.flipX = source != null && source.flipX;
            target.flipY = source != null && source.flipY;
            target.sortingLayerID = source != null ? source.sortingLayerID : 0;
            target.sortingOrder = sortingOrder;
            var color = source != null ? source.color : Color.white;
            color.a *= Mathf.Clamp01(opacity);
            target.color = color;
            target.enabled = sprite != null;
        }

        private static int ToDirectionIndex(Vector2 direction)
        {
            if (direction.sqrMagnitude <= DirectionThreshold)
            {
                return 0;
            }

            var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var index = Mathf.RoundToInt(angle / (360f / FarmHandDirectionalLayerDefinition.DirectionCount));
            return (index % FarmHandDirectionalLayerDefinition.DirectionCount +
                    FarmHandDirectionalLayerDefinition.DirectionCount) %
                   FarmHandDirectionalLayerDefinition.DirectionCount;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            fallbackMovementFrameDuration = Mathf.Max(0.01f, fallbackMovementFrameDuration);
            ResolveReferences();
        }
#endif
    }
}
