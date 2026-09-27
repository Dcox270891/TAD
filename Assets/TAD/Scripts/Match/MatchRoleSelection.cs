using UnityEngine;

namespace TAD.Match
{
    /// <summary>
    /// Stores the human player's side while a level is loading and across a level restart.
    /// A fresh Play Mode session defaults to Defender so scenes can still be opened directly in the Editor.
    /// </summary>
    public enum PlayerRole
    {
        Attacker,
        Defender
    }

    public static class MatchRoleSelection
    {
        private static PlayerRole selectedRole = PlayerRole.Defender;

        /// <summary>The side currently controlled by the human player.</summary>
        public static PlayerRole SelectedRole => selectedRole;

        public static bool IsPlayerAttacker => selectedRole == PlayerRole.Attacker;
        public static bool IsPlayerDefender => selectedRole == PlayerRole.Defender;

        /// <summary>Sets the human side before loading a level.</summary>
        public static void Select(PlayerRole role)
        {
            selectedRole = role;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            selectedRole = PlayerRole.Defender;
        }
    }
}