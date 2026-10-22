using System;
using System.Collections.Generic;
using System.Diagnostics;
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

#if UNITY_EDITOR
        // PlayMode regression fixtures must not inherit progress from the developer's saved
        // profile.  This is intentionally editor-only and never writes through to PlayerPrefs;
        // gameplay builds always read the persistent profile below.
        private static PlayerProfileData editorTestProfileOverride;
#endif

        private PlayerProfileData profile;
        private long energyStopwatchAnchorMilliseconds;
        private long nextEnergyAnchorPersistenceMilliseconds;
        private bool isEnergyClockInitialized;
        private bool isEnergyClockRollbackDetected;
        private bool isEnergyApplicationBackgrounded;

        /// <summary>Raised after a setting is saved and applied to the running game.</summary>
        public event Action<PlayerProfileData> SettingsChanged;

        /// <summary>Raised after a whole energy point is gained, spent, or refunded.</summary>
        public event Action<PlayerEnergySnapshot> EnergyChanged;

        /// <summary>Raised after a saved account-level upgrade rank or pending-card queue changes.</summary>
        public event Action<PlayerProfileData> PlayerUpgradesChanged;

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
#if UNITY_EDITOR
                if (editorTestProfileOverride != null)
                {
                    return editorTestProfileOverride;
                }
#endif
                return profile;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Temporarily supplies deterministic account data to editor PlayMode tests without
        /// changing a developer's saved profile. Call <see cref="ClearEditorTestProfileOverride"/>
        /// in fixture teardown.
        /// </summary>
        public static void SetEditorTestProfileOverride(PlayerProfileData testProfile)
        {
            editorTestProfileOverride = testProfile;
        }

        /// <summary>Stops using the editor-only test profile override.</summary>
        public static void ClearEditorTestProfileOverride()
        {
            editorTestProfileOverride = null;
        }
