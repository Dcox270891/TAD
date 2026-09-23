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
        public bool musicEnabled = true;
        public float musicVolume = 1f;
        public int videoQuality;
        public bool notificationsEnabled = true;
        public bool controlHintsEnabled = true;
    }

    /// <summary>
    /// Persists the current prototype profile through the platform-supported PlayerPrefs store.
    /// </summary>
    public static class PlayerProfileStore
    {
        public const int CurrentVersion = 1;

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
            return new PlayerProfileData();
        }

        private static void Sanitize(PlayerProfileData profile)
        {
            profile.version = CurrentVersion;
            profile.lastPlayedLevelId ??= string.Empty;
            profile.metaCurrency = Mathf.Max(0, profile.metaCurrency);
            profile.musicVolume = Mathf.Clamp01(profile.musicVolume);
        }
    }
}