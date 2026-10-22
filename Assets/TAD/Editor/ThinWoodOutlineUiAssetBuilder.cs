using System;
using System.IO;
using System.Linq;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Editor
{
    /// <summary>
    /// Builds the reusable thin-wood button and migrates every shipped UI prefab and Main Menu
    /// panel to the same brass-free frame. It uses Unity's asset APIs so serialized UI is
    /// authored safely while the editor is open.
    /// </summary>
    internal static class ThinWoodOutlineUiAssetBuilder
    {
        private const string OutlineAssetPath = "Assets/TAD/Art/UI/TAD_ThinWoodOutline.png";
        private const string SquareFillAssetPath = "Assets/TAD/Art/UI/TAD_SquareUiFill.png";
        private const string BoldFontAssetPath = "Assets/TAD/Art/UI/Fonts/Coiny-Regular.ttf";
        private const string ButtonPrefabPath = "Assets/TAD/Prefabs/UI/ThinWoodOutlineButton.prefab";
        private const string PlayerLevelUpCardPrefabPath = "Assets/TAD/Prefabs/UI/PlayerLevelUpCard.prefab";
        private const string UiPrefabsFolder = "Assets/TAD/Prefabs/UI";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string CircularMaskSpritePath = "UI/Skin/Knob.psd";
        private const float CloseButtonEdgeInset = 18f;
        // The source artwork is high-resolution. A high PPU keeps its actual timber trim at
        // roughly nine pixels on a 52-pixel compact control instead of inheriting the very
        // heavy border of the older farm UI assets.
        private const float SpritePixelsPerUnit = 512f;
        private static readonly Vector4 SliceBorder = new(64f, 64f, 64f, 64f);

        [InitializeOnLoadMethod]
        private static void QueueInitialBuild()
        {
            // The first import creates the dedicated assets. Later edits are opt-in through the
            // menu command unless a required generated asset is genuinely missing.
            EditorApplication.delayCall += BuildInitialAssetsIfNeeded;
        }

        [MenuItem("TAD/UI/Build Thin Wood Outline Assets")]
        private static void BuildThinWoodOutlineAssets()
        {
            RebuildAllUiAssets();
        }

        /// <summary>Explicit entry point for CI or an editor command after shared UI art changes.</summary>
        public static void RebuildAllUiAssets()
        {
            CreateOrUpdateAssets();
        }

        private static void BuildInitialAssetsIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(ToAbsoluteProjectPath(OutlineAssetPath)) ||
                !NeedsInitialBuild())
            {
                return;
            }

            CreateOrUpdateAssets();
        }

        private static void CreateOrUpdateAssets()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var outlineSprite = ConfigureOutlineImporter();
                var squareFillSprite = CreateOrUpdateSquareFillSprite();
                var boldFont = AssetDatabase.LoadAssetAtPath<Font>(BoldFontAssetPath);
                if (outlineSprite == null)
                {
                    throw new InvalidOperationException($"Unable to import the outline sprite at '{OutlineAssetPath}'.");
                }

                if (squareFillSprite == null)
                {
                    throw new InvalidOperationException($"Unable to import the square fill sprite at '{SquareFillAssetPath}'.");
                }

                if (boldFont == null)
                {
                    throw new InvalidOperationException($"Unable to import the bold UI font at '{BoldFontAssetPath}'.");
                }

                CreateOrUpdateButtonPrefab(outlineSprite, boldFont);
                var levelUpCardPrefab = CreateOrUpdatePlayerLevelUpCardPrefab(outlineSprite);
                ApplyThinOutlineThemeToUiPrefabs(outlineSprite, squareFillSprite, levelUpCardPrefab, boldFont);
                ApplyThinOutlineThemeToMainMenu(outlineSprite, boldFont);
                AssetDatabase.SaveAssets();
                Debug.Log("TAD: Thin wood outlines are now applied to all UI panels and buttons.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"TAD: Could not build the thin wood UI assets. {exception}");
            }
        }

        private static Sprite ConfigureOutlineImporter()
        {
            var importer = AssetImporter.GetAtPath(OutlineAssetPath) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = SpritePixelsPerUnit;
            importer.spriteBorder = SliceBorder;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(OutlineAssetPath);
        }

        /// <summary>
        /// Provides an explicit opaque sprite for Filled Images. uGUI does not render a
        /// sprite-less Filled Image, which otherwise makes the food bar look empty at every
        /// value even though its fill amount is correct.
        /// </summary>
        private static Sprite CreateOrUpdateSquareFillSprite()
        {
            var absolutePath = ToAbsoluteProjectPath(SquareFillAssetPath);
            if (!File.Exists(absolutePath))
            {
                var directory = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                try
                {
                    var pixels = new Color32[8 * 8];
                    for (var index = 0; index < pixels.Length; index++)
                    {
                        pixels[index] = new Color32(255, 255, 255, 255);
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply(false, false);
                    File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                AssetDatabase.ImportAsset(SquareFillAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = AssetImporter.GetAtPath(SquareFillAssetPath) as TextureImporter;
            if (importer == null)
            {
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = Vector4.zero;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SquareFillAssetPath);
        }

        private static void CreateOrUpdateButtonPrefab(Sprite outlineSprite, Font boldFont)
        {
            var directory = Path.GetDirectoryName(ButtonPrefabPath);
            if (!string.IsNullOrWhiteSpace(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var buttonObject = new GameObject(
                "ThinWoodOutlineButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement),
                typeof(FarmUiThinWoodOutline));

            try
            {
                buttonObject.layer = LayerMask.NameToLayer("UI");
                if (buttonObject.layer < 0)
                {
                    buttonObject.layer = 5;
                }

                var buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.sizeDelta = new Vector2(220f, 72f);

                var image = buttonObject.GetComponent<Image>();
                image.raycastTarget = true;

                var outline = buttonObject.GetComponent<FarmUiThinWoodOutline>();
                outline.Configure(outlineSprite);

                var button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 0.88f, 1f);
                colors.pressedColor = new Color(0.72f, 0.84f, 0.55f, 1f);
                colors.selectedColor = new Color(0.94f, 1f, 0.74f, 1f);
                colors.disabledColor = new Color(0.46f, 0.46f, 0.42f, 0.75f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                button.colors = colors;

                var layout = buttonObject.GetComponent<LayoutElement>();
                layout.minWidth = 160f;
                layout.minHeight = 72f;
                layout.preferredWidth = 220f;
                layout.preferredHeight = 72f;

                CreateLabel(buttonObject.transform, boldFont);
                PrefabUtility.SaveAsPrefabAsset(buttonObject, ButtonPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
            }
        }

        /// <summary>
        /// Creates the shared surface for the player-level modal. The progression presenter adds
        /// current content at runtime, while future three-choice upgrades can extend this prefab
        /// without replacing the post-match flow.
        /// </summary>
        private static GameObject CreateOrUpdatePlayerLevelUpCardPrefab(Sprite outlineSprite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerLevelUpCardPrefabPath);
            if (existing != null)
            {
                return existing;
            }

            var directory = Path.GetDirectoryName(PlayerLevelUpCardPrefabPath);
            if (!string.IsNullOrWhiteSpace(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var cardObject = new GameObject(
                "PlayerLevelUpCard",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            try
            {
                cardObject.layer = LayerMask.NameToLayer("UI");
                if (cardObject.layer < 0)
                {
                    cardObject.layer = 5;
                }

                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(680f, 380f);
                var image = cardObject.GetComponent<Image>();
                image.color = new Color(0.94f, 0.86f, 0.66f, 1f);
                image.raycastTarget = true;
                FarmUiThinWoodOutline.EnsureFrame(image, outlineSprite);
                return PrefabUtility.SaveAsPrefabAsset(cardObject, PlayerLevelUpCardPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cardObject);
            }
        }

        private static void CreateLabel(Transform parent, Font boldFont)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.layer = parent.gameObject.layer;
            labelObject.transform.SetParent(parent, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 6f);
            labelRect.offsetMax = new Vector2(-20f, -6f);

            var label = labelObject.GetComponent<Text>();
            label.font = boldFont != null ? boldFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "BUTTON";
            label.alignment = TextAnchor.MiddleCenter;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20;
            label.resizeTextMaxSize = 32;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(0.24f, 0.105f, 0.03f, 1f);
            label.raycastTarget = false;
        }

        private static void ApplyThinOutlineThemeToUiPrefabs(
            Sprite outlineSprite,
            Sprite squareFillSprite,
            GameObject levelUpCardPrefab,
            Font boldFont)
        {
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var prefabPath in prefabPaths)
            {
                var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    EnsureSkin(prefabRoot, outlineSprite, boldFont);
                    RepairMaskedButtonTargets(prefabRoot);
                    FarmUiSkin.ApplyThinWoodOutlineTheme(prefabRoot.transform, outlineSprite, boldFont);
                    ConfigureFoodStoreHud(prefabRoot, squareFillSprite);
                    ConfigureAttackerQueuePanel(prefabRoot);
                    ConfigureCloseButtonPlacement(prefabRoot);
                    EnsurePostMatchExperiencePresentation(prefabRoot, levelUpCardPrefab);
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private static void ConfigureFoodStoreHud(GameObject prefabRoot, Sprite squareFillSprite)
        {
            var foodStoreHud = prefabRoot != null ? prefabRoot.GetComponent<FoodStoreHud>() : null;
            foodStoreHud?.ConfigureSquareFillSprite(squareFillSprite);
        }

        private static void ConfigureAttackerQueuePanel(GameObject prefabRoot)
        {
            var queuePanel = prefabRoot != null ? prefabRoot.GetComponent<AttackerQueuePanel>() : null;
            queuePanel?.ApplyResponsiveLayout();
        }

        /// <summary>
        /// Both terminal overlays share the same post-match account progression presentation.
        /// Keeping the component on their prefab roots means it inherits the overlay Canvas and
        /// stays available even when a level's view is instantiated by the day-cycle controller.
        /// </summary>
        private static void EnsurePostMatchExperiencePresentation(GameObject prefabRoot, GameObject levelUpCardPrefab)
        {
            if (prefabRoot == null)
            {
                return;
            }

            foreach (var attackerOverlay in prefabRoot.GetComponentsInChildren<AttackerWinOverlay>(true))
            {
                EnsurePresentationOnTerminalOverlay(attackerOverlay.gameObject, levelUpCardPrefab);
            }

            foreach (var defenderOverlay in prefabRoot.GetComponentsInChildren<DefenderWinOverlay>(true))
            {
                EnsurePresentationOnTerminalOverlay(defenderOverlay.gameObject, levelUpCardPrefab);
            }
        }

        private static void EnsurePresentationOnTerminalOverlay(GameObject terminalOverlayRoot, GameObject levelUpCardPrefab)
        {
            if (terminalOverlayRoot == null)
            {
                return;
            }

            var presentation = terminalOverlayRoot.GetComponent<PlayerMatchExperiencePresentation>();
            if (presentation == null)
            {
                presentation = terminalOverlayRoot.AddComponent<PlayerMatchExperiencePresentation>();
            }

            presentation.ConfigureLevelUpCardTemplate(levelUpCardPrefab);
        }

        /// <summary>
        /// Every ordinary modal close control belongs in the same familiar place. Layout-group
        /// children are intentionally excluded because their owner (for example Settings) is
        /// responsible for their row order instead of RectTransform anchors.
        /// </summary>
        private static void ConfigureCloseButtonPlacement(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button == null ||
                    (!string.Equals(button.name, "Close", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(button.name, "CloseButton", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var rect = button.transform as RectTransform;
                var parent = rect != null ? rect.parent : null;
                if (rect == null || parent == null || parent.GetComponent<LayoutGroup>() != null)
                {
                    continue;
                }

                rect.anchorMin = Vector2.one;
                rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = new Vector2(-CloseButtonEdgeInset, -CloseButtonEdgeInset);
                rect.SetAsLastSibling();
            }
        }

        private static void ApplyThinOutlineThemeToMainMenu(Sprite outlineSprite, Font boldFont)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                throw new InvalidOperationException($"The Main Menu scene was not found at '{MainMenuScenePath}'.");
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var menuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                foreach (var root in menuScene.GetRootGameObjects())
                {
                    foreach (var skin in root.GetComponentsInChildren<FarmUiSkin>(true))
                    {
                        skin.ConfigureThinWoodOutline(outlineSprite);
                        skin.ConfigureBoldFarmFont(boldFont);
                        EditorUtility.SetDirty(skin);
                    }

                    RepairMaskedButtonTargets(root);
                    FarmUiSkin.ApplyThinWoodOutlineTheme(root.transform, outlineSprite, boldFont);
                    ConfigureCloseButtonPlacement(root);
                }

                EditorSceneManager.SaveScene(menuScene);
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void EnsureSkin(GameObject prefabRoot, Sprite outlineSprite, Font boldFont)
        {
            var skin = prefabRoot.GetComponent<FarmUiSkin>();
            if (skin == null)
            {
                skin = prefabRoot.AddComponent<FarmUiSkin>();
            }

            skin.ConfigureThinWoodOutline(outlineSprite);
            skin.ConfigureBoldFarmFont(boldFont);
        }

        /// <summary>
        /// A uGUI Mask uses its owning Image's alpha as its stencil shape. Keep those images
        /// filled and circular; the regular transparent-centre timber frame cannot safely be
        /// used as the mask graphic.
        /// </summary>
        private static void RepairMaskedButtonTargets(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var circularMask = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircularMaskSpritePath);
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                var targetImage = button.targetGraphic as Image ?? button.GetComponent<Image>();
                var mask = targetImage != null ? targetImage.GetComponent<Mask>() : null;
                if (targetImage == null || mask == null)
                {
                    continue;
                }

                var directOutline = targetImage.GetComponent<FarmUiThinWoodOutline>();
                if (directOutline != null)
                {
                    UnityEngine.Object.DestroyImmediate(directOutline);
                }

                var oldFrame = targetImage.transform.Find(FarmUiThinWoodOutline.FrameObjectName);
                if (oldFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(oldFrame.gameObject);
                }

                targetImage.sprite = circularMask;
                targetImage.type = Image.Type.Simple;
                targetImage.fillCenter = true;
                targetImage.preserveAspect = false;
                targetImage.color = new Color(0.08f, 0.16f, 0.13f, 0.96f);
                targetImage.raycastTarget = true;
                mask.showMaskGraphic = true;
            }
        }

        private static bool NeedsInitialBuild()
        {
            var boldFont = AssetDatabase.LoadAssetAtPath<Font>(BoldFontAssetPath);
            if (boldFont == null)
            {
                // Wait for Unity's font importer rather than writing partial fallback styling.
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath) == null)
            {
                return true;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerLevelUpCardPrefabPath) == null)
            {
                return true;
            }

            var buttonLabel = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath)
                .GetComponentInChildren<Text>(true);
            return buttonLabel == null || buttonLabel.font != boldFont;
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }
    }
}