#endif

        public bool HasAttackerHealthDisplayUpgrade => PlayerUpgradeProgression.IsGeneralUpgradeUnlocked(
            Profile,
            GeneralPlayerUpgrade.AttackerHealthDisplay);
        public bool HasDefenderExperienceDisplayUpgrade => PlayerUpgradeProgression.IsGeneralUpgradeUnlocked(
            Profile,
            GeneralPlayerUpgrade.DefenderExperienceDisplay);
        public bool HasQueuedRouteDisplayUpgrade => PlayerUpgradeProgression.IsGeneralUpgradeUnlocked(
            Profile,
            GeneralPlayerUpgrade.QueuedRouteDisplay);
        public bool HasIcePlacementUpgrade => PlayerUpgradeProgression.IsGeneralUpgradeUnlocked(
            Profile,
            GeneralPlayerUpgrade.IcePlacement);
        public bool HasMudPlacementUpgrade => PlayerUpgradeProgression.IsGeneralUpgradeUnlocked(
            Profile,
            GeneralPlayerUpgrade.MudPlacement);
        public bool AreAttackerHealthBarsVisible => HasAttackerHealthDisplayUpgrade && Profile.showAttackerHealthBars;
        public bool IsDefenderExperienceVisible => HasDefenderExperienceDisplayUpgrade && Profile.showDefenderExperience;
        public bool AreNotificationsEnabled => Profile.notificationsEnabled;

        /// <summary>Returns the current profile-owned energy after applying trusted live-time progress.</summary>
        public PlayerEnergySnapshot PlayerEnergy
        {
            get
            {
                RefreshPlayerEnergy();
                return PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected);
            }
        }

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
            InitializeEnergyClock();
            ApplyAll();
        }

        private void Update()
        {
            // Once the application is backgrounded, resume reconciliation owns elapsed time.
            // This avoids adding the same interval once from Stopwatch and again from the
            // platform/offline clock when Unity is configured to keep updating in background.
            if (isEnergyApplicationBackgrounded)
            {
                return;
            }

            RefreshPlayerEnergy();
            if (!isEnergyClockInitialized)
            {
                return;
            }

            var nowMilliseconds = GetMonotonicMilliseconds();
            if (nowMilliseconds >= nextEnergyAnchorPersistenceMilliseconds)
            {
                CaptureEnergyClockAnchor();
                PlayerProfileStore.Save(profile);
                nextEnergyAnchorPersistenceMilliseconds = nowMilliseconds + 30000L;
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                PrepareEnergyForBackground();
            }
            else
            {
                ReconcileEnergyAfterBackground();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ReconcileEnergyAfterBackground();
            }
            else
            {
                PrepareEnergyForBackground();
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
            if (!HasAttackerHealthDisplayUpgrade)
            {
                return;
            }

            UpdateProfile(data => data.showAttackerHealthBars = isVisible);
        }

        public void SetDefenderExperienceVisible(bool isVisible)
        {
            if (!HasDefenderExperienceDisplayUpgrade)
            {
                return;
            }

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
            for (var level = reward.StartingLevel + 1; level <= reward.ResultingLevel; level++)
            {
                PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, level);
            }

            PlayerProfileStore.Save(profile);
            SettingsChanged?.Invoke(profile);
            if (reward.LevelsGained > 0)
            {
                PlayerUpgradesChanged?.Invoke(profile);
            }
            return reward;
        }

        /// <summary>Copies the outstanding persistent card choices for one newly reached account level.</summary>
        public int CopyPendingPlayerUpgradeChoices(
            int playerLevel,
            List<PendingPlayerUpgradeChoiceData> destination)
        {
            EnsureProfile();
            return PlayerUpgradeProgression.CopyPendingChoicesForLevel(profile, playerLevel, destination);
        }

        /// <summary>Number of saved player-upgrade cards still waiting for the player to choose one.</summary>
        public int PendingPlayerUpgradeChoiceCount
        {
            get
            {
                EnsureProfile();
                return PlayerUpgradeProgression.GetPendingChoiceCount(profile);
            }
        }

        /// <summary>Selects one of the persisted player-level upgrade cards and saves immediately.</summary>
        public bool TryChoosePlayerUpgrade(
            int playerLevel,
            PlayerUpgradeRewardSlot rewardSlot,
            string upgradeId)
        {
            EnsureProfile();
            if (!PlayerUpgradeProgression.TryChooseUpgrade(profile, playerLevel, rewardSlot, upgradeId))
            {
                return false;
            }

            PlayerProfileStore.Save(profile);
            ApplyAll();
            SettingsChanged?.Invoke(profile);
            PlayerUpgradesChanged?.Invoke(profile);
            return true;
        }

        /// <summary>
        /// Pays the fixed energy cost to start a new match. The charge is persisted immediately so
        /// double-clicking start controls or closing the application cannot create a duplicate run.
        /// </summary>
        public bool TrySpendEnergyForNewGame(out PlayerEnergySnapshot remainingEnergy)
        {
            RefreshPlayerEnergy();
            if (!PlayerEnergyProgression.TrySpend(profile, PlayerEnergyProgression.GameStartEnergyCost))
            {
                remainingEnergy = PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected);
                return false;
            }

            CaptureEnergyClockAnchor();
            PlayerProfileStore.Save(profile);
            remainingEnergy = PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected);
            EnergyChanged?.Invoke(remainingEnergy);
            return true;
        }

        /// <summary>Returns one start charge when a requested scene cannot begin loading.</summary>
        public void RefundEnergyForFailedNewGameStart()
        {
            RefreshPlayerEnergy();
            PlayerEnergyProgression.Refund(profile, PlayerEnergyProgression.GameStartEnergyCost);
            CaptureEnergyClockAnchor();
            PlayerProfileStore.Save(profile);
            EnergyChanged?.Invoke(PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected));
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
            RefreshPlayerEnergy();
            CaptureEnergyClockAnchor();
            PlayerProfileStore.Save(profile);
        }

        private void UpdateProfile(Action<PlayerProfileData> update)
        {
            EnsureProfile();
            RefreshPlayerEnergy();
            update?.Invoke(profile);
            CaptureEnergyClockAnchor();
            PlayerProfileStore.Save(profile);
            ApplyAll();
            SettingsChanged?.Invoke(profile);
        }

        private void EnsureProfile()
        {
            profile ??= PlayerProfileStore.Load();
        }

        private void InitializeEnergyClock()
        {
            if (isEnergyClockInitialized)
            {
                return;
            }

            EnsureProfile();
            var refresh = PlayerEnergyProgression.InitializeFromClock(
                profile,
                PlayerEnergyClock.UtcNowTicks,
                PlayerEnergyClock.GetDeviceElapsedRealtimeMilliseconds());
            isEnergyClockRollbackDetected = refresh.ClockRollbackDetected;
            energyStopwatchAnchorMilliseconds = GetMonotonicMilliseconds();
            nextEnergyAnchorPersistenceMilliseconds = energyStopwatchAnchorMilliseconds + 30000L;
            isEnergyClockInitialized = true;

            // Establishing/migrating a clock anchor must survive an immediate app close.
            PlayerProfileStore.Save(profile);
        }

        private void RefreshPlayerEnergy()
        {
            EnsureProfile();
            InitializeEnergyClock();
            if (!isEnergyClockInitialized || isEnergyApplicationBackgrounded)
            {
                return;
            }

            var nowMilliseconds = GetMonotonicMilliseconds();
            var elapsedMilliseconds = Math.Max(0L, nowMilliseconds - energyStopwatchAnchorMilliseconds);
            energyStopwatchAnchorMilliseconds = nowMilliseconds;
            if (elapsedMilliseconds <= 0L)
            {
                return;
            }

            var energyChanged = PlayerEnergyProgression.AdvanceByMilliseconds(profile, elapsedMilliseconds);
            if (isEnergyClockRollbackDetected &&
                PlayerEnergyClock.UtcNowTicks >= profile.playerEnergyHighestWallClockUtcTicks)
            {
                isEnergyClockRollbackDetected = false;
            }

            if (!energyChanged)
            {
                return;
            }

            CaptureEnergyClockAnchor();
            PlayerProfileStore.Save(profile);
            EnergyChanged?.Invoke(PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected));
        }

        /// <summary>
        /// Saves a reliable anchor immediately before Unity stops receiving normal Update calls.
        /// On Android this records elapsedRealtime so a device wall-clock edit cannot affect the
        /// amount restored when the app resumes during the same device boot.
        /// </summary>
        private void PrepareEnergyForBackground()
        {
            if (isEnergyApplicationBackgrounded)
            {
                return;
            }

            Save();
            isEnergyApplicationBackgrounded = true;
        }

        /// <summary>
        /// Reconciles time spent away from the application, then resets the per-process Stopwatch
        /// anchor so resumed frames cannot count the same time twice.
        /// </summary>
        private void ReconcileEnergyAfterBackground()
        {
            if (!isEnergyApplicationBackgrounded)
            {
                return;
            }

            EnsureProfile();
            InitializeEnergyClock();
            var refresh = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                PlayerEnergyClock.UtcNowTicks,
                PlayerEnergyClock.GetDeviceElapsedRealtimeMilliseconds());
            isEnergyClockRollbackDetected = refresh.ClockRollbackDetected;
            energyStopwatchAnchorMilliseconds = GetMonotonicMilliseconds();
            nextEnergyAnchorPersistenceMilliseconds = energyStopwatchAnchorMilliseconds + 30000L;
            isEnergyApplicationBackgrounded = false;

            // RefreshFromOfflineClock also moves the persisted time anchors, including partial
            // progress toward the next point, so save even when no complete point was awarded.
            PlayerProfileStore.Save(profile);
            if (refresh.EnergyChanged)
            {
                EnergyChanged?.Invoke(PlayerEnergyProgression.CreateSnapshot(profile, isEnergyClockRollbackDetected));
            }
        }

        private void CaptureEnergyClockAnchor()
        {
            if (!isEnergyClockInitialized || profile == null)
            {
                return;
            }

            PlayerEnergyProgression.RecordClockAnchor(
                profile,
                PlayerEnergyClock.UtcNowTicks,
                PlayerEnergyClock.GetDeviceElapsedRealtimeMilliseconds());
        }

        private static long GetMonotonicMilliseconds()
        {
            return Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
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
