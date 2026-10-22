using TAD.Navigation;
using UnityEngine;

namespace TAD.Environment
{
    /// <summary>
    /// Presentation and collision border for the unused space outside an authored rectangular
    /// <see cref="FarmPlayableArea"/>.
    ///
    /// The component deliberately leaves the authored grass untouched. Instead it creates a
    /// camera-fitted exterior renderer behind that grass and four thin, full-cover wall strips
    /// immediately outside the farm bounds. The strips keep actors and projectiles out of the
    /// decorative exterior without becoming part of the walkable farm.
    /// </summary>
    [DefaultExecutionOrder(-650)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmPlayableArea))]
    public sealed class FarmViewportBorder : MonoBehaviour
    {
        public const string EnvironmentLayerName = "Environment";

        private const string RuntimeRootName = "Runtime Farm Viewport Border";
        private const string ExteriorBackdropName = "Exterior Backdrop";
        private const int ExteriorSortingOrder = -20;
        private const float MinimumBorderThickness = 0.01f;
        private const float MinimumViewportDimension = 0.01f;
        private const float ChangeEpsilon = 0.0001f;

        [SerializeField] private FarmPlayableArea playableArea;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Sprite exteriorSprite;
        [SerializeField] private Material exteriorMaterial;

        [SerializeField, Min(MinimumBorderThickness)]
        [Tooltip("World-space thickness of the solid full-cover strips just outside the farm.")]
        private float borderThickness = 0.5f;

        [SerializeField, Min(0f)]
        [Tooltip("Extra exterior art retained beyond the camera edge in world units.")]
        private float viewportOverscan = 0.5f;

        private Transform runtimeRoot;
        private SpriteRenderer exteriorRenderer;
        private readonly FarmWall[] borderWalls = new FarmWall[4];
        private readonly BoxCollider2D[] borderColliders = new BoxCollider2D[4];

        private Rect lastWorldBounds;
        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;
        private float lastOrthographicSize;
        private float lastCameraAspect;
        private Vector2Int lastScreenSize;
        private bool hasBuilt;
        private bool hasFittedViewport;

        /// <summary>Thin wall positions around the rectangular farm bounds.</summary>
        public enum Edge
        {
            Left = 0,
            Right = 1,
            Bottom = 2,
            Top = 3
        }

        /// <summary>The authored area whose exterior this component protects.</summary>
        public FarmPlayableArea PlayableArea => playableArea;

        /// <summary>The camera used to cover the visible exterior presentation.</summary>
        public Camera GameplayCamera => gameplayCamera;

        /// <summary>The full-cover sprite tiled behind the authored grass outside the farm.</summary>
        public Sprite ExteriorSprite => exteriorSprite;

        /// <summary>The optional material used by the tiled exterior presentation.</summary>
        public Material ExteriorMaterial => exteriorMaterial;

        /// <summary>The runtime-only renderer behind the authored grass.</summary>
        public SpriteRenderer ExteriorRenderer => exteriorRenderer;

        /// <summary>The number of generated exterior full-cover walls.</summary>
        public int BorderWallCount => borderWalls.Length;

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            Rebuild();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && !hasBuilt)
            {
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            if (!hasBuilt || playableArea == null)
            {
                return;
            }

            var currentBounds = playableArea.WorldBounds;
            if (!ApproximatelyEqual(lastWorldBounds, currentBounds))
            {
                Rebuild();
                return;
            }

            if (ViewportChanged())
            {
                FitExteriorBackdropToViewport();
            }
        }

        private void OnDestroy()
        {
            DestroyRuntimeChildren();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            borderThickness = Mathf.Max(MinimumBorderThickness, borderThickness);
            viewportOverscan = Mathf.Max(0f, viewportOverscan);
            playableArea ??= GetComponent<FarmPlayableArea>();
        }
