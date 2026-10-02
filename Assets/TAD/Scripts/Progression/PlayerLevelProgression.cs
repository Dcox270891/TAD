using System;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Defines the account-level experience curve independently from the per-farmhand combat curve.
    /// A standard winning match is represented by one hundred experience. The opening requirement
    /// deliberately uses three loss rewards (75 experience), while later requirements grow in
    /// three-level bands so the curve stays predictable and easy to tune.
    /// </summary>
    public static class PlayerLevelProgression
    {
        public const int StartingLevel = 1;
        public const float StandardWinExperience = 100f;
        public const float WinRewardMultiplier = 1f;
        public const float LossRewardMultiplier = 0.25f;
        public const int LossesForFirstLevel = 3;
        public const int LevelsPerRequirementStep = 3;

        /// <summary>
        /// Returns the account XP required to advance from <paramref name="currentLevel"/>.
        /// Level one costs 75 XP so three 25%-reward losses on a 100-XP match make the first level.
        /// Levels two and three cost one standard win, level four costs two, and the requirement then
        /// increases by one standard win after every further three levels.
        /// </summary>
        public static float GetExperienceRequiredForNextLevel(int currentLevel)
        {
            var normalizedLevel = Mathf.Max(StartingLevel, currentLevel);
            if (normalizedLevel == StartingLevel)
            {
                return StandardWinExperience * LossRewardMultiplier * LossesForFirstLevel;
            }

            var standardWinsRequired = 1 + Mathf.FloorToInt(
                (normalizedLevel - StartingLevel) / (float)LevelsPerRequirementStep);
            return StandardWinExperience * standardWinsRequired;
        }

        /// <summary>
        /// Applies a completed-match reward to one persistent profile, retaining overflow after every
        /// level-up and keeping the profile's lifetime total separate from its current level progress.
        /// </summary>
        public static PlayerMatchExperienceReward AwardMatchExperience(
            PlayerProfileData profile,
            float matchExperience,
            bool playerWon)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var eligibleExperience = RoundToNearestTenth(Mathf.Max(0f, matchExperience));
            var rewardMultiplier = playerWon ? WinRewardMultiplier : LossRewardMultiplier;
            var awardedExperience = RoundToNearestTenth(eligibleExperience * rewardMultiplier);
            var startingLevel = Mathf.Max(StartingLevel, profile.playerLevel);
            var startingProgress = RoundToNearestTenth(Mathf.Max(0f, profile.playerExp));
            var startingExperienceRequired = GetExperienceRequiredForNextLevel(startingLevel);
            var currentLevel = startingLevel;
            var currentProgress = RoundToNearestTenth(startingProgress + awardedExperience);
            var levelsGained = 0;

            while (currentProgress + 0.0001f >= GetExperienceRequiredForNextLevel(currentLevel))
            {
                currentProgress = RoundToNearestTenth(
                    currentProgress - GetExperienceRequiredForNextLevel(currentLevel));
                currentLevel++;
                levelsGained++;
            }

            profile.playerLevel = currentLevel;
            profile.playerExp = currentProgress;
            profile.totalPlayerExperience = RoundToNearestTenth(
                Mathf.Max(profile.totalPlayerExperience, startingProgress) + awardedExperience);

            return new PlayerMatchExperienceReward(
                eligibleExperience,
                rewardMultiplier,
                awardedExperience,
                startingLevel,
                startingProgress,
                startingExperienceRequired,
                currentLevel,
                levelsGained,
                currentProgress,
                GetExperienceRequiredForNextLevel(currentLevel));
        }

        public static float RoundToNearestTenth(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }
    }

    /// <summary>Immutable account-progression result for one completed match.</summary>
    public readonly struct PlayerMatchExperienceReward
    {
        public PlayerMatchExperienceReward(
            float matchExperience,
            float rewardMultiplier,
            float awardedExperience,
            int startingLevel,
            float startingExperience,
            float startingExperienceRequired,
            int resultingLevel,
            int levelsGained,
            float currentExperience,
            float experienceRequiredForNextLevel)
        {
            MatchExperience = matchExperience;
            RewardMultiplier = rewardMultiplier;
            AwardedExperience = awardedExperience;
            StartingLevel = startingLevel;
            StartingExperience = startingExperience;
            StartingExperienceRequired = startingExperienceRequired;
            ResultingLevel = resultingLevel;
            LevelsGained = levelsGained;
            CurrentExperience = currentExperience;
            ExperienceRequiredForNextLevel = experienceRequiredForNextLevel;
        }

        /// <summary>
        /// Compatibility constructor for callers that only need the settled result. New match
        /// presentation code should use the richer snapshot produced by PlayerLevelProgression.
        /// </summary>
        public PlayerMatchExperienceReward(
            float matchExperience,
            float rewardMultiplier,
            float awardedExperience,
            int startingLevel,
            int resultingLevel,
            int levelsGained,
            float currentExperience,
            float experienceRequiredForNextLevel)
            : this(
                matchExperience,
                rewardMultiplier,
                awardedExperience,
                startingLevel,
                0f,
                PlayerLevelProgression.GetExperienceRequiredForNextLevel(startingLevel),
                resultingLevel,
                levelsGained,
                currentExperience,
                experienceRequiredForNextLevel)
        {
        }

        /// <summary>The total match XP collected before the win/loss multiplier.</summary>
        public float MatchExperience { get; }

        /// <summary>One for a win, or 0.25 for a loss.</summary>
        public float RewardMultiplier { get; }

        /// <summary>The amount added to the persistent player profile.</summary>
        public float AwardedExperience { get; }

        public int StartingLevel { get; }

        /// <summary>Progress held toward the starting level before this match's reward.</summary>
        public float StartingExperience { get; }

        /// <summary>Requirement for the level that was active when the match began.</summary>
        public float StartingExperienceRequired { get; }

        public int ResultingLevel { get; }
        public int LevelsGained { get; }
        public float CurrentExperience { get; }
        public float ExperienceRequiredForNextLevel { get; }
    }
}
