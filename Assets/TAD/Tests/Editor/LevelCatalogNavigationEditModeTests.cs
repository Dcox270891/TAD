using System.Linq;
using NUnit.Framework;
using TAD.Levels;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class LevelCatalogNavigationEditModeTests
    {
        [Test]
        public void TryGetNextUnlockedLevel_UsesTheCatalogOrder()
        {
            var levelOne = CreateLevel("level-01", "Level 1", "Level1", unlocked: true);
            var levelTwo = CreateLevel("level-02", "Level 2", "Level2", unlocked: true);
            var levelThree = CreateLevel("level-03", "Level 3", "Level3", unlocked: true);
            var levelFour = CreateLevel("level-04", "Level 4", "Level4", unlocked: true);
            var catalog = CreateCatalog(levelOne, levelTwo, levelThree, levelFour);

            try
            {
                Assert.That(catalog.TryGetNextUnlockedLevel("Level1", out var nextLevel), Is.True);
                Assert.That(nextLevel, Is.EqualTo(levelTwo),
                    "Next level navigation must follow the authored catalog order, not Build Settings order.");

                Assert.That(catalog.TryGetNextUnlockedLevel("Level2", out nextLevel), Is.True);
                Assert.That(nextLevel, Is.EqualTo(levelThree));

                Assert.That(catalog.TryGetNextUnlockedLevel("Level3", out nextLevel), Is.True);
                Assert.That(nextLevel, Is.EqualTo(levelFour));

                Assert.That(catalog.TryGetNextUnlockedLevel("Level4", out nextLevel), Is.False);
                Assert.That(nextLevel, Is.Null);
            }
            finally
            {
                DestroyCatalogAndLevels(catalog, levelOne, levelTwo, levelThree, levelFour);
            }
        }

        [Test]
        public void TryGetNextUnlockedLevel_DoesNotSkipAnImmediateLockedSuccessor()
        {
            var levelOne = CreateLevel("level-01", "Level 1", "Level1", unlocked: true);
            var lockedLevelTwo = CreateLevel("level-02", "Level 2", "Level2", unlocked: false);
            var levelThree = CreateLevel("level-03", "Level 3", "Level3", unlocked: true);
            var catalog = CreateCatalog(levelOne, lockedLevelTwo, levelThree);

            try
            {
                Assert.That(catalog.TryGetNextUnlockedLevel("Level1", out var nextLevel), Is.False);
                Assert.That(nextLevel, Is.Null,
                    "A locked immediate successor must hide Next Level rather than skipping ahead to a later map.");
            }
            finally
            {
                DestroyCatalogAndLevels(catalog, levelOne, lockedLevelTwo, levelThree);
            }
        }

        [Test]
        public void ExistingPrototypeLevels_AreUnlockedByDefault()
        {
            var existingLevelPaths = new[]
            {
                "Assets/TAD/Data/Levels/Level1Definition.asset",
                "Assets/TAD/Data/Levels/Level2Definition.asset",
                "Assets/TAD/Data/Levels/Level3Definition.asset",
                "Assets/TAD/Data/Levels/Level4Definition.asset"
            };

            foreach (var levelPath in existingLevelPaths)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(levelPath);
                Assert.That(level, Is.Not.Null, "Missing authored level definition " + levelPath + ".");
                Assert.That(level.IsUnlocked, Is.True,
                    "The current prototype catalog should keep every shipped level available until win-based progression is added.");
            }
        }

        [Test]
        public void AuthoredUnlockedCatalogLevels_ResolveToEnabledBuildScenes()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/TAD/Data/Levels/LevelCatalog.asset");
            Assert.That(catalog, Is.Not.Null, "The authored level catalog must exist.");

            foreach (var level in catalog.Levels)
            {
                if (level == null || !level.IsUnlocked)
                {
                    continue;
                }

                var resolved = false;
                foreach (var buildScene in EditorBuildSettings.scenes)
                {
                    if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                    {
                        continue;
                    }

                    var buildSceneName = System.IO.Path.GetFileNameWithoutExtension(buildScene.path);
                    if (string.Equals(buildSceneName, level.SceneName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        resolved = true;
                        break;
                    }
                }

                Assert.That(resolved, Is.True,
                    $"Unlocked catalog level '{level.DisplayName}' targets '{level.SceneName}', which is not an enabled Build Settings scene.");
            }
        }

        [Test]
        public void AuthoredCatalogLevels_MatchEnabledGameplayBuildSceneOrder()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/TAD/Data/Levels/LevelCatalog.asset");
            Assert.That(catalog, Is.Not.Null, "The authored level catalog must exist.");

            var catalogSceneNames = catalog.Levels
                .Where(level => level != null && level.IsUnlocked && !string.IsNullOrWhiteSpace(level.SceneName))
                .Select(level => level.SceneName)
                .ToArray();
            var gameplayBuildSceneNames = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => System.IO.Path.GetFileNameWithoutExtension(scene.path))
                .Where(sceneName => !string.Equals(sceneName, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.That(gameplayBuildSceneNames, Is.EqualTo(catalogSceneNames),
                "The catalogue must contain every enabled gameplay scene in the same progression order.");
        }

        private static LevelDefinition CreateLevel(string id, string displayName, string sceneName, bool unlocked)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            var serializedLevel = new SerializedObject(level);
            serializedLevel.FindProperty("id").stringValue = id;
            serializedLevel.FindProperty("displayName").stringValue = displayName;
            serializedLevel.FindProperty("sceneName").stringValue = sceneName;
            serializedLevel.FindProperty("unlocked").boolValue = unlocked;
            serializedLevel.ApplyModifiedPropertiesWithoutUndo();
            return level;
        }

        private static LevelCatalog CreateCatalog(params LevelDefinition[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var serializedCatalog = new SerializedObject(catalog);
            var levelEntries = serializedCatalog.FindProperty("levels");
            levelEntries.arraySize = levels.Length;
            for (var index = 0; index < levels.Length; index++)
            {
                levelEntries.GetArrayElementAtIndex(index).objectReferenceValue = levels[index];
            }

            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }

        private static void DestroyCatalogAndLevels(LevelCatalog catalog, params LevelDefinition[] levels)
        {
            Object.DestroyImmediate(catalog);
            foreach (var level in levels)
            {
                Object.DestroyImmediate(level);
            }
        }
    }
}
