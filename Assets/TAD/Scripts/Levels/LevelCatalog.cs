using System;
using System.Collections.Generic;
using UnityEngine;

namespace TAD.Levels
{
    /// <summary>
    /// Holds the ordered set of maps currently available from the Main Menu.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "TAD/Levels/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [SerializeField] private LevelDefinition[] levels;

        public IReadOnlyList<LevelDefinition> Levels => levels;

        /// <summary>
        /// Determines whether a catalog level is currently available to enter.
        /// The authored flag keeps every existing prototype map available; persistent player
        /// progression can later extend this single check without changing navigation UI.
        /// </summary>
        public bool IsLevelUnlocked(LevelDefinition level)
        {
            return level != null && level.IsUnlocked;
        }

        /// <summary>
        /// Gets the immediate following playable catalog entry when it is unlocked.
        /// A locked successor intentionally prevents later entries from being offered.
        /// </summary>
        public bool TryGetNextUnlockedLevel(string currentSceneName, out LevelDefinition level)
        {
            level = null;
            if (levels == null || string.IsNullOrWhiteSpace(currentSceneName))
            {
                return false;
            }

            var currentIndex = -1;
            for (var index = 0; index < levels.Length; index++)
            {
                var candidate = levels[index];
                if (candidate != null &&
                    string.Equals(candidate.SceneName, currentSceneName, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return false;
            }

            for (var index = currentIndex + 1; index < levels.Length; index++)
            {
                var candidate = levels[index];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SceneName))
                {
                    continue;
                }

                if (!IsLevelUnlocked(candidate))
                {
                    return false;
                }

                level = candidate;
                return true;
            }

            return false;
        }

        public bool TryGetRandomLevel(out LevelDefinition level)
        {
            level = null;
            if (levels == null || levels.Length == 0)
            {
                return false;
            }

            var validCount = 0;
            foreach (var candidate in levels)
            {
                if (candidate != null &&
                    !string.IsNullOrWhiteSpace(candidate.SceneName) &&
                    IsLevelUnlocked(candidate))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return false;
            }

            var selectedIndex = UnityEngine.Random.Range(0, validCount);
            foreach (var candidate in levels)
            {
                if (candidate == null ||
                    string.IsNullOrWhiteSpace(candidate.SceneName) ||
                    !IsLevelUnlocked(candidate))
                {
                    continue;
                }

                if (selectedIndex-- == 0)
                {
                    level = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
