#if UNITY_EDITOR
using System;
using System.Linq;
using TAD.Environment;
using TAD.Navigation;
using TAD.Waves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAD.Editor
{
    /// <summary>
    /// Keeps the visual farm field exactly aligned with the authored playable area, while giving
    /// camera-only surplus a non-walkable full-cover exterior.  This is deliberately a scene
    /// authoring pass: it keeps grass and exterior artwork in step with the playable area and
    /// camera frame. It also performs a one-time repair for the reusable template's legacy
    /// placeholder endpoints, which were saved outside its own authored field.
    /// </summary>
    public static class FarmViewportPresentationAssetBuilder
    {
        private const string LightWoodExteriorTexturePath =
            "Assets/TAD/Art/Environment/Presentation/TAD_LightWoodSurround.png";
        private const float LightWoodPixelsPerUnit = 100f;
        private const string LevelTemplateScenePath = "Assets/Scenes/Templates/LevelTemplate.unity";
        private const float BoundsTolerance = 0.015f;

        private static readonly string[] SavedScenePaths =
        {
            "Assets/Scenes/Levels/Level1.unity",
            "Assets/Scenes/Levels/Level2.unity",
            "Assets/Scenes/Levels/Level3.unity",
            "Assets/Scenes/Levels/Level4.unity",
            LevelTemplateScenePath
        };

        [InitializeOnLoadMethod]
        private static void QueueMissingPresentationRepair()
        {
            // Scene presentation is designer-authored.  Do not schedule a serialized scene
            // migration merely because the Editor opened or returned from Play Mode. Explicit
            // menu commands remain available when a deliberate presentation pass is wanted.
        }

        /// <summary>
        /// Applies the exact ground footprint and the exterior border to all shipping levels and
        /// to the reusable template. This is intentionally explicit so a designer's unsaved
        /// terrain adjustment is never overwritten merely by opening Unity.
        /// </summary>
        [MenuItem("TAD/Level Tools/Legacy (Changes Layout)/Apply Farm Ground + Exterior Borders")]
        public static void ApplyFarmGroundAndExteriorBorders()
        {
            PrepareReusableTemplateRouteEndpoints();
            FarmPlayableAreaAssetBuilder.ApplySavedFarmPlayableAreasForPresentation();
            ApplyPresentation(forceGroundAlignment: true);
            ValidateSavedFarmViewportPresentation();
            Debug.Log("TAD aligned the authored grass with each Farm Playable Area and configured non-walkable exterior borders.");
        }

        /// <summary>
        /// Applies the light-wood exterior art and its matching camera fallback without touching
        /// a level's grass, playable boundary, endpoints, camera framing, or any route data.
        /// Existing FarmViewportBorder components are updated in place only.
        /// </summary>
        [MenuItem("TAD/Level Tools/Apply Light-Oak Exterior Presentation Only")]
        public static void ApplyLightOakExteriorPresentationOnly()
        {
            GameplayCameraFramingAssetBuilder.ApplyLightOakCameraFallbackOnly();
            ApplyExteriorArtworkOnly();
            ValidateSavedLightOakExteriorPresentation();
            Debug.Log("Applied the light-oak exterior presentation without changing level layouts.");
        }

        /// <summary>Batch-mode entry point for the non-layout exterior presentation migration.</summary>
        public static void ApplyAndValidateFromCommandLine()
        {
            ApplyLightOakExteriorPresentationOnly();
        }

        /// <summary>
        /// Lightweight live-editor entry point. Kept separate from the all-level migration so
        /// integrations with a short main-thread command budget can prepare the template first.
        /// </summary>
        public static void RepairReusableTemplateEndpointsForPresentation()
        {
            PrepareReusableTemplateRouteEndpoints();
        }

        /// <summary>
        /// Lightweight batch/editor entry point for existing border visuals only. It does not
        /// alter grass, playable polygons, endpoints, or camera orthographic sizes.
        /// </summary>
        public static void ApplySavedViewportPresentationForCommandLine()
        {
            ApplyLightOakExteriorPresentationOnly();
        }

        /// <summary>
        /// Checks that each saved level uses only its authored field as grass, contains its route
        /// endpoints, and has a serialized runtime border ready for resolution/orientation changes.
        /// </summary>
        public static void ValidateSavedFarmViewportPresentation()
        {
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var scenePath in SavedScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var mapper = FindInScene<FarmRouteMapper>(scene);
                    var area = mapper != null ? mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>() : null;
                    var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                    var grass = FindAuthoredGrass(scene);
                    var waveController = FindInScene<WaveController>(scene);
                    var foodStore = FindInScene<FoodStore>(scene);
                    if (mapper == null || area == null || !area.HasBoundary || camera == null || grass == null ||
                        waveController == null || foodStore == null)
                    {
                        throw new InvalidOperationException(
                            $"{scenePath} is missing its Farm Route Mapper, playable area, Main Camera, grass, Wave Controller, or Food Store.");
                    }

                    if (!IsGrassAlignedToPlayableArea(grass, area.WorldBounds))
                    {
                        throw new InvalidOperationException(
                            $"{scene.name} displays grass outside or short of its Farm Playable Area. Run TAD/Level Tools/Apply Farm Ground + Exterior Borders.");
                    }

                    var border = area.GetComponent<FarmViewportBorder>();
                    if (border == null || border.PlayableArea != area || border.GameplayCamera != camera)
                    {
                        throw new InvalidOperationException(
                            $"{scene.name} is missing a configured Farm Viewport Border on its Farm Playable Area.");
                    }

                    if (!area.ContainsPoint(waveController.SpawnPosition) || !area.ContainsPoint(foodStore.transform.position))
                    {
                        throw new InvalidOperationException(
                            $"{scene.name} leaves its spawn point or Food Store outside the authored grass/playable field.");
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
        /// Validates only the non-layout exterior presentation contract: the existing border is
        /// configured with the shared light-wood sprite and each level camera has the matching
        /// solid fallback. It intentionally makes no assertion about authored map geometry.
        /// </summary>
        public static void ValidateSavedLightOakExteriorPresentation()
        {
            var (exteriorSprite, exteriorMaterial) = LoadExteriorArtwork();
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var scenePath in SavedScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                    var borders = FindAllInScene<FarmViewportBorder>(scene);
                    if (camera == null || borders.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{scenePath} is missing its Main Camera or an existing Farm Viewport Border.");
                    }

                    if (!GameplayCameraFramingAssetBuilder.UsesLightOakFallback(camera))
                    {
                        throw new InvalidOperationException(
                            $"{scene.name} does not use the light-oak camera fallback.");
                    }

                    foreach (var border in borders)
                    {
                        var area = border.GetComponent<FarmPlayableArea>();
                        if (area == null || border.PlayableArea != area || border.GameplayCamera != camera ||
                            border.ExteriorSprite != exteriorSprite || border.ExteriorMaterial != exteriorMaterial)
                        {
                            throw new InvalidOperationException(
                                $"{scene.name} has a Farm Viewport Border that is not configured for the light-oak exterior.");
                        }
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

        private static void RepairMissingPresentationAfterEditorStartup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnyLoadedSceneIsDirty())
            {
                return;
            }

            // Startup only refreshes the exterior references and fallback colour. It deliberately
            // never alters any authored map layout, grass footprint, endpoint, or camera framing.
            ApplyLightOakExteriorPresentationOnly();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall -= RepairMissingPresentationAfterEditorStartup;
                EditorApplication.delayCall += RepairMissingPresentationAfterEditorStartup;
            }
        }

        /// <summary>
        /// Updates only serialized art dependencies on components that are already present. This
        /// is intentionally separate from the legacy alignment pass so the presentation migration
        /// can never resize grass, create a border, move endpoints, or alter playable geometry.
        /// </summary>
        private static void ApplyExteriorArtworkOnly()
        {
            var (exteriorSprite, exteriorMaterial) = LoadExteriorArtwork();
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            var changedAnyScene = false;
            try
            {
                foreach (var scenePath in SavedScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                    var borders = FindAllInScene<FarmViewportBorder>(scene);
                    if (camera == null || borders.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"{scenePath} is missing its Main Camera or an existing Farm Viewport Border.");
                    }

                    var changed = false;
                    foreach (var border in borders)
                    {
                        var area = border.GetComponent<FarmPlayableArea>();
                        if (area == null)
                        {
                            throw new InvalidOperationException(
                                $"{scene.name} has a Farm Viewport Border without a Farm Playable Area.");
                        }

                        if (border.PlayableArea == area && border.GameplayCamera == camera &&
                            border.ExteriorSprite == exteriorSprite && border.ExteriorMaterial == exteriorMaterial)
                        {
                            continue;
                        }

                        // ConfigureForAuthoring only stores references; it never creates the
                        // runtime visual/collider children that are built on Awake.
                        border.ConfigureForAuthoring(camera, exteriorSprite, exteriorMaterial);
                        RecordPrefabOverride(border);
                        EditorUtility.SetDirty(border);
                        changed = true;
                    }

                    if (!changed)
                    {
                        continue;
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changedAnyScene = true;
                }
            }
            finally
            {
                if (restoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            if (changedAnyScene)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static void ApplyPresentation(bool forceGroundAlignment)
        {
            var (exteriorSprite, exteriorMaterial) = LoadExteriorArtwork();
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            var changedAnyScene = false;
            try
            {
                foreach (var scenePath in SavedScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var mapper = FindInScene<FarmRouteMapper>(scene);
                    var area = mapper != null ? mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>() : null;
                    var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                    var grass = FindAuthoredGrass(scene);
                    if (mapper == null || area == null || !area.HasBoundary || camera == null || grass == null)
                    {
                        throw new InvalidOperationException(
                            $"{scenePath} is missing its Farm Route Mapper, playable area, Main Camera, or TAD Grass Background.");
                    }

                    var changed = false;
                    if (forceGroundAlignment)
                    {
                        changed |= AlignGrassToPlayableArea(grass, area.WorldBounds);
                    }

                    var border = area.GetComponent<FarmViewportBorder>();
                    if (border == null)
                    {
                        border = area.gameObject.AddComponent<FarmViewportBorder>();
                        changed = true;
                    }

                    // The configured references are authored once and runtime-only children are
                    // created later by Awake. Comparing all dependencies means this repair is
                    // idempotent: ordinary editor starts do not churn scene files after the
                    // requested presentation has already been applied.
                    if (border.PlayableArea != area || border.GameplayCamera != camera ||
                        border.ExteriorSprite != exteriorSprite || border.ExteriorMaterial != exteriorMaterial)
                    {
                        border.ConfigureForAuthoring(camera, exteriorSprite, exteriorMaterial);
                        changed = true;
                    }

                    if (!changed)
                    {
                        continue;
                    }

                    RecordPrefabOverride(grass);
                    RecordPrefabOverride(grass.transform);
                    RecordPrefabOverride(border);
                    EditorUtility.SetDirty(grass);
                    EditorUtility.SetDirty(grass.transform);
                    EditorUtility.SetDirty(border);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changedAnyScene = true;
                }
            }
            finally
            {
                if (restoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            if (changedAnyScene)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static bool AlignGrassToPlayableArea(SpriteRenderer grass, Rect worldBounds)
        {
            var grassTransform = grass.transform;
            var changed = false;
            var targetPosition = new Vector3(worldBounds.center.x, worldBounds.center.y, grassTransform.position.z);
            if ((grassTransform.position - targetPosition).sqrMagnitude > BoundsTolerance * BoundsTolerance)
            {
                grassTransform.position = targetPosition;
                changed = true;
            }

            if (Quaternion.Angle(grassTransform.rotation, Quaternion.identity) > BoundsTolerance)
            {
                grassTransform.rotation = Quaternion.identity;
                changed = true;
            }

            if ((grassTransform.localScale - Vector3.one).sqrMagnitude > BoundsTolerance * BoundsTolerance)
            {
                grassTransform.localScale = Vector3.one;
                changed = true;
            }

            if (grass.drawMode != SpriteDrawMode.Tiled)
            {
                grass.drawMode = SpriteDrawMode.Tiled;
                changed = true;
            }

            // SpriteRenderer.size is measured before the inherited transform scale. Derive the
            // requested local size after the scale reset so this remains precise if a future
            // template nests the grass below a uniformly-scaled presentation root.
            var lossyScale = grassTransform.lossyScale;
            var targetSize = new Vector2(
                worldBounds.width / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)),
                worldBounds.height / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.y)));
            if ((grass.size - targetSize).sqrMagnitude > BoundsTolerance * BoundsTolerance)
            {
                grass.size = targetSize;
                changed = true;
            }

            return changed;
        }

        private static bool IsGrassAlignedToPlayableArea(SpriteRenderer grass, Rect areaBounds)
        {
            if (grass == null || grass.drawMode != SpriteDrawMode.Tiled)
            {
                return false;
            }

            var grassBounds = grass.bounds;
            return Mathf.Abs(grassBounds.min.x - areaBounds.xMin) <= BoundsTolerance &&
                   Mathf.Abs(grassBounds.min.y - areaBounds.yMin) <= BoundsTolerance &&
                   Mathf.Abs(grassBounds.max.x - areaBounds.xMax) <= BoundsTolerance &&
                   Mathf.Abs(grassBounds.max.y - areaBounds.yMax) <= BoundsTolerance;
        }

        /// <summary>
        /// Repairs the template only. Its original controller transform doubles as a prototype
        /// spawn point and lives beyond the small authored field, so a dedicated child anchor is
        /// added rather than moving the controller (which would also move its playable-area
        /// polygon). The food anchor is similarly brought just inside the bottom-right edge.
        /// </summary>
        private static void PrepareReusableTemplateRouteEndpoints()
        {
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                var scene = EditorSceneManager.OpenScene(LevelTemplateScenePath, OpenSceneMode.Single);
                var waveController = FindInScene<WaveController>(scene);
                var foodStore = FindInScene<FoodStore>(scene);
                if (waveController == null || foodStore == null)
                {
                    throw new InvalidOperationException(
                        "LevelTemplate is missing its Wave Controller or Food Store and cannot be prepared as a reusable level.");
                }

                var templateBounds = new Rect(-9.55f, -1.30f, 24.02f, 8.30f);
                const float EndpointInset = 0.5f;
                var spawnTarget = new Vector3(templateBounds.xMin + EndpointInset, templateBounds.yMax - EndpointInset, waveController.transform.position.z);
                var foodTarget = new Vector3(templateBounds.xMax - EndpointInset, templateBounds.yMin + EndpointInset, foodStore.transform.position.z);
                var changed = false;

                var spawnAnchor = waveController.SpawnPointTransform;
                if (spawnAnchor == waveController.transform)
                {
                    // Undo the old shared-controller placement if it was modified by a previous
                    // presentation pass, then create an independent child anchor.
                    var sourceTransform = PrefabUtility.GetCorrespondingObjectFromSource(waveController.transform) as Transform;
                    if (sourceTransform != null &&
                        (waveController.transform.localPosition - sourceTransform.localPosition).sqrMagnitude > BoundsTolerance * BoundsTolerance)
                    {
                        waveController.transform.localPosition = sourceTransform.localPosition;
                        RecordPrefabOverride(waveController.transform);
                        EditorUtility.SetDirty(waveController.transform);
                        changed = true;
                    }

                    var anchorObject = new GameObject("Template Attacker Spawn Point");
                    spawnAnchor = anchorObject.transform;
                    spawnAnchor.SetParent(waveController.transform, false);
                    var controllerProperties = new SerializedObject(waveController);
                    controllerProperties.FindProperty("spawnPoint").objectReferenceValue = spawnAnchor;
                    controllerProperties.ApplyModifiedPropertiesWithoutUndo();
                    RecordPrefabOverride(waveController);
                    EditorUtility.SetDirty(waveController);
                    changed = true;
                }

                if (!templateBounds.Contains(spawnAnchor.position) || changed)
                {
                    spawnAnchor.position = spawnTarget;
                    EditorUtility.SetDirty(spawnAnchor);
                    changed = true;
                }

                if (!templateBounds.Contains(foodStore.transform.position) || changed)
                {
                    foodStore.transform.position = foodTarget;
                    RecordPrefabOverride(foodStore.transform);
                    EditorUtility.SetDirty(foodStore.transform);
                    changed = true;
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    AssetDatabase.SaveAssets();
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

        private static (Sprite Sprite, Material Material) LoadExteriorArtwork()
        {
            var importer = AssetImporter.GetAtPath(LightWoodExteriorTexturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"The exterior needs the light-wood texture at {LightWoodExteriorTexturePath}.");
            }

            var importerChanged = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importerChanged = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importerChanged = true;
            }

            // SpriteRenderer's Tiled draw mode requires a full rectangular sprite mesh. A
            // generated texture imports as Tight by default, which produces a tiling warning
            // and can render magenta under the project's URP 2D renderer.
            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            if (spriteSettings.spriteMeshType != SpriteMeshType.FullRect)
            {
                spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(spriteSettings);
                importerChanged = true;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importerChanged = true;
            }

            if (Mathf.Abs(importer.spritePixelsPerUnit - LightWoodPixelsPerUnit) > 0.001f)
            {
                importer.spritePixelsPerUnit = LightWoodPixelsPerUnit;
                importerChanged = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                importerChanged = true;
            }

            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                importerChanged = true;
            }

            if (importerChanged)
            {
                importer.SaveAndReimport();
            }

            var lightWoodSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LightWoodExteriorTexturePath);
            if (lightWoodSprite == null)
            {
                throw new InvalidOperationException(
                    $"{LightWoodExteriorTexturePath} did not import as a single Sprite.");
            }

            // Leave the material empty so the renderer uses the project's default URP 2D Sprite
            // material. This keeps the honey-oak surround consistently light across maps.
            return (lightWoodSprite, null);
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
                    "Save or close the current untitled scene before applying the farm ground presentation.");
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

        private static void RecordPrefabOverride(UnityEngine.Object target)
        {
            if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private static SpriteRenderer FindAuthoredGrass(Scene scene)
        {
            return FindInScene<SpriteRenderer>(scene, candidate => candidate.gameObject.name == "TAD Grass Background");
        }

        private static T FindInScene<T>(Scene scene, Func<T, bool> predicate = null) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component != null && (predicate == null || predicate(component)));
        }

        private static T[] FindAllInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .Where(component => component != null)
                .ToArray();
        }
    }
}
#endif
