#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TAD.Attackers;
using TAD.Defenders;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Editor
{
    /// <summary>
    /// Applies the cohesive carved-wood / parchment UI family to reusable screens using Unity's
    /// prefab and scene APIs. It deliberately never changes gameplay bindings or layout owners.
    /// </summary>
    internal static class FarmUiArtDirectionAssetBuilder
    {
        private const string ArtFolder = "Assets/TAD/Art/UI/FarmUiKit";
        private const string ButtonAssetPath = ArtFolder + "/TAD_WoodPlankButton.png";
        private const string CardAssetPath = ArtFolder + "/TAD_ParchmentInfoCard.png";
        private const string StatusBarFrameAssetPath = ArtFolder + "/TAD_WoodStatusBarFrame.png";
        private const string StatusBarFillAssetPath = ArtFolder + "/TAD_StatusBarFill.png";
        private const string OutlineAssetPath = "Assets/TAD/Art/UI/TAD_ThinWoodOutline.png";
        private const string FontAssetPath = "Assets/TAD/Art/UI/Fonts/Coiny-Regular.ttf";
        private const string UiPrefabFolder = "Assets/TAD/Prefabs/UI";
        private const string AttackerPrefabFolder = "Assets/TAD/Prefabs/Attackers";
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const float LandscapeMenuButtonHeight = 80f;
        private const float MainMenuPanelMinimumHeight = 780f;
        private const float LevelSelectionPanelMinimumHeight = 640f;

        private static readonly Vector4 ButtonSliceBorder = new(140f, 94f, 140f, 94f);
        private static readonly Vector4 CardSliceBorder = new(150f, 176f, 150f, 154f);
        private static readonly Vector4 StatusFrameSliceBorder = new(176f, 128f, 176f, 128f);
        private static readonly Vector4 StatusFillSliceBorder = new(16f, 8f, 16f, 8f);

        [InitializeOnLoadMethod]
        private static void QueueInitialFarmUiArtPass()
        {
            EditorApplication.delayCall -= ApplyInitialFarmUiArtPassIfNeeded;
            EditorApplication.delayCall += ApplyInitialFarmUiArtPassIfNeeded;
        }

        [MenuItem("TAD/UI/Apply Rustic Farm Art Direction")]
        public static void RebuildAllUiAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += RebuildAllUiAssets;
                return;
            }

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var buttonSprite = ConfigureSprite(ButtonAssetPath, 512f, ButtonSliceBorder);
                var cardSprite = ConfigureSprite(CardAssetPath, 512f, CardSliceBorder);
                // A high PPU lets this same visual remain crisp when drawn by a tiny world-space
                // health bar as well as by a larger uGUI HUD.
                var statusFrameSprite = ConfigureSprite(StatusBarFrameAssetPath, 8192f, StatusFrameSliceBorder);
                var statusFillSprite = CreateOrUpdateStatusFillSprite();
                var outlineSprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutlineAssetPath);
                var font = AssetDatabase.LoadAssetAtPath<Font>(FontAssetPath);
                if (buttonSprite == null || cardSprite == null || statusFrameSprite == null ||
                    statusFillSprite == null || font == null)
                {
                    throw new InvalidOperationException("One or more Farm UI art assets could not be imported.");
                }

                ApplyToUiPrefabs(buttonSprite, cardSprite, statusFrameSprite, statusFillSprite, outlineSprite, font);
                ApplyToWorldStatusBars(statusFrameSprite, statusFillSprite);
                ApplyToMainMenu(buttonSprite, cardSprite, statusFrameSprite, statusFillSprite, outlineSprite, font);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("TAD: Applied the rustic farm UI art direction to menus, cards, and status bars.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"TAD: Could not apply the rustic farm UI art direction. {exception}");
            }
        }

        private static void ApplyInitialFarmUiArtPassIfNeeded()
        {
            if (EditorApplication.isCompiling)
            {
                EditorApplication.update -= ApplyFarmUiArtAfterCompilation;
                EditorApplication.update += ApplyFarmUiArtAfterCompilation;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= ApplyFarmUiArtAfterReturningToEditMode;
                EditorApplication.playModeStateChanged += ApplyFarmUiArtAfterReturningToEditMode;
                return;
            }

            if (
                !File.Exists(ToAbsoluteProjectPath(ButtonAssetPath)) ||
                !File.Exists(ToAbsoluteProjectPath(CardAssetPath)) ||
                !File.Exists(ToAbsoluteProjectPath(StatusBarFrameAssetPath)) ||
                !NeedsFarmUiArtPass())
            {
                return;
            }

            RebuildAllUiAssets();
        }

        private static void ApplyFarmUiArtAfterCompilation()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            EditorApplication.update -= ApplyFarmUiArtAfterCompilation;
            EditorApplication.delayCall += ApplyInitialFarmUiArtPassIfNeeded;
        }

        private static void ApplyFarmUiArtAfterReturningToEditMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.playModeStateChanged -= ApplyFarmUiArtAfterReturningToEditMode;
            EditorApplication.delayCall += ApplyInitialFarmUiArtPassIfNeeded;
        }

        private static bool NeedsFarmUiArtPass()
        {
            var expectedButton = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonAssetPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TAD/Prefabs/UI/ThinWoodOutlineButton.prefab");
            var skin = prefab != null ? prefab.GetComponent<FarmUiSkin>() : null;
            if (expectedButton == null || skin == null || skin.WoodPlankButtonSprite != expectedButton ||
                skin.AppliedFarmUiLayoutRevision < FarmUiSkin.CurrentFarmUiLayoutRevision)
            {
                return true;
            }

            // The main menu owns several controls directly rather than through the shared
            // prefab.  Check its serialized references too, otherwise an open, unrelated
            // scene could leave the front door on the former visual family.
            var menuPath = ToAbsoluteProjectPath(MainMenuScenePath);
            if (!File.Exists(menuPath))
            {
                return false;
            }

            var buttonGuid = AssetDatabase.AssetPathToGUID(ButtonAssetPath);
            var cardGuid = AssetDatabase.AssetPathToGUID(CardAssetPath);
            var menuContents = File.ReadAllText(menuPath);
            return string.IsNullOrWhiteSpace(buttonGuid) || string.IsNullOrWhiteSpace(cardGuid) ||
                   !menuContents.Contains(buttonGuid) || !menuContents.Contains(cardGuid) ||
                   !SceneHasCurrentUiLayoutRevision(menuContents);
        }

        /// <summary>
        /// The shared button prefab can already be up to date while the two independent Main Menu
        /// canvases still use an older layout.  The latter owns the level-card template, so looking
        /// only at the prefab would skip the migration and leave its labels too close to the plank
        /// edge.  This small serialized check is deliberately read-only; the normal Unity API pass
        /// below remains the only code that changes the scene.
        /// </summary>
        private static bool SceneHasCurrentUiLayoutRevision(string menuContents)
        {
            if (string.IsNullOrEmpty(menuContents))
            {
                return false;
            }

            const string skinMarker = "m_EditorClassIdentifier: TAD.Runtime::TAD.UI.FarmUiSkin";
            var expectedRevisionMarker = $"appliedFarmUiLayoutRevision: {FarmUiSkin.CurrentFarmUiLayoutRevision}";
            return CountOccurrences(menuContents, skinMarker) > 0 &&
                   CountOccurrences(menuContents, skinMarker) == CountOccurrences(menuContents, expectedRevisionMarker);
        }

        private static int CountOccurrences(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static Sprite ConfigureSprite(string assetPath, float pixelsPerUnit, Vector4 border)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static Sprite CreateOrUpdateStatusFillSprite()
        {
            var absolutePath = ToAbsoluteProjectPath(StatusBarFillAssetPath);
            if (!File.Exists(absolutePath))
            {
                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                const int width = 128;
                const int height = 32;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                try
                {
                    var pixels = new Color32[width * height];
                    for (var y = 0; y < height; y++)
                    {
                        var vertical = y / (float)(height - 1);
                        var brightness = Mathf.Lerp(0.78f, 1f, vertical);
                        for (var x = 0; x < width; x++)
                        {
                            var horizontal = x / (float)(width - 1);
                            var radius = Mathf.Min(width * 0.18f, height * 0.45f);
                            var leftDistance = x + 0.5f;
                            var rightDistance = width - x - 0.5f;
                            var edgeDistance = Mathf.Min(leftDistance, rightDistance);
                            var alpha = Mathf.Clamp01(edgeDistance / Mathf.Max(1f, radius));
                            alpha *= Mathf.Clamp01(Mathf.Min(y + 0.75f, height - y - 0.25f) / Mathf.Max(1f, height * 0.24f));
                            var shine = 0.10f * Mathf.Sin(horizontal * Mathf.PI);
                            var value = Mathf.Clamp01(brightness + shine);
                            pixels[y * width + x] = new Color(value, value, value, Mathf.RoundToInt(alpha * 255f));
                        }
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                AssetDatabase.ImportAsset(StatusBarFillAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return ConfigureSprite(StatusBarFillAssetPath, 8192f, StatusFillSliceBorder);
        }

        private static void ApplyToUiPrefabs(
            Sprite buttonSprite,
            Sprite cardSprite,
            Sprite statusFrameSprite,
            Sprite statusFillSprite,
            Sprite outlineSprite,
            Font font)
        {
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);
            foreach (var prefabPath in prefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    ApplyThemeToRoot(root, buttonSprite, cardSprite, statusFrameSprite, statusFillSprite, outlineSprite, font);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void ApplyToWorldStatusBars(Sprite statusFrameSprite, Sprite statusFillSprite)
        {
            var attackerPaths = AssetDatabase.FindAssets("t:Prefab", new[] { AttackerPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);
            foreach (var prefabPath in attackerPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    foreach (var bar in root.GetComponentsInChildren<AttackerHealthBar>(true))
                    {
                        bar.ConfigureArtwork(statusFrameSprite, statusFillSprite);
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            var farmHand = AssetDatabase.LoadAssetAtPath<GameObject>(FarmHandPrefabPath);
            if (farmHand == null)
            {
                return;
            }

            var farmHandRoot = PrefabUtility.LoadPrefabContents(FarmHandPrefabPath);
            try
            {
                foreach (var bar in farmHandRoot.GetComponentsInChildren<FarmHandExperienceBar>(true))
                {
                    bar.ConfigureArtwork(statusFrameSprite, statusFillSprite);
                }

                PrefabUtility.SaveAsPrefabAsset(farmHandRoot, FarmHandPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(farmHandRoot);
            }
        }

        private static void ApplyToMainMenu(
            Sprite buttonSprite,
            Sprite cardSprite,
            Sprite statusFrameSprite,
            Sprite statusFillSprite,
            Sprite outlineSprite,
            Font font)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                return;
            }

            var scene = SceneManager.GetSceneByPath(MainMenuScenePath);
            var openedTemporarily = false;
            if (!scene.isLoaded)
            {
                // Do not replace or save the user's current working scene.  Opening this one
                // additively lets the art pass update just the menu asset, even while gameplay
                // work is open in the editor.
                var openMode = SceneManager.sceneCount > 0 ? OpenSceneMode.Additive : OpenSceneMode.Single;
                scene = EditorSceneManager.OpenScene(MainMenuScenePath, openMode);
                openedTemporarily = true;
            }

            if (scene.isDirty)
            {
                // An already-open Main Menu has unsaved user edits; leave it alone until it is
                // saved, rather than folding unrelated scene changes into the art update.
                if (openedTemporarily && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                return;
            }

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    ApplyThemeToRoot(root, buttonSprite, cardSprite, statusFrameSprite, statusFillSprite, outlineSprite, font);
                }

                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedTemporarily && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ApplyThemeToRoot(
            GameObject root,
            Sprite buttonSprite,
            Sprite cardSprite,
            Sprite statusFrameSprite,
            Sprite statusFillSprite,
            Sprite outlineSprite,
            Font font)
        {
            if (root == null)
            {
                return;
            }

            foreach (var skin in root.GetComponentsInChildren<FarmUiSkin>(true))
            {
                skin.ConfigureThinWoodOutline(outlineSprite);
                skin.ConfigureFarmArtDirection(buttonSprite, cardSprite, statusFrameSprite, statusFillSprite);
                skin.ConfigureBoldFarmFont(font);
                EditorUtility.SetDirty(skin);
            }

            // Some compact prefab roots contain the controls directly and do not yet own a skin.
            if (root.GetComponentInChildren<FarmUiSkin>(true) == null)
            {
                var skin = root.GetComponent<FarmUiSkin>() ?? root.AddComponent<FarmUiSkin>();
                skin.ConfigureThinWoodOutline(outlineSprite);
                skin.ConfigureFarmArtDirection(buttonSprite, cardSprite, statusFrameSprite, statusFillSprite);
                skin.ConfigureBoldFarmFont(font);
            }

            FarmUiSkin.ApplyFarmArtDirectionTheme(root.transform, outlineSprite, buttonSprite, cardSprite, font);
            NormalizeSharedButtonLayouts(root);
            foreach (var foodStoreHud in root.GetComponentsInChildren<FoodStoreHud>(true))
            {
                foodStoreHud.ConfigureFarmStatusBar(statusFrameSprite, statusFillSprite);
            }

            foreach (var defenderDetails in root.GetComponentsInChildren<DefenderDetailsPanel>(true))
            {
                defenderDetails.ConfigureFarmStatusBar(statusFrameSprite, statusFillSprite);
            }

            AttachAutoSizingToInformationCards(root);
        }

        /// <summary>
        /// Applies the common wood-frame breathing room through Unity APIs rather than changing
        /// serialized scene or prefab text. Only ordinary single-label menu controls receive a
        /// height adjustment; information cards and masked queue icons keep their bespoke layouts.
        /// </summary>
        private static void NormalizeSharedButtonLayouts(GameObject root)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                FarmUiSkin.EnsureReadableWoodButtonLayout(button);

                if (button.name == "LevelButtonTemplate" || HasAncestorNamed(button.transform, "MainMenuPanel"))
                {
                    FarmUiSkin.EnsureReadableWoodButtonLayout(button, LandscapeMenuButtonHeight);
                }
            }

            var mainMenuPanel = FindDescendant(root.transform, "MainMenuPanel") as RectTransform;
            if (mainMenuPanel != null)
            {
                mainMenuPanel.sizeDelta = new Vector2(
                    mainMenuPanel.sizeDelta.x,
                    Mathf.Max(mainMenuPanel.sizeDelta.y, MainMenuPanelMinimumHeight));
                EditorUtility.SetDirty(mainMenuPanel);
            }

            var levelSelectionPanel = FindDescendant(root.transform, "LevelSelectionPanel") as RectTransform;
            if (levelSelectionPanel != null)
            {
                levelSelectionPanel.sizeDelta = new Vector2(
                    levelSelectionPanel.sizeDelta.x,
                    Mathf.Max(levelSelectionPanel.sizeDelta.y, LevelSelectionPanelMinimumHeight));
                EditorUtility.SetDirty(levelSelectionPanel);
            }
        }

        private static Transform FindDescendant(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasAncestorNamed(Transform transform, string ancestorName)
        {
            for (var current = transform != null ? transform.parent : null; current != null; current = current.parent)
            {
                if (current.name == ancestorName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AttachAutoSizingToInformationCards(GameObject root)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (!IsInformationCard(image))
                {
                    continue;
                }

                var card = image.rectTransform;
                // A card may use a layout group to arrange its contents.  That group controls
                // the children, not the card's own RectTransform, so the responsive outer size
                // can safely grow without upsetting the button stack inside it.
                var autoSizer = card.GetComponent<FarmUiCardAutoSizer>() ?? card.gameObject.AddComponent<FarmUiCardAutoSizer>();
                autoSizer.Configure(Mathf.Max(1f, card.rect.height));
                EditorUtility.SetDirty(autoSizer);
            }
        }

        private static bool IsInformationCard(Image image)
        {
            if (image == null || image.GetComponent<Button>() != null || image.GetComponent<Mask>() != null)
            {
                return false;
            }

            var name = image.gameObject.name.ToLowerInvariant();
            return name == "card" || name.Contains("detailspanel") || name.Contains("infopanel") ||
                   name.Contains("upgradechoicepanel") || name.Contains("upgradepanel") ||
                   name.Contains("playerenergypanel") || name.Contains("playerlevelupcard") ||
                   name.Contains("messagepanel") || name.Contains("pausepanel") || name.Contains("winpanel");
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }
    }
}
#endif
