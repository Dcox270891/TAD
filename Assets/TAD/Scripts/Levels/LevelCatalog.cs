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
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.SceneName))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return false;
            }

            var selectedIndex = Random.Range(0, validCount);
            foreach (var candidate in levels)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SceneName))
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