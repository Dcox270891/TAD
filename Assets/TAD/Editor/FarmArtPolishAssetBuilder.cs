#if UNITY_EDITOR
using System;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Editor
{
    /// <summary>
    /// Applies the hand-authored farm-vista artwork to the menu through Unity's asset APIs.
    /// It intentionally changes only presentation references: game logic and navigation stay intact.
    /// </summary>
    public static class FarmArtPolishAssetBuilder
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MenuBackdropPath = "Assets/TAD/Art/Environment/Ground/TAD_FarmMenuBackdrop.png";

        [MenuItem("TAD/Visuals/Apply Farm Art Polish")]
        public static void ApplyFromMenu()
        {
            ApplyFromAutomation();
        }

        public static void ApplyFromAutomation()
        {
            var backdrop = PrepareBackdropSprite();
            if (backdrop == null)
            {
                throw new InvalidOperationException($"Farm menu backdrop is missing at '{MenuBackdropPath}'.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                throw new InvalidOperationException($"Main Menu scene is missing at '{MainMenuScenePath}'.");
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                var applied = 0;
                foreach (var skin in UnityEngine.Object.FindObjectsByType<FarmUiSkin>(FindObjectsInactive.Include))
                {
                    skin.ConfigureMenuBackdrop(backdrop);
                    skin.ApplyTheme();
                    EditorUtility.SetDirty(skin);
                    applied++;
                }

                foreach (var image in UnityEngine.Object.FindObjectsByType<Image>(FindObjectsInactive.Include))
                {
                    if (image == null || image.gameObject.name != "Background")
                    {
                        continue;
                    }

                    var isMainMenuBackground = false;
                    for (var current = image.transform.parent; current != null; current = current.parent)
                    {
                        if (current.name.Contains("MainMenu"))
                        {
                            isMainMenuBackground = true;
                            break;
                        }
                    }

                    if (!isMainMenuBackground)
                    {
                        continue;
                    }

                    image.sprite = backdrop;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                    image.color = Color.white;
                    EditorUtility.SetDirty(image);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"TAD applied the refreshed farm menu backdrop to {applied} UI skin(s).");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Sprite PrepareBackdropSprite()
        {
            var importer = AssetImporter.GetAtPath(MenuBackdropPath) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            var changed = importer.textureType != TextureImporterType.Sprite ||
                          importer.spriteImportMode != SpriteImportMode.Single ||
                          importer.mipmapEnabled ||
                          importer.wrapMode != TextureWrapMode.Clamp;
            if (changed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackdropPath);
        }
    }
}
#endif
