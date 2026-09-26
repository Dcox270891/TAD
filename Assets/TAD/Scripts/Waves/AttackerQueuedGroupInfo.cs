using TAD.Attackers;

namespace TAD.Waves
{
    /// <summary>
    /// Read-only presentation data for one player-selected attacker group. A group remains distinct
    /// even when several clicks selected the same attacker definition.
    /// </summary>
    public readonly struct AttackerQueuedGroupInfo
    {
        public AttackerQueuedGroupInfo(
            long id,
            AttackerDefinition definition,
            int totalCount,
            int remainingAlive,
            bool isDispatching,
            bool isCurrentGroup)
        {
            Id = id;
            Definition = definition;
            TotalCount = totalCount;
            RemainingAlive = remainingAlive;
            IsDispatching = isDispatching;
            IsCurrentGroup = isCurrentGroup;
        }

        public long Id { get; }
        public AttackerDefinition Definition { get; }
        public int TotalCount { get; }
        public int RemainingAlive { get; }
        public bool IsDispatching { get; }
        public bool IsCurrentGroup { get; }
    }
}