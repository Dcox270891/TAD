using NUnit.Framework;
using TAD.Environment;
using TAD.Navigation;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("ResponsiveUi")]
    public sealed class GameplayPortraitOrientationEditModeTests
    {
        [Test]
        public void PortraitPresentation_PutsSpawnAboveFoodWithoutMovingTheAuthoredFarm()
        {
            var cameraObject = new GameObject("PortraitGameplayCamera", typeof(Camera));
            var spawnObject = new GameObject("PortraitSpawn");
            var foodObject = new GameObject("PortraitFood", typeof(FoodStore));
            var controllerObject = new GameObject("PortraitOrientationController");
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 8f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.transform.rotation = Quaternion.identity;

                spawnObject.transform.position = new Vector3(-12f, 2f, 0f);
                foodObject.transform.position = new Vector3(12f, -2f, 0f);
                var originalSpawn = spawnObject.transform.position;
                var originalFood = foodObject.transform.position;
                var originalCameraPosition = camera.transform.position;
                var originalCameraRotation = camera.transform.rotation;

                var controller = controllerObject.AddComponent<GameplayPortraitOrientationController>();
                controller.Configure(camera, null, null, foodObject.GetComponent<FoodStore>(), spawnObject.transform);
                controller.SetPortraitTestOverride(true);

                var spawnViewport = camera.WorldToViewportPoint(spawnObject.transform.position);
                var foodViewport = camera.WorldToViewportPoint(foodObject.transform.position);
                Assert.That(spawnViewport.y, Is.GreaterThan(0.5f));
                Assert.That(foodViewport.y, Is.LessThan(0.5f));
                Assert.That(spawnViewport.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(foodViewport.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(spawnObject.transform.position, Is.EqualTo(originalSpawn));
                Assert.That(foodObject.transform.position, Is.EqualTo(originalFood));

                controller.SetPortraitTestOverride(false);
                Assert.That(camera.transform.position, Is.EqualTo(originalCameraPosition));
                Assert.That(Quaternion.Angle(camera.transform.rotation, originalCameraRotation), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(foodObject);
                Object.DestroyImmediate(spawnObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
