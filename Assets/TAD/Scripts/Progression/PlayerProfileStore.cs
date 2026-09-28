using System;
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
        public const int CurrentVersion = 2;

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
            if (profile.version >= CurrentVersion)
            {
                return;
            }

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

        private static void Sanitize(PlayerProfileData profile)
        {
            profile.version = CurrentVersion;
            profile.lastPlayedLevelId ??= string.Empty;
            profile.metaCurrency = Mathf.Max(0, profile.metaCurrency);
            profile.musicVolume = Mathf.Clamp01(profile.musicVolume);
            profile.masterVolume = Mathf.Clamp01(profile.masterVolume);
            profile.attackerVolume = Mathf.Clamp01(profile.attackerVolume);
            profile.defenderVolume = Mathf.Clamp01(profile.defenderVolume);
            profile.projectileVolume = Mathf.Clamp01(profile.projectileVolume);
            profile.ambientGameVolume = Mathf.Clamp01(profile.ambientGameVolume);
            profile.videoQuality = GetSafeQualityIndex(profile.videoQuality);
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
