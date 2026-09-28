using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Owns the experience curve shared by every farmhand. The opening thresholds are intentionally
    /// explicit for player readability; later levels grow gently so a committed defender can still
    /// reach level 10 during a full ten-day match.
    /// </summary>
    public static class DefenderLevelProgression
    {
        public const int DefaultLevelOneToTwoExperience = 10;
        public const int DefaultLevelTwoToThreeExperience = 12;
        public const int DefaultLevelThreeToFourExperience = 15;
        public const float LaterLevelGrowthMultiplier = 1.12f;

        /// <summary>
        /// Gets the XP required to advance from <paramref name="currentLevel"/> to the next level.
        /// A configured base of 10 therefore produces 10, 12, 15, 17, 19, 22, 24, 27, and 30 XP
        /// through level 10.
        /// </summary>
        public static int GetExperienceRequiredForNextLevel(int currentLevel, int baseExperience)
        {
            var normalizedLevel = Mathf.Max(1, currentLevel);
            var normalizedBaseExperience = Mathf.Max(1, baseExperience);

            if (normalizedLevel == 1)
            {
                return normalizedBaseExperience;
            }

            if (normalizedLevel == 2)
            {
                return Mathf.CeilToInt(normalizedBaseExperience * 1.2f);
            }

            var levelThreeRequirement = Mathf.CeilToInt(normalizedBaseExperience * 1.5f);
            if (normalizedLevel == 3)
            {
                return levelThreeRequirement;
            }

            return Mathf.CeilToInt(levelThreeRequirement * Mathf.Pow(
                LaterLevelGrowthMultiplier,
                normalizedLevel - 3));
        }

        /// <summary>Returns the total XP required to progress from one level to another.</summary>
        public static int GetTotalExperienceRequired(int startingLevel, int targetLevel, int baseExperience)
        {
            var normalizedStartingLevel = Mathf.Max(1, startingLevel);
            var normalizedTargetLevel = Mathf.Max(normalizedStartingLevel, targetLevel);
            var total = 0;
            for (var level = normalizedStartingLevel; level < normalizedTargetLevel; level++)
            {
                total += GetExperienceRequiredForNextLevel(level, baseExperience);
            }

            return total;
        }

        /// <summary>Applies a per-level multiplier while preserving a neutral level-one value.</summary>
        public static float GetLevelMultiplier(int level, float perLevelMultiplier)
        {
            return Mathf.Pow(Mathf.Max(0f, perLevelMultiplier), Mathf.Max(0, level - 1));
        }
    }
}
