using System.Linq;
using NUnit.Framework;
using TAD.Environment;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>
    /// Guards the authored grass-ground contract: it is tiled but never stretched or moved to
    /// manufacture additional walkable-looking ground for a larger camera viewport.
    /// </summary>
    [TestFixture]
    [Category("GamePresentation")]
    public sealed class GameplayGrassBackdropEditModeTests
    {
        private const string GameplayLevelCorePrefabPath = "Assets/TAD/Prefabs/Levels/GameplayLevelCore.prefab";

        private GameObject cameraObject;
        private GameObject backdropObject;
        private Texture2D grassTexture;
        private Sprite grassSprite;
        private Camera gameplayCamera;
        private SpriteRenderer grassRenderer;
        private GameplayGrassBackdrop grassBackdrop;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Gameplay Grass Backdrop Test Camera", typeof(Camera));
            gameplayCamera = cameraObject.GetComponent<Camera>();
            gameplayCamera.orthographic = true;
            gameplayCamera.nearClipPlane = 0.1f;
            gameplayCamera.farClipPlane = 100f;
            gameplayCamera.transform.position = new Vector3(0f, 0f, -10f);

            backdropObject = new GameObject("Gameplay Grass Backdrop Test", typeof(SpriteRenderer));
            grassRenderer = backdropObject.GetComponent<SpriteRenderer>();
            grassTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            grassSprite = Sprite.Create(grassTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            grassRenderer.sprite = grassSprite;
            grassBackdrop = backdropObject.AddComponent<GameplayGrassBackdrop>();
            grassBackdrop.Configure(gameplayCamera, grassRenderer);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(backdropObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(grassSprite);
            Object.DestroyImmediate(grassTexture);
        }

        [Test]
        public void FitToCameraViewport_AuthoredGrass_RemainsAtItsAuthoredFootprint()
        {
            gameplayCamera.aspect = 16f / 9f;
            gameplayCamera.orthographicSize = 6f;
            gameplayCamera.transform.SetPositionAndRotation(
                new Vector3(4f, -2f, -10f),
                Quaternion.identity);

            backdropObject.transform.SetPositionAndRotation(
                new Vector3(2f, -1f, 0f),
                Quaternion.Euler(0f, 0f, 18f));
            backdropObject.transform.localScale = new Vector3(1.25f, 0.8f, 1f);
            grassRenderer.size = new Vector2(13f, 7f);
            var authoredPosition = backdropObject.transform.position;
            var authoredRotation = backdropObject.transform.rotation;
            var authoredScale = backdropObject.transform.localScale;
            var authoredSize = grassRenderer.size;

            grassBackdrop.FitToCameraViewport();

            Assert.That(grassBackdrop.GameplayCamera, Is.SameAs(gameplayCamera));
            Assert.That(grassBackdrop.GrassRenderer, Is.SameAs(grassRenderer));
            Assert.That(grassRenderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
            Assert.That(backdropObject.transform.position, Is.EqualTo(authoredPosition));
            Assert.That(Quaternion.Angle(backdropObject.transform.rotation, authoredRotation), Is.LessThan(0.001f));
            Assert.That(backdropObject.transform.localScale, Is.EqualTo(authoredScale));
            Assert.That(grassRenderer.size, Is.EqualTo(authoredSize));
        }

        [Test]
        public void FitToCameraViewport_PortraitCamera_DoesNotCreateExtraGrass()
        {
            gameplayCamera.aspect = 9f / 16f;
            gameplayCamera.orthographicSize = 8f;
            gameplayCamera.transform.SetPositionAndRotation(
                new Vector3(-3f, 5f, -10f),
                Quaternion.Euler(0f, 0f, 90f));

            backdropObject.transform.position = new Vector3(1f, 2f, 0f);
            grassRenderer.size = new Vector2(9f, 6f);
            var authoredPosition = backdropObject.transform.position;
            var authoredSize = grassRenderer.size;

            grassBackdrop.FitToCameraViewport();

            Assert.That(grassRenderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
            Assert.That(backdropObject.transform.position, Is.EqualTo(authoredPosition));
            Assert.That(grassRenderer.size, Is.EqualTo(authoredSize));
        }

        [Test]
        public void SharedGameplayLevelCore_ProvidesAnAuthoredTiledGrassRenderer()
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(GameplayLevelCorePrefabPath);
            try
            {
                var mainCamera = prefabRoot.GetComponentsInChildren<Camera>(true)
                    .FirstOrDefault(candidate => candidate != null && candidate.CompareTag("MainCamera"));
                var sharedGrassRenderer = prefabRoot.GetComponentsInChildren<SpriteRenderer>(true)
                    .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == "TAD Grass Background");

                Assert.That(mainCamera, Is.Not.Null, "GameplayLevelCore needs a Main Camera for its farm presentation.");
                Assert.That(sharedGrassRenderer, Is.Not.Null, "GameplayLevelCore needs its TAD Grass Background renderer.");

                var sharedBackdrop = sharedGrassRenderer.GetComponent<GameplayGrassBackdrop>();
                Assert.That(sharedBackdrop, Is.Not.Null,
                    "GameplayLevelCore must attach GameplayGrassBackdrop to its shared grass renderer.");
                Assert.That(sharedBackdrop.GrassRenderer, Is.SameAs(sharedGrassRenderer));
                Assert.That(sharedGrassRenderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

    }
}
