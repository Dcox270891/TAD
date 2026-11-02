using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Navigation;
using TAD.Pooling;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    /// <summary>Guards against returning to an all-at-once warmup as the attacker roster grows.</summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class SpawnPoolStartupPlayModeTests
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
        public IEnumerator AdditionalAttackerPool_WarmsAcrossFramesAndCanSpawnImmediately()
        {
            var definition = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(definition, Is.Not.Null);

            var attackerPrefab = new GameObject("TAD Test Deferred Attacker Prefab");
            attackerPrefab.SetActive(false);
            var prefabAttacker = attackerPrefab.AddComponent<BasicAttacker>();
            prefabAttacker.ConfigureDefinition(definition);

            var poolObject = new GameObject("TAD Test Deferred Spawn Pool");
            poolObject.SetActive(false);
            poolObject.AddComponent<FarmRouteMapper>();
            var pool = poolObject.AddComponent<SpawnPool>();
            SetPrivateField(pool, "additionalAttackerPrefabs", new[] { attackerPrefab });
            SetPrivateField(pool, "additionalAttackerInitialCapacity", 5);

            poolObject.SetActive(true);

            Assert.That(pool.PendingDeferredPrewarmObjectCount, Is.EqualTo(5),
                "Additional buckets should be scheduled rather than instantiated in SpawnPool.Awake.");

            var spawned = pool.SpawnAttacker(definition, Vector3.zero, Quaternion.identity);
            Assert.That(spawned, Is.Not.Null);
            Assert.That(spawned.activeSelf, Is.True,
                "A wave may spawn before the optional roster has finished warming.");
            pool.Release(spawned.GetComponent<PooledInstance>());

            yield return null;
            Assert.That(pool.PendingDeferredPrewarmObjectCount, Is.GreaterThan(0),
                "Only a small batch should be created in the first warmup frame.");

            for (var frame = 0; frame < 5 && pool.PendingDeferredPrewarmObjectCount > 0; frame++)
            {
                yield return null;
            }

            Assert.That(pool.PendingDeferredPrewarmObjectCount, Is.Zero);
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field {fieldName} was not found.");
            field.SetValue(target, value);
        }
    }
}
