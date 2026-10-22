using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Environment;
using TAD.Navigation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    /// <summary>
    /// Guards the visual-ground contract: camera surplus may be visible, but neither side may
    /// route, place, or relocate an actor outside the authored grass tile.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FarmGrassMovementBoundsPlayModeTests
    {
        private Texture2D grassTexture;
        private Sprite grassSprite;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (grassSprite != null)
            {
                Object.Destroy(grassSprite);
            }

            if (grassTexture != null)
            {
                Object.Destroy(grassTexture);
            }

            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTest]
        public IEnumerator GrassFootprint_ConstrainsAttackerRoutesAndDefenderGroundMovement()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var foodStore = GameBasicsTestSupport.CreateFoodStore(new Vector2(2f, 0f));

            var grassObject = new GameObject("TAD Test Grass Ground");
            grassTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            grassSprite = Sprite.Create(
                grassTexture,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f);
            var renderer = grassObject.AddComponent<SpriteRenderer>();
            renderer.sprite = grassSprite;
            var grassBackdrop = grassObject.AddComponent<GameplayGrassBackdrop>();
            grassBackdrop.Configure(renderer: renderer);

            // A boundary component without vertices prevents a background level's authored area
            // from affecting this focused fallback-boundary fixture.
            grassObject.AddComponent<FarmPlayableArea>();
            var mapper = grassObject.AddComponent<FarmRouteMapper>();
            mapper.SetFoodStore(foodStore);

            yield return null;

            Assert.That(mapper.RebuildRoutes(), Is.True);
            Assert.That(mapper.TryGetPlayableGrassBounds(out var grassBounds), Is.True);
            Assert.That(grassBounds, Is.EqualTo(Rect.MinMaxRect(-4f, -4f, 4f, 4f)));

            // Defender placement and relocation both go through this query.
            Assert.That(mapper.CanOccupyGroundPosition(new Vector2(-2f, 0f), 0.5f), Is.True);
            Assert.That(mapper.CanOccupyGroundPosition(new Vector2(4.1f, 0f), 0.5f), Is.False);
            Assert.That(mapper.TryClampToVisibleMap(new Vector2(4.1f, 0f), 0.5f, out _), Is.False);

            var defenderRoute = new List<Vector3>();
            Assert.That(mapper.TryGetWalkableGroundRoute(
                new Vector3(-2f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                0.5f,
                defenderRoute), Is.True);
            foreach (var point in defenderRoute)
            {
                Assert.That(grassBounds.Contains(point), Is.True, "A defender route point left the visible grass.");
            }

            // Attacker route fields are built from the same ground constraint, so spawning in
            // camera surplus cannot produce a path that runs around the level artwork.
            var attackerRoute = new List<Vector3>();
            Assert.That(mapper.TryGetFastestRoute(new Vector3(4.1f, 0f, 0f), 0.5f, attackerRoute), Is.False);
        }

        [UnityTest]
        public IEnumerator BarnBeyondGrass_ResolvesArrivalFromTheLastLegalGrassCell()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var foodStore = GameBasicsTestSupport.CreateFoodStore(new Vector2(4.6f, 0f));

            var grassObject = new GameObject("TAD Test Terminal Grass Ground");
            grassTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            grassSprite = Sprite.Create(
                grassTexture,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f);
            var renderer = grassObject.AddComponent<SpriteRenderer>();
            renderer.sprite = grassSprite;
            var grassBackdrop = grassObject.AddComponent<GameplayGrassBackdrop>();
            grassBackdrop.Configure(renderer: renderer);
            var mapper = grassObject.AddComponent<FarmRouteMapper>();
            mapper.SetFoodStore(foodStore);

            yield return null;

            Assert.That(mapper.RebuildRoutes(), Is.True);
            Assert.That(mapper.TryGetPlayableGrassBounds(out var grassBounds), Is.True);

            var definition = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(definition, Is.Not.Null);
            var route = new List<Vector3>();
            Assert.That(mapper.TryGetFastestRoute(new Vector3(-3f, 0f, 0f), definition, route), Is.True);
            Assert.That(mapper.RequiresGrassBoundFoodStoreArrival(definition.NavigationDiameter), Is.True);

            var legalMaximumX = grassBounds.xMax - (definition.NavigationDiameter * 0.5f);
            foreach (var point in route)
            {
                Assert.That(point.x, Is.LessThanOrEqualTo(legalMaximumX + 0.001f),
                    "The virtual barn arrival must not append a point beyond the rendered grass.");
                Assert.That(point.x, Is.GreaterThanOrEqualTo(grassBounds.xMin + (definition.NavigationDiameter * 0.5f) - 0.001f));
                Assert.That(point.y, Is.LessThanOrEqualTo(grassBounds.yMax - (definition.NavigationDiameter * 0.5f) + 0.001f));
                Assert.That(point.y, Is.GreaterThanOrEqualTo(grassBounds.yMin + (definition.NavigationDiameter * 0.5f) - 0.001f));
            }

            var attacker = GameBasicsTestSupport.CreateAttacker(definition);
            attacker.transform.position = new Vector3(-3f, 0f, 0f);
            attacker.SetRouteMapper(mapper);
            var arrivals = 0;
            foodStore.RaiderReached += _ => arrivals++;

            yield return new WaitForSeconds(4f);

            Assert.That(arrivals, Is.EqualTo(1),
                "Reaching the grass-edge route terminal should use the normal Food Store arrival path exactly once.");
            Assert.That(foodStore.CurrentFood, Is.LessThan(foodStore.StartingFood));
            Assert.That(attacker.gameObject.activeSelf, Is.False,
                "The attacker should finish through its normal food-arrival lifecycle rather than waiting outside the grass.");
        }
    }
}
