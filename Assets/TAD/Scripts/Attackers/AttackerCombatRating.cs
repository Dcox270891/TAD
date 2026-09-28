using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Calculates the player-facing challenge value for one attacker card. It deliberately combines
    /// the authored combat and economy inputs, then normalizes the result by the number of animals
    /// sent by one card so large groups do not inflate a single animal's displayed rating.
    /// </summary>
    public static class AttackerCombatRating
    {
        /// <summary>
        /// Returns the sum of health, movement speed, send cost, and wave income per sent attacker,
        /// rounded to one decimal place for compact UI display.
        /// </summary>
        public static float Calculate(
            float maximumHealth,
            float movementSpeed,
            int sendCost,
            int waveIncome,
            int sendSize)
        {
            var combinedValue =
                Mathf.Max(0f, maximumHealth) +
                Mathf.Max(0f, movementSpeed) +
                Mathf.Max(0, sendCost) +
                Mathf.Max(0, waveIncome);
            var perAttackerValue = combinedValue / Mathf.Max(1, sendSize);
            return Mathf.Round(perAttackerValue * 10f) / 10f;
        }

        /// <summary>Calculates the rating from an authored definition, returning zero for missing data.</summary>
        public static float Calculate(AttackerDefinition definition)
        {
            return definition == null
                ? 0f
                : Calculate(
                    definition.MaximumHealth,
                    definition.MovementSpeed,
                    definition.SendCost,
                    definition.WaveIncome,
                    definition.SendSize);
        }
    }
}
