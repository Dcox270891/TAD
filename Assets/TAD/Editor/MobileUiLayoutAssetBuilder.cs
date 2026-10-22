#if UNITY_EDITOR
using System;
using System.Linq;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Editor
{
    /// <summary>Wires the responsive canvas controller into every reusable UI source prefab and main menu.</summary>
    public static class MobileUiLayoutAssetBuilder
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string PrefabSearchRoot = "Assets/TAD/Prefabs";

        [MenuItem("TAD/UI/Apply Responsive Mobile Layout")]
        public static void ApplyFromMenu()
        {
            ApplyFromAutomation();
        }

        /// <summary>Safe automation entry point; does not alter scene-instance overrides in level scenes.</summary>
        public static void ApplyFromAutomation()
        {
            var attachedCount = 0;
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchRoot });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var changed = false;
                    foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    {
                        if (canvas == null || canvas.GetComponent<CanvasScaler>() == null)
                        {
                            continue;
                        }

                        if (canvas.GetComponent<MobileUiLayoutController>() == null)
                        {
                            canvas.gameObject.AddComponent<MobileUiLayoutController>();
                            attachedCount++;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null)
            {
                var originalScene = SceneManager.GetActiveScene().path;
                var menuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                var changed = false;
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                {
                    if (canvas == null || canvas.GetComponent<CanvasScaler>() == null)
                    {
                        continue;
                    }

                    if (canvas.GetComponent<MobileUiLayoutController>() == null)
                    {
                        canvas.gameObject.AddComponent<MobileUiLayoutController>();
                        attachedCount++;
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(menuScene);
                    EditorSceneManager.SaveScene(menuScene);
                }

                if (!string.IsNullOrEmpty(originalScene) && originalScene != MainMenuScenePath)
                {
                    EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"TAD attached responsive mobile UI layout to {attachedCount} reusable Canvas roots.");
        }
    }
}
#endif
