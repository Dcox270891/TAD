#if UNITY_EDITOR
using System;
using TAD.Navigation;
using TAD.Pooling;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Keeps the reusable gameplay core responsible for portrait camera presentation. Existing
    /// level scenes are instances of this core, so future levels receive the controller too.
    /// </summary>
    internal static class GameplayPortraitOrientationAssetBuilder
    {
        private const string GameplayLevelCorePrefabPath = "Assets/TAD/Prefabs/Levels/GameplayLevelCore.prefab";

        [InitializeOnLoadMethod]
        private static void QueueSharedCoreRepair()
        {
            EditorApplication.delayCall -= RepairSharedCoreIfNeeded;
            EditorApplication.delayCall += RepairSharedCoreIfNeeded;
        }

        [MenuItem("TAD/Level Tools/Enable Portrait Gameplay Orientation")]
        public static void RepairSharedCoreIfNeeded()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(GameplayLevelCorePrefabPath);
            try
            {
                var spawnPool = root.GetComponentInChildren<SpawnPool>(true);
                if (spawnPool == null)
                {
                    throw new InvalidOperationException("GameplayLevelCore is missing its SpawnPool.");
                }

                if (spawnPool.GetComponent<GameplayPortraitOrientationController>() != null)
                {
                    return;
                }

                spawnPool.gameObject.AddComponent<GameplayPortraitOrientationController>();
                PrefabUtility.SaveAsPrefabAsset(root, GameplayLevelCorePrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
