using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("WorldScale")]
    public sealed class FarmWorldScaleEditModeTests
    {
        // Keep these expectations in the test assembly rather than referencing the editor-only
        // asset builder assembly. The builder is the authoring source; these are the gameplay
        // contract that protects level readability on a phone.
        private const float StandardWallModuleWidth = 2f;
        private const float MajorLandmarkWidth = 3.4f;
        private const float TerrainPatchWidth = 2.4f;
        private const float SpawnLandmarkWidth = 3.6f;

        [Test]
        public void EnvironmentPrefabs_FollowTheSharedMobileWorldScale()
        {
            var expectedSizes = new Dictionary<string, Vector2>
            {
                ["Assets/TAD/Prefabs/Environment/FullCoverWall.prefab"] = new(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/FullCoverWoodFence.prefab"] = new(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/FullCoverChainLinkFence.prefab"] = new(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/HalfCoverWall.prefab"] = new(StandardWallModuleWidth, 1.35f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverHayBales.prefab"] = new(StandardWallModuleWidth, 1.3f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverFarmBins.prefab"] = new(1.6f, 1.05f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverChickenCoop.prefab"] = new(2.2f, 1.55f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverLowBrickWall.prefab"] = new(StandardWallModuleWidth, 1.05f),
                ["Assets/TAD/Prefabs/Environment/FoodStore.prefab"] = new(MajorLandmarkWidth, 2.27f),
                ["Assets/TAD/Prefabs/Environment/Mud.prefab"] = new(TerrainPatchWidth, TerrainPatchWidth),
                ["Assets/TAD/Prefabs/Environment/Ice.prefab"] = new(TerrainPatchWidth, TerrainPatchWidth),
                ["Assets/TAD/Prefabs/Environment/SpawnWoodlandBackdrop.prefab"] = new(SpawnLandmarkWidth, 2.4f)
            };

            foreach (var pair in expectedSizes)
            {
                var root = PrefabUtility.LoadPrefabContents(pair.Key);
                try
                {
                    var visual = root.transform.Find("Visual")?.GetComponent<SpriteRenderer>() ??
                                 root.GetComponentInChildren<SpriteRenderer>(true);
                    Assert.That(visual, Is.Not.Null, pair.Key + " is missing a visible sprite.");
                    Assert.That(visual.sprite, Is.Not.Null, pair.Key + " is missing its sprite.");
                    Assert.That(visual.bounds.size.x, Is.EqualTo(pair.Value.x).Within(0.01f), pair.Key + " has an incorrect visual width.");
                    Assert.That(visual.bounds.size.y, Is.EqualTo(pair.Value.y).Within(0.01f), pair.Key + " has an incorrect visual height.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
    }
}
