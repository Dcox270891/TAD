#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TAD.Environment;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Establishes a compact, mobile-readable world scale: a wall module is two Unity units wide,
    /// a farmhand fits between modules, and major landmarks read clearly without oversized hitboxes.
    /// The values are centralised here so future environment art can be swapped safely.
    /// </summary>
    public static class FarmWorldScaleAssetBuilder
    {
        public const float StandardWallModuleWidth = 2f;
        public const float MajorLandmarkWidth = 3.4f;
        public const float TerrainPatchWidth = 2.4f;
        public const float SpawnLandmarkWidth = 3.6f;

        private const string SpawnWoodlandPrefabPath = "Assets/TAD/Prefabs/Environment/SpawnWoodlandBackdrop.prefab";
        private const string SpawnWoodlandArtPath = "Assets/TAD/Art/Environment/Spawn/TAD_SpawnWoodland.png";
        private const string SpawnWoodlandSpriteName = "SpawnWoodland_Entrance";

        private static readonly IReadOnlyDictionary<string, Vector2> TargetVisualSizes =
            new Dictionary<string, Vector2>
            {
                ["Assets/TAD/Prefabs/Environment/FullCoverWall.prefab"] = new Vector2(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/FullCoverWoodFence.prefab"] = new Vector2(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/FullCoverChainLinkFence.prefab"] = new Vector2(StandardWallModuleWidth, StandardWallModuleWidth),
                ["Assets/TAD/Prefabs/Environment/HalfCoverWall.prefab"] = new Vector2(StandardWallModuleWidth, 1.35f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverHayBales.prefab"] = new Vector2(StandardWallModuleWidth, 1.3f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverFarmBins.prefab"] = new Vector2(1.6f, 1.05f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverChickenCoop.prefab"] = new Vector2(2.2f, 1.55f),
                ["Assets/TAD/Prefabs/Environment/HalfCoverLowBrickWall.prefab"] = new Vector2(StandardWallModuleWidth, 1.05f),
                ["Assets/TAD/Prefabs/Environment/FoodStore.prefab"] = new Vector2(MajorLandmarkWidth, 2.27f),
                ["Assets/TAD/Prefabs/Environment/Mud.prefab"] = new Vector2(TerrainPatchWidth, TerrainPatchWidth),
                ["Assets/TAD/Prefabs/Environment/Ice.prefab"] = new Vector2(TerrainPatchWidth, TerrainPatchWidth)
            };

        [MenuItem("TAD/Environment/Normalize World Scale")]
        public static void NormalizeFromMenu()
        {
            NormalizeFromAutomation();
        }

        /// <summary>Safe reusable entry point for art revisions and project automation.</summary>
        public static void NormalizeFromAutomation()
        {
            var changedCount = 0;
            foreach (var pair in TargetVisualSizes)
            {
                if (NormalizePrefab(pair.Key, pair.Value))
                {
                    changedCount++;
                }
            }

            if (NormalizeSpawnWoodlandBackdrop())
            {
                changedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"TAD normalised {changedCount} environment prefab(s) to the shared mobile world scale.");
        }

        private static bool NormalizeSpawnWoodlandBackdrop()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnWoodlandPrefabPath);
            if (prefab == null)
            {
                return false;
            }

            // This texture is imported as a multi-sprite sheet. Explicitly retrieving its named
            // entrance avoids the invalid "whole texture" reference Unity can otherwise leave
            // on the prefab and makes the visual a consistently scaled, readable spawn landmark.
            var spawnSprite = AssetDatabase.LoadAllAssetsAtPath(SpawnWoodlandArtPath)
                .OfType<Sprite>()
                .FirstOrDefault(sprite => string.Equals(sprite.name, SpawnWoodlandSpriteName, StringComparison.Ordinal));
            if (spawnSprite == null || spawnSprite.bounds.size.x <= Mathf.Epsilon)
            {
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(SpawnWoodlandPrefabPath);
            try
            {
                var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null)
                {
                    return false;
                }

                var targetScale = new Vector3(
                    SpawnLandmarkWidth / spawnSprite.bounds.size.x,
                    SpawnLandmarkWidth / spawnSprite.bounds.size.x,
                    renderer.transform.localScale.z == 0f ? 1f : renderer.transform.localScale.z);
                var changed = renderer.sprite != spawnSprite || renderer.transform.localScale != targetScale;
                renderer.sprite = spawnSprite;
                renderer.transform.localScale = targetScale;

                if (changed)
                {
                    EditorUtility.SetDirty(renderer);
                    EditorUtility.SetDirty(renderer.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, SpawnWoodlandPrefabPath);
                }

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool NormalizePrefab(string path, Vector2 targetSize)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                // The project can remain compatible with a partial art pack; missing optional
                // prop types are skipped rather than making level authoring fail.
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var visual = root.transform.Find("Visual")?.GetComponent<SpriteRenderer>() ??
                             root.GetComponentInChildren<SpriteRenderer>(true);
                if (visual == null || visual.sprite == null || visual.sprite.bounds.size.x <= Mathf.Epsilon)
                {
                    return false;
                }

                var localScale = visual.transform.localScale;
                var spriteSize = visual.sprite.bounds.size;
                var targetScale = new Vector3(
                    Mathf.Sign(localScale.x == 0f ? 1f : localScale.x) * (targetSize.x / spriteSize.x),
                    Mathf.Sign(localScale.y == 0f ? 1f : localScale.y) * (targetSize.y / spriteSize.y),
                    localScale.z == 0f ? 1f : localScale.z);
                var changed = visual.transform.localScale != targetScale;
                visual.transform.localScale = targetScale;

                if (path.EndsWith("/FoodStore.prefab", StringComparison.Ordinal))
                {
                    changed |= NormalizeFoodBagPiles(root, targetSize.x);
                }

                var sourceCollider = root.GetComponent<BoxCollider2D>();
                if (sourceCollider != null)
                {
                    var synchronizer = root.GetComponent<SpriteBoxColliderSynchronizer>();
                    if (synchronizer == null)
                    {
                        synchronizer = root.AddComponent<SpriteBoxColliderSynchronizer>();
                        changed = true;
                    }

                    synchronizer.Synchronize();
                    EditorUtility.SetDirty(synchronizer);
                    EditorUtility.SetDirty(sourceCollider);
                }

                if (changed)
                {
                    EditorUtility.SetDirty(visual.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool NormalizeFoodBagPiles(GameObject root, float barnWidth)
        {
            // These normalized locations retain the authored five-pile cluster when the barn is
            // enlarged from a token-sized sprite into a legible landmark. FoodStoreVisualController
            // still owns which piles are visible as food is consumed.
            var normalizedPositions = new[]
            {
                new Vector2(-0.458f, -0.104f),
                new Vector2(-0.130f, -0.156f),
                new Vector2(0.199f, -0.104f),
                new Vector2(-0.355f, 0.208f),
                new Vector2(0.112f, 0.208f)
            };
            var changed = false;
            for (var index = 0; index < normalizedPositions.Length; index++)
            {
                var pile = root.transform.Find($"FoodBagPile_{index}");
                var renderer = pile != null ? pile.GetComponent<SpriteRenderer>() : null;
                if (pile == null || renderer == null || renderer.sprite == null)
                {
                    continue;
                }

                var desiredPosition = (Vector3)(normalizedPositions[index] * barnWidth);
                var desiredWidth = barnWidth * 0.245f;
                var spriteSize = renderer.sprite.bounds.size;
                var desiredScale = new Vector3(
                    desiredWidth / spriteSize.x,
                    (desiredWidth * (spriteSize.y / spriteSize.x)) / spriteSize.y,
                    pile.localScale.z == 0f ? 1f : pile.localScale.z);
                if (pile.localPosition != desiredPosition || pile.localScale != desiredScale)
                {
                    pile.localPosition = desiredPosition;
                    pile.localScale = desiredScale;
                    EditorUtility.SetDirty(pile);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
#endif
