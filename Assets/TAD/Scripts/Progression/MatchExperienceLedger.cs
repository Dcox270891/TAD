using TAD.Attackers;
using TAD.Defenders;
using TAD.Match;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Owns match-local experience totals. Attacker experience is recorded when an animal is
    /// actually dispatched, and defender experience is supplied as combat rewards occur.
    /// The two totals remain available throughout a match for a later results presentation.
    /// </summary>
    public sealed class MatchExperienceLedger
    {
        public float AttackerExperienceSent { get; private set; }
        public float DefenderExperienceEarned { get; private set; }

        public void Reset()
        {
            AttackerExperienceSent = 0f;
            DefenderExperienceEarned = 0f;
        }

        /// <summary>
        /// Adds the exact amount of combat experience a lone eligible defender would receive
        /// from this dispatched animal. This mirrors the existing combat reward calculation,
        /// including its send-size and test-balance multiplier rules.
        /// </summary>
        public void RecordAttackerDispatched(AttackerDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            var perAttackerExperience = DefenderRewardCalculator.Calculate(
                definition.ExpGiven,
                definition.SendSize,
                eligibleAssistCount: 0).TotalExperience;
            AttackerExperienceSent = PlayerLevelProgression.RoundToNearestTenth(
                AttackerExperienceSent + Mathf.Max(0f, perAttackerExperience));
        }

        /// <summary>Adds a verified increment from the combined FarmHand combat rewards.</summary>
        public void RecordDefenderExperience(float experienceAwarded)
        {
            DefenderExperienceEarned = PlayerLevelProgression.RoundToNearestTenth(
                DefenderExperienceEarned + Mathf.Max(0f, experienceAwarded));
        }

        public float GetExperienceFor(PlayerRole playerRole)
        {
            return playerRole == PlayerRole.Attacker
                ? AttackerExperienceSent
                : DefenderExperienceEarned;
        }
    }
}
