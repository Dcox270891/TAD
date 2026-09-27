using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Computes the experience split for one defeated attacker. The calculation is shared by combat and
    /// automated tests so assist rewards, the kill reward, and the maximum assist cap cannot drift apart.
    /// </summary>
    public static class DefenderRewardCalculator
    {
        public const float HelpedToKillExperiencePercent = 0.1f;
        public const float MaximumAssistExperiencePercent = 0.5f;
        public const float TestingExperienceMultiplier = 5f;

        /// <summary>
        /// Calculates a post-testing-multiplier reward split. Each qualifying assistant earns ten percent of
        /// the defeated attacker's group-adjusted XP, rounded to one decimal place, until the combined assist
        /// allocation reaches half of the award. The killer receives everything that remains and never less
        /// than half.
        /// </summary>
        public static DefenderRewardDistribution Calculate(
            int expGiven,
            int sendSize,
            int eligibleAssistCount)
        {
            var distributedExperience = expGiven <= 0
                ? 0f
                : expGiven / (float)Mathf.Max(1, sendSize);
            var totalExperience = RoundToNearestTenth(distributedExperience * TestingExperienceMultiplier);
            var sanitizedAssistCount = Mathf.Max(0, eligibleAssistCount);
            if (totalExperience <= 0f || sanitizedAssistCount == 0)
            {
                return new DefenderRewardDistribution(
                    totalExperience,
                    totalExperience,
                    0f,
                    sanitizedAssistCount,
                    0);
            }

            var standardAssistExperience = RoundToNearestTenth(
                totalExperience * HelpedToKillExperiencePercent);
            if (standardAssistExperience <= 0f)
            {
                return new DefenderRewardDistribution(
                    totalExperience,
                    totalExperience,
                    0f,
                    sanitizedAssistCount,
                    0);
            }

            var maximumAssistExperience = RoundToNearestTenth(
                totalExperience * MaximumAssistExperiencePercent);
            var cappedRecipientCount = Mathf.Min(
                sanitizedAssistCount,
                Mathf.FloorToInt((maximumAssistExperience + 0.0001f) / standardAssistExperience));
            var awardedAssistExperience = RoundToNearestTenth(
                standardAssistExperience * cappedRecipientCount);
            var killerExperience = RoundToNearestTenth(Mathf.Max(
                totalExperience * MaximumAssistExperiencePercent,
                totalExperience - awardedAssistExperience));

            return new DefenderRewardDistribution(
                totalExperience,
                killerExperience,
                standardAssistExperience,
                sanitizedAssistCount,
                cappedRecipientCount);
        }

        public static float RoundToNearestTenth(float value)
        {
            return Mathf.Round(value * 10f) / 10f;
        }
    }

    /// <summary>Immutable experience allocation for a killed attacker group.</summary>
    public readonly struct DefenderRewardDistribution
    {
        public DefenderRewardDistribution(
            float totalExperience,
            float killerExperience,
            float assistExperiencePerRecipient,
            int eligibleAssistCount,
            int assistRecipientAwardCount)
        {
            TotalExperience = totalExperience;
            KillerExperience = killerExperience;
            AssistExperiencePerRecipient = assistExperiencePerRecipient;
            EligibleAssistCount = eligibleAssistCount;
            AssistRecipientAwardCount = assistRecipientAwardCount;
        }

        public float TotalExperience { get; }
        public float KillerExperience { get; }
        public float AssistExperiencePerRecipient { get; }
        public int EligibleAssistCount { get; }
        public int AssistRecipientAwardCount { get; }
        public float TotalAssistExperience => DefenderRewardCalculator.RoundToNearestTenth(
            AssistExperiencePerRecipient * AssistRecipientAwardCount);

        /// <summary>
        /// Returns the award for one deterministic assistant slot. Slots beyond the cap receive no XP so
        /// the combined assist allocation can never exceed fifty percent of the total reward.
        /// </summary>
        public float GetAssistExperienceForRecipient(int recipientIndex)
        {
            return recipientIndex >= 0 && recipientIndex < AssistRecipientAwardCount
                ? AssistExperiencePerRecipient
                : 0f;
        }
    }
}
