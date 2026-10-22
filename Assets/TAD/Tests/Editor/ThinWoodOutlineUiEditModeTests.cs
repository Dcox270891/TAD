using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FarmUiArtDirectionEditModeTests
    {
        private const string ButtonPath = "Assets/TAD/Art/UI/FarmUiKit/TAD_WoodPlankButton.png";
        private const string CardPath = "Assets/TAD/Art/UI/FarmUiKit/TAD_ParchmentInfoCard.png";
        private const string StatusFramePath = "Assets/TAD/Art/UI/FarmUiKit/TAD_WoodStatusBarFrame.png";
        private const string StatusFillPath = "Assets/TAD/Art/UI/FarmUiKit/TAD_StatusBarFill.png";
        private const string BoldFontPath = "Assets/TAD/Art/UI/Fonts/Coiny-Regular.ttf";
        private const string ButtonPrefabPath = "Assets/TAD/Prefabs/UI/ThinWoodOutlineButton.prefab";

        [Test]
        public void FarmUiArtSpritesAreImportedAsStretchSafeNineSlices()
        {
            // The generated art has deliberately uneven hand-hewn edges.  A 72px slice is
            // sufficient to preserve those corners at the smallest mobile control size while
            // leaving a generous centre region for dynamically sized copy.
            AssertNineSlice(ButtonPath, 512f, 72f);
            AssertNineSlice(CardPath, 512f, 96f);
            AssertNineSlice(StatusFramePath, 8192f, 72f);
            AssertNineSlice(StatusFillPath, 8192f, 8f);
        }

        [Test]
        public void SharedButtonPrefabUsesTheWoodenPlankSurfaceAndKeepsItsLabelClickableState()
        {
            var buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            var image = prefab.GetComponent<Image>();
            var button = prefab.GetComponent<Button>();
            var label = prefab.GetComponentInChildren<Text>(true);
            Assert.That(button, Is.Not.Null);
            Assert.That(image, Is.Not.Null);
            Assert.That(image.sprite, Is.EqualTo(buttonSprite));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.fillCenter, Is.True);
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.raycastTarget, Is.False);
            Assert.That(prefab.GetComponent<LayoutElement>().minHeight, Is.GreaterThanOrEqualTo(72f));
            Assert.That(prefab.GetComponent<LayoutElement>().preferredHeight, Is.GreaterThanOrEqualTo(72f));
            AssertReadableWoodLabelPadding(label);
        }

        [Test]
        public void EveryShippedUiPrefabUsesTheSharedFarmArtAndReadableBoldFont()
        {
            var buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            var cardSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardPath);
            var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFramePath);
            var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFillPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>(BoldFontPath);
            Assert.That(font, Is.Not.Null);

            foreach (var prefabPath in FindUiPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);
                var skin = prefab.GetComponent<FarmUiSkin>();
                Assert.That(skin, Is.Not.Null, $"{prefabPath} is missing its shared farm UI skin.");
                Assert.That(skin.BoldFarmFont, Is.EqualTo(font));
                Assert.That(skin.WoodPlankButtonSprite, Is.EqualTo(buttonSprite));
                Assert.That(skin.ParchmentCardSprite, Is.EqualTo(cardSprite));
                Assert.That(skin.StatusBarFrameSprite, Is.EqualTo(frameSprite));
                Assert.That(skin.StatusBarFillSprite, Is.EqualTo(fillSprite));

                foreach (var text in prefab.GetComponentsInChildren<Text>(true))
                {
                    Assert.That(text.font, Is.EqualTo(font), $"{prefabPath}/{text.name} is using a fallback font.");
                    var isCompactAttackerBadge = text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null ||
                                                 text.GetComponentInParent<AttackerArchetypeBadge>(true) != null;
                    var sharedPadding = text.GetComponent<FarmUiTextPadding>();
                    // Some generated card controls preserve a legacy visual child hierarchy.
                    // The padding component records its direct button owner, which is a more
                    // reliable ownership check for those serialized prefab children.
                    var isButtonLabel = text.GetComponentInParent<Button>(true) != null ||
                                        (sharedPadding != null && sharedPadding.ContentOwner != null &&
                                         sharedPadding.ContentOwner.GetComponent<Button>() != null);
                    // A card may host a deliberately compact action such as "Remove from queue".
                    // Those labels retain a readable 14pt floor after their four-sided padding is
                    // applied, while menu/HUD copy remains at the standard 16pt floor.
                    var minimumReadableSize = isCompactAttackerBadge ? 7 : isButtonLabel ? 14 : 16;
                    Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(minimumReadableSize),
                        $"{prefabPath}/{text.name} must use a readable size for its display role.");
                    if (isButtonLabel)
                    {
                        Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow),
                            $"{prefabPath}/{text.name} must remain a single-line button label.");
                        Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                    }
                    else
                    {
                        Assert.That(text.horizontalOverflow, Is.Not.EqualTo(HorizontalWrapMode.Overflow),
                            $"{prefabPath}/{text.name} can paint outside its card or panel.");
                    }
                    Assert.That(text.GetComponents<Shadow>(), Is.Empty,
                        $"{prefabPath}/{text.name} retains a blurred shadow or outline.");
                }
            }
        }

        [Test]
        public void ButtonsUseWoodenPlanksWithoutBreakingCircularQueueMasks()
        {
            var buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            Assert.That(buttonSprite, Is.Not.Null);
            foreach (var prefabPath in FindUiPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                foreach (var button in prefab.GetComponentsInChildren<Button>(true))
                {
                    var target = button.targetGraphic as Image ?? button.GetComponent<Image>();
                    Assert.That(target, Is.Not.Null, $"{prefabPath}/{button.name} has no image target.");
                    if (target.GetComponent<Mask>() != null)
                    {
                        Assert.That(target.type, Is.EqualTo(Image.Type.Simple));
                        Assert.That(target.fillCenter, Is.True);
                        Assert.That(target.GetComponent<FarmUiThinWoodOutline>(), Is.Null);
                        continue;
                    }

                    Assert.That(target.sprite, Is.EqualTo(buttonSprite), $"{prefabPath}/{button.name} is not a wood-plank control.");
                    Assert.That(target.type, Is.EqualTo(Image.Type.Sliced));
                    Assert.That(target.fillCenter, Is.True);
                    Assert.That(target.GetComponent<FarmUiThinWoodOutline>(), Is.Null);
                    var legacySurface = target.transform.Find("FarmUiButtonSurface");
                    Assert.That(legacySurface == null || !legacySurface.gameObject.activeSelf, Is.True,
                        $"{prefabPath}/{button.name} still overlays its new button art with the former flat surface.");
                }
            }
        }

        [TestCase("Assets/TAD/Prefabs/UI/AttackerDetailsPanel.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/DefenderDetailsPanel.prefab")]
        public void InformationCardsUseParchmentSurfacesAndCanGrowForWrappedCopy(string prefabPath)
        {
            var cardSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var card = FindChild(prefab.transform, "Card");
            Assert.That(card, Is.Not.Null);

            var image = card.GetComponent<Image>();
            Assert.That(image, Is.Not.Null);
            Assert.That(image.sprite, Is.EqualTo(cardSprite));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.fillCenter, Is.True);
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(card.GetComponent<FarmUiCardAutoSizer>(), Is.Not.Null,
                "Information cards need to grow when current stats or descriptions require more room.");
        }

        [TestCase("Assets/TAD/Prefabs/UI/DefenderLevelUpgradeOverlay.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/ModalOverlay.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/PauseOverlay.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/AttackerWinOverlay.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/PlayerLevelUpCard.prefab")]
        public void PlayerFacingDialogsCanGrowToContainLongerCopy(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<FarmUiCardAutoSizer>(true), Is.Not.Null,
                $"{prefabPath} needs a responsive parchment card for longer player-facing text.");
        }

        [Test]
        public void FoodAndExperienceBarsUseTheSharedRecessedWoodFrame()
        {
            var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFramePath);
            var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFillPath);
            const string foodPrefabPath = "Assets/TAD/Prefabs/UI/FoodStoreHud.prefab";
            var foodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(foodPrefabPath);
            var background = FindChild(foodPrefab.transform, "Background")?.GetComponent<Image>();
            var fill = FindChild(foodPrefab.transform, "Fill")?.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(fill, Is.Not.Null);
            Assert.That(background.sprite, Is.EqualTo(frameSprite));
            Assert.That(background.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(fill.sprite, Is.EqualTo(fillSprite));
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            Assert.That(fill.rectTransform.offsetMin, Is.EqualTo(new Vector2(6f, 6f)));
            Assert.That(fill.rectTransform.offsetMax, Is.EqualTo(new Vector2(-6f, -6f)));
            Assert.That(background.transform.Find("ThinWoodOutlineFrame"), Is.Null,
                "The recessed status-bar frame already contains its wood border.");

            var defenderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TAD/Prefabs/Defenders/FarmHand.prefab");
            var defenderBar = defenderPrefab.GetComponentInChildren<FarmHandExperienceBar>(true);
            AssertSerializedSprite(defenderBar, "barFrameSprite", frameSprite);
            AssertSerializedSprite(defenderBar, "barFillSprite", fillSprite);

            foreach (var attackerPath in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/Attackers" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(attackerPath));
                var attackerBar = prefab.GetComponentInChildren<AttackerHealthBar>(true);
                AssertSerializedSprite(attackerBar, "barFrameSprite", frameSprite);
                AssertSerializedSprite(attackerBar, "barFillSprite", fillSprite);
            }
        }

        [Test]
        public void MainMenuInheritsTheWoodenAndParchmentFamily()
        {
            var buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            var cardSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardPath);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var button in root.GetComponentsInChildren<Button>(true))
                    {
                        var target = button.targetGraphic as Image ?? button.GetComponent<Image>();
                        if (target != null && target.GetComponent<Mask>() == null)
                        {
                            Assert.That(target.sprite, Is.EqualTo(buttonSprite));
                        }
                    }

                    foreach (var image in root.GetComponentsInChildren<Image>(true))
                    {
                        if (image.gameObject.name.IndexOf("Panel", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                            image.GetComponent<Button>() == null)
                        {
                            Assert.That(image.sprite, Is.EqualTo(cardSprite));
                        }
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void MainMenuAndLevelSelectionButtonsReserveRoomForBoldWoodLabels()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects()
                    .FirstOrDefault(candidate => FindChild(candidate.transform, "MainMenuPanel") != null);
                Assert.That(root, Is.Not.Null);

                var mainMenuPanel = FindChild(root.transform, "MainMenuPanel") as RectTransform;
                Assert.That(mainMenuPanel, Is.Not.Null);
                Assert.That(mainMenuPanel.rect.height, Is.GreaterThanOrEqualTo(780f));

                foreach (var buttonName in new[]
                         {
                             "PlayButton", "SelectLevelButton", "SettingsButton", "PlayerUpgradesButton", "ExitGameButton"
                         })
                {
                    var button = FindChild(root.transform, buttonName)?.GetComponent<Button>();
                    Assert.That(button, Is.Not.Null, buttonName + " is missing from the main menu.");
                    Assert.That(button.GetComponent<LayoutElement>().minHeight, Is.GreaterThanOrEqualTo(80f));
                    AssertReadableWoodLabelPadding(button.GetComponentInChildren<Text>(true));
                }

                // The menu and its level-picker overlay deliberately live on separate canvases so
                // the modal can always render above the menu.  Find the template in the opened
                // scene rather than assuming it shares the MainMenuPanel's canvas root.
                var template = FindChildInScene(scene, "LevelButtonTemplate")?.GetComponent<Button>();
                Assert.That(template, Is.Not.Null);
                Assert.That(template.GetComponent<LayoutElement>().minHeight, Is.GreaterThanOrEqualTo(80f));
                AssertReadableWoodLabelPadding(template.GetComponentInChildren<Text>(true));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ResourceBankHudCreatesWoodPlaquesBeforeTheFirstBankEvent()
        {
            const string prefabPath = "Assets/TAD/Prefabs/UI/ResourceBanksHud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            // Instantiate a plain scene clone, matching the runtime hierarchy.  A connected
            // editor prefab instance is intentionally not allowed to re-parent its children, but
            // the HUD creates its presentation-only wood plaques at runtime before bank events.
            var instance = Object.Instantiate(prefab);
            try
            {
                Assert.That(instance, Is.Not.Null);
                var skin = instance.GetComponent<FarmUiSkin>() ?? instance.GetComponentInChildren<FarmUiSkin>(true);
                Assert.That(skin, Is.Not.Null);
                skin.ApplyThemeTo(instance.transform);
                foreach (var labelName in new[] { "AttackerGoldText", "AttackerIncomeText", "DefenderGoldText" })
                {
                    var plaque = instance.transform.Find("FarmUiBadge_" + labelName);
                    Assert.That(plaque, Is.Not.Null, labelName + " is missing its wood plaque.");
                    Assert.That(plaque.GetComponent<Image>(), Is.Not.Null);
                    Assert.That(plaque.GetComponent<Image>().raycastTarget, Is.False);
                    Assert.That(plaque.Find(labelName), Is.Not.Null, labelName + " is not contained by its plaque.");
                }
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void CloseButtonsRemainInTheTopRightCornerOfModalCards()
        {
            foreach (var prefabPath in FindUiPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                AssertCloseButtonsAreTopRight(prefab.transform, prefabPath);
            }
        }

        [Test]
        public void AbsolutePositionedButtonLabel_ReceivesFourSidedPaddingAndStaysOnOneLine()
        {
            var canvasRoot = new GameObject(
                "CardCopyPaddingTest",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(FarmUiSkin));
            var buttonObject = new GameObject(
                "ChoiceCard",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            try
            {
                buttonObject.transform.SetParent(canvasRoot.transform, false);
                var buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.sizeDelta = new Vector2(220f, 128f);

                textObject.transform.SetParent(buttonObject.transform, false);
                var text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = "Select Random Level";
                text.rectTransform.anchorMin = new Vector2(0f, 1f);
                text.rectTransform.anchorMax = new Vector2(1f, 1f);
                text.rectTransform.pivot = new Vector2(0.5f, 1f);
                // Deliberately reproduce the former edge-crossing layout; the shared skin owns
                // its correction without a prefab-specific position rule.
                text.rectTransform.anchoredPosition = new Vector2(0f, 10f);
                text.rectTransform.sizeDelta = new Vector2(24f, 64f);

                canvasRoot.GetComponent<FarmUiSkin>().ApplyTheme();
                var padding = text.GetComponent<FarmUiTextPadding>();
                Assert.That(padding, Is.Not.Null);
                Assert.That(padding.ContentOwner, Is.EqualTo(buttonRect));
                Assert.That(padding.HorizontalPadding, Is.GreaterThanOrEqualTo(18f));
                Assert.That(padding.VerticalPadding, Is.GreaterThanOrEqualTo(8f));
                Assert.That(padding.KeepsSingleLine, Is.True);

                Canvas.ForceUpdateCanvases();
                padding.ApplyNow();
                AssertTextFitsInside(text.rectTransform, buttonRect, padding.HorizontalPadding, padding.VerticalPadding);
                Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
                Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                Assert.That(text.resizeTextForBestFit, Is.False,
                    "The shared padding component owns the deterministic single-line fit.");
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 0.1f),
                    "The button label must shrink to its padded width rather than wrap.");
            }
            finally
            {
                Object.DestroyImmediate(canvasRoot);
            }
        }

        [Test]
        public void DirectNarrowButton_GrowsItsWoodSurfaceBeforeShrinkingBelowReadableText()
        {
            var canvasRoot = new GameObject(
                "DirectButtonReadableWidthTest",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(FarmUiSkin));
            var buttonObject = new GameObject(
                "RemoveFromQueue",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            try
            {
                var canvasRect = canvasRoot.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(480f, 300f);

                buttonObject.transform.SetParent(canvasRoot.transform, false);
                var buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.sizeDelta = new Vector2(120f, 56f);

                textObject.transform.SetParent(buttonObject.transform, false);
                var text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = "REMOVE FROM QUEUE";
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.sizeDelta = Vector2.zero;

                canvasRoot.GetComponent<FarmUiSkin>().ApplyTheme();
                var padding = text.GetComponent<FarmUiTextPadding>();
                Assert.That(padding, Is.Not.Null);

                Canvas.ForceUpdateCanvases();
                padding.ApplyNow();
                Assert.That(buttonRect.rect.width, Is.GreaterThan(120f),
                    "The wood surface should grow before a regular mobile action label becomes illegible.");
                Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(14));
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 0.1f));
                AssertTextFitsInside(text.rectTransform, buttonRect, padding.HorizontalPadding, padding.VerticalPadding);
            }
            finally
            {
                Object.DestroyImmediate(canvasRoot);
            }
        }

        [Test]
        public void CombatRatingBadge_KeepsBothValuesInsideItsClippedStarInterior()
        {
            var canvasRoot = new GameObject(
                "CombatRatingBadgeTest",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(FarmUiSkin));
            var attackerImageObject = new GameObject(
                "AttackerImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var definition = ScriptableObject.CreateInstance<AttackerDefinition>();
            try
            {
                attackerImageObject.transform.SetParent(canvasRoot.transform, false);
                var attackerImage = attackerImageObject.GetComponent<Image>();
                attackerImage.rectTransform.sizeDelta = new Vector2(96f, 96f);

                var badge = AttackerCombatRatingBadge.ShowFor(attackerImage, definition);
                Assert.That(badge, Is.Not.Null);
                var safeArea = badge.transform.Find("RatingSafeArea") as RectTransform;
                Assert.That(safeArea, Is.Not.Null);
                Assert.That(safeArea.GetComponent<RectMask2D>(), Is.Not.Null,
                    "The star needs an explicit clip so a compact card can never paint its rating outside the shape.");

                var rating = safeArea.Find("Rating")?.GetComponent<Text>();
                var sendSize = safeArea.Find("SendSize")?.GetComponent<Text>();
                Assert.That(rating, Is.Not.Null);
                Assert.That(sendSize, Is.Not.Null);
                Canvas.ForceUpdateCanvases();
                AssertTextFitsInside(rating.rectTransform, safeArea, 0f, 0f);
                AssertTextFitsInside(sendSize.rectTransform, safeArea, 0f, 0f);
                Assert.That(rating.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
                Assert.That(rating.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                Assert.That(sendSize.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
                Assert.That(sendSize.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(canvasRoot);
            }
        }

        [Test]
        public void EveryButtonLabelUsesSharedFourSidedPaddingAndSingleLineMetrics()
        {
            foreach (var prefabPath in FindUiPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);
                foreach (var button in prefab.GetComponentsInChildren<Button>(true))
                {
                    foreach (var label in button.GetComponentsInChildren<Text>(true))
                    {
                        Assert.That(label.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow),
                            $"{prefabPath}/{button.name}/{label.name} can wrap onto a second line.");
                        Assert.That(label.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));

                        if (label.GetComponentInParent<AttackerCombatRatingBadge>(true) != null ||
                            label.GetComponentInParent<AttackerArchetypeBadge>(true) != null)
                        {
                            // Compact badges own their separate clipped / inset presentation.
                            continue;
                        }

                        var padding = label.GetComponent<FarmUiTextPadding>();
                        Assert.That(padding, Is.Not.Null,
                            $"{prefabPath}/{button.name}/{label.name} is missing shared button padding.");
                        Assert.That(padding.HorizontalPadding, Is.GreaterThan(0f));
                        Assert.That(padding.VerticalPadding, Is.GreaterThan(0f));
                        Assert.That(padding.KeepsSingleLine, Is.True);
                    }
                }
            }
        }

        private static string[] FindUiPrefabPaths()
        {
            return System.Array.ConvertAll(
                AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" }),
                AssetDatabase.GUIDToAssetPath);
        }

        private static void AssertNineSlice(string path, float pixelsPerUnit, float minimumBorder)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(sprite, Is.Not.Null, path);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(pixelsPerUnit).Within(0.01f));
            Assert.That(sprite.border.x, Is.GreaterThanOrEqualTo(minimumBorder));
            Assert.That(sprite.border.y, Is.GreaterThanOrEqualTo(minimumBorder));
        }

        private static void AssertSerializedSprite(Object target, string propertyName, Sprite expected)
        {
            Assert.That(target, Is.Not.Null);
            var property = new SerializedObject(target).FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.EqualTo(expected));
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindChildInScene(UnityEngine.SceneManagement.Scene scene, string childName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var child = FindChild(root.transform, childName);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static void AssertReadableWoodLabelPadding(Text label)
        {
            Assert.That(label, Is.Not.Null);
            Assert.That(label.rectTransform.offsetMin.x, Is.GreaterThanOrEqualTo(28f));
            Assert.That(label.rectTransform.offsetMin.y, Is.GreaterThanOrEqualTo(14f));
            Assert.That(label.rectTransform.offsetMax.x, Is.LessThanOrEqualTo(-28f));
            Assert.That(label.rectTransform.offsetMax.y, Is.LessThanOrEqualTo(-14f));
            Assert.That(label.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
        }

        private static void AssertTextFitsInside(
            RectTransform textRect,
            RectTransform owner,
            float horizontalPadding,
            float verticalPadding)
        {
            Assert.That(textRect, Is.Not.Null);
            Assert.That(owner, Is.Not.Null);
            var corners = new Vector3[4];
            textRect.GetWorldCorners(corners);
            var min = owner.InverseTransformPoint(corners[0]);
            var max = min;
            for (var index = 1; index < corners.Length; index++)
            {
                var local = owner.InverseTransformPoint(corners[index]);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            var bounds = owner.rect;
            Assert.That(min.x, Is.GreaterThanOrEqualTo(bounds.xMin + horizontalPadding - 0.1f));
            Assert.That(max.x, Is.LessThanOrEqualTo(bounds.xMax - horizontalPadding + 0.1f));
            Assert.That(min.y, Is.GreaterThanOrEqualTo(bounds.yMin + verticalPadding - 0.1f));
            Assert.That(max.y, Is.LessThanOrEqualTo(bounds.yMax - verticalPadding + 0.1f));
        }

        private static void AssertCloseButtonsAreTopRight(Transform root, string owner)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name != "Close" && button.name != "CloseButton")
                {
                    continue;
                }

                var rect = button.transform as RectTransform;
                Assert.That(rect, Is.Not.Null, $"{owner}/{button.name} has no RectTransform.");
                if (rect.parent.GetComponent<LayoutGroup>() != null)
                {
                    continue;
                }

                Assert.That(rect.anchorMin, Is.EqualTo(Vector2.one));
                Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(rect.pivot, Is.EqualTo(Vector2.one));
                Assert.That(rect.anchoredPosition.x, Is.LessThan(0f));
                Assert.That(rect.anchoredPosition.y, Is.LessThan(0f));
            }
        }
    }
}
