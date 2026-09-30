using System.Collections.Generic;
using NUnit.Framework;
using TAD.Defenders;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FarmHandFootprintEditModeTests
    {
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";
        private const string FarmHandBodyPath = "Assets/TAD/Data/Defenders/Visuals/FarmHandBody_Default.asset";

        [Test]
        public void PlacementFootprint_MatchesTheCurrentBodySpriteOutlineAfterVisualChanges()
        {
            var root = PrefabUtility.LoadPrefabContents(FarmHandPrefabPath);
            try
            {
                var farmHand = root.GetComponent<FarmHand>();
                var visual = farmHand != null ? farmHand.Visual : null;
                Assert.That(farmHand, Is.Not.Null, "The FarmHand prefab needs its runtime placement owner.");
                Assert.That(visual, Is.Not.Null, "The FarmHand prefab needs its body sprite renderer.");
                Assert.That(visual.sprite, Is.Not.Null, "The FarmHand body renderer needs a generated sprite outline.");

                farmHand.SynchronizePlacementFootprint();
                AssertPlacementFootprintMatchesBody(farmHand, visual);

                var bodyDefinition = AssetDatabase.LoadAssetAtPath<FarmHandBodyVisualDefinition>(FarmHandBodyPath);
                Assert.That(bodyDefinition, Is.Not.Null);
                var alternateSprite = FindDifferentBodySprite(bodyDefinition, visual.sprite);
                Assert.That(alternateSprite, Is.Not.Null, "The FarmHand body definition needs more than one directional sprite.");

                visual.sprite = alternateSprite;
                visual.transform.localScale *= 1.35f;
                farmHand.SynchronizePlacementFootprint();
                AssertPlacementFootprintMatchesBody(farmHand, visual);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Sprite FindDifferentBodySprite(FarmHandBodyVisualDefinition definition, Sprite currentSprite)
        {
            for (var direction = 0; direction < FarmHandDirectionalLayerDefinition.DirectionCount; direction++)
            {
                for (var frame = 0; frame < FarmHandDirectionalLayerDefinition.MovementFrameCount; frame++)
                {
                    var candidate = definition.GetMovementSprite(direction, frame);
                    if (candidate != null && candidate != currentSprite && candidate.GetPhysicsShapeCount() > 0)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static void AssertPlacementFootprintMatchesBody(FarmHand farmHand, SpriteRenderer visual)
        {
            var placementCollider = farmHand.PlacementCollider;
            Assert.That(placementCollider, Is.Not.Null);
            Assert.That(placementCollider.isTrigger, Is.True,
                "Placement should remain a defender-only trigger and never physically block attackers or projectiles.");
            Assert.That(placementCollider.gameObject.layer, Is.EqualTo(farmHand.gameObject.layer));
            Assert.That(visual.sprite.GetPhysicsShapeCount(), Is.GreaterThan(0),
                "FarmHand body artwork must provide a generated physics outline.");
            Assert.That(placementCollider.pathCount, Is.EqualTo(visual.sprite.GetPhysicsShapeCount()));

            var expectedPath = new List<Vector2>();
            for (var pathIndex = 0; pathIndex < placementCollider.pathCount; pathIndex++)
            {
                expectedPath.Clear();
                visual.sprite.GetPhysicsShape(pathIndex, expectedPath);
                var actualPath = placementCollider.GetPath(pathIndex);
                Assert.That(actualPath.Length, Is.EqualTo(expectedPath.Count));

                for (var pointIndex = 0; pointIndex < expectedPath.Count; pointIndex++)
                {
                    var expectedPoint = (Vector2)placementCollider.transform.InverseTransformPoint(
                        visual.transform.TransformPoint(expectedPath[pointIndex]));
                    Assert.That(actualPath[pointIndex].x, Is.EqualTo(expectedPoint.x).Within(0.0001f));
                    Assert.That(actualPath[pointIndex].y, Is.EqualTo(expectedPoint.y).Within(0.0001f));
                }
            }
        }
    }
}
