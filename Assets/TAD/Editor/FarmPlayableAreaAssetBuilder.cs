#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    /// Authors the non-physical playable-ground polygons saved with the shipped farm scenes.
    /// The old route map used the whole camera rectangle, which left a large unused outer ring
    /// around the wall layouts. These upper-lane polygons keep the intended gate route open for
    /// the largest current animal while preventing an attacker from escaping around the level.
    /// </summary>
    public static class FarmPlayableAreaAssetBuilder
    {
        private const string LevelTemplateScenePath = "Assets/Scenes/Templates/LevelTemplate.unity";

        private static readonly AreaDefinition[] SavedAreaDefinitions =
        {
            // Vertices are clockwise in world space. The 1.30 lower edge closes the unused lower
            // ring; the individual upper edges retain a complete path around each level's highest
            // full-cover gate for the 1.75-diameter Bison and all smaller shipped attackers.
            new(
                "Assets/Scenes/Levels/Level1.unity",
                new[]
                {
                    new Vector2(-18.44f, -1.30f),
                    new Vector2(-18.44f, 10.00f),
                    new Vector2(21.34f, 10.00f),
                    new Vector2(21.34f, -1.30f)
                }),
            new(
                "Assets/Scenes/Levels/Level2.unity",
                new[]
                {
                    new Vector2(-18.31f, -1.30f),
                    new Vector2(-18.31f, 10.35f),
                    new Vector2(20.65f, 10.35f),
                    new Vector2(20.65f, -1.30f)
                }),
            new(
                "Assets/Scenes/Levels/Level3.unity",
                new[]
                {
                    new Vector2(-18.48f, -1.30f),
                    new Vector2(-18.48f, 9.95f),
                    new Vector2(20.83f, 9.95f),
                    new Vector2(20.83f, -1.30f)
                }),
            new(
                "Assets/Scenes/Levels/Level4.unity",
                new[]
                {
                    new Vector2(-18.70f, -1.30f),
                    new Vector2(-18.70f, 10.40f),
                    new Vector2(20.59f, 10.40f),
                    new Vector2(20.59f, -1.30f)
                }),
            // The empty reusable template has its own compact, neutral envelope. Level designers
            // can reshape it after placing gates, but new levels no longer default to a route that
            // covers the entire camera rectangle.
            new(
                LevelTemplateScenePath,
                new[]
                {
                    new Vector2(-9.55f, -1.30f),
                    new Vector2(-9.55f, 7.00f),
                    new Vector2(14.47f, 7.00f),
                    new Vector2(14.47f, -1.30f)
                })
        };

        [InitializeOnLoadMethod]
        private static void QueueMissingAreaRepairAfterEditorStartup()
        {
            // Playable-area boundaries are designer-authored. Do not create or save scene
            // components automatically when the Editor opens or returns from Play Mode.
            // The explicit Level Tools command remains available for deliberate migrations.
        }

        [MenuItem("TAD/Level Tools/Apply Saved Farm Playable Areas")]
        public static void ApplySavedFarmPlayableAreas()
        {
            ApplySavedFarmPlayableAreas(forceDefinitions: true);
            ValidateSavedFarmPlayableAreas();
            Debug.Log("TAD applied the authored Farm Playable Areas to Level 1–4 and the reusable level template.");
        }

        /// <summary>Batch-mode entry point for validation and project automation.</summary>
        public static void ApplyAndValidateFromCommandLine()
        {
            ApplySavedFarmPlayableAreas(forceDefinitions: true);
            ValidateSavedFarmPlayableAreas();
        }

        /// <summary>
        /// Reapplies the fixed saved polygons without validating route endpoints. Presentation
        /// migration uses this after repairing a legacy template spawn anchor, so it can restore
        /// the exact authored field before its grass artwork is fitted to it.
        /// </summary>
        public static void ApplySavedFarmPlayableAreasForPresentation()
        {
            ApplySavedFarmPlayableAreas(forceDefinitions: true);
        }

        /// <summary>
        /// Verifies that every shipping level and template owns a non-physical boundary containing
        /// both route endpoints. It does not re-author designer-owned polygons.
        /// </summary>
        public static void ValidateSavedFarmPlayableAreas()
        {
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            try
            {
                foreach (var definition in SavedAreaDefinitions)
                {
                    var scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
                    var mapper = FindInScene<FarmRouteMapper>(scene);
                    var waveController = FindInScene<WaveController>(scene);
                    var foodStore = FindInScene<FoodStore>(scene);
                    if (mapper == null || waveController == null || foodStore == null)
                    {
                        throw new InvalidOperationException(
                            $"{definition.ScenePath} is missing its Farm Route Mapper, Wave Controller, or Food Store.");
                    }

                    var area = mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>();
                    if (area == null || !area.HasBoundary)
                    {
                        throw new InvalidOperationException($"{definition.ScenePath} has no authored Farm Playable Area.");
                    }

                    if (!area.ContainsPoint(waveController.SpawnPosition) ||
                        !area.ContainsPoint(foodStore.transform.position))
                    {
                        throw new InvalidOperationException(
                            $"{definition.ScenePath} leaves its spawn point or Food Store outside the Farm Playable Area.");
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

        private static void RepairMissingAreasAfterEditorStartup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnyLoadedSceneIsDirty())
            {
                return;
            }

            ApplySavedFarmPlayableAreas(forceDefinitions: false);
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += RepairMissingAreasAfterEditorStartup;
            }
        }

        private static void ApplySavedFarmPlayableAreas(bool forceDefinitions)
        {
            var setup = CaptureSceneSetupForSceneOperation(out var restoreSetup);
            var changedAnyScene = false;
            try
            {
                foreach (var definition in SavedAreaDefinitions)
                {
                    var scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Single);
                    var mapper = FindInScene<FarmRouteMapper>(scene);
                    var waveController = FindInScene<WaveController>(scene);
                    var foodStore = FindInScene<FoodStore>(scene);
                    if (mapper == null || waveController == null || foodStore == null)
                    {
                        throw new InvalidOperationException(
                            $"{definition.ScenePath} is missing its Farm Route Mapper, Wave Controller, or Food Store.");
                    }

                    var area = mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>();
                    var requiresBoundary = area == null || !area.HasBoundary;
                    if (!forceDefinitions && !requiresBoundary)
                    {
                        continue;
                    }

                    if (area == null)
                    {
                        area = mapper.gameObject.AddComponent<FarmPlayableArea>();
                    }

                    var localBoundary = new Vector2[definition.WorldBoundary.Length];
                    for (var index = 0; index < definition.WorldBoundary.Length; index++)
                    {
                        localBoundary[index] = (Vector2)area.transform.InverseTransformPoint(definition.WorldBoundary[index]);
                    }

                    area.SetLocalBoundary(localBoundary);
                    mapper.SetPlayableArea(area);
                    RecordPrefabOverride(area);
                    RecordPrefabOverride(mapper);
                    EditorUtility.SetDirty(area);
                    EditorUtility.SetDirty(mapper);
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

        /// <summary>
        /// Scene-manager setup restoration requires a persisted, active scene. The Unity
        /// command-line runner starts with none, so it simply leaves the last temporary scene
        /// open until the process exits. An editor session with an untitled scene is rejected
        /// rather than risking that scene's contents while level files are opened singly.
        /// </summary>
        private static SceneSetup[] CaptureSceneSetupForSceneOperation(out bool restoreSetup)
        {
            var hasLoadedScene = false;
            var hasUntitledScene = false;
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded)
                {
                    hasLoadedScene = true;
                    hasUntitledScene |= string.IsNullOrWhiteSpace(scene.path);
                }
            }

            if (!hasLoadedScene)
            {
                restoreSetup = false;
                return Array.Empty<SceneSetup>();
            }

            if (hasUntitledScene)
            {
                if (Application.isBatchMode)
                {
                    restoreSetup = false;
                    return Array.Empty<SceneSetup>();
                }

                throw new InvalidOperationException(
                    "Save or close the current untitled scene before applying saved farm playable areas.");
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

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component != null);
        }

        private readonly struct AreaDefinition
        {
            public AreaDefinition(string scenePath, Vector2[] worldBoundary)
            {
                ScenePath = scenePath;
                WorldBoundary = worldBoundary;
            }

            public string ScenePath { get; }
            public Vector2[] WorldBoundary { get; }
        }
    }
}
#endif
