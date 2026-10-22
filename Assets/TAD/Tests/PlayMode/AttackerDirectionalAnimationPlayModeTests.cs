using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Pooling;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class AttackerDirectionalAnimationPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTest]
        public IEnumerator ProceduralAtlasAttacker_TurnsFallsAndResetsWhenReturnedToItsPool()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var definitions = new List<AttackerDefinition>();
            pool.CopyAvailableAttackerDefinitions(definitions);
            var chicken = definitions.Single(definition => definition.DisplayName == "Chicken");
            var attackerObject = pool.SpawnAttacker(chicken, Vector3.zero, Quaternion.identity);
            var attacker = attackerObject.GetComponent<BasicAttacker>();
            var pooledInstance = attackerObject.GetComponent<PooledInstance>();
            var visualController = attackerObject.GetComponentInChildren<AttackerVisualController>(true);
            var visualRenderer = visualController.GetComponent<SpriteRenderer>();
            var bodyCollider = attackerObject.GetComponent<PolygonCollider2D>();
            var deathPuff = visualController.transform.Find("DeathPuff")?.GetComponent<SpriteRenderer>();

            Assert.That(attacker, Is.Not.Null);
            Assert.That(pooledInstance, Is.Not.Null);
            Assert.That(visualController, Is.Not.Null);
            Assert.That(visualRenderer, Is.Not.Null);
            Assert.That(deathPuff, Is.Not.Null);

            attacker.SetMoveDirection(Vector2.right);
            yield return null;
            var rightFacing = visualController.transform.localEulerAngles.z;

            attacker.SetMoveDirection(Vector2.up);
            yield return null;
            var upFacing = visualController.transform.localEulerAngles.z;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(rightFacing, upFacing)), Is.EqualTo(90f).Within(0.5f));
            Assert.That(bodyCollider.bounds.center.x, Is.EqualTo(visualRenderer.bounds.center.x).Within(0.02f));
            Assert.That(bodyCollider.bounds.center.y, Is.EqualTo(visualRenderer.bounds.center.y).Within(0.02f));

            attacker.TakeDamage(attacker.RemainingHealth);
            yield return null;
            Assert.That(attacker.IsDefeated, Is.True);
            Assert.That(deathPuff.enabled, Is.True);
            Assert.That(visualRenderer.color.a, Is.LessThan(1f));

            yield return new WaitForSeconds(0.65f);
            Assert.That(attackerObject.activeSelf, Is.False);

            var reusedObject = pool.SpawnAttacker(chicken, Vector3.zero, Quaternion.identity);
            yield return null;
            Assert.That(reusedObject, Is.SameAs(attackerObject));

            var reusedVisual = reusedObject.GetComponentInChildren<AttackerVisualController>(true);
            var reusedRenderer = reusedVisual.GetComponent<SpriteRenderer>();
            var reusedPuff = reusedVisual.transform.Find("DeathPuff")?.GetComponent<SpriteRenderer>();
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, reusedVisual.transform.localEulerAngles.z)), Is.LessThan(0.5f));
            Assert.That(reusedRenderer.color.a, Is.EqualTo(1f).Within(0.001f));
            Assert.That(reusedPuff.enabled, Is.False);

            reusedObject.GetComponent<PooledInstance>().ReturnToPool();
        }
    }
}
