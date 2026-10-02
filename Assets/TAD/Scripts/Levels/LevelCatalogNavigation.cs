using System;
using System.IO;
using UnityEngine.SceneManagement;

namespace TAD.Levels
{
    /// <summary>
    /// Resolves a catalog's immediate available successor to its Build Settings scene.
    /// Keeping this in one place makes pause and result screens follow the same route.
    /// </summary>
    public static class LevelCatalogNavigation
    {
        public static bool TryGetNextUnlockedLevel(
            LevelCatalog catalog,
            string currentSceneName,
            out LevelDefinition level,
            out int sceneBuildIndex)
        {
            level = null;
            sceneBuildIndex = -1;
            if (catalog == null ||
                !catalog.TryGetNextUnlockedLevel(currentSceneName, out level) ||
                level == null)
            {
                return false;
            }

            if (!TryGetBuildSceneIndex(level.SceneName, out sceneBuildIndex))
            {
                level = null;
                return false;
            }

            return true;
        }

        private static bool TryGetBuildSceneIndex(string sceneName, out int sceneBuildIndex)
        {
            sceneBuildIndex = -1;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            for (var index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                var scenePath = SceneUtility.GetScenePathByBuildIndex(index);
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                var pathWithoutExtension = Path.ChangeExtension(scenePath, null);
                var buildSceneName = Path.GetFileNameWithoutExtension(scenePath);
                if (!string.Equals(sceneName, scenePath, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sceneName, pathWithoutExtension, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sceneName, buildSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sceneBuildIndex = index;
                return true;
            }

            return false;
        }
    }
}
