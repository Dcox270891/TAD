using System;
using System.Collections.Generic;
using System.IO;
using TAD.Levels;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Promotes the authored fourth map into the playable catalogue and Build Settings.
    /// The operation is deliberately idempotent so it only changes projects which have not
    /// yet been promoted.
    /// </summary>
    [InitializeOnLoad]
    internal static class LevelFourCatalogPromotion
    {
        private const string LevelFourScenePath = "Assets/Scenes/Levels/Level4.unity";
        private const string LevelFourDefinitionPath = "Assets/TAD/Data/Levels/Level4Definition.asset";
        private const string LevelCatalogPath = "Assets/TAD/Data/Levels/LevelCatalog.asset";
        private const string LegacyLevelThreeRepairPath = "Assets/TAD/Editor/LevelCatalogSceneRouteRepair.cs";

        static LevelFourCatalogPromotion()
        {
            EditorApplication.delayCall += PromoteIfNeeded;
        }

        [MenuItem("TAD/Maintenance/Promote Level 4")]
        private static void PromoteFromMenu()
        {
            PromoteIfNeeded();
        }

        private static void PromoteIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!File.Exists(LevelFourScenePath))
            {
                Debug.LogError("Level 4 promotion could not find the authored Level4 scene.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(LevelCatalogPath);
            if (catalog == null)
            {
                Debug.LogError("Level 4 promotion could not find the level catalogue.");
                return;
            }

            var levelFour = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelFourDefinitionPath);
            var createdDefinition = levelFour == null;
            if (createdDefinition)
            {
                levelFour = ScriptableObject.CreateInstance<LevelDefinition>();
                ConfigureDefinition(levelFour);
                AssetDatabase.CreateAsset(levelFour, LevelFourDefinitionPath);
            }

            var catalogChanged = EnsureCatalogContains(catalog, levelFour);
            var buildSettingsChanged = EnsureBuildSettingsContainsLevelFour();
            if (createdDefinition || catalogChanged || buildSettingsChanged)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Promoted Level 4 into the playable catalogue and Build Settings.");
            }

            RemoveCompletedLegacyRepair();
        }

        private static void ConfigureDefinition(LevelDefinition definition)
        {
            var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("id").stringValue = "level-04";
            serializedDefinition.FindProperty("displayName").stringValue = "Level 4: Harvest Run";
            serializedDefinition.FindProperty("sceneName").stringValue = "Level4";
            serializedDefinition.FindProperty("description").stringValue =
                "A terrain-heavy farm route with mud, ice, and layered cover.";
            serializedDefinition.FindProperty("unlocked").boolValue = true;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool EnsureCatalogContains(LevelCatalog catalog, LevelDefinition levelFour)
        {
            var serializedCatalog = new SerializedObject(catalog);
            var levels = serializedCatalog.FindProperty("levels");
            for (var index = 0; index < levels.arraySize; index++)
            {
                if (levels.GetArrayElementAtIndex(index).objectReferenceValue == levelFour)
                {
                    return false;
                }
            }

            levels.InsertArrayElementAtIndex(levels.arraySize);
            levels.GetArrayElementAtIndex(levels.arraySize - 1).objectReferenceValue = levelFour;
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return true;
        }

        private static bool EnsureBuildSettingsContainsLevelFour()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (var index = 0; index < scenes.Count; index++)
            {
                if (!string.Equals(scenes[index].path, LevelFourScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (scenes[index].enabled)
                {
                    return false;
                }

                scenes[index] = new EditorBuildSettingsScene(LevelFourScenePath, enabled: true);
                EditorBuildSettings.scenes = scenes.ToArray();
                return true;
            }

            var insertIndex = scenes.Count;
            for (var index = 0; index < scenes.Count; index++)
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(scenes[index].path), "Level3", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = index + 1;
                    break;
                }
            }

            scenes.Insert(insertIndex, new EditorBuildSettingsScene(LevelFourScenePath, enabled: true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        private static void RemoveCompletedLegacyRepair()
        {
            var levelThree = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/TAD/Data/Levels/Level3Definition.asset");
            if (levelThree == null || !string.Equals(levelThree.SceneName, "Level3", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<MonoScript>(LegacyLevelThreeRepairPath) != null)
            {
                AssetDatabase.DeleteAsset(LegacyLevelThreeRepairPath);
            }
        }
    }
}
