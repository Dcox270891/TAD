using System.Linq;
using NUnit.Framework;
using TAD.Environment;
using TAD.Navigation;
using TAD.Waves;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAD.Tests.Editor
{
    /// <summary>
    /// Guards the explicit presentation contract without forcing a serialized migration onto a
    /// designer-authored gameplay map. Recovered maps use FarmRouteMapper's camera-bounded
    /// fallback; the reusable template retains its opt-in authored boundary and exterior border.
    /// </summary>
    [TestFixture]
    [Category("GamePresentation")]
    public sealed class FarmViewportPresentationAssetBuilderEditModeTests
    {
        private const string LevelTemplateScenePath = "Assets/Scenes/Templates/LevelTemplate.unity";

        private static readonly string[] RecoveredLevelScenePaths =
        {
            "Assets/Scenes/Levels/Level1.unity",
            "Assets/Scenes/Levels/Level2.unity",
            "Assets/Scenes/Levels/Level3.unity",
            "Assets/Scenes/Levels/Level4.unity"
        };

        [Test]
        public void RecoveredLevels_KeepTheirAuthoredLayoutWithoutAPresentationMigration()
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var scenePath in RecoveredLevelScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var mapper = FindInScene<FarmRouteMapper>(scene);
                    var area = mapper != null ? mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>() : null;
                    var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                    var waveController = FindInScene<WaveController>(scene);
                    var foodStore = FindInScene<FoodStore>(scene);

                    Assert.That(mapper, Is.Not.Null, scene.name + " needs a Farm Route Mapper.");
                    Assert.That(camera, Is.Not.Null, scene.name + " needs a Main Camera.");
                    Assert.That(camera.orthographic, Is.True, scene.name + " needs an orthographic gameplay camera.");
                    Assert.That(waveController, Is.Not.Null, scene.name + " needs a Wave Controller.");
                    Assert.That(foodStore, Is.Not.Null, scene.name + " needs a Food Store.");

                    // These scenes were explicitly restored to their own saved map layouts.
                    // A FarmPlayableArea is optional: FarmRouteMapper falls back to its visible
                    // camera map when no authored boundary is present.
                    Assert.That(area, Is.Null, scene.name + " must not receive an automatic playable-area migration.");
                    Assert.That(scene.isDirty, Is.False, scene.name + " must not be modified merely by opening it.");
                }
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }
        }

        [Test]
        public void LevelTemplate_KeepsItsExplicitBoundaryAndExteriorPresentation()
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(LevelTemplateScenePath, OpenSceneMode.Single);
                var mapper = FindInScene<FarmRouteMapper>(scene);
                var area = mapper != null ? mapper.PlayableArea ?? mapper.GetComponent<FarmPlayableArea>() : null;
                var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                var grass = FindInScene<SpriteRenderer>(scene, candidate => candidate.gameObject.name == "TAD Grass Background");

                Assert.That(mapper, Is.Not.Null);
                Assert.That(area, Is.Not.Null);
                Assert.That(area.HasBoundary, Is.True);
                Assert.That(camera, Is.Not.Null);
                Assert.That(grass, Is.Not.Null);
                Assert.That(grass.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));

                var border = area.GetComponent<FarmViewportBorder>();
                Assert.That(border, Is.Not.Null);
                Assert.That(border.PlayableArea, Is.SameAs(area));
                Assert.That(border.GameplayCamera, Is.SameAs(camera));
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }
        }

        private static T FindInScene<T>(Scene scene, System.Func<T, bool> predicate = null) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .FirstOrDefault(component => component != null && (predicate == null || predicate(component)));
        }
    }
}
