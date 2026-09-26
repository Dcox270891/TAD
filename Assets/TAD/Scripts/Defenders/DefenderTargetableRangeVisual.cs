using TAD.Environment;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Renders the part of a Farmhand's range that can really be targeted through its current cover rules.
    /// Melee is clipped by both wall types; ranged attacks pass half cover but are clipped by full cover.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DefenderTargetableRangeVisual : MonoBehaviour
    {
        private const int LineOfSightHitCapacity = 16;
        private const float WallEdgeInset = 0.015f;

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
        private bool wasVisible;
        private float lastRange = -1f;
        private WeaponDefinition lastWeapon;
        private Vector3 lastPosition;

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
            ResolveComponents();
            if (farmHand == null)
            {
                SetVisible(false);
                return;
            }

            // The legacy uniform circle remains available to placement previews, but selected Farmhands
            // use this cover-aware mesh instead.
            fallbackCircle?.SetVisible(false);

            var shouldBeVisible = farmHand.isActiveAndEnabled && farmHand.IsRangeIndicatorVisible &&
                                  farmHand.Range > 0f;
            if (!shouldBeVisible)
            {
                SetVisible(false);
                return;
            }

            var weapon = farmHand.WeaponDefinition;
            var currentPosition = farmHand.transform.position;
            if (!wasVisible || !Mathf.Approximately(lastRange, farmHand.Range) || lastWeapon != weapon ||
                (currentPosition - lastPosition).sqrMagnitude > 0.000001f)
            {
                RebuildMesh();
                lastRange = farmHand.Range;
                lastWeapon = weapon;
                lastPosition = currentPosition;
            }

            SetVisible(true);
        }

        private void OnDestroy()
        {
            if (rangeMesh != null)
            {
                Destroy(rangeMesh);
            }
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

        private void ConfigureRenderer()
        {
            if (meshRenderer == null)
            {
                return;
            }

            meshRenderer.sharedMaterial = GetSharedMaterial(tint);
            var farmHandVisual = farmHand != null ? farmHand.Visual : null;
            meshRenderer.sortingLayerID = farmHandVisual != null ? farmHandVisual.sortingLayerID : 0;
            meshRenderer.sortingOrder = farmHandVisual != null ? farmHandVisual.sortingOrder - 1 : 0;
        }

        private void RebuildMesh()
        {
            if (rangeMesh == null || farmHand == null)
            {
                return;
            }

            ConfigureRenderer();
            var sampleCount = Mathf.Max(24, radialSamples);
            var vertices = new Vector3[sampleCount + 2];
            var triangles = new int[sampleCount * 3];
            var origin = (Vector2)farmHand.transform.position;
            var range = farmHand.Range;
            var projectileType = GetProjectileType();
            vertices[0] = transform.InverseTransformPoint(origin);

            for (var index = 0; index <= sampleCount; index++)
            {
                var angle = (Mathf.PI * 2f * index) / sampleCount;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var targetableDistance = GetTargetableDistance(origin, direction, range, projectileType);
                vertices[index + 1] = transform.InverseTransformPoint(origin + (direction * targetableDistance));
            }

            for (var index = 0; index < sampleCount; index++)
            {
                var triangleIndex = index * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = index + 1;
                triangles[triangleIndex + 2] = index + 2;
            }

            rangeMesh.Clear();
            rangeMesh.vertices = vertices;
            rangeMesh.triangles = triangles;
            rangeMesh.RecalculateBounds();
        }

        private ProjectileType GetProjectileType()
        {
            var projectile = farmHand != null && farmHand.WeaponDefinition != null
                ? farmHand.WeaponDefinition.ProjectileDefinition
                : null;
            return projectile != null ? projectile.ProjectileType : ProjectileType.Melee;
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