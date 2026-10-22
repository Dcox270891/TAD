using System;
using System.Linq;
using TAD.Environment;
using TAD.Waves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAD.Editor
{
    /// <summary>
    /// Maintains the camera framing shared by every gameplay level. The route mapper deliberately
    /// maps only the visible gameplay area, so both the spawn and Food Store must remain inside
    /// that area at the smallest supported landscape aspect ratio.
    /// </summary>
    public static class GameplayCameraFramingAssetBuilder
    {
        private const string GameplayLevelCorePrefabPath = "Assets/TAD/Prefabs/Levels/GameplayLevelCore.prefab";
        private const string LevelTemplateScenePath = "Assets/Scenes/Templates/LevelTemplate.unity";
        private const float MinimumLandscapeAspect = 16f / 9f;
        private const float EndpointViewportPadding = 0.75f;
        // The grass is strictly the authored farm field. Any one-frame camera surplus is a
        // light-oak fallback sampled from TAD_LightWoodSurround while FarmViewportBorder draws
        // the tiled exterior wall. Keeping this close to the texture avoids a dark flash during
        // scene loading, a resize, or the first presentation rebuild.
        private static readonly Color ExteriorWallFallbackClearColor = new(0.855f, 0.604f, 0.298f, 1f);

        private static readonly string[] SavedGameplayScenePaths =
        {
            "Assets/Scenes/Levels/Level1.unity",
            "Assets/Scenes/Levels/Level2.unity",
            "Assets/Scenes/Levels/Level3.unity",
            "Assets/Scenes/Levels/Level4.unity",
            LevelTemplateScenePath
        };

        [InitializeOnLoadMethod]
        private static void QueueRequiredCameraFramingRepair()
        {
            // Camera framing is designer-authored. Do not schedule a serialized scene migration
            // merely because the Editor opened or returned from Play Mode. The explicit repair
            // menu command remains available when a deliberate framing pass is wanted.
        }

        [MenuItem("TAD/Level Tools/Repair Shared Gameplay Camera Framing")]
        public static void RepairSharedGameplayCameraFraming()
        {
            RepairAllGameplayCameraFrames();
            ValidateAllShippedGameplayScenes();
            Debug.Log("Repaired gameplay camera framing. Spawn points and Food Stores now fit inside each saved level's 16:9 route map.");
        }

        /// <summary>
        /// Updates only the visual fallback outside the authored farm. Unlike the framing
        /// repair, this never changes endpoints, playable bounds, or a camera's orthographic size.
        /// </summary>
        [MenuItem("TAD/Level Tools/Apply Light-Oak Camera Fallback Only")]
        public static void ApplyLightOakCameraFallbackOnly()
        {
            ApplySharedGameplayCameraFallbackOnly();

            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var scenePath in SavedGameplayScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (EnsureSceneCameraFallback(scene))
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                if (restoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Applied the light-oak camera fallback without changing gameplay camera framing.");
        }

        private static void RepairIfRequiredAfterEditorStartup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnyLoadedSceneIsDirty())
            {
                return;
            }

            // Automatic editor hooks are presentation-only. Camera framing can intentionally be
            // authored per level, so changing orthographic size remains available solely through
            // the explicit "Repair Shared Gameplay Camera Framing" menu command.
            ApplyLightOakCameraFallbackOnly();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += RepairIfRequiredAfterEditorStartup;
            }
        }

        private static void RepairAllGameplayCameraFrames()
        {
            EnsureSharedGameplayCameraFraming();

            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var scenePath in SavedGameplayScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (!EnsureSceneCameraFramesEndpoints(scene))
                    {
                        continue;
                    }

                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (restoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static void EnsureSharedGameplayCameraFraming()
        {
            var root = PrefabUtility.LoadPrefabContents(GameplayLevelCorePrefabPath);
            try
            {
                var gameplayCamera = root.GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(camera => camera != null && camera.CompareTag("MainCamera"));
                if (gameplayCamera == null)
                {
                    throw new InvalidOperationException("GameplayLevelCore is missing its Main Camera.");
                }

                var changed = false;
                if (!gameplayCamera.orthographic)
                {
                    gameplayCamera.orthographic = true;
                    changed = true;
                }

                changed |= EnsureLightOakFallback(gameplayCamera);

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, GameplayLevelCorePrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>Batch-mode entry point used by the project health check.</summary>
        public static void RepairAndValidateFromCommandLine()
        {
            RepairSharedGameplayCameraFraming();
        }

        /// <summary>Batch-mode entry point for the non-layout light-oak fallback migration.</summary>
        public static void ApplyLightOakCameraFallbackOnlyFromCommandLine()
        {
            ApplyLightOakCameraFallbackOnly();
        }

        private static void ValidateAllShippedGameplayScenes()
        {
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var scenePath in SavedGameplayScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var camera = FindInScene<Camera>(scene, component => component.CompareTag("MainCamera"));
                    var waveController = FindInScene<WaveController>(scene);
                    var foodStore = FindInScene<FoodStore>(scene);
                    if (camera == null || waveController == null || foodStore == null)
                    {
                        throw new InvalidOperationException(scenePath + " is missing a gameplay camera, wave controller, or Food Store.");
                    }

                    if (!DoesLandscapeFrameContain(camera, waveController.SpawnPosition) ||
                        !DoesLandscapeFrameContain(camera, foodStore.transform.position))
                    {
                        throw new InvalidOperationException(
                            scene.name + " leaves its spawn point or Food Store outside the visible route map.");
                    }
                }
            }
            finally
            {
                if (restoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
        }

        /// <summary>
        /// Persists only the amount of orthographic room this particular level needs. The Wave
        /// Controller repeats this check at runtime for rotation/resolution changes; saving it
        /// here means route fields are valid before the first frame as well.
        /// </summary>
        private static bool EnsureSceneCameraFramesEndpoints(Scene scene)
        {
            var camera = FindInScene<Camera>(scene, component => component.CompareTag("MainCamera"));
            var waveController = FindInScene<WaveController>(scene);
            var foodStore = FindInScene<FoodStore>(scene);
            if (camera == null || waveController == null || foodStore == null)
            {
                throw new InvalidOperationException(
                    scene.path + " is missing a gameplay camera, wave controller, or Food Store.");
            }

            var changed = false;
            if (!camera.orthographic)
            {
                camera.orthographic = true;
                changed = true;
            }

            // A scene can retain an inherited prefab override, so apply the matching fallback
            // here as well as on GameplayLevelCore. This keeps every saved level free of the
            // old dark-brown flash while its exterior presentation initializes.
            changed |= EnsureLightOakFallback(camera);

            var spawnInCameraSpace = camera.transform.InverseTransformPoint(waveController.SpawnPosition);
            var foodInCameraSpace = camera.transform.InverseTransformPoint(foodStore.transform.position);
            var requiredHalfHeight = Mathf.Max(
                Mathf.Abs(spawnInCameraSpace.y),
                Mathf.Abs(foodInCameraSpace.y)) + EndpointViewportPadding;
            var requiredHalfWidth = Mathf.Max(
                Mathf.Abs(spawnInCameraSpace.x),
                Mathf.Abs(foodInCameraSpace.x)) + EndpointViewportPadding;
            var requiredSize = Mathf.Max(requiredHalfHeight, requiredHalfWidth / MinimumLandscapeAspect);
            if (requiredSize > camera.orthographicSize + 0.01f)
            {
                camera.orthographicSize = requiredSize;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(camera);
            }

            return changed;
        }

        private static void ApplySharedGameplayCameraFallbackOnly()
        {
            var root = PrefabUtility.LoadPrefabContents(GameplayLevelCorePrefabPath);
            try
            {
                var gameplayCamera = root.GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(camera => camera != null && camera.CompareTag("MainCamera"));
                if (gameplayCamera == null)
                {
                    throw new InvalidOperationException("GameplayLevelCore is missing its Main Camera.");
                }

                if (EnsureLightOakFallback(gameplayCamera))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, GameplayLevelCorePrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool EnsureSceneCameraFallback(Scene scene)
        {
            var camera = FindInScene<Camera>(scene, component => component.CompareTag("MainCamera"));
            if (camera == null)
            {
                throw new InvalidOperationException(scene.path + " is missing its Main Camera.");
            }

            if (!EnsureLightOakFallback(camera))
            {
                return false;
            }

            EditorUtility.SetDirty(camera);
            return true;
        }

        internal static bool UsesLightOakFallback(Camera camera)
        {
            return camera != null &&
                   camera.clearFlags == CameraClearFlags.SolidColor &&
                   camera.backgroundColor == ExteriorWallFallbackClearColor;
        }

        private static bool EnsureLightOakFallback(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            var changed = false;
            if (camera.clearFlags != CameraClearFlags.SolidColor)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                changed = true;
            }

            if (camera.backgroundColor != ExteriorWallFallbackClearColor)
            {
                camera.backgroundColor = ExteriorWallFallbackClearColor;
                changed = true;
            }

            return changed;
        }

        private static bool DoesLandscapeFrameContain(Camera camera, Vector2 endpoint)
        {
            if (camera == null || !camera.orthographic)
            {
                return false;
            }

            var localEndpoint = camera.transform.InverseTransformPoint(endpoint);
            var halfWidth = camera.orthographicSize * MinimumLandscapeAspect;
            return Mathf.Abs(localEndpoint.x) <= halfWidth + 0.01f &&
                   Mathf.Abs(localEndpoint.y) <= camera.orthographicSize + 0.01f;
        }

        private static SceneSetup[] CaptureSceneSetupForSceneOperation(out bool restoreSetup)
        {
            var hasLoadedScene = false;
            var hasUntitledScene = false;
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded)
                {
                    continue;
                }

                hasLoadedScene = true;
                hasUntitledScene |= string.IsNullOrWhiteSpace(scene.path);
            }

            if (!hasLoadedScene || (hasUntitledScene && Application.isBatchMode))
            {
                restoreSetup = false;
                return Array.Empty<SceneSetup>();
            }

            if (hasUntitledScene)
            {
                throw new InvalidOperationException(
                    "Save or close the current untitled scene before repairing saved gameplay camera framing.");
            }

            restoreSetup = true;
            return EditorSceneManager.GetSceneManagerSetup();
        }

        private static bool AnyLoadedSceneIsDirty()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static T FindInScene<T>(Scene scene, Func<T, bool> predicate = null) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component != null && (predicate == null || predicate(component)));
        }
    }
}
