using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TAD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("ResponsiveUi")]
    public sealed class MobileUiLayoutEditModeTests
    {
        [Test]
        public void ReusableUiCanvases_HaveThePortraitLayoutController()
        {
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/TAD/Prefabs/UI" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .ToArray();

            Assert.That(prefabPaths, Is.Not.Empty, "The responsive UI test requires the authored UI prefabs.");

            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, "Could not load " + path + ".");

                foreach (var canvas in prefab.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.GetComponent<CanvasScaler>() == null)
                    {
                        continue;
                    }

                    Assert.That(canvas.GetComponent<MobileUiLayoutController>(), Is.Not.Null,
                        path + " contains a scalable Canvas without its mobile portrait layout controller.");
                }
            }
        }

        [Test]
        public void PortraitOverride_CanBeEnabledAndRestoredWithoutChangingTheLandscapeProfile()
        {
            var objectUnderTest = new GameObject("PortraitLayoutTest", typeof(Canvas), typeof(CanvasScaler));
            try
            {
                var controller = objectUnderTest.AddComponent<MobileUiLayoutController>();
                Assert.That(controller.IsPortraitLayout, Is.False);

                Assert.DoesNotThrow(() => controller.SetPortraitTestOverride(true));
                Assert.That(controller.IsPortraitLayout, Is.True);

                Assert.DoesNotThrow(() => controller.SetPortraitTestOverride(false));
                Assert.That(controller.IsPortraitLayout, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(objectUnderTest);
            }
        }

        [Test]
        public void PortraitOverride_EnforcesReadableTypographyOnTheSharedSkin()
        {
            var canvasRoot = new GameObject(
                "PortraitTypographyTest",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(FarmUiSkin),
                typeof(MobileUiLayoutController));
            var textObject = new GameObject("DescriptionText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            try
            {
                textObject.transform.SetParent(canvasRoot.transform, false);
                var text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 14;

                var controller = canvasRoot.GetComponent<MobileUiLayoutController>();
                controller.SetPortraitTestOverride(true);

                Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(26));
                Assert.That(text.resizeTextForBestFit, Is.True);
                Assert.That(text.resizeTextMinSize, Is.GreaterThanOrEqualTo(19));
            }
            finally
            {
                Object.DestroyImmediate(textObject);
                Object.DestroyImmediate(canvasRoot);
            }
        }

        [Test]
        public void MainMenuPortraitProfile_UsesLargeStackedTouchTargets()
        {
            const string scenePath = "Assets/Scenes/MainMenu.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MobileUiLayoutController>(true))
                    .FirstOrDefault();
                var panel = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                    .FirstOrDefault(rect => rect.name == "MainMenuPanel");

                Assert.That(controller, Is.Not.Null);
                Assert.That(panel, Is.Not.Null);

                controller.SetPortraitTestOverride(true);

                Assert.That(panel.sizeDelta.x, Is.LessThanOrEqualTo(880f));
                foreach (var button in panel.GetComponentsInChildren<Button>(false))
                {
                    var layout = button.GetComponent<LayoutElement>();
                    Assert.That(layout, Is.Not.Null, button.name + " is missing a layout element.");
                    Assert.That(layout.minHeight, Is.GreaterThanOrEqualTo(92f),
                        button.name + " does not meet the portrait touch-target size.");
                }

                controller.SetPortraitTestOverride(false);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void PortraitUpgradeChoiceCards_ReceiveReadableStackedHeights()
        {
            var root = new GameObject("UpgradeChoicePortraitTest", typeof(RectTransform));
            try
            {
                var panel = new GameObject("UpgradeChoicePanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                var cards = new GameObject(
                    "Cards",
                    typeof(RectTransform),
                    typeof(HorizontalLayoutGroup),
                    typeof(LayoutElement));
                cards.transform.SetParent(panel.transform, false);

                for (var index = 0; index < 3; index++)
                {
                    var choice = new GameObject(
                        "Choice_" + index,
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image),
                        typeof(Button),
                        typeof(LayoutElement));
                    choice.transform.SetParent(cards.transform, false);
                }

                var configure = typeof(MobileUiLayoutController).GetMethod(
                    "ConfigurePortraitUpgradeChoices",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(configure, Is.Not.Null);
                Assert.DoesNotThrow(() => configure.Invoke(null, new object[] { panel.GetComponent<RectTransform>() }));

                Assert.That(cards.GetComponent<HorizontalLayoutGroup>().enabled, Is.False);
                foreach (Transform choice in cards.transform)
                {
                    var element = choice.GetComponent<LayoutElement>();
                    Assert.That(element.minHeight, Is.EqualTo(280f));
                    Assert.That(element.preferredHeight, Is.EqualTo(280f));
                    Assert.That(element.ignoreLayout, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PortraitUpgradeChoiceCardCopy_StaysOnOneLineInsideItsButton()
        {
            var canvasRoot = new GameObject(
                "UpgradeChoiceCardTypographyTest",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(FarmUiSkin),
                typeof(MobileUiLayoutController));
            try
            {
                var card = new GameObject(
                    "ChoiceCard",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                card.transform.SetParent(canvasRoot.transform, false);
                var textObject = new GameObject("CardText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textObject.transform.SetParent(card.transform, false);
                var text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = "A longer permanent upgrade explanation.";

                canvasRoot.GetComponent<MobileUiLayoutController>().SetPortraitTestOverride(true);

                Assert.That(text.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
                Assert.That(text.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                var padding = text.GetComponent<FarmUiTextPadding>();
                Assert.That(padding, Is.Not.Null);
                Assert.That(padding.KeepsSingleLine, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(canvasRoot);
            }
        }
    }
}
