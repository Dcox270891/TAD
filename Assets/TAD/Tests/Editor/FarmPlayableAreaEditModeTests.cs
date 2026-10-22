using System.Collections.Generic;
using NUnit.Framework;
using TAD.Navigation;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("Navigation")]
    public sealed class FarmPlayableAreaEditModeTests
    {
        private GameObject areaObject;
        private FarmPlayableArea playableArea;

        [SetUp]
        public void SetUp()
        {
            areaObject = new GameObject("Farm Playable Area Test");
            playableArea = areaObject.AddComponent<FarmPlayableArea>();
            playableArea.SetLocalBoundary(new List<Vector2>
            {
                new(-5f, -4f),
                new(5f, -4f),
                new(5f, 4f),
                new(-5f, 4f)
            });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(areaObject);
        }

        [Test]
        public void ContainsCircle_AcceptsFootprintFullyInsideAuthoredFarm()
        {
            Assert.That(playableArea.ContainsCircle(Vector2.zero, 1f), Is.True);
            Assert.That(playableArea.ContainsCircle(new Vector2(3.9f, 0f), 1f), Is.True);
        }

        [Test]
        public void ContainsCircle_RejectsFootprintThatTouchesOrLeavesFarmBoundary()
        {
            Assert.That(playableArea.ContainsCircle(new Vector2(4.1f, 0f), 1f), Is.False);
            Assert.That(playableArea.ContainsCircle(new Vector2(5.1f, 0f), 0f), Is.False);
            Assert.That(playableArea.ContainsCircle(new Vector2(0f, 3.75f), 0.5f), Is.False);
        }

        [Test]
        public void ContainsCircle_UsesWorldSpaceWhenAreaTransformIsMovedOrRotated()
        {
            areaObject.transform.SetPositionAndRotation(new Vector3(10f, 3f, 0f), Quaternion.Euler(0f, 0f, 90f));

            Assert.That(playableArea.ContainsCircle(new Vector2(10f, 3f), 1f), Is.True);
            Assert.That(playableArea.ContainsCircle(new Vector2(14.5f, 3f), 0.6f), Is.False);
        }
    }
}
