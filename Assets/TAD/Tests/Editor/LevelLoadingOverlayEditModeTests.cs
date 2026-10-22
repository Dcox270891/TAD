using NUnit.Framework;
using TAD.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class LevelLoadingOverlayEditModeTests
    {
        private const string LoadingOverlayPrefabPath = "Assets/TAD/Prefabs/UI/LevelLoadingOverlay.prefab";
        private const string StatusFramePath = "Assets/TAD/Art/UI/FarmUiKit/TAD_WoodStatusBarFrame.png";
        private const string StatusFillPath = "Assets/TAD/Art/UI/FarmUiKit/TAD_StatusBarFill.png";

        [Test]
        public void ScenePreparationProgressNeverClaimsFullCompletion()
        {
            Assert.That(LevelLoadingOverlay.GetPreActivationProgress(-1f), Is.EqualTo(0f));
            Assert.That(LevelLoadingOverlay.GetPreActivationProgress(0.45f), Is.EqualTo(0.48f).Within(0.001f));
            Assert.That(LevelLoadingOverlay.GetPreActivationProgress(0.9f),
                Is.EqualTo(LevelLoadingOverlay.PreActivationProgressLimit).Within(0.001f));
            Assert.That(LevelLoadingOverlay.GetPreActivationProgress(1f), Is.LessThan(1f));
            Assert.That(LevelLoadingOverlay.PreActivationProgressLimit, Is.LessThan(1f));
        }

        [Test]
        public void LoadingOverlayUsesTheSharedFoodAndExperienceStatusBarTreatment()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoadingOverlayPrefabPath);
            var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFramePath);
            var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StatusFillPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(frameSprite, Is.Not.Null);
            Assert.That(fillSprite, Is.Not.Null);

            var instance = Object.Instantiate(prefab);
            try
            {
                var overlay = instance.GetComponent<LevelLoadingOverlay>();
                Assert.That(overlay, Is.Not.Null);
                overlay.Show();

                var progressBar = instance.transform.Find("LoadingPanel/ProgressBar");
                var frame = progressBar != null ? progressBar.GetComponent<Image>() : null;
                var fill = progressBar != null ? progressBar.Find("Fill")?.GetComponent<Image>() : null;
                var label = progressBar != null ? progressBar.Find("ProgressLabel")?.GetComponent<Text>() : null;

                Assert.That(frame, Is.Not.Null);
                Assert.That(fill, Is.Not.Null);
                Assert.That(label, Is.Not.Null);
                Assert.That(frame.sprite, Is.EqualTo(frameSprite));
                Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(fill.sprite, Is.EqualTo(fillSprite));
                Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
                Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
                Assert.That(fill.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
                Assert.That(fill.rectTransform.offsetMin, Is.EqualTo(new Vector2(6f, 6f)));
                Assert.That(fill.rectTransform.offsetMax, Is.EqualTo(new Vector2(-6f, -6f)));
                Assert.That(label.rectTransform.parent, Is.EqualTo(progressBar));
                Assert.That(label.rectTransform.offsetMin, Is.EqualTo(new Vector2(18f, 6f)));
                Assert.That(label.rectTransform.offsetMax, Is.EqualTo(new Vector2(-18f, -6f)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
