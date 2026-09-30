using System;
using System.IO;
using TAD.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Editor
{
    /// <summary>
    /// Builds the small reusable thin-wood button and applies the same outline to the two
    /// gameplay information cards. It uses Unity's asset APIs so prefab and importer data are
    /// authored safely while the editor is open.
    /// </summary>
    internal static class ThinWoodOutlineUiAssetBuilder
    {
        private const string OutlineAssetPath = "Assets/TAD/Art/UI/TAD_ThinWoodOutline.png";
        private const string ButtonPrefabPath = "Assets/TAD/Prefabs/UI/ThinWoodOutlineButton.prefab";
        private const string AttackerDetailsPrefabPath = "Assets/TAD/Prefabs/UI/AttackerDetailsPanel.prefab";
        private const string DefenderDetailsPrefabPath = "Assets/TAD/Prefabs/UI/DefenderDetailsPanel.prefab";
        // The source artwork is high-resolution. A high PPU keeps its actual timber trim at
        // roughly nine pixels on a 52-pixel compact control instead of inheriting the very
        // heavy border of the older farm UI assets.
        private const float SpritePixelsPerUnit = 512f;
        private static readonly Vector4 SliceBorder = new(64f, 64f, 64f, 64f);
        private static readonly Color InformationCardInterior = new(0.94f, 0.86f, 0.66f, 0.97f);

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
                if (outlineSprite == null)
                {
                    throw new InvalidOperationException($"Unable to import the outline sprite at '{OutlineAssetPath}'.");
                }

                CreateOrUpdateButtonPrefab(outlineSprite);
                ApplyOutlineToInformationPanel(AttackerDetailsPrefabPath, outlineSprite);
                ApplyOutlineToInformationPanel(DefenderDetailsPrefabPath, outlineSprite);
                AssetDatabase.SaveAssets();
                Debug.Log("TAD: Thin wood outline button and information-card frames are ready.");
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

        private static void CreateOrUpdateButtonPrefab(Sprite outlineSprite)
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
                buttonRect.sizeDelta = new Vector2(180f, 52f);

                var image = buttonObject.GetComponent<Image>();
                image.raycastTarget = true;

                var outline = buttonObject.GetComponent<FarmUiThinWoodOutline>();
                outline.ConfigureForAuthoring(outlineSprite);

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
                layout.minWidth = 120f;
                layout.minHeight = 40f;
                layout.preferredWidth = 180f;
                layout.preferredHeight = 52f;

                CreateLabel(buttonObject.transform);
                PrefabUtility.SaveAsPrefabAsset(buttonObject, ButtonPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
            }
        }

        private static void CreateLabel(Transform parent)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
            labelObject.layer = parent.gameObject.layer;
            labelObject.transform.SetParent(parent, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 6f);
            labelRect.offsetMax = new Vector2(-20f, -6f);

            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "BUTTON";
            label.alignment = TextAnchor.MiddleCenter;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 24;
            label.fontStyle = FontStyle.Bold;
            label.color = new Color(1f, 0.97f, 0.82f, 1f);
            label.raycastTarget = false;

            var shadow = labelObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(0.16f, 0.07f, 0.02f, 0.88f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);
            shadow.useGraphicAlpha = true;
        }

        private static void ApplyOutlineToInformationPanel(string prefabPath, Sprite outlineSprite)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var card = FindChildImage(prefabRoot.transform, "Card");
                if (card == null)
                {
                    throw new InvalidOperationException($"The information card was not found in '{prefabPath}'.");
                }

                // If an interrupted older setup put the outline on the card itself, remove it
                // before moving the decorative trim to its own non-interactive overlay.
                var cardOutline = card.GetComponent<FarmUiThinWoodOutline>();
                if (cardOutline != null)
                {
                    UnityEngine.Object.DestroyImmediate(cardOutline);
                }

                // The outline has no centre fill by design. A quiet, flat parchment surface
                // preserves the existing dark copy treatment without bringing back a brass
                // panel or decorative corners.
                card.sprite = null;
                card.type = Image.Type.Simple;
                card.fillCenter = true;
                card.color = InformationCardInterior;

                var frame = card.transform.Find("ThinWoodOutlineFrame");
                if (frame == null)
                {
                    var frameObject = new GameObject(
                        "ThinWoodOutlineFrame",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image),
                        typeof(FarmUiThinWoodOutline));
                    frameObject.layer = card.gameObject.layer;
                    frameObject.transform.SetParent(card.transform, false);
                    frame = frameObject.transform;
                }

                var frameRect = frame.GetComponent<RectTransform>();
                frameRect.anchorMin = Vector2.zero;
                frameRect.anchorMax = Vector2.one;
                frameRect.pivot = new Vector2(0.5f, 0.5f);
                frameRect.anchoredPosition = Vector2.zero;
                frameRect.sizeDelta = Vector2.zero;

                var frameImage = frame.GetComponent<Image>();
                frameImage.raycastTarget = false;
                var outline = frame.GetComponent<FarmUiThinWoodOutline>();
                outline.ConfigureForAuthoring(outlineSprite);
                frame.SetAsLastSibling();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Image FindChildImage(Transform root, string childName)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.gameObject.name == childName)
                {
                    return image;
                }
            }

            return null;
        }

        private static bool NeedsInitialBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath) == null)
            {
                return true;
            }

            return !HasThinOutlineFrame(AttackerDetailsPrefabPath) ||
                   !HasThinOutlineFrame(DefenderDetailsPrefabPath);
        }

        private static bool HasThinOutlineFrame(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return false;
            }

            var card = FindChildImage(prefab.transform, "Card");
            return card != null && card.transform.Find("ThinWoodOutlineFrame") != null;
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }
    }
}
