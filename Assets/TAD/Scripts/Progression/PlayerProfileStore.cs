using System;
using System.Collections.Generic;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Small, versioned player profile for menu-owned data that must persist between application launches.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfileData
    {
        public int version = PlayerProfileStore.CurrentVersion;
        public string lastPlayedLevelId = string.Empty;
        public int metaCurrency;

        [Header("Player Progression")]
        [Tooltip("Experience currently held toward the next account-level reward.")]
        public float playerExp;
        [Tooltip("Account level earned from completed-match experience.")]
        public int playerLevel = PlayerLevelProgression.StartingLevel;
        [Tooltip("Lifetime account experience, retained independently from current level progress.")]
        public float totalPlayerExperience;

        [Header("Player Upgrades")]
        [Tooltip("Ranks purchased from the account-level player upgrade tree.")]
        public List<PlayerUpgradeRankData> playerUpgradeRanks = new();
        [Tooltip("Saved level-up card offers waiting for a player choice.")]
        public List<PendingPlayerUpgradeChoiceData> pendingPlayerUpgradeChoices = new();

        [Header("Player Energy")]
        [Tooltip("Energy available to begin a new match.")]
        public int playerEnergy = PlayerEnergyProgression.MaximumEnergy;
        [Tooltip("Trusted live-time progress accumulated toward the next energy point.")]
        public long playerEnergyRechargeRemainderMilliseconds;
        [Tooltip("Last device wall-clock anchor recorded for offline energy reconciliation.")]
        public long playerEnergyLastWallClockUtcTicks;
        [Tooltip("Highest device wall-clock time ever observed, retained to reject clock rollbacks.")]
        public long playerEnergyHighestWallClockUtcTicks;
        [Tooltip("Android elapsed realtime anchor when that platform clock is available; -1 elsewhere.")]
        public long playerEnergyLastDeviceElapsedRealtimeMilliseconds = -1L;

        // Kept for backward compatibility with the first menu-profile format.
        public bool musicEnabled = true;
        public float musicVolume = 1f;
        public int videoQuality;
        public bool notificationsEnabled = true;
        public bool controlHintsEnabled = true;

        [Header("Gameplay Display")]
        public bool showAttackerHealthBars = true;
        public bool showDefenderExperience = true;

        [Header("Audio")]
        public float masterVolume = 1f;
        public float attackerVolume = 1f;
        public float defenderVolume = 1f;
        public float projectileVolume = 1f;
        public float ambientGameVolume = 1f;
    }

    /// <summary>
    /// Persists the current prototype profile through the platform-supported PlayerPrefs store.
    /// </summary>
    public static class PlayerProfileStore
    {
        public const int CurrentVersion = 5;

        private const string ProfileKey = "TAD.PlayerProfile";

        public static PlayerProfileData Load()
        {
            if (!PlayerPrefs.HasKey(ProfileKey))
            {
                return CreateDefault();
            }

            try
            {
                var serializedProfile = PlayerPrefs.GetString(ProfileKey, string.Empty);
                var profile = JsonUtility.FromJson<PlayerProfileData>(serializedProfile);
                if (profile == null || profile.version <= 0)
                {
                    Debug.LogWarning("TAD could not read the saved player profile, so default menu data will be used.");
                    return CreateDefault();
                }

                Migrate(profile);
                Sanitize(profile);
                return profile;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"TAD could not read the saved player profile: {exception.Message}");
                return CreateDefault();
            }
        }

        public static void Save(PlayerProfileData profile)
        {
            profile ??= CreateDefault();
            Sanitize(profile);
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
        }

        private static PlayerProfileData CreateDefault()
        {
            return new PlayerProfileData
            {
                videoQuality = GetSafeQualityIndex(QualitySettings.GetQualityLevel())
            };
        }

        private static void Migrate(PlayerProfileData profile)
        {
            if (profile.version < 2)
            {
                // Version 1 only had a single music slider. It becomes the master setting so an
                // established player's audio preference is preserved on the first upgrade.
                profile.masterVolume = profile.musicEnabled ? Mathf.Clamp01(profile.musicVolume) : 0f;
                profile.attackerVolume = 1f;
                profile.defenderVolume = 1f;
                profile.projectileVolume = 1f;
                profile.ambientGameVolume = 1f;
                profile.showAttackerHealthBars = true;
                profile.showDefenderExperience = true;

                // The previous profile exposed this field but never applied it. Preserve the
                // project's current quality level rather than unexpectedly changing an existing
                // player's rendering quality on their first launch with real settings support.
                profile.videoQuality = GetSafeQualityIndex(QualitySettings.GetQualityLevel());
            }

            if (profile.version < 3)
            {
                // Account progression begins cleanly for profiles written before match rewards.
                profile.playerExp = 0f;
                profile.playerLevel = PlayerLevelProgression.StartingLevel;
                profile.totalPlayerExperience = 0f;
            }

            if (profile.version < 4)
            {
                // Existing players begin the new energy system with a full reserve. Clock anchors
                // are established by PlayerSettingsService on the next application launch.
                profile.playerEnergy = PlayerEnergyProgression.MaximumEnergy;
                profile.playerEnergyRechargeRemainderMilliseconds = 0L;
                profile.playerEnergyLastWallClockUtcTicks = 0L;
                profile.playerEnergyHighestWallClockUtcTicks = 0L;
                profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = -1L;
            }

            if (profile.version < 5)
            {
                // These display controls were already available to existing players. Keep their
                // preference intact rather than making a profile migration unexpectedly hide UI.
                PlayerUpgradeProgression.GrantUpgradeForMigration(
                    profile,
                    PlayerUpgradeProgression.GetGeneralUpgradeId(GeneralPlayerUpgrade.AttackerHealthDisplay));
                PlayerUpgradeProgression.GrantUpgradeForMigration(
                    profile,
                    PlayerUpgradeProgression.GetGeneralUpgradeId(GeneralPlayerUpgrade.DefenderExperienceDisplay));

                // Account levels earned before the tree was introduced still deserve their
                // rewards. The persistent queue lets players work through those choices safely.
                PlayerUpgradeProgression.EnqueueChoicesThroughLevel(profile, profile.playerLevel);
            }
        }

        private static void Sanitize(PlayerProfileData profile)
        {
            profile.version = CurrentVersion;
            profile.lastPlayedLevelId ??= string.Empty;
            profile.metaCurrency = Mathf.Max(0, profile.metaCurrency);
            profile.playerExp = PlayerLevelProgression.RoundToNearestTenth(Mathf.Max(0f, profile.playerExp));
            profile.playerLevel = Mathf.Clamp(
                profile.playerLevel,
                PlayerLevelProgression.StartingLevel,
                PlayerLevelProgression.MaximumLevel);
            if (profile.playerLevel >= PlayerLevelProgression.MaximumLevel)
            {
                profile.playerExp = 0f;
            }
            profile.totalPlayerExperience = PlayerLevelProgression.RoundToNearestTenth(
                Mathf.Max(profile.playerExp, profile.totalPlayerExperience));
            profile.playerEnergy = Mathf.Clamp(profile.playerEnergy, 0, PlayerEnergyProgression.MaximumEnergy);
            profile.playerEnergyRechargeRemainderMilliseconds = profile.playerEnergy >= PlayerEnergyProgression.MaximumEnergy
                ? 0L
                : Math.Max(
                    0L,
                    Math.Min(
                        PlayerEnergyProgression.RechargeIntervalMilliseconds - 1L,
                        profile.playerEnergyRechargeRemainderMilliseconds));
            profile.playerEnergyLastWallClockUtcTicks = Math.Max(0L, profile.playerEnergyLastWallClockUtcTicks);
            profile.playerEnergyHighestWallClockUtcTicks = Math.Max(
                profile.playerEnergyLastWallClockUtcTicks,
                profile.playerEnergyHighestWallClockUtcTicks);
            profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = Math.Max(
                -1L,
                profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds);
            profile.musicVolume = Mathf.Clamp01(profile.musicVolume);
            profile.masterVolume = Mathf.Clamp01(profile.masterVolume);
            profile.attackerVolume = Mathf.Clamp01(profile.attackerVolume);
            profile.defenderVolume = Mathf.Clamp01(profile.defenderVolume);
            profile.projectileVolume = Mathf.Clamp01(profile.projectileVolume);
            profile.ambientGameVolume = Mathf.Clamp01(profile.ambientGameVolume);
            profile.videoQuality = GetSafeQualityIndex(profile.videoQuality);
            PlayerUpgradeProgression.Sanitize(profile);
        }

        private static int GetSafeQualityIndex(int qualityIndex)
        {
            var qualityCount = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            return qualityCount <= 0
                ? 0
                : Mathf.Clamp(qualityIndex, 0, qualityCount - 1);
        }
    }
}
