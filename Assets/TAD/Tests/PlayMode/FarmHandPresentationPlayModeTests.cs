using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Defenders;
using TAD.Pooling;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    /// <summary>Exercises the presentation state that belongs to a pooled defender lifetime.</summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FarmHandPresentationPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // This fixture samples a time-based visual cycle. Clear its owned pool objects and
            // reset time scale so no earlier Game Basics fixture can freeze its visual clock.
            Time.timeScale = 1f;
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTest]
        public IEnumerator RelocatingFarmHand_CyclesItsAuthoredWalkFrames()
        {
            GameBasicsTestSupport.CreateMainCamera();
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);

            var defenderObject = pool.Spawn(PooledObjectType.Defender, Vector3.zero, Quaternion.identity);
            var defender = defenderObject != null ? defenderObject.GetComponent<FarmHand>() : null;
            Assert.That(defender, Is.Not.Null);
            Assert.That(defender.GetComponent<FarmHandVisualController>(), Is.Not.Null);

            yield return null;
            var idleSprite = defender.Visual.sprite;
            Assert.That(idleSprite, Is.Not.Null);

            var route = new List<Vector3> { Vector3.zero, new(2f, 0f, 0f) };
            Assert.That(defender.TryBeginRelocation(new Vector2(2f, 0f), route), Is.True);

            yield return new WaitForSeconds(0.22f);
            // FarmHandVisualController advances the authored walk pose in LateUpdate. The test
            // coroutine resumes during Update, so advance one normal frame rather than racing its
            // presentation pass. A normal frame works in both the Editor and headless CI.
            yield return null;
            Assert.That(defender.IsRelocating, Is.True);
            Assert.That(defender.Visual.sprite, Is.Not.EqualTo(idleSprite),
                "The body should advance to its second walk pose while the defender is relocating.");

            Object.Destroy(pool.gameObject);
        }
    }
}
