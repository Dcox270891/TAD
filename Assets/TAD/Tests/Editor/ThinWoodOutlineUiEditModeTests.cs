using NUnit.Framework;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class ThinWoodOutlineUiEditModeTests
    {
        private const string OutlinePath = "Assets/TAD/Art/UI/TAD_ThinWoodOutline.png";
        private const string SquareFillPath = "Assets/TAD/Art/UI/TAD_SquareUiFill.png";
        private const string ButtonPrefabPath = "Assets/TAD/Prefabs/UI/ThinWoodOutlineButton.prefab";

        [Test]
        public void ThinWoodOutlineSpriteIsConfiguredForFineNineSliceScaling()
        {
            var importer = AssetImporter.GetAtPath(OutlinePath) as TextureImporter;
            var outline = AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);

            Assert.That(importer, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(512f).Within(0.01f));
            Assert.That(importer.spriteBorder.x, Is.GreaterThanOrEqualTo(60f));
            Assert.That(importer.spriteBorder.y, Is.GreaterThanOrEqualTo(60f));
            Assert.That(outline.border.x, Is.GreaterThanOrEqualTo(60f));
        }

        [Test]
        public void ThinWoodOutlineButtonUsesATransparentSlicedFrameAndClickableImage()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            var image = prefab.GetComponent<Image>();
            var button = prefab.GetComponent<Button>();
            var outline = prefab.GetComponent<FarmUiThinWoodOutline>();
            var label = prefab.GetComponentInChildren<Text>(true);

            Assert.That(button, Is.Not.Null);
            Assert.That(image, Is.Not.Null);
            Assert.That(outline, Is.Not.Null);
            Assert.That(outline.OutlineSprite, Is.EqualTo(image.sprite));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.fillCenter, Is.False);
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.raycastTarget, Is.False);
        }

        [Test]
        public void EveryShippedUiButtonUsesTheThinWoodOutlineWithoutBreakingMasks()
        {
            var outlineSprite = AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" });

            Assert.That(prefabGuids, Is.Not.Empty);
            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);

                foreach (var button in prefab.GetComponentsInChildren<Button>(true))
                {
                    var targetImage = button.targetGraphic as Image ?? button.GetComponent<Image>();
                    Assert.That(targetImage, Is.Not.Null, $"{prefabPath}/{button.name} has no Image target.");

                    if (targetImage.GetComponent<Mask>() != null)
                    {
                        var rim = targetImage.GetComponent<Outline>();

                        Assert.That(targetImage.GetComponent<FarmUiThinWoodOutline>(), Is.Null,
                            $"{prefabPath}/{button.name} must not use an outline-only mask graphic.");
                        Assert.That(targetImage.sprite, Is.EqualTo(AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd")));
                        Assert.That(targetImage.type, Is.EqualTo(Image.Type.Simple));
                        Assert.That(targetImage.fillCenter, Is.True);
                        Assert.That(rim, Is.Not.Null, $"{prefabPath}/{button.name} is missing its circular rim.");
                        Assert.That(rim.enabled, Is.True);
                        continue;
                    }

                    var outline = targetImage.GetComponent<FarmUiThinWoodOutline>();
                    var buttonSurface = targetImage.transform.Find("FarmUiButtonSurface");
                    var buttonSurfaceImage = buttonSurface != null ? buttonSurface.GetComponent<Image>() : null;
                    Assert.That(outline, Is.Not.Null, $"{prefabPath}/{button.name} is missing its thin wood outline.");
                    Assert.That(outline.OutlineSprite, Is.EqualTo(outlineSprite));
                    Assert.That(targetImage.type, Is.EqualTo(Image.Type.Sliced));
                    Assert.That(targetImage.fillCenter, Is.False);
                    Assert.That(buttonSurfaceImage, Is.Not.Null,
                        $"{prefabPath}/{button.name} is missing its readable parchment surface.");
                    Assert.That(buttonSurfaceImage.raycastTarget, Is.False);
                }
            }
        }

        [Test]
        public void AttackerQueueButtonKeepsItsOpaqueCircularMaskAndCircularRim()
        {
            const string prefabPath = "Assets/TAD/Prefabs/UI/AttackerQueueGroupButton.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var image = prefab != null ? prefab.GetComponent<Image>() : null;
            var mask = prefab != null ? prefab.GetComponent<Mask>() : null;
            var rim = prefab != null ? prefab.GetComponent<Outline>() : null;

            Assert.That(prefab, Is.Not.Null);
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.showMaskGraphic, Is.True);
            Assert.That(image, Is.Not.Null);
            Assert.That(image.sprite, Is.EqualTo(AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd")));
            Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(image.fillCenter, Is.True);
            Assert.That(image.GetComponent<FarmUiThinWoodOutline>(), Is.Null);
            Assert.That(rim, Is.Not.Null);
            Assert.That(rim.enabled, Is.True);
        }

        [Test]
        public void EveryShippedUiTextUsesCrispDarkInkWithoutBlurEffects()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" });
            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);

                foreach (var text in prefab.GetComponentsInChildren<Text>(true))
                {
                    Assert.That(text.GetComponents<Shadow>(), Is.Empty,
                        $"{prefabPath}/{text.name} retains a shadow or outline effect.");
                    Assert.That(text.color.r, Is.LessThan(0.5f), $"{prefabPath}/{text.name} is not dark ink.");
                    Assert.That(text.color.g, Is.LessThan(0.2f), $"{prefabPath}/{text.name} is not dark ink.");
                    Assert.That(text.color.b, Is.LessThan(0.1f), $"{prefabPath}/{text.name} is not dark ink.");
                    Assert.That(text.fontStyle, Is.Not.EqualTo(FontStyle.Normal),
                        $"{prefabPath}/{text.name} should use the bold UI treatment.");
                }
            }
        }

        [Test]
        public void HudAndButtonTextAreHorizontallyCentredAndHudTextCanShrinkToFit()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" });
            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);

                foreach (var text in prefab.GetComponentsInChildren<Text>(true))
                {
                    if (text.GetComponentInParent<Button>(true) != null)
                    {
                        Assert.That(IsHorizontallyCentred(text.alignment), Is.True,
                            $"{prefabPath}/{text.name} is not centred within its button region.");
                    }
                }
            }

            const string banksHudPath = "Assets/TAD/Prefabs/UI/ResourceBanksHud.prefab";
            var banksHud = AssetDatabase.LoadAssetAtPath<GameObject>(banksHudPath);
            Assert.That(banksHud, Is.Not.Null);
            foreach (var name in new[] { "AttackerGoldText", "AttackerIncomeText", "DefenderGoldText" })
            {
                var text = FindChild(banksHud.transform, name)?.GetComponent<Text>();
                Assert.That(text, Is.Not.Null, $"{banksHudPath}/{name} is missing.");
                Assert.That(text.alignment, Is.EqualTo(TextAnchor.MiddleCenter),
                    $"{banksHudPath}/{name} should be centred within its plaque.");
                Assert.That(text.resizeTextForBestFit, Is.True,
                    $"{banksHudPath}/{name} must shrink before it reaches the plaque frame.");
            }
        }

        [Test]
        public void ResourceBankBadgesStackWithoutOverlapAndContainTheirCentredLabels()
        {
            const string banksHudPath = "Assets/TAD/Prefabs/UI/ResourceBanksHud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(banksHudPath);
            Assert.That(prefab, Is.Not.Null);

            var instance = Object.Instantiate(prefab);
            try
            {
                var skin = instance.GetComponent<FarmUiSkin>();
                Assert.That(skin, Is.Not.Null);
                skin.ApplyTheme();

                var attackerGold = FindChild(instance.transform, "AttackerGoldText")?.GetComponent<Text>();
                var attackerIncome = FindChild(instance.transform, "AttackerIncomeText")?.GetComponent<Text>();
                var defenderGold = FindChild(instance.transform, "DefenderGoldText")?.GetComponent<Text>();
                Assert.That(attackerGold, Is.Not.Null);
                Assert.That(attackerIncome, Is.Not.Null);
                Assert.That(defenderGold, Is.Not.Null);

                var attackerGoldBadge = attackerGold.transform.parent as RectTransform;
                var attackerIncomeBadge = attackerIncome.transform.parent as RectTransform;
                var defenderGoldBadge = defenderGold.transform.parent as RectTransform;
                Assert.That(attackerGoldBadge.name, Is.EqualTo("FarmUiBadge_AttackerGoldText"));
                Assert.That(attackerIncomeBadge.name, Is.EqualTo("FarmUiBadge_AttackerIncomeText"));
                Assert.That(defenderGoldBadge.name, Is.EqualTo("FarmUiBadge_DefenderGoldText"));
                Assert.That(attackerGold.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
                Assert.That(attackerIncome.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
                Assert.That(defenderGold.alignment, Is.EqualTo(TextAnchor.MiddleCenter));
                Assert.That(attackerGold.rectTransform.offsetMin.x, Is.GreaterThanOrEqualTo(16f));
                Assert.That(attackerGold.rectTransform.offsetMax.x, Is.LessThanOrEqualTo(-16f));

                var goldBottom = attackerGoldBadge.anchoredPosition.y - attackerGoldBadge.rect.height;
                var incomeTop = attackerIncomeBadge.anchoredPosition.y;
                Assert.That(goldBottom - incomeTop, Is.GreaterThanOrEqualTo(12f),
                    "The gold and income plaques must retain a visible vertical gap.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FoodStoreHudUsesAnOpaqueSquareSpriteForItsInsetHorizontalFill()
        {
            const string prefabPath = "Assets/TAD/Prefabs/UI/FoodStoreHud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var squareFill = AssetDatabase.LoadAssetAtPath<Sprite>(SquareFillPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(squareFill, Is.Not.Null);

            var background = FindChild(prefab.transform, "Background")?.GetComponent<Image>();
            var fill = FindChild(prefab.transform, "Fill")?.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(fill, Is.Not.Null);
            Assert.That(background.sprite, Is.Null);
            Assert.That(background.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(fill.sprite, Is.EqualTo(squareFill));
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
            Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            Assert.That(fill.rectTransform.offsetMin, Is.EqualTo(new Vector2(7f, 7f)));
            Assert.That(fill.rectTransform.offsetMax, Is.EqualTo(new Vector2(-7f, -7f)));
        }

        [Test]
        public void AttackerQueueStartsBelowTheStackedAttackerBankHud()
        {
            const string prefabPath = "Assets/TAD/Prefabs/UI/AttackerQueuePanel.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var instance = Object.Instantiate(prefab);
            try
            {
                var scrollView = FindChild(instance.transform, "ScrollView") as RectTransform;
                var heading = FindChild(instance.transform, "Heading") as RectTransform;
                Assert.That(scrollView, Is.Not.Null);
                Assert.That(heading, Is.Not.Null);
                Assert.That(scrollView.anchoredPosition.y, Is.EqualTo(-212f).Within(0.01f));
                Assert.That(scrollView.sizeDelta.y, Is.EqualTo(602f).Within(0.01f));
                Assert.That(heading.anchoredPosition.y, Is.EqualTo(-189f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ShippedCloseButtonsUseTheTopRightCornerOfTheirModal()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" });
            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);
                AssertCloseButtonsAreTopRight(prefab.transform, prefabPath);
            }

            const string scenePath = "Assets/Scenes/MainMenu.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    AssertCloseButtonsAreTopRight(root.transform, scenePath);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void EveryShippedPanelUsesANonInteractiveThinWoodFrame()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" });
            foreach (var guid in prefabGuids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);

                foreach (var image in prefab.GetComponentsInChildren<Image>(true))
                {
                    if (!IsPanelSurface(image))
                    {
                        continue;
                    }

                    var frame = image.transform.Find(FarmUiThinWoodOutline.FrameObjectName);
                    var frameImage = frame != null ? frame.GetComponent<Image>() : null;
                    var frameOutline = frame != null ? frame.GetComponent<FarmUiThinWoodOutline>() : null;

                    Assert.That(frame, Is.Not.Null, $"{prefabPath}/{image.name} is missing its thin wood frame.");
                    Assert.That(frameImage, Is.Not.Null, $"{prefabPath}/{image.name} frame has no Image.");
                    Assert.That(frameOutline, Is.Not.Null, $"{prefabPath}/{image.name} frame has no outline component.");
                    Assert.That(frameImage.raycastTarget, Is.False);
                    Assert.That(frameImage.fillCenter, Is.False);
                }
            }
        }

        [Test]
        public void MainMenuSkinHasTheThinOutlineSpriteAssigned()
        {
            const string scenePath = "Assets/Scenes/MainMenu.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                FarmUiSkin skin = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    skin = root.GetComponentInChildren<FarmUiSkin>(true);
                    if (skin != null)
                    {
                        break;
                    }
                }

                var outline = AssetDatabase.LoadAssetAtPath<Sprite>(OutlinePath);

                Assert.That(skin, Is.Not.Null);
                Assert.That(skin.ThinWoodOutlineSprite, Is.EqualTo(outline));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void MainMenuPanelsAndButtonsUseTheThinWoodOutline()
        {
            const string scenePath = "Assets/Scenes/MainMenu.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var button in root.GetComponentsInChildren<Button>(true))
                    {
                        var targetImage = button.targetGraphic as Image ?? button.GetComponent<Image>();
                        Assert.That(targetImage, Is.Not.Null, $"Main Menu/{button.name} has no Image target.");
                        Assert.That(targetImage.GetComponent<FarmUiThinWoodOutline>(), Is.Not.Null,
                            $"Main Menu/{button.name} is missing its thin wood outline.");
                    }

                    foreach (var image in root.GetComponentsInChildren<Image>(true))
                    {
                        if (!IsPanelSurface(image) || IsMainMenuGrassBackground(image))
                        {
                            continue;
                        }

                        Assert.That(image.transform.Find(FarmUiThinWoodOutline.FrameObjectName), Is.Not.Null,
                            $"Main Menu/{image.name} is missing its thin wood frame.");
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [TestCase("Assets/TAD/Prefabs/UI/AttackerDetailsPanel.prefab")]
        [TestCase("Assets/TAD/Prefabs/UI/DefenderDetailsPanel.prefab")]
        public void InformationCardsKeepTheirReadableSurfaceAndUseTheThinWoodOutlineFrame(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            var card = FindChild(prefab.transform, "Card");
            Assert.That(card, Is.Not.Null, $"Card was not found in {prefabPath}.");

            var image = card.GetComponent<Image>();
            var cardOutline = card.GetComponent<FarmUiThinWoodOutline>();
            var frame = card.Find("ThinWoodOutlineFrame");
            var frameImage = frame != null ? frame.GetComponent<Image>() : null;
            var frameOutline = frame != null ? frame.GetComponent<FarmUiThinWoodOutline>() : null;

            Assert.That(image, Is.Not.Null);
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(image.color.r, Is.GreaterThan(0.85f));
            Assert.That(cardOutline, Is.Null);
            Assert.That(frame, Is.Not.Null);
            Assert.That(frameImage, Is.Not.Null);
            Assert.That(frameOutline, Is.Not.Null);
            Assert.That(frameOutline.OutlineSprite, Is.EqualTo(frameImage.sprite));
            Assert.That(frameImage.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(frameImage.fillCenter, Is.False);
            Assert.That(frameImage.raycastTarget, Is.False);
        }

        private static Transform FindChild(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsHorizontallyCentred(TextAnchor alignment)
        {
            return alignment == TextAnchor.UpperCenter ||
                   alignment == TextAnchor.MiddleCenter ||
                   alignment == TextAnchor.LowerCenter;
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
                if (rect.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                {
                    continue;
                }

                Assert.That(rect.anchorMin, Is.EqualTo(Vector2.one), $"{owner}/{button.name} is not top-right anchored.");
                Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one), $"{owner}/{button.name} is not top-right anchored.");
                Assert.That(rect.pivot, Is.EqualTo(Vector2.one), $"{owner}/{button.name} is not top-right pivoted.");
                Assert.That(rect.anchoredPosition.x, Is.LessThan(0f), $"{owner}/{button.name} should be inset from the right.");
                Assert.That(rect.anchoredPosition.y, Is.LessThan(0f), $"{owner}/{button.name} should be inset from the top.");
            }
        }

        private static bool IsPanelSurface(Image image)
        {
            if (image == null ||
                image.GetComponent<FarmUiThinWoodOutline>() != null ||
                image.GetComponent<Button>() != null ||
                image.GetComponentInParent<Button>(true) != null ||
                image.GetComponentInParent<Toggle>(true) != null ||
                image.GetComponentInParent<Slider>(true) != null ||
                image.GetComponentInParent<Scrollbar>(true) != null ||
                image.GetComponent<Mask>() != null ||
                image.GetComponent<RectMask2D>() != null)
            {
                return false;
            }

            var name = image.gameObject.name.ToLowerInvariant();
            if (name.Contains("blocker") || name.Contains("backdrop") ||
                name.Contains("fill") || name.Contains("attackerimage") ||
                name.Contains("defenderimage") || name.Contains("projectile") ||
                name.Contains("health") || name.Contains("experience") ||
                name.Contains("checkmark") || name.Contains("handle") || name == "viewport")
            {
                return false;
            }

            return name.Contains("panel") ||
                   name.Contains("background") ||
                   name.Contains("card") ||
                   name.Contains("details") ||
                   name.Contains("window") ||
                   name.Contains("topcentrefoodbar") ||
                   name.Contains("selection") ||
                   name.Contains("queue");
        }

        private static bool IsMainMenuGrassBackground(Image image)
        {
            if (image == null || image.gameObject.name != "Background")
            {
                return false;
            }

            for (var current = image.transform.parent; current != null; current = current.parent)
            {
                if (current.name.Contains("MainMenu"))
                {
                    // FarmUiSkin supplies the grass artwork at runtime, so the serialized
                    // background deliberately remains sprite-less in the scene asset.
                    return true;
                }
            }

            return false;
        }
    }
}
