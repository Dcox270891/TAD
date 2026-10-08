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
        /// from this dispatched animal. Callers may supply the account-modified experience value
        /// so attacker reward, defender combat XP, and the end-of-match player XP ledger agree.
        /// </summary>
        public void RecordAttackerDispatched(AttackerDefinition definition, int effectiveExperienceGiven = -1)
        {
            if (definition == null)
            {
                return;
            }

            var experienceGiven = effectiveExperienceGiven >= 0
                ? effectiveExperienceGiven
                : definition.ExpGiven;
            var perAttackerExperience = DefenderRewardCalculator.Calculate(
                experienceGiven,
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
