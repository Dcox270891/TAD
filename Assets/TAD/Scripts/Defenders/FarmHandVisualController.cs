using TAD.Pooling;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("fallbackMovementFrameDuration")]
        [SerializeField, Min(0.01f)]
        [Tooltip("How long the farmhand briefly shifts into a newly selected aim pose.")]
        private float aimTransitionDuration = 0.08f;

        private FarmHand farmHand;
        private float aimElapsed;
        private float firingElapsed;
        private int directionIndex;
        private bool isFiring;
        private bool isAiming;
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

            if (isAiming)
            {
                aimElapsed += Time.deltaTime;
                if (aimElapsed >= aimTransitionDuration)
                {
                    isAiming = false;
                }
            }

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

            var reloadingWeaponVisual = GetWeaponVisual(farmHand.WeaponDefinition);
            if (farmHand.IsReloading && reloadingWeaponVisual != null && reloadingWeaponVisual.HasReloadSprites)
            {
                SetSprite(weaponRenderer, reloadingWeaponVisual.GetReloadSprite(directionIndex, farmHand.ReloadProgress));
                return;
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
            aimElapsed = 0f;
            isAiming = false;
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
            isAiming = false;
            aimElapsed = 0f;
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

            aimElapsed = 0f;
            isAiming = true;
            firingElapsed = 0f;
            var weaponVisual = GetWeaponVisual(weapon);
            isFiring = weaponVisual != null && weaponVisual.HasFiringSprites;
            RefreshAllLayers();
        }

        private void HandleWeaponChanged(WeaponDefinition weapon)
        {
            isFiring = false;
            firingElapsed = 0f;
            aimElapsed = 0f;
            isAiming = false;
            RefreshAllLayers();
        }

        private void ResetPresentation()
        {
            aimElapsed = 0f;
            isAiming = false;
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
            var frameIndex = GetAimFrameIndex();
            var bodySpriteChanged = SetSprite(bodyRenderer, appearance != null && appearance.Body != null
                ? appearance.Body.GetMovementSprite(directionIndex, frameIndex)
                : bodyRenderer != null ? bodyRenderer.sprite : null);
            SetSprite(hairRenderer, appearance != null && appearance.Hair != null
                ? appearance.Hair.GetMovementSprite(directionIndex, frameIndex)
                : null);
            SetSprite(outfitRenderer, appearance != null && appearance.Outfit != null
                ? appearance.Outfit.GetMovementSprite(directionIndex, frameIndex)
                : null);

            // The body is the defender's placement footprint. Rebuild it only when the visible
            // body actually changes, avoiding polygon work during ordinary aiming frames.
            if (bodySpriteChanged)
            {
                farmHand?.SynchronizePlacementFootprint();
            }
        }

        private void RefreshHeldWeapon()
        {
            SetSprite(weaponRenderer, GetHeldWeaponSprite());
        }

        private Sprite GetBodySprite()
        {
            var frameIndex = GetAimFrameIndex();
            return appearance != null && appearance.Body != null
                ? appearance.Body.GetMovementSprite(directionIndex, frameIndex)
                : bodyRenderer != null ? bodyRenderer.sprite : null;
        }

        private Sprite GetHairSprite()
        {
            return appearance != null && appearance.Hair != null
                ? appearance.Hair.GetMovementSprite(directionIndex, GetAimFrameIndex())
                : null;
        }

        private Sprite GetOutfitSprite()
        {
            return appearance != null && appearance.Outfit != null
                ? appearance.Outfit.GetMovementSprite(directionIndex, GetAimFrameIndex())
                : null;
        }

        private Sprite GetHeldWeaponSprite()
        {
            var weaponVisual = GetWeaponVisual(farmHand != null ? farmHand.WeaponDefinition : null);
            return weaponVisual != null
                ? weaponVisual.GetMovementSprite(directionIndex, GetAimFrameIndex())
                : null;
        }

        private int GetAimFrameIndex()
        {
            return isAiming ? FarmHandDirectionalLayerDefinition.MovementFrameCount - 1 : 0;
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

            // Final evolutions inherit the proven directional presentation of their exact level-five
            // parent until bespoke artwork is authored. This keeps a newly selected Rake, Shovel,
            // Heavy Roll, Bounce, Semi Automatic, Pistol, Poison Trapper, or Slow Trapper visible
            // and firing immediately rather than hiding the held-weapon layer.
            if (weapon.RequiredPreviousWeapon != null && weapon.RequiredPreviousWeapon != weapon)
            {
                return GetWeaponVisual(weapon.RequiredPreviousWeapon);
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

        private static bool SetSprite(SpriteRenderer renderer, Sprite sprite)
        {
            if (renderer == null)
            {
                return false;
            }

            renderer.enabled = sprite != null;
            if (sprite != null && renderer.sprite != sprite)
            {
                renderer.sprite = sprite;
                return true;
            }

            return false;
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
            aimTransitionDuration = Mathf.Max(0.01f, aimTransitionDuration);
            ResolveReferences();
        }
#endif
    }
}
