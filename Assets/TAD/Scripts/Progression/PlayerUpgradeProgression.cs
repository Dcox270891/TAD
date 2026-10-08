using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>Top-level pages and persistent reward groups in the player-upgrade tree.</summary>
    public enum PlayerUpgradeCategory
    {
        Attacker = 0,
        Defender = 1,
        General = 2
    }

    /// <summary>One choice awarded by a player-level increase.</summary>
    public enum PlayerUpgradeRewardSlot
    {
        Attacker = 0,
        Defender = 1,
        Random = 2,
        General = 3
    }

    public enum AttackerUpgradeStat
    {
        Health = 0,
        Speed = 1,
        Cost = 2,
        WaveIncome = 3,
        ExperienceGiven = 4
    }

    public enum DefenderUpgradeStat
    {
        Cost = 0,
        Range = 1,
        AttackSpeed = 2,
        Damage = 3,
        ProjectileEffects = 4
    }

    public enum GeneralPlayerUpgrade
    {
        DefenderExperienceDisplay = 0,
        AttackerHealthDisplay = 1,
        QueuedRouteDisplay = 2,
        IcePlacement = 3,
        MudPlacement = 4
    }

    /// <summary>JsonUtility-friendly rank record. A list keeps the profile portable and version-safe.</summary>
    [Serializable]
    public sealed class PlayerUpgradeRankData
    {
        public string upgradeId = string.Empty;
        public int rank;
    }

    /// <summary>
    /// A persisted set of offered cards. Saving the offers prevents closing the app to reroll the
    /// random choice, while the selected rank itself remains the authoritative progression state.
    /// </summary>
    [Serializable]
    public sealed class PendingPlayerUpgradeChoiceData
    {
        public int playerLevel;
        public PlayerUpgradeRewardSlot rewardSlot;
        public List<string> offeredUpgradeIds = new();
    }

    /// <summary>Immutable, UI-ready description of one repeatable player upgrade.</summary>
    public readonly struct PlayerUpgradeDefinition
    {
        public PlayerUpgradeDefinition(
            string id,
            PlayerUpgradeCategory category,
            string groupName,
            string title,
            string description,
            int maxRank)
        {
            Id = id;
            Category = category;
            GroupName = groupName;
            Title = title;
            Description = description;
            MaxRank = maxRank;
        }

        public string Id { get; }
        public PlayerUpgradeCategory Category { get; }
        public string GroupName { get; }
        public string Title { get; }
        public string Description { get; }
        public int MaxRank { get; }
    }

    /// <summary>
    /// Defines account-level upgrades and their persisted choice queue. This class deliberately
    /// owns no Unity assets: authored attacker/weapon definitions remain immutable and runtime
    /// consumers resolve their effective values through this shared, testable model.
    /// </summary>
    public static class PlayerUpgradeProgression
    {
        public const int AttackerMaximumRank = 5;
        public const int DefenderMaximumRank = 10;
        public const int GeneralMaximumRank = 1;
        public const float RankStep = 0.02f;
        public const int FirstChallengeTier = 1;
        /// <summary>
        /// The current roster supplies three tiers, while the progression contract reserves ten
        /// so future attacker content can continue unlocking every ten account levels to L100.
        /// </summary>
        public const int HighestCurrentChallengeTier = 10;
        public const int ChallengeTierLevelInterval = 10;

        private static readonly List<PlayerUpgradeDefinition> AllDefinitionsInternal = BuildDefinitions();
        private static readonly Dictionary<string, PlayerUpgradeDefinition> DefinitionsById = BuildDefinitionLookup();

        public static IReadOnlyList<PlayerUpgradeDefinition> AllDefinitions => AllDefinitionsInternal;

        /// <summary>Tier one is available at level one; tier two at level ten and tier three at level twenty.</summary>
        public static int GetUnlockedChallengeTier(int playerLevel)
        {
            return Mathf.Clamp(
                FirstChallengeTier + (Mathf.Max(PlayerLevelProgression.StartingLevel, playerLevel) / ChallengeTierLevelInterval),
                FirstChallengeTier,
                HighestCurrentChallengeTier);
        }

        public static bool IsChallengeTierUnlocked(int playerLevel, int challengeTier)
        {
            return challengeTier <= GetUnlockedChallengeTier(playerLevel);
        }

        public static string GetAttackerUpgradeId(AttackerArchetype archetype, AttackerUpgradeStat stat)
        {
            return $"attacker:{archetype}:{stat}";
        }

        public static string GetDefenderUpgradeId(ProjectileType projectileType, DefenderUpgradeStat stat)
        {
            return $"defender:{projectileType}:{stat}";
        }

        public static string GetGeneralUpgradeId(GeneralPlayerUpgrade upgrade)
        {
            return $"general:{upgrade}";
        }

        public static bool TryGetDefinition(string upgradeId, out PlayerUpgradeDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
            {
                definition = default;
                return false;
            }

            return DefinitionsById.TryGetValue(upgradeId, out definition);
        }

        public static int GetRank(PlayerProfileData profile, string upgradeId)
        {
            if (profile?.playerUpgradeRanks == null || string.IsNullOrWhiteSpace(upgradeId))
            {
                return 0;
            }

            for (var index = 0; index < profile.playerUpgradeRanks.Count; index++)
            {
                var entry = profile.playerUpgradeRanks[index];
                if (entry != null && string.Equals(entry.upgradeId, upgradeId, StringComparison.Ordinal))
                {
                    return Mathf.Max(0, entry.rank);
                }
            }

            return 0;
        }

        public static int GetAttackerRank(PlayerProfileData profile, AttackerArchetype archetype, AttackerUpgradeStat stat)
        {
            return GetRank(profile, GetAttackerUpgradeId(archetype, stat));
        }

        public static int GetDefenderRank(PlayerProfileData profile, ProjectileType projectileType, DefenderUpgradeStat stat)
        {
            return GetRank(profile, GetDefenderUpgradeId(projectileType, stat));
        }

        public static bool IsGeneralUpgradeUnlocked(PlayerProfileData profile, GeneralPlayerUpgrade upgrade)
        {
            return GetRank(profile, GetGeneralUpgradeId(upgrade)) > 0;
        }

        /// <summary>
        /// Migration-only compatibility helper. Older profiles already had both combat-display
        /// settings available, so their established visibility choices remain usable after the
        /// display unlocks become part of the account tree.
        /// </summary>
        public static void GrantUpgradeForMigration(PlayerProfileData profile, string upgradeId)
        {
            if (profile == null || !TryGetDefinition(upgradeId, out var definition))
            {
                return;
            }

            SetRank(profile, upgradeId, definition.MaxRank, definition.MaxRank);
        }

        public static float GetAttackerMultiplier(
            PlayerProfileData profile,
            AttackerArchetype archetype,
            AttackerUpgradeStat stat)
        {
            var rank = Mathf.Clamp(GetAttackerRank(profile, archetype, stat), 0, AttackerMaximumRank);
            return stat is AttackerUpgradeStat.Cost or AttackerUpgradeStat.ExperienceGiven
                ? Mathf.Max(0f, 1f - (rank * RankStep))
                : 1f + (rank * RankStep);
        }

        public static float GetDefenderMultiplier(
            PlayerProfileData profile,
            ProjectileType projectileType,
            DefenderUpgradeStat stat)
        {
            var rank = Mathf.Clamp(GetDefenderRank(profile, projectileType, stat), 0, DefenderMaximumRank);
            return stat == DefenderUpgradeStat.Cost
                ? Mathf.Max(0f, 1f - (rank * RankStep))
                : 1f + (rank * RankStep);
        }

        public static float GetEffectiveAttackerHealth(PlayerProfileData profile, AttackerDefinition definition)
        {
            return definition == null
                ? 0f
                : definition.MaximumHealth * GetAttackerMultiplier(profile, definition.Archetype, AttackerUpgradeStat.Health);
        }

        public static float GetEffectiveAttackerSpeed(PlayerProfileData profile, AttackerDefinition definition)
        {
            return definition == null
                ? 0f
                : definition.MovementSpeed * GetAttackerMultiplier(profile, definition.Archetype, AttackerUpgradeStat.Speed);
        }

        public static int GetEffectiveAttackerCost(PlayerProfileData profile, AttackerDefinition definition)
        {
            return definition == null
                ? 0
                : RoundResourceValue(definition.SendCost * GetAttackerMultiplier(profile, definition.Archetype, AttackerUpgradeStat.Cost));
        }

        public static int GetEffectiveAttackerIncome(PlayerProfileData profile, AttackerDefinition definition)
        {
            return definition == null
                ? 0
                : RoundResourceValue(definition.WaveIncome * GetAttackerMultiplier(profile, definition.Archetype, AttackerUpgradeStat.WaveIncome));
        }

        public static int GetEffectiveAttackerExperienceGiven(PlayerProfileData profile, AttackerDefinition definition)
        {
            return definition == null
                ? 0
                : RoundResourceValue(definition.ExpGiven * GetAttackerMultiplier(profile, definition.Archetype, AttackerUpgradeStat.ExperienceGiven));
        }

        public static int GetEffectiveDefenderCost(PlayerProfileData profile, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            var projectileType = GetProjectileType(definition);
            return RoundResourceValue(definition.Cost * GetDefenderMultiplier(profile, projectileType, DefenderUpgradeStat.Cost));
        }

        public static float GetEffectiveDefenderRange(PlayerProfileData profile, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            return definition.Range * GetDefenderMultiplier(profile, GetProjectileType(definition), DefenderUpgradeStat.Range);
        }

        public static float GetEffectiveDefenderAttackSpeed(PlayerProfileData profile, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            return definition.AttackSpeed * GetDefenderMultiplier(profile, GetProjectileType(definition), DefenderUpgradeStat.AttackSpeed);
        }

        public static float GetEffectiveDefenderDamage(PlayerProfileData profile, WeaponDefinition definition)
        {
            if (definition == null)
            {
                return 0f;
            }

            return definition.Damage * GetDefenderMultiplier(profile, GetProjectileType(definition), DefenderUpgradeStat.Damage);
        }

        public static float GetDefenderProjectileEffectsMultiplier(PlayerProfileData profile, WeaponDefinition definition)
        {
            return definition == null
                ? 1f
                : GetDefenderMultiplier(profile, GetProjectileType(definition), DefenderUpgradeStat.ProjectileEffects);
        }

        public static void EnsureStorage(PlayerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.playerUpgradeRanks ??= new List<PlayerUpgradeRankData>();
            profile.pendingPlayerUpgradeChoices ??= new List<PendingPlayerUpgradeChoiceData>();
        }

        /// <summary>Adds the three standard cards and any milestone general card for one earned player level.</summary>
        public static void EnqueueChoicesForLevel(PlayerProfileData profile, int earnedLevel)
        {
            if (profile == null || earnedLevel <= PlayerLevelProgression.StartingLevel ||
                earnedLevel > PlayerLevelProgression.MaximumLevel)
            {
                return;
            }

            EnsureStorage(profile);
            AddChoiceIfMissing(profile, earnedLevel, PlayerUpgradeRewardSlot.Attacker);
            AddChoiceIfMissing(profile, earnedLevel, PlayerUpgradeRewardSlot.Defender);
            AddChoiceIfMissing(profile, earnedLevel, PlayerUpgradeRewardSlot.Random);
            if (IsGeneralUpgradeLevel(earnedLevel))
            {
                AddChoiceIfMissing(profile, earnedLevel, PlayerUpgradeRewardSlot.General);
            }
        }

        /// <summary>Builds an upgrade backlog for older profiles that had already earned account levels.</summary>
        public static void EnqueueChoicesThroughLevel(PlayerProfileData profile, int playerLevel)
        {
            if (profile == null)
            {
                return;
            }

            var finalLevel = Mathf.Clamp(
                playerLevel,
                PlayerLevelProgression.StartingLevel,
                PlayerLevelProgression.MaximumLevel);
            for (var level = PlayerLevelProgression.StartingLevel + 1; level <= finalLevel; level++)
            {
                EnqueueChoicesForLevel(profile, level);
            }
        }

        public static int CopyPendingChoicesForLevel(
            PlayerProfileData profile,
            int playerLevel,
            List<PendingPlayerUpgradeChoiceData> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (profile?.pendingPlayerUpgradeChoices == null)
            {
                return 0;
            }

            RefreshExhaustedChoices(profile);
            for (var index = 0; index < profile.pendingPlayerUpgradeChoices.Count; index++)
            {
                var choice = profile.pendingPlayerUpgradeChoices[index];
                if (choice != null && choice.playerLevel == playerLevel)
                {
                    destination.Add(choice);
                }
            }

            destination.Sort(ComparePendingChoices);
            return destination.Count;
        }

        public static int GetPendingChoiceCount(PlayerProfileData profile)
        {
            RefreshExhaustedChoices(profile);
            return profile?.pendingPlayerUpgradeChoices?.Count ?? 0;
        }

        public static bool TryChooseUpgrade(
            PlayerProfileData profile,
            int playerLevel,
            PlayerUpgradeRewardSlot rewardSlot,
            string upgradeId)
        {
            if (profile?.pendingPlayerUpgradeChoices == null || string.IsNullOrWhiteSpace(upgradeId) ||
                !TryGetDefinition(upgradeId, out var definition))
            {
                return false;
            }

            for (var index = 0; index < profile.pendingPlayerUpgradeChoices.Count; index++)
            {
                var pending = profile.pendingPlayerUpgradeChoices[index];
                if (pending == null || pending.playerLevel != playerLevel || pending.rewardSlot != rewardSlot ||
                    pending.offeredUpgradeIds == null || !pending.offeredUpgradeIds.Contains(upgradeId) ||
                    GetRank(profile, upgradeId) >= definition.MaxRank)
                {
                    continue;
                }

                SetRank(profile, upgradeId, GetRank(profile, upgradeId) + 1, definition.MaxRank);
                profile.pendingPlayerUpgradeChoices.RemoveAt(index);
                RefreshExhaustedChoices(profile);
                return true;
            }

            return false;
        }

        public static bool IsGeneralUpgradeLevel(int level)
        {
            return level > 0 && level % 20 == 0 && level <= PlayerLevelProgression.MaximumLevel;
        }

        /// <summary>Normalizes ranks, removes unknown data, and repairs incomplete persisted card offers.</summary>
        public static void Sanitize(PlayerProfileData profile)
        {
            if (profile == null)
            {
                return;
            }

            EnsureStorage(profile);
            for (var index = profile.playerUpgradeRanks.Count - 1; index >= 0; index--)
            {
                var rank = profile.playerUpgradeRanks[index];
                if (rank == null || !TryGetDefinition(rank.upgradeId, out var definition))
                {
                    profile.playerUpgradeRanks.RemoveAt(index);
                    continue;
                }

                rank.rank = Mathf.Clamp(rank.rank, 0, definition.MaxRank);
                if (rank.rank <= 0)
                {
                    profile.playerUpgradeRanks.RemoveAt(index);
                }
            }

            for (var index = profile.pendingPlayerUpgradeChoices.Count - 1; index >= 0; index--)
            {
                var pending = profile.pendingPlayerUpgradeChoices[index];
                if (pending == null || pending.playerLevel <= PlayerLevelProgression.StartingLevel ||
                    pending.playerLevel > PlayerLevelProgression.MaximumLevel ||
                    (pending.rewardSlot == PlayerUpgradeRewardSlot.General && !IsGeneralUpgradeLevel(pending.playerLevel)))
                {
                    profile.pendingPlayerUpgradeChoices.RemoveAt(index);
                    continue;
                }

                pending.offeredUpgradeIds ??= new List<string>();
                for (var offeredIndex = pending.offeredUpgradeIds.Count - 1; offeredIndex >= 0; offeredIndex--)
                {
                    if (!TryGetDefinition(pending.offeredUpgradeIds[offeredIndex], out _))
                    {
                        pending.offeredUpgradeIds.RemoveAt(offeredIndex);
                    }
                }
            }

            RefreshExhaustedChoices(profile);
        }

        private static void AddChoiceIfMissing(
            PlayerProfileData profile,
            int playerLevel,
            PlayerUpgradeRewardSlot rewardSlot)
        {
            for (var index = 0; index < profile.pendingPlayerUpgradeChoices.Count; index++)
            {
                var existing = profile.pendingPlayerUpgradeChoices[index];
                if (existing != null && existing.playerLevel == playerLevel && existing.rewardSlot == rewardSlot)
                {
                    return;
                }
            }

            var choice = new PendingPlayerUpgradeChoiceData
            {
                playerLevel = playerLevel,
                rewardSlot = rewardSlot,
                offeredUpgradeIds = BuildOffer(profile, playerLevel, rewardSlot)
            };

            // It is possible for a historical profile to have already maxed a category. In that
            // case no unnecessary empty card is added to the presentation queue.
            if (choice.offeredUpgradeIds.Count > 0)
            {
                profile.pendingPlayerUpgradeChoices.Add(choice);
            }
        }

        private static void RefreshExhaustedChoices(PlayerProfileData profile)
        {
            if (profile?.pendingPlayerUpgradeChoices == null)
            {
                return;
            }

            for (var index = profile.pendingPlayerUpgradeChoices.Count - 1; index >= 0; index--)
            {
                var pending = profile.pendingPlayerUpgradeChoices[index];
                if (pending == null)
                {
                    profile.pendingPlayerUpgradeChoices.RemoveAt(index);
                    continue;
                }

                // A different card can complete an upgrade that this persisted offer also
                // referenced. Remove those stale entries before the UI sees them: a reward card
                // must never remain visibly selectable when it can no longer be claimed.
                pending.offeredUpgradeIds ??= new List<string>();
                for (var offerIndex = pending.offeredUpgradeIds.Count - 1; offerIndex >= 0; offerIndex--)
                {
                    var upgradeId = pending.offeredUpgradeIds[offerIndex];
                    if (!TryGetDefinition(upgradeId, out var definition) ||
                        !IsDefinitionEligibleForSlot(definition, pending.playerLevel, pending.rewardSlot) ||
                        GetRank(profile, upgradeId) >= definition.MaxRank)
                    {
                        pending.offeredUpgradeIds.RemoveAt(offerIndex);
                    }
                }

                if (!HasAnyEligibleOffer(profile, pending))
                {
                    pending.offeredUpgradeIds = BuildOffer(profile, pending.playerLevel, pending.rewardSlot);
                }

                if (!HasAnyEligibleOffer(profile, pending))
                {
                    // A category can finish early through its random rewards. It is intentionally
                    // removed rather than blocking the account XP presentation with an empty card.
                    profile.pendingPlayerUpgradeChoices.RemoveAt(index);
                }
            }
        }

        private static bool HasAnyEligibleOffer(PlayerProfileData profile, PendingPlayerUpgradeChoiceData pending)
        {
            if (pending?.offeredUpgradeIds == null)
            {
                return false;
            }

            for (var index = 0; index < pending.offeredUpgradeIds.Count; index++)
            {
                var upgradeId = pending.offeredUpgradeIds[index];
                if (TryGetDefinition(upgradeId, out var definition) && GetRank(profile, upgradeId) < definition.MaxRank)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> BuildOffer(
            PlayerProfileData profile,
            int playerLevel,
            PlayerUpgradeRewardSlot rewardSlot)
        {
            var eligible = new List<PlayerUpgradeDefinition>();
            foreach (var definition in AllDefinitionsInternal)
            {
                if (!IsDefinitionEligibleForSlot(definition, playerLevel, rewardSlot) ||
                    GetRank(profile, definition.Id) >= definition.MaxRank)
                {
                    continue;
                }

                eligible.Add(definition);
            }

            var offers = new List<string>(3);
            if (eligible.Count == 0)
            {
                return offers;
            }

            // Persisted seed = same offer after a restart, while players still receive a varied
            // set of three cards inside the appropriate reward family.
            var random = new System.Random((playerLevel * 15485863) + ((int)rewardSlot * 32452843) + 47);
            while (eligible.Count > 0 && offers.Count < 3)
            {
                var index = random.Next(eligible.Count);
                offers.Add(eligible[index].Id);
                eligible.RemoveAt(index);
            }

            return offers;
        }

        private static bool IsDefinitionEligibleForSlot(
            PlayerUpgradeDefinition definition,
            int playerLevel,
            PlayerUpgradeRewardSlot rewardSlot)
        {
            return rewardSlot switch
            {
                PlayerUpgradeRewardSlot.Attacker => definition.Category == PlayerUpgradeCategory.Attacker,
                PlayerUpgradeRewardSlot.Defender => definition.Category == PlayerUpgradeCategory.Defender,
                PlayerUpgradeRewardSlot.General => definition.Category == PlayerUpgradeCategory.General,
                // Guarantee one extra rank for each core tree so the 99 dedicated level-up cards
                // plus the random rewards can complete all 100 attacker and defender ranks by L100.
                PlayerUpgradeRewardSlot.Random when playerLevel == 2 => definition.Category == PlayerUpgradeCategory.Attacker,
                PlayerUpgradeRewardSlot.Random when playerLevel == 3 => definition.Category == PlayerUpgradeCategory.Defender,
                PlayerUpgradeRewardSlot.Random => definition.Category is PlayerUpgradeCategory.Attacker or PlayerUpgradeCategory.Defender,
                _ => false
            };
        }

        private static void SetRank(PlayerProfileData profile, string upgradeId, int rank, int maximumRank)
        {
            EnsureStorage(profile);
            for (var index = 0; index < profile.playerUpgradeRanks.Count; index++)
            {
                var entry = profile.playerUpgradeRanks[index];
                if (entry != null && string.Equals(entry.upgradeId, upgradeId, StringComparison.Ordinal))
                {
                    entry.rank = Mathf.Clamp(rank, 0, maximumRank);
                    return;
                }
            }

            profile.playerUpgradeRanks.Add(new PlayerUpgradeRankData
            {
                upgradeId = upgradeId,
                rank = Mathf.Clamp(rank, 0, maximumRank)
            });
        }

        private static int ComparePendingChoices(PendingPlayerUpgradeChoiceData left, PendingPlayerUpgradeChoiceData right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return left.rewardSlot.CompareTo(right.rewardSlot);
        }

        private static ProjectileType GetProjectileType(WeaponDefinition definition)
        {
            return definition != null && definition.ProjectileDefinition != null
                ? definition.ProjectileDefinition.ProjectileType
                : ProjectileType.Melee;
        }

        private static int RoundResourceValue(float value)
        {
            return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0f, value)));
        }

        private static List<PlayerUpgradeDefinition> BuildDefinitions()
        {
            var definitions = new List<PlayerUpgradeDefinition>();
            foreach (AttackerArchetype archetype in Enum.GetValues(typeof(AttackerArchetype)))
            {
                foreach (AttackerUpgradeStat stat in Enum.GetValues(typeof(AttackerUpgradeStat)))
                {
                    definitions.Add(new PlayerUpgradeDefinition(
                        GetAttackerUpgradeId(archetype, stat),
                        PlayerUpgradeCategory.Attacker,
                        ToTitle(archetype),
                        GetAttackerStatTitle(stat),
                        GetAttackerStatDescription(stat, ToTitle(archetype)),
                        AttackerMaximumRank));
                }
            }

            foreach (var projectileType in new[] { ProjectileType.Melee, ProjectileType.Ranged })
            {
                foreach (DefenderUpgradeStat stat in Enum.GetValues(typeof(DefenderUpgradeStat)))
                {
                    definitions.Add(new PlayerUpgradeDefinition(
                        GetDefenderUpgradeId(projectileType, stat),
                        PlayerUpgradeCategory.Defender,
                        projectileType == ProjectileType.Melee ? "Melee" : "Ranged",
                        GetDefenderStatTitle(stat),
                        GetDefenderStatDescription(stat, projectileType),
                        DefenderMaximumRank));
                }
            }

            foreach (GeneralPlayerUpgrade upgrade in Enum.GetValues(typeof(GeneralPlayerUpgrade)))
            {
                definitions.Add(new PlayerUpgradeDefinition(
                    GetGeneralUpgradeId(upgrade),
                    PlayerUpgradeCategory.General,
                    "General",
                    GetGeneralTitle(upgrade),
                    GetGeneralDescription(upgrade),
                    GeneralMaximumRank));
            }

            return definitions;
        }

        private static Dictionary<string, PlayerUpgradeDefinition> BuildDefinitionLookup()
        {
            var lookup = new Dictionary<string, PlayerUpgradeDefinition>(StringComparer.Ordinal);
            foreach (var definition in AllDefinitionsInternal)
            {
                lookup[definition.Id] = definition;
            }

            return lookup;
        }

        private static string ToTitle(Enum value)
        {
            return value.ToString().ToUpperInvariant();
        }

        private static string GetAttackerStatTitle(AttackerUpgradeStat stat)
        {
            return stat switch
            {
                AttackerUpgradeStat.Health => "HEARTY HERD",
                AttackerUpgradeStat.Speed => "QUICK FEET",
                AttackerUpgradeStat.Cost => "THRIFTY SEND",
                AttackerUpgradeStat.WaveIncome => "RICHER RETURNS",
                AttackerUpgradeStat.ExperienceGiven => "LOWER BOUNTY",
                _ => stat.ToString().ToUpperInvariant()
            };
        }

        private static string GetAttackerStatDescription(AttackerUpgradeStat stat, string archetype)
        {
            return stat switch
            {
                AttackerUpgradeStat.Health => $"{archetype} maximum health +2% per rank.",
                AttackerUpgradeStat.Speed => $"{archetype} movement speed +2% per rank.",
                AttackerUpgradeStat.Cost => $"{archetype} send cost -2% per rank.",
                AttackerUpgradeStat.WaveIncome => $"{archetype} wave income +2% per rank.",
                AttackerUpgradeStat.ExperienceGiven => $"{archetype} experience given to defenders -2% per rank.",
                _ => string.Empty
            };
        }

        private static string GetDefenderStatTitle(DefenderUpgradeStat stat)
        {
            return stat switch
            {
                DefenderUpgradeStat.Cost => "WISE SPENDING",
                DefenderUpgradeStat.Range => "LONGER REACH",
                DefenderUpgradeStat.AttackSpeed => "FIRING RHYTHM",
                DefenderUpgradeStat.Damage => "HARDER HITS",
                DefenderUpgradeStat.ProjectileEffects => "STRONGER EFFECTS",
                _ => stat.ToString().ToUpperInvariant()
            };
        }

        private static string GetDefenderStatDescription(DefenderUpgradeStat stat, ProjectileType projectileType)
        {
            var group = projectileType == ProjectileType.Melee ? "Melee" : "Ranged";
            return stat switch
            {
                DefenderUpgradeStat.Cost => $"{group} weapon and placement cost -2% per rank.",
                DefenderUpgradeStat.Range => $"{group} attack range +2% per rank.",
                DefenderUpgradeStat.AttackSpeed => $"{group} attack speed +2% per rank.",
                DefenderUpgradeStat.Damage => $"{group} damage +2% per rank.",
                DefenderUpgradeStat.ProjectileEffects => $"{group} projectile effects +2% per rank.",
                _ => string.Empty
            };
        }

        private static string GetGeneralTitle(GeneralPlayerUpgrade upgrade)
        {
            return upgrade switch
            {
                GeneralPlayerUpgrade.DefenderExperienceDisplay => "FARMHAND PROGRESS",
                GeneralPlayerUpgrade.AttackerHealthDisplay => "ANIMAL HEALTH",
                GeneralPlayerUpgrade.QueuedRouteDisplay => "ROUTE SCOUTING",
                GeneralPlayerUpgrade.IcePlacement => "ICE PATCHES",
                GeneralPlayerUpgrade.MudPlacement => "MUD PATCHES",
                _ => upgrade.ToString().ToUpperInvariant()
            };
        }

        private static string GetGeneralDescription(GeneralPlayerUpgrade upgrade)
        {
            return upgrade switch
            {
                GeneralPlayerUpgrade.DefenderExperienceDisplay => "Unlock the option to show defender experience bars.",
                GeneralPlayerUpgrade.AttackerHealthDisplay => "Unlock the option to show attacker health bars.",
                GeneralPlayerUpgrade.QueuedRouteDisplay => "Show every queued attack group's planned route.",
                GeneralPlayerUpgrade.IcePlacement => "Unlock one Ice Patch placement during each attacker preparation phase.",
                GeneralPlayerUpgrade.MudPlacement => "Unlock one Mud Patch placement during each defender preparation phase.",
                _ => string.Empty
            };
        }
    }
}
