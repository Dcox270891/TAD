namespace TAD.Attackers
{
    /// <summary>High-level role used to organise attacker data and wave compositions.</summary>
    public enum AttackerArchetype
    {
        Basic,
        Fast,
        Strong,
        Flying
    }

    /// <summary>Describes the static cover an attacker may treat as traversable.</summary>
    public enum AttackerTraversalProfile
    {
        Ground,
        IgnoresHalfCover
    }
}
