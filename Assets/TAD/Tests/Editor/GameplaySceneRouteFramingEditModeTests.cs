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
    /// Guards saved-level authoring without imposing a new serialized boundary on restored maps.
    /// The runtime visible-route contract is verified by the shipped-scene Play Mode route test.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameplaySceneRouteFramingEditModeTests
    {
        [TestCase("Assets/Scenes/Levels/Level1.unity")]
        [TestCase("Assets/Scenes/Levels/Level2.unity")]
        [TestCase("Assets/Scenes/Levels/Level3.unity")]
        [TestCase("Assets/Scenes/Levels/Level4.unity")]
        public void RecoveredGameplayLevel_UsesTheLegacyCameraBoundedRouteContract(string scenePath)
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var camera = FindInScene<Camera>(scene, candidate => candidate.CompareTag("MainCamera"));
                var waveController = FindInScene<WaveController>(scene);
                var routeMapper = FindInScene<FarmRouteMapper>(scene);
                var foodStore = FindInScene<FoodStore>(scene);

                Assert.That(camera, Is.Not.Null, scenePath + " needs a Main Camera.");
                Assert.That(camera.orthographic, Is.True, scenePath + " requires an orthographic top-down camera.");
                Assert.That(waveController, Is.Not.Null, scenePath + " needs a Wave Controller.");
                Assert.That(routeMapper, Is.Not.Null, scenePath + " needs a Farm Route Mapper.");
                Assert.That(foodStore, Is.Not.Null, scenePath + " needs a Food Store.");
                Assert.That(routeMapper.PlayableArea ?? routeMapper.GetComponent<FarmPlayableArea>(), Is.Null,
                    scene.name + " must retain its restored camera-bounded route layout.");
                Assert.That(scene.isDirty, Is.False, scene.name + " must not be changed by static route validation.");
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
        public void LevelTemplate_StillOwnsItsExplicitPlayableArea()
        {
            const string scenePath = "Assets/Scenes/Templates/LevelTemplate.unity";
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var routeMapper = FindInScene<FarmRouteMapper>(scene);

                Assert.That(routeMapper, Is.Not.Null, scenePath + " needs a Farm Route Mapper.");
                var area = routeMapper.PlayableArea ?? routeMapper.GetComponent<FarmPlayableArea>();
                Assert.That(area, Is.Not.Null, scenePath + " needs an authored Farm Playable Area.");
                Assert.That(area.HasBoundary, Is.True, scenePath + " needs a valid Farm Playable Area polygon.");
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