#endif

        /// <summary>
        /// Provides the visual dependencies without modifying the authored grass or farm area.
        /// Editor builders can leave <paramref name="rebuild"/> false (or rely on the editor-safe
        /// default behaviour) to serialize only these references, never the runtime child objects.
        /// </summary>
        public void Configure(Camera camera, Sprite sprite, Material material = null, bool rebuild = true)
        {
            gameplayCamera = camera;
            exteriorSprite = sprite;
            exteriorMaterial = material;
            ResolveReferences();
            if (rebuild && Application.isPlaying)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Stores the serialized visual references without creating runtime children. Shared scene
        /// and prefab builders should use this form while they are editing authored assets.
        /// </summary>
        public void ConfigureForAuthoring(Camera camera, Sprite sprite, Material material = null)
        {
            Configure(camera, sprite, material, rebuild: false);
        }

        /// <summary>
        /// Creates or refreshes the exterior visual and solid border strips. It is safe to call
        /// repeatedly after a camera, area, or visual assignment changes.
        /// </summary>
        public bool Rebuild()
        {
            ResolveReferences();
            if (playableArea == null || !playableArea.HasBoundary)
            {
                hasBuilt = false;
                hasFittedViewport = false;
                DestroyRuntimeChildren();
                return false;
            }

            EnsureRuntimeChildren();
            lastWorldBounds = playableArea.WorldBounds;
            UpdateBorderWalls(lastWorldBounds);
            FitExteriorBackdropToViewport();
            hasBuilt = true;
            return true;
        }

        /// <summary>Updates only the camera-fitted presentation, leaving the farm and wall bounds unchanged.</summary>
        public void FitExteriorBackdropToViewport()
        {
            if (gameplayCamera == null || exteriorRenderer == null || !gameplayCamera.orthographic)
            {
                hasFittedViewport = false;
                return;
            }

            var cameraTransform = gameplayCamera.transform;
            var backdropTransform = exteriorRenderer.transform;
            backdropTransform.SetPositionAndRotation(
                new Vector3(cameraTransform.position.x, cameraTransform.position.y, 0f),
                Quaternion.AngleAxis(cameraTransform.eulerAngles.z, Vector3.forward));
            backdropTransform.localScale = Vector3.one;

            exteriorRenderer.drawMode = SpriteDrawMode.Tiled;
            var halfHeight = Mathf.Max(MinimumViewportDimension, gameplayCamera.orthographicSize);
            var halfWidth = halfHeight * Mathf.Max(MinimumViewportDimension, gameplayCamera.aspect);
            exteriorRenderer.size = new Vector2(
                (halfWidth * 2f) + (viewportOverscan * 2f),
                (halfHeight * 2f) + (viewportOverscan * 2f));

            lastCameraPosition = cameraTransform.position;
            lastCameraRotation = cameraTransform.rotation;
            lastOrthographicSize = gameplayCamera.orthographicSize;
            lastCameraAspect = gameplayCamera.aspect;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            hasFittedViewport = true;
        }

        /// <summary>Returns the requested generated full-cover wall, or null before a successful rebuild.</summary>
        public FarmWall GetBorderWall(Edge edge)
        {
            return borderWalls[(int)edge];
        }

        /// <summary>Returns whether the exterior tile covers all corners of the current orthographic camera viewport.</summary>
        public bool CoversCurrentViewport()
        {
            if (gameplayCamera == null || exteriorRenderer == null || !gameplayCamera.orthographic)
            {
                return false;
            }

            var distance = Mathf.Abs(gameplayCamera.transform.position.z - exteriorRenderer.transform.position.z);
            var halfSize = exteriorRenderer.size * 0.5f;
            for (var horizontal = 0; horizontal <= 1; horizontal++)
            {
                for (var vertical = 0; vertical <= 1; vertical++)
                {
                    var corner = gameplayCamera.ViewportToWorldPoint(new Vector3(horizontal, vertical, distance));
                    var localCorner = exteriorRenderer.transform.InverseTransformPoint(corner);
                    if (Mathf.Abs(localCorner.x) > halfSize.x + ChangeEpsilon ||
                        Mathf.Abs(localCorner.y) > halfSize.y + ChangeEpsilon)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void ResolveReferences()
        {
            playableArea ??= GetComponent<FarmPlayableArea>();
            gameplayCamera ??= Camera.main;
        }

        private void EnsureRuntimeChildren()
        {
            var environmentLayer = ResolveEnvironmentLayer();
            if (runtimeRoot == null)
            {
                var root = new GameObject(RuntimeRootName);
                root.transform.SetParent(transform, false);
                root.hideFlags = HideFlags.DontSaveInEditor;
                runtimeRoot = root.transform;
            }

            runtimeRoot.gameObject.layer = environmentLayer;
            if (exteriorRenderer == null)
            {
                var exteriorObject = new GameObject(ExteriorBackdropName, typeof(SpriteRenderer));
                exteriorObject.transform.SetParent(runtimeRoot, false);
                exteriorObject.hideFlags = HideFlags.DontSaveInEditor;
                exteriorRenderer = exteriorObject.GetComponent<SpriteRenderer>();
            }

            exteriorRenderer.gameObject.layer = environmentLayer;
            exteriorRenderer.sprite = exteriorSprite;
            exteriorRenderer.sharedMaterial = exteriorMaterial;
            exteriorRenderer.sortingLayerName = "Default";
            exteriorRenderer.sortingOrder = ExteriorSortingOrder;

            for (var index = 0; index < borderWalls.Length; index++)
            {
                if (borderWalls[index] != null)
                {
                    borderWalls[index].gameObject.layer = environmentLayer;
                    continue;
                }

                var wallObject = new GameObject(
                    "Exterior Border " + ((Edge)index),
                    typeof(BoxCollider2D),
                    typeof(FarmWall));
                wallObject.transform.SetParent(runtimeRoot, false);
                wallObject.hideFlags = HideFlags.DontSaveInEditor;
                wallObject.layer = environmentLayer;
                borderWalls[index] = wallObject.GetComponent<FarmWall>();
                borderColliders[index] = wallObject.GetComponent<BoxCollider2D>();
            }
        }

        private void UpdateBorderWalls(Rect bounds)
        {
            var safeThickness = Mathf.Max(MinimumBorderThickness, borderThickness);
            var expandedWidth = bounds.width + (safeThickness * 2f);
            var expandedHeight = bounds.height + (safeThickness * 2f);
            var centreX = bounds.center.x;
            var centreY = bounds.center.y;

            ConfigureWall(
                Edge.Left,
                new Vector2(bounds.xMin - (safeThickness * 0.5f), centreY),
                new Vector2(safeThickness, expandedHeight));
            ConfigureWall(
                Edge.Right,
                new Vector2(bounds.xMax + (safeThickness * 0.5f), centreY),
                new Vector2(safeThickness, expandedHeight));
            ConfigureWall(
                Edge.Bottom,
                new Vector2(centreX, bounds.yMin - (safeThickness * 0.5f)),
                new Vector2(expandedWidth, safeThickness));
            ConfigureWall(
                Edge.Top,
                new Vector2(centreX, bounds.yMax + (safeThickness * 0.5f)),
                new Vector2(expandedWidth, safeThickness));
        }

        private void ConfigureWall(Edge edge, Vector2 centre, Vector2 size)
        {
            var index = (int)edge;
            var wall = borderWalls[index];
            var collider = borderColliders[index];
            if (wall == null || collider == null)
            {
                return;
            }

            var wallTransform = wall.transform;
            wallTransform.SetPositionAndRotation(new Vector3(centre.x, centre.y, 0f), Quaternion.identity);
            wallTransform.localScale = Vector3.one;
            collider.offset = Vector2.zero;
            collider.size = size;
            collider.isTrigger = false;
            collider.enabled = true;
        }

        private bool ViewportChanged()
        {
            if (!hasFittedViewport)
            {
                return true;
            }

            if (gameplayCamera == null)
            {
                return false;
            }

            var cameraTransform = gameplayCamera.transform;
            return (cameraTransform.position - lastCameraPosition).sqrMagnitude > ChangeEpsilon ||
                   Quaternion.Angle(cameraTransform.rotation, lastCameraRotation) > ChangeEpsilon ||
                   Mathf.Abs(gameplayCamera.orthographicSize - lastOrthographicSize) > ChangeEpsilon ||
                   Mathf.Abs(gameplayCamera.aspect - lastCameraAspect) > ChangeEpsilon ||
                   lastScreenSize != new Vector2Int(Screen.width, Screen.height);
        }

        private void DestroyRuntimeChildren()
        {
            if (runtimeRoot == null)
            {
                return;
            }

#if UNITY_EDITOR
            // EditMode tests and authoring rebuilds need deterministic cleanup immediately rather
            // than waiting for Unity's deferred play-mode destruction queue.
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DestroyImmediate(runtimeRoot.gameObject);
            }
            else
#endif
            if (Application.isPlaying)
            {
                Destroy(runtimeRoot.gameObject);
            }
            else
            {
                DestroyImmediate(runtimeRoot.gameObject);
            }

            runtimeRoot = null;
            exteriorRenderer = null;
            for (var index = 0; index < borderWalls.Length; index++)
            {
                borderWalls[index] = null;
                borderColliders[index] = null;
            }
        }

        private static int ResolveEnvironmentLayer()
        {
            var environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
            return environmentLayer >= 0 ? environmentLayer : 0;
        }

        private static bool ApproximatelyEqual(Rect first, Rect second)
        {
            return Mathf.Abs(first.xMin - second.xMin) <= ChangeEpsilon &&
                   Mathf.Abs(first.yMin - second.yMin) <= ChangeEpsilon &&
                   Mathf.Abs(first.xMax - second.xMax) <= ChangeEpsilon &&
                   Mathf.Abs(first.yMax - second.yMax) <= ChangeEpsilon;
        }
    }
}
