using System;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Owns the currently loaded player profile and applies player-facing settings consistently
    /// across the menu and every loaded level.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerSettingsService : MonoBehaviour
    {
        private const string ServiceObjectName = "TAD Player Settings";

        private static PlayerSettingsService instance;

        private PlayerProfileData profile;

        /// <summary>Raised after a setting is saved and applied to the running game.</summary>
        public event Action<PlayerProfileData> SettingsChanged;

        public static PlayerSettingsService Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public PlayerProfileData Profile
        {
            get
            {
                EnsureProfile();
                return profile;
            }
        }

        public bool AreAttackerHealthBarsVisible => Profile.showAttackerHealthBars;
        public bool IsDefenderExperienceVisible => Profile.showDefenderExperience;
        public bool AreNotificationsEnabled => Profile.notificationsEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateForGameSession()
        {
            EnsureInstance();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureProfile();
            ApplyAll();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                Save();
            }
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public void SetAttackerHealthBarsVisible(bool isVisible)
        {
            UpdateProfile(data => data.showAttackerHealthBars = isVisible);
        }

        public void SetDefenderExperienceVisible(bool isVisible)
        {
            UpdateProfile(data => data.showDefenderExperience = isVisible);
        }

        public void SetMasterVolume(float value)
        {
            UpdateProfile(data =>
            {
                data.masterVolume = Mathf.Clamp01(value);
                data.musicVolume = data.masterVolume;
                data.musicEnabled = data.masterVolume > 0f;
            });
        }

        public void SetAttackerVolume(float value)
        {
            UpdateProfile(data => data.attackerVolume = Mathf.Clamp01(value));
        }

        public void SetDefenderVolume(float value)
        {
            UpdateProfile(data => data.defenderVolume = Mathf.Clamp01(value));
        }

        public void SetProjectileVolume(float value)
        {
            UpdateProfile(data => data.projectileVolume = Mathf.Clamp01(value));
        }

        public void SetAmbientGameVolume(float value)
        {
            UpdateProfile(data => data.ambientGameVolume = Mathf.Clamp01(value));
        }

        public void SetNotificationsEnabled(bool isEnabled)
        {
            UpdateProfile(data => data.notificationsEnabled = isEnabled);
        }

        public void SetControlHintsEnabled(bool isEnabled)
        {
            UpdateProfile(data => data.controlHintsEnabled = isEnabled);
        }

        public void SetVideoQuality(int qualityIndex)
        {
            UpdateProfile(data => data.videoQuality = qualityIndex);
        }

        /// <summary>
        /// Applies one completed match's account experience, immediately persists it, and notifies
        /// profile observers so a future level/progression view can refresh without reloading.
        /// </summary>
        public PlayerMatchExperienceReward AwardMatchExperience(float matchExperience, bool playerWon)
        {
            EnsureProfile();
            var reward = PlayerLevelProgression.AwardMatchExperience(profile, matchExperience, playerWon);
            PlayerProfileStore.Save(profile);
            SettingsChanged?.Invoke(profile);
            return reward;
        }

        /// <summary>Returns an authored category multiplier; master volume is applied by AudioListener.</summary>
        public float GetChannelVolume(TadAudioChannelType channel)
        {
            var data = Profile;
            return channel switch
            {
                TadAudioChannelType.Attacker => data.attackerVolume,
                TadAudioChannelType.Defender => data.defenderVolume,
                TadAudioChannelType.Projectile => data.projectileVolume,
                TadAudioChannelType.Ambient => data.ambientGameVolume,
                _ => 1f
            };
        }

        public void Save()
        {
            EnsureProfile();
            PlayerProfileStore.Save(profile);
        }

        private void UpdateProfile(Action<PlayerProfileData> update)
        {
            EnsureProfile();
            update?.Invoke(profile);
            PlayerProfileStore.Save(profile);
            ApplyAll();
            SettingsChanged?.Invoke(profile);
        }

        private void EnsureProfile()
        {
            profile ??= PlayerProfileStore.Load();
        }

        private void ApplyAll()
        {
            EnsureProfile();
            ApplyVideoQuality();
            AudioListener.volume = profile.masterVolume;
        }

        private void ApplyVideoQuality()
        {
            var qualityNames = QualitySettings.names;
            if (qualityNames == null || qualityNames.Length == 0)
            {
                return;
            }

            var safeQuality = Mathf.Clamp(profile.videoQuality, 0, qualityNames.Length - 1);
            if (QualitySettings.GetQualityLevel() != safeQuality)
            {
                QualitySettings.SetQualityLevel(safeQuality, applyExpensiveChanges: true);
            }
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            instance = FindAnyObjectByType<PlayerSettingsService>(FindObjectsInactive.Include);
            if (instance != null)
            {
                return;
            }

            var serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<PlayerSettingsService>();
        }
    }
}
