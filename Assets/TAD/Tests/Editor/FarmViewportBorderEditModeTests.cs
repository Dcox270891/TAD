using NUnit.Framework;
using TAD.Environment;
using TAD.Navigation;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GamePresentation")]
    public sealed class FarmViewportBorderEditModeTests
    {
        private const float Epsilon = 0.001f;

        private GameObject cameraObject;
        private GameObject areaObject;
        private GameObject authoredGrassObject;
        private Texture2D texture;
        private Sprite sprite;
        private Camera camera;
        private FarmPlayableArea playableArea;
        private SpriteRenderer authoredGrass;
        private FarmViewportBorder viewportBorder;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Farm Viewport Border Test Camera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 16f / 9f;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.tag = "MainCamera";

            texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 4f, 4f),
                new Vector2(0.5f, 0.5f),
                4f,
                0,
                SpriteMeshType.FullRect);

            areaObject = new GameObject("Farm Viewport Border Test Area");
            playableArea = areaObject.AddComponent<FarmPlayableArea>();
            playableArea.SetLocalBoundary(new[]
            {
                new Vector2(-5f, -3f),
                new Vector2(5f, -3f),
                new Vector2(5f, 3f),
                new Vector2(-5f, 3f)
            });

            authoredGrassObject = new GameObject("Authored Grass", typeof(SpriteRenderer));
            authoredGrassObject.transform.SetParent(areaObject.transform, false);
            authoredGrass = authoredGrassObject.GetComponent<SpriteRenderer>();
            authoredGrass.sprite = sprite;
            authoredGrass.drawMode = SpriteDrawMode.Tiled;
            authoredGrass.size = new Vector2(10f, 6f);
            authoredGrass.sortingOrder = -10;

            viewportBorder = areaObject.AddComponent<FarmViewportBorder>();
            viewportBorder.Configure(camera, sprite);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(areaObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void Rebuild_CreatesFourThinFullCoverEnvironmentWallsOutsideThePlayableBounds()
        {
            Assert.That(viewportBorder.Rebuild(), Is.True);
            Assert.That(viewportBorder.BorderWallCount, Is.EqualTo(4));

            var bounds = playableArea.WorldBounds;
            AssertWallTouchesExteriorBoundary(FarmViewportBorder.Edge.Left, bounds.xMin, horizontal: true, isMinimumEdge: false);
            AssertWallTouchesExteriorBoundary(FarmViewportBorder.Edge.Right, bounds.xMax, horizontal: true, isMinimumEdge: true);
            AssertWallTouchesExteriorBoundary(FarmViewportBorder.Edge.Bottom, bounds.yMin, horizontal: false, isMinimumEdge: false);
            AssertWallTouchesExteriorBoundary(FarmViewportBorder.Edge.Top, bounds.yMax, horizontal: false, isMinimumEdge: true);
        }

        [Test]
        public void ConfigureForAuthoring_StoresDependenciesWithoutCreatingRuntimeChildrenOrAlteringGeometry()
        {
            var originalAreaBounds = playableArea.WorldBounds;
            var originalCameraPosition = camera.transform.position;
            var originalCameraRotation = camera.transform.rotation;
            var originalCameraSize = camera.orthographicSize;
            var originalGrassPosition = authoredGrass.transform.position;
            var originalGrassRotation = authoredGrass.transform.rotation;
            var originalGrassScale = authoredGrass.transform.localScale;
            var originalGrassSize = authoredGrass.size;

            viewportBorder.ConfigureForAuthoring(camera, sprite);

            Assert.That(viewportBorder.PlayableArea, Is.SameAs(playableArea));
            Assert.That(viewportBorder.GameplayCamera, Is.SameAs(camera));
            Assert.That(viewportBorder.ExteriorRenderer, Is.Null);
            Assert.That(playableArea.WorldBounds, Is.EqualTo(originalAreaBounds));
            Assert.That(camera.transform.position, Is.EqualTo(originalCameraPosition));
            Assert.That(camera.transform.rotation, Is.EqualTo(originalCameraRotation));
            Assert.That(camera.orthographicSize, Is.EqualTo(originalCameraSize));
            Assert.That(authoredGrass.transform.position, Is.EqualTo(originalGrassPosition));
            Assert.That(authoredGrass.transform.rotation, Is.EqualTo(originalGrassRotation));
            Assert.That(authoredGrass.transform.localScale, Is.EqualTo(originalGrassScale));
            Assert.That(authoredGrass.size, Is.EqualTo(originalGrassSize));

            Assert.That(viewportBorder.Rebuild(), Is.True);
            Assert.That(viewportBorder.ExteriorRenderer, Is.Not.Null);
        }

        [Test]
        public void Rebuild_PreservesAuthoredGrassAndPlacesExteriorBelowIt()
        {
            var originalPosition = authoredGrass.transform.position;
            var originalRotation = authoredGrass.transform.rotation;
            var originalSize = authoredGrass.size;
            var originalOrder = authoredGrass.sortingOrder;

            viewportBorder.Rebuild();

            Assert.That(authoredGrass.transform.position, Is.EqualTo(originalPosition));
            Assert.That(authoredGrass.transform.rotation, Is.EqualTo(originalRotation));
            Assert.That(authoredGrass.size, Is.EqualTo(originalSize));
            Assert.That(authoredGrass.sortingOrder, Is.EqualTo(originalOrder));
            Assert.That(viewportBorder.ExteriorRenderer, Is.Not.Null);
            Assert.That(viewportBorder.ExteriorRenderer.sprite, Is.SameAs(sprite));
            Assert.That(viewportBorder.ExteriorRenderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
            Assert.That(viewportBorder.ExteriorRenderer.sortingOrder, Is.LessThan(authoredGrass.sortingOrder));
            Assert.That(viewportBorder.CoversCurrentViewport(), Is.True);
        }

        [Test]
        public void FitExteriorBackdropToViewport_TracksAPortraitCameraWithoutMovingTheFarmOrWalls()
        {
            viewportBorder.Rebuild();
            var leftWallPosition = viewportBorder.GetBorderWall(FarmViewportBorder.Edge.Left).transform.position;
            var authoredGrassPosition = authoredGrass.transform.position;

            camera.aspect = 9f / 16f;
            camera.orthographicSize = 7f;
            camera.transform.SetPositionAndRotation(
                new Vector3(3f, 4f, -10f),
                Quaternion.Euler(0f, 0f, 90f));

            viewportBorder.FitExteriorBackdropToViewport();

            Assert.That(viewportBorder.CoversCurrentViewport(), Is.True);
            Assert.That(
                Quaternion.Angle(viewportBorder.ExteriorRenderer.transform.rotation, camera.transform.rotation),
                Is.LessThan(Epsilon));
            Assert.That(viewportBorder.GetBorderWall(FarmViewportBorder.Edge.Left).transform.position, Is.EqualTo(leftWallPosition));
            Assert.That(authoredGrass.transform.position, Is.EqualTo(authoredGrassPosition));
        }

        [Test]
        public void RebuildWithoutAnAreaBoundary_CleansUpOnlyItsRuntimeChildren()
        {
            viewportBorder.Rebuild();
            var exterior = viewportBorder.ExteriorRenderer.gameObject;
            var leftWall = viewportBorder.GetBorderWall(FarmViewportBorder.Edge.Left).gameObject;

            playableArea.SetLocalBoundary(System.Array.Empty<Vector2>());
            Assert.That(viewportBorder.Rebuild(), Is.False);

            Assert.That(exterior == null, Is.True);
            Assert.That(leftWall == null, Is.True);
            Assert.That(authoredGrass, Is.Not.Null);
            Assert.That(authoredGrass.gameObject, Is.Not.Null);
        }

        private void AssertWallTouchesExteriorBoundary(
            FarmViewportBorder.Edge edge,
            float boundary,
            bool horizontal,
            bool isMinimumEdge)
        {
            var wall = viewportBorder.GetBorderWall(edge);
            var collider = wall.GetComponent<BoxCollider2D>();

            Assert.That(wall, Is.Not.Null);
            Assert.That(wall.Cover, Is.EqualTo(FarmWallCover.Full));
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(wall.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer(FarmViewportBorder.EnvironmentLayerName)));

            var colliderBounds = collider.bounds;
            var touchingEdge = horizontal
                ? (isMinimumEdge ? colliderBounds.min.x : colliderBounds.max.x)
                : (isMinimumEdge ? colliderBounds.min.y : colliderBounds.max.y);
            Assert.That(touchingEdge, Is.EqualTo(boundary).Within(Epsilon));
        }
    }
}
