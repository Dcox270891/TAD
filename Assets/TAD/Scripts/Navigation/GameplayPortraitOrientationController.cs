using TAD.Environment;
using TAD.Waves;
using UnityEngine;

namespace TAD.Navigation
{
    /// <summary>
    /// Keeps the whole farm readable when a phone changes from landscape to portrait.
    ///
    /// The level itself is deliberately never rotated or repositioned: walls, colliders, route
    /// data, and defender placement all continue to use their authored world coordinates. Instead
    /// this controller turns the orthographic gameplay camera so the spawn is above the Food Store
    /// on screen, then asks the route mapper to rebuild for that new visible view.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class GameplayPortraitOrientationController : MonoBehaviour
    {
        private const float LandscapeReferenceAspect = 16f / 9f;
        private const float EndpointViewportPadding = 0.75f;
        private const float ChangeEpsilon = 0.0001f;

        [Header("Gameplay references")]
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private FarmRouteMapper routeMapper;
        [SerializeField] private WaveController waveController;
        [SerializeField] private FoodStore foodStore;
        [SerializeField] private Transform spawnPoint;

        [Header("Preview")]
        [SerializeField]
        [Tooltip("Editor-only preview switch. Builds use the real mobile orientation.")]
        private bool previewPortraitInEditor;

        [SerializeField]
        [Tooltip("Lets focused tests exercise portrait presentation without changing the device.")]
        private bool forcePortraitForTests;

        private CameraPose landscapePose;
        private float landscapeCameraAspect;
        private bool hasCapturedLandscapePose;
        private bool hasAppliedPresentation;
        private bool appliedPortrait;
        private Vector2Int lastScreenSize;
        private Rect lastSafeArea;

        /// <summary>Whether this controller is currently presenting the farm in portrait orientation.</summary>
        public bool IsPortraitPresentation => ResolvePortraitPresentation();

        private struct CameraPose
        {
            public CameraPose(Vector3 position, Quaternion rotation, float orthographicSize)
            {
                Position = position;
                Rotation = rotation;
                OrthographicSize = orthographicSize;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public float OrthographicSize { get; }
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureLandscapePoseIfNeeded();
        }

        private void Start()
        {
            ApplyPresentation(force: true);
        }

        private void OnEnable()
        {
            hasAppliedPresentation = false;
        }

        private void LateUpdate()
        {
            var portrait = ResolvePortraitPresentation();
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var safeArea = Screen.safeArea;
            if (!hasAppliedPresentation || portrait != appliedPortrait ||
                screenSize != lastScreenSize || safeArea != lastSafeArea)
            {
                ApplyPresentation(force: true);
            }
        }

        /// <summary>Allows the edit-mode test suite to verify the mobile framing deterministically.</summary>
        public void SetPortraitTestOverride(bool enabled)
        {
            forcePortraitForTests = enabled;
            ApplyPresentation(force: true);
        }

        /// <summary>
        /// Supplies explicit references for a reusable level, test fixture, or runtime-created
        /// spawn pool. Normal levels may leave these blank; they are discovered safely.
        /// </summary>
        public void Configure(
            Camera camera = null,
            FarmRouteMapper mapper = null,
            WaveController waves = null,
            FoodStore store = null,
            Transform spawn = null)
        {
            gameplayCamera = camera ?? gameplayCamera;
            routeMapper = mapper ?? routeMapper;
            waveController = waves ?? waveController;
            foodStore = store ?? foodStore;
            spawnPoint = spawn ?? spawnPoint;
            ResolveReferences();
            CaptureLandscapePoseIfNeeded();
        }

        private void ApplyPresentation(bool force)
        {
            ResolveReferences();
            CaptureLandscapePoseIfNeeded();
            if (gameplayCamera == null || !gameplayCamera.orthographic || !hasCapturedLandscapePose)
            {
                return;
            }

            var portrait = ResolvePortraitPresentation();
            if (!force && hasAppliedPresentation && portrait == appliedPortrait)
            {
                return;
            }

            if (portrait && TryGetRouteEndpoints(out var spawn, out var food))
            {
                ApplyPortraitCameraPose(spawn, food);
            }
            else
            {
                RestoreLandscapeCameraPose();
            }

            appliedPortrait = portrait;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastSafeArea = Screen.safeArea;
            hasAppliedPresentation = true;

            // Rotating an orthographic camera changes the mapper's visible rectangle. Rebuild once
            // per orientation/screen transition, after physics has observed the new transform.
            Physics2D.SyncTransforms();
            if (waveController != null)
            {
                waveController.RefreshRouteMapForCurrentViewport();
            }
            else
            {
                routeMapper?.RebuildRoutes();
            }
        }

        private void ApplyPortraitCameraPose(Vector2 spawn, Vector2 food)
        {
            var desiredUp = spawn - food;
            if (desiredUp.sqrMagnitude <= ChangeEpsilon)
            {
                desiredUp = (Vector2)(landscapePose.Rotation * Vector3.up);
            }

            desiredUp.Normalize();
            var baselineUp = (Vector2)(landscapePose.Rotation * Vector3.up);
            var turnDegrees = Vector2.SignedAngle(baselineUp, desiredUp);
            var portraitRotation = Quaternion.AngleAxis(turnDegrees, Vector3.forward) * landscapePose.Rotation;

            // Centre the two route endpoints. With the camera's up direction facing the spawn,
            // this makes the spawn appear at the top and the Food Store directly beneath it.
            var midpoint = (spawn + food) * 0.5f;
            var portraitPosition = new Vector3(midpoint.x, midpoint.y, landscapePose.Position.z);
            gameplayCamera.transform.SetPositionAndRotation(portraitPosition, portraitRotation);

            var localSpawn = gameplayCamera.transform.InverseTransformPoint(new Vector3(spawn.x, spawn.y, portraitPosition.z));
            var localFood = gameplayCamera.transform.InverseTransformPoint(new Vector3(food.x, food.y, portraitPosition.z));
            var requiredEndpointHalfHeight = Mathf.Max(Mathf.Abs(localSpawn.y), Mathf.Abs(localFood.y)) + EndpointViewportPadding;
            var requiredEndpointHalfWidth = Mathf.Max(Mathf.Abs(localSpawn.x), Mathf.Abs(localFood.x)) + EndpointViewportPadding;
            var portraitAspect = Mathf.Max(0.01f, gameplayCamera.aspect);

            // Preserve the whole authored landscape frame where possible, rather than zooming
            // into only the two endpoints. This keeps walls and the farm presentation in view.
            var landscapeHalfWidth = landscapePose.OrthographicSize * Mathf.Max(LandscapeReferenceAspect, landscapeCameraAspect);
            var landscapeHalfHeight = landscapePose.OrthographicSize;
            var requiredSize = Mathf.Max(
                requiredEndpointHalfHeight,
                requiredEndpointHalfWidth / portraitAspect,
                landscapeHalfWidth,
                landscapeHalfHeight / portraitAspect);
            gameplayCamera.orthographicSize = Mathf.Max(landscapePose.OrthographicSize, requiredSize);
        }

        private void RestoreLandscapeCameraPose()
        {
            gameplayCamera.transform.SetPositionAndRotation(landscapePose.Position, landscapePose.Rotation);
            gameplayCamera.orthographicSize = landscapePose.OrthographicSize;
        }

        private bool ResolvePortraitPresentation()
        {
            if (forcePortraitForTests)
            {
                return true;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && previewPortraitInEditor)
            {
                return true;
            }
#endif

            if (!Application.isMobilePlatform)
            {
                return false;
            }

            var safeArea = Screen.safeArea;
            return safeArea.height > safeArea.width;
        }

        private void ResolveReferences()
        {
            gameplayCamera ??= Camera.main;
            routeMapper ??= GetComponent<FarmRouteMapper>() ?? UnityEngine.Object.FindAnyObjectByType<FarmRouteMapper>();
            waveController ??= GetComponent<WaveController>() ?? UnityEngine.Object.FindAnyObjectByType<WaveController>();
            if (foodStore == null && waveController != null)
            {
                foodStore = waveController.FoodStore;
            }

            foodStore ??= UnityEngine.Object.FindAnyObjectByType<FoodStore>();
            if (spawnPoint == null && waveController != null)
            {
                spawnPoint = waveController.SpawnPointTransform;
            }
        }

        private void CaptureLandscapePoseIfNeeded()
        {
            if (hasCapturedLandscapePose || gameplayCamera == null || !gameplayCamera.orthographic)
            {
                return;
            }

            landscapePose = new CameraPose(
                gameplayCamera.transform.position,
                gameplayCamera.transform.rotation,
                gameplayCamera.orthographicSize);
            landscapeCameraAspect = Mathf.Max(LandscapeReferenceAspect, gameplayCamera.aspect);
            hasCapturedLandscapePose = true;
        }

        private bool TryGetRouteEndpoints(out Vector2 spawn, out Vector2 food)
        {
            var source = spawnPoint != null
                ? spawnPoint.position
                : waveController != null ? waveController.SpawnPosition : transform.position;
            var destination = foodStore != null ? foodStore.transform.position : source;
            spawn = (Vector2)source;
            food = (Vector2)destination;
            return foodStore != null;
        }
    }
}
