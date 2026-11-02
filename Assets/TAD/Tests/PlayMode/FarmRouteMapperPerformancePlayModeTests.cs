using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Navigation;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    /// <summary>
    /// Covers the split between cheap runtime movement routes and the richer timing snapshots
    /// deliberately reserved for AI planning.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FarmRouteMapperPerformancePlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTest]
        public IEnumerator PooledDefinitionRegistration_PrecomputesRouteWithoutPrewarmedInstance()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var foodStore = GameBasicsTestSupport.CreateFoodStore(new Vector2(4f, 0f));
            var mapperObject = new GameObject("TAD Test Pooled Definition Route Mapper");
            var mapper = mapperObject.AddComponent<FarmRouteMapper>();
            mapper.SetFoodStore(foodStore);

            var chicken = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(chicken, Is.Not.Null);

            mapper.RegisterPooledAttackerDefinition(chicken);
            yield return null;

            Assert.That(mapper.RebuildRoutes(), Is.True);
            var descriptors = new List<AttackerRouteDescriptor>();
            mapper.CopyPrecomputedAttackerRoutes(descriptors);
            Assert.That(descriptors.Exists(descriptor =>
                    Mathf.Approximately(descriptor.AttackerSize, chicken.NavigationDiameter) &&
                    descriptor.TraversalProfile == chicken.TraversalProfile &&
                    descriptor.IgnoresTerrainEffects == (chicken.Archetype == AttackerArchetype.Flying)),
                Is.True,
                "The mapper should receive pooled roster data without requiring inactive attacker instances.");
        }

        [UnityTest]
        public IEnumerator GameplayWaypointRoute_MatchesTheAiTimingRoute()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var foodStore = GameBasicsTestSupport.CreateFoodStore(new Vector2(4f, 0f));
            var mapperObject = new GameObject("TAD Test Timing-Free Route Mapper");
            var mapper = mapperObject.AddComponent<FarmRouteMapper>();
            mapper.SetFoodStore(foodStore);

            var chicken = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(chicken, Is.Not.Null);

            yield return null;
            Assert.That(mapper.RebuildRoutes(), Is.True);

            var movementWaypoints = new List<Vector3>();
            var aiWaypoints = new List<Vector3>();
            var startPosition = new Vector3(-4f, 0f, 0f);

            Assert.That(mapper.TryGetFastestRouteWaypoints(startPosition, chicken, movementWaypoints), Is.True);
            Assert.That(mapper.TryGetFastestRoute(startPosition, chicken, aiWaypoints, out var timing), Is.True);
            Assert.That(timing.IsValid, Is.True);
            Assert.That(movementWaypoints.Count, Is.EqualTo(aiWaypoints.Count));

            for (var index = 0; index < movementWaypoints.Count; index++)
            {
                Assert.That(movementWaypoints[index], Is.EqualTo(aiWaypoints[index]));
            }
        }

        [UnityTest]
        public IEnumerator GameplayWaypointRoutes_ReuseExactSpawnRoutesAndInvalidateAfterRebuild()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var foodStore = GameBasicsTestSupport.CreateFoodStore(new Vector2(4f, 0f));
            var mapperObject = new GameObject("TAD Test Cached Waypoint Route Mapper");
            var mapper = mapperObject.AddComponent<FarmRouteMapper>();
            mapper.SetFoodStore(foodStore);

            var chicken = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(chicken, Is.Not.Null);

            yield return null;
            Assert.That(mapper.RebuildRoutes(), Is.True);

            var spawnPosition = new Vector3(-4f, 0f, 0f);
            var firstRoute = new List<Vector3>();
            var secondRoute = new List<Vector3>();
            var alternateRoute = new List<Vector3>();

            Assert.That(mapper.TryGetFastestRouteWaypoints(spawnPosition, chicken, firstRoute), Is.True);
            var firstMissCount = mapper.WaypointRouteCacheMissCount;
            Assert.That(mapper.CachedWaypointRouteCount, Is.EqualTo(1));

            Assert.That(mapper.TryGetFastestRouteWaypoints(spawnPosition, chicken, secondRoute), Is.True);
            Assert.That(mapper.WaypointRouteCacheMissCount, Is.EqualTo(firstMissCount));
            CollectionAssert.AreEqual(firstRoute, secondRoute);

            // Starts in the same map but at a different precise position must not share an initial
            // segment: that segment carries the wall-clearance validation for a live spawn point.
            Assert.That(mapper.TryGetFastestRouteWaypoints(new Vector3(-3f, 0f, 0f), chicken, alternateRoute), Is.True);
            Assert.That(mapper.WaypointRouteCacheMissCount, Is.EqualTo(firstMissCount + 1));
            Assert.That(mapper.CachedWaypointRouteCount, Is.EqualTo(2));

            Assert.That(mapper.RebuildRoutes(), Is.True);
            Assert.That(mapper.CachedWaypointRouteCount, Is.Zero);

            secondRoute.Clear();
            Assert.That(mapper.TryGetFastestRouteWaypoints(spawnPosition, chicken, secondRoute), Is.True);
            Assert.That(mapper.WaypointRouteCacheMissCount, Is.EqualTo(firstMissCount + 2));
            CollectionAssert.AreEqual(firstRoute, secondRoute);
        }
    }
}
