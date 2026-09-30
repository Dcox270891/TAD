using NUnit.Framework;
using TAD.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class ThinWoodOutlineUiEditModeTests
    {
        private const string OutlinePath = "Assets/TAD/Art/UI/TAD_ThinWoodOutline.png";
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
    }
}
