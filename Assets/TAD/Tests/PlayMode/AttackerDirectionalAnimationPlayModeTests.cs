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

        [UnityTest]
        public IEnumerator ProceduralAtlasAttacker_KeepsItsPhysicsFootprintStableDuringCosmeticWalkFrames()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var definitions = new List<AttackerDefinition>();
            pool.CopyAvailableAttackerDefinitions(definitions);
            var chicken = definitions.Single(definition => definition.DisplayName == "Chicken");
            var attackerObject = pool.SpawnAttacker(chicken, Vector3.zero, Quaternion.identity);
            var attacker = attackerObject.GetComponent<BasicAttacker>();
            var bodyCollider = attackerObject.GetComponent<PolygonCollider2D>();

            Assert.That(attacker, Is.Not.Null);
            Assert.That(bodyCollider, Is.Not.Null);

            attacker.SetMoveDirection(Vector2.right);
            yield return null;
            Physics2D.SyncTransforms();
            var initialShapeHash = bodyCollider.GetShapeHash();

            // The procedural roster repeats one atlas sprite. Its alternating walk frame is only
            // a visual bob/squash, so it must not rebuild the active PolygonCollider2D.
            yield return new WaitForSeconds(0.2f);
            Physics2D.SyncTransforms();
            Assert.That(bodyCollider.GetShapeHash(), Is.EqualTo(initialShapeHash));

            attackerObject.GetComponent<PooledInstance>().ReturnToPool();
        }

        [UnityTest]
        public IEnumerator PooledAttackers_UseTheSharedNonBlockingAttackerLayerRule()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var definitions = new List<AttackerDefinition>();
            pool.CopyAvailableAttackerDefinitions(definitions);
            var chicken = definitions.Single(definition => definition.DisplayName == "Chicken");
            var firstObject = pool.SpawnAttacker(chicken, Vector3.zero, Quaternion.identity);
            var secondObject = pool.SpawnAttacker(chicken, Vector3.right * 2f, Quaternion.identity);
            var firstCollider = firstObject.GetComponent<PolygonCollider2D>();
            var secondCollider = secondObject.GetComponent<PolygonCollider2D>();

            Assert.That(firstCollider, Is.Not.Null);
            Assert.That(secondCollider, Is.Not.Null);
            yield return new WaitForFixedUpdate();
            var attackerLayer = LayerMask.NameToLayer("Attacker");
            Assert.That(attackerLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(Physics2D.GetIgnoreLayerCollision(attackerLayer, attackerLayer), Is.True);

            firstObject.GetComponent<PooledInstance>().ReturnToPool();
            yield return null;
            var reusedObject = pool.SpawnAttacker(chicken, Vector3.left * 2f, Quaternion.identity);
            var reusedCollider = reusedObject.GetComponent<PolygonCollider2D>();

            Assert.That(reusedObject, Is.SameAs(firstObject));
            Assert.That(reusedCollider, Is.Not.Null);
            yield return new WaitForFixedUpdate();
            Assert.That(Physics2D.GetIgnoreLayerCollision(attackerLayer, attackerLayer), Is.True,
                "Pooled attackers must retain their shared non-blocking body collision rule after reuse.");

            reusedObject.GetComponent<PooledInstance>().ReturnToPool();
            secondObject.GetComponent<PooledInstance>().ReturnToPool();
        }
    }
}
