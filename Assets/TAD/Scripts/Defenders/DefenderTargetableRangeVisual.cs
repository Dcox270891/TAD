using TAD.Environment;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Renders the part of a Farmhand's range that can really be targeted through its current cover rules.
    /// Melee is clipped by both wall types; ranged attacks pass half cover but are clipped by full cover.
    /// The same mesh can also be configured for a moving farmhand placement preview.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DefenderTargetableRangeVisual : MonoBehaviour
    {
        private const int LineOfSightHitCapacity = 16;
        private const float WallEdgeInset = 0.015f;
        // A placement ghost can move every rendered frame. Re-casting every radial line at that
        // rate is wasteful on a touch drag, while ten visual updates per second remains smooth
        // enough for a non-authoritative placement preview.
        private const float PreviewMotionRebuildInterval = 0.1f;
        private const float PreviewMotionRebuildDistance = 0.025f;

        private static Material sharedMaterial;

        [SerializeField] private FarmHand farmHand;
        [SerializeField] private DefenderRangeVisual fallbackCircle;
        [SerializeField, Min(24)] private int radialSamples = 160;
        [SerializeField] private Color tint = new(1f, 0.08f, 0.08f, 0.42f);

        private readonly RaycastHit2D[] lineOfSightHits = new RaycastHit2D[LineOfSightHitCapacity];
        private ContactFilter2D lineOfSightFilter;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh rangeMesh;
        private Vector3[] meshVertices;
        private int[] meshTriangles;
        private int meshBufferSampleCount;
        private bool usesPreviewSettings;
        private float previewRange;
        private ProjectileType previewProjectileType;
        private int previewSortingLayerId;
        private int previewSortingOrder;
        private bool wasVisible;
        private bool lastUsedPreviewSettings;
        private float lastRange = -1f;
        private WeaponDefinition lastWeapon;
        private ProjectileType lastProjectileType;
        private int lastSortingLayerId;
        private int lastSortingOrder;
        private Vector3 lastPosition;
        private float nextPreviewMotionRebuildAt;

        private void Awake()
        {
            ResolveComponents();
            ConfigureLineOfSightFilter();
            ConfigureRenderer();
            SetVisible(false);
        }

        private void OnEnable()
        {
            ResolveComponents();
            ConfigureLineOfSightFilter();
            ConfigureRenderer();
        }

        private void LateUpdate()
        {
            RefreshPresentation(forceRebuild: false);
        }

        private void OnDestroy()
        {
            if (rangeMesh != null)
            {
                Destroy(rangeMesh);
            }
        }

        /// <summary>
        /// Uses this solid targetable-range mesh for a placement ghost instead of the fading legacy circle.
        /// The mesh follows this transform and is rebuilt only while the preview moves or its settings change.
        /// </summary>
        public void ConfigurePreview(
            float range,
            ProjectileType projectileType,
            int sortingLayerId,
            int sortingOrder)
        {
            usesPreviewSettings = true;
            previewRange = Mathf.Max(0f, range);
            previewProjectileType = projectileType;
            previewSortingLayerId = sortingLayerId;
            previewSortingOrder = sortingOrder;
            ResolveComponents();
            ConfigureLineOfSightFilter();
            RefreshPresentation(forceRebuild: true);
        }

        private void ResolveComponents()
        {
            farmHand ??= GetComponentInParent<FarmHand>();
            fallbackCircle ??= farmHand != null
                ? farmHand.GetComponentInChildren<DefenderRangeVisual>(true)
                : null;
            meshFilter ??= GetComponent<MeshFilter>();
            meshRenderer ??= GetComponent<MeshRenderer>();

            if (rangeMesh == null)
            {
                rangeMesh = new Mesh
                {
                    name = "TAD Defender Targetable Range",
                    hideFlags = HideFlags.DontSave
                };
                rangeMesh.MarkDynamic();
            }

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = rangeMesh;
            }
        }

        private void ConfigureLineOfSightFilter()
        {
            lineOfSightFilter = new ContactFilter2D();
            lineOfSightFilter.useLayerMask = true;
            lineOfSightFilter.layerMask = LayerMask.GetMask("Environment");
            lineOfSightFilter.useTriggers = false;
        }

        private void RefreshPresentation(bool forceRebuild)
        {
            ResolveComponents();
            if (!TryGetPresentation(out var origin, out var range, out var projectileType, out var weapon))
            {
                SetVisible(false);
                return;
            }

            if (!usesPreviewSettings)
            {
                // The legacy fading circle remains available for older assets, but placed Farmhands use this
                // solid cover-aware mesh whenever their range is selected.
                fallbackCircle?.SetVisible(false);
            }

            ConfigureRenderer();
            var currentPosition = (Vector3)origin;
            var presentationSettingsChanged = forceRebuild || !wasVisible ||
                                              lastUsedPreviewSettings != usesPreviewSettings ||
                                              !Mathf.Approximately(lastRange, range) ||
                                              lastWeapon != weapon ||
                                              lastProjectileType != projectileType ||
                                              lastSortingLayerId != GetSortingLayerId() ||
                                              lastSortingOrder != GetSortingOrder();
            var positionChanged = (currentPosition - lastPosition).sqrMagnitude > 0.000001f;
            var canRebuildPreviewMotion = !usesPreviewSettings ||
                                          (currentPosition - lastPosition).sqrMagnitude >=
                                          PreviewMotionRebuildDistance * PreviewMotionRebuildDistance &&
                                          Time.unscaledTime >= nextPreviewMotionRebuildAt;
            var presentationChanged = presentationSettingsChanged || (positionChanged && canRebuildPreviewMotion);
            if (presentationChanged)
            {
                RebuildMesh(origin, range, projectileType);
                lastUsedPreviewSettings = usesPreviewSettings;
                lastRange = range;
                lastWeapon = weapon;
                lastProjectileType = projectileType;
                lastSortingLayerId = GetSortingLayerId();
                lastSortingOrder = GetSortingOrder();
                lastPosition = currentPosition;
                nextPreviewMotionRebuildAt = Time.unscaledTime + PreviewMotionRebuildInterval;
            }

            SetVisible(true);
        }

        private bool TryGetPresentation(
            out Vector2 origin,
            out float range,
            out ProjectileType projectileType,
            out WeaponDefinition weapon)
        {
            if (usesPreviewSettings)
            {
                origin = transform.position;
                range = previewRange;
                projectileType = previewProjectileType;
                weapon = null;
                return range > 0f;
            }

            if (farmHand == null || !farmHand.isActiveAndEnabled || !farmHand.IsRangeIndicatorVisible)
            {
                origin = default;
                range = 0f;
                projectileType = ProjectileType.Melee;
                weapon = null;
                return false;
            }

            weapon = farmHand.WeaponDefinition;
            var projectile = weapon != null ? weapon.ProjectileDefinition : null;
            origin = farmHand.transform.position;
            range = farmHand.Range;
            projectileType = projectile != null ? projectile.ProjectileType : ProjectileType.Melee;
            return range > 0f;
        }

        private void ConfigureRenderer()
        {
            if (meshRenderer == null)
            {
                return;
            }

            meshRenderer.sharedMaterial = GetSharedMaterial(tint);
            meshRenderer.sortingLayerID = GetSortingLayerId();
            meshRenderer.sortingOrder = GetSortingOrder();
        }

        private int GetSortingLayerId()
        {
            if (usesPreviewSettings)
            {
                return previewSortingLayerId;
            }

            return farmHand != null && farmHand.Visual != null ? farmHand.Visual.sortingLayerID : 0;
        }

        private int GetSortingOrder()
        {
            if (usesPreviewSettings)
            {
                return previewSortingOrder;
            }

            return farmHand != null && farmHand.Visual != null ? farmHand.Visual.sortingOrder - 1 : 0;
        }

        private void RebuildMesh(Vector2 origin, float range, ProjectileType projectileType)
        {
            if (rangeMesh == null)
            {
                return;
            }

            var sampleCount = Mathf.Max(24, radialSamples);
            EnsureMeshBuffers(sampleCount);
            meshVertices[0] = transform.InverseTransformPoint(origin);

            for (var index = 0; index <= sampleCount; index++)
            {
                var angle = (Mathf.PI * 2f * index) / sampleCount;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var targetableDistance = GetTargetableDistance(origin, direction, range, projectileType);
                meshVertices[index + 1] = transform.InverseTransformPoint(origin + (direction * targetableDistance));
            }

            rangeMesh.Clear();
            rangeMesh.vertices = meshVertices;
            rangeMesh.triangles = meshTriangles;
            rangeMesh.RecalculateBounds();
        }

        private void EnsureMeshBuffers(int sampleCount)
        {
            if (meshVertices != null && meshBufferSampleCount == sampleCount)
            {
                return;
            }

            meshBufferSampleCount = sampleCount;
            meshVertices = new Vector3[sampleCount + 2];
            meshTriangles = new int[sampleCount * 3];
            for (var index = 0; index < sampleCount; index++)
            {
                var triangleIndex = index * 3;
                meshTriangles[triangleIndex] = 0;
                meshTriangles[triangleIndex + 1] = index + 1;
                meshTriangles[triangleIndex + 2] = index + 2;
            }
        }

        // Mirrors FarmHand's live firing rule so the visual never claims an unreachable portion of the map.
        private float GetTargetableDistance(
            Vector2 origin,
            Vector2 direction,
            float maximumRange,
            ProjectileType projectileType)
        {
            var hitCount = Physics2D.Raycast(
                origin,
                direction,
                lineOfSightFilter,
                lineOfSightHits,
                maximumRange);

            // This is deliberately conservative for the same reason as live combat: a saturated query could
            // otherwise miss a later full-cover wall.
            if (hitCount >= lineOfSightHits.Length)
            {
                return 0f;
            }

            var targetableDistance = maximumRange;
            for (var index = 0; index < hitCount; index++)
            {
                var wall = lineOfSightHits[index].collider.GetComponentInParent<FarmWall>();
                if (wall == null || (projectileType == ProjectileType.Ranged && !wall.BlocksAllAttacks))
                {
                    continue;
                }

                targetableDistance = Mathf.Min(targetableDistance, lineOfSightHits[index].distance - WallEdgeInset);
            }

            return Mathf.Max(0f, targetableDistance);
        }

        private void SetVisible(bool visible)
        {
            wasVisible = visible;
            if (meshRenderer != null)
            {
                meshRenderer.enabled = visible;
            }
        }

        private static Material GetSharedMaterial(Color color)
        {
            if (sharedMaterial != null)
            {
                return sharedMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                         Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Transparent");
            sharedMaterial = new Material(shader)
            {
                name = "TAD Defender Targetable Range Material",
                hideFlags = HideFlags.DontSave
            };
            if (sharedMaterial.HasProperty("_Color"))
            {
                sharedMaterial.color = color;
            }

            return sharedMaterial;
        }
    }
}
