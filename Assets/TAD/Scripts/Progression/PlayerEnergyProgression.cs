using System;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Pure rules for the persistent energy used to start a new TAD match. Runtime code supplies
    /// clocks and presentation; this class only mutates the profile-owned energy state.
    /// </summary>
    public static class PlayerEnergyProgression
    {
        public const int MaximumEnergy = 30;
        public const int GameStartEnergyCost = 5;
        public const long RechargeIntervalMilliseconds = 6L * 60L * 1000L;
        public const long FullRechargeMilliseconds = MaximumEnergy * RechargeIntervalMilliseconds;

        /// <summary>
        /// Initializes an unanchored profile or reconciles it with the time that passed while the
        /// application was not running. Android elapsed-realtime is preferred when available
        /// because it is unaffected by the device wall clock.
        /// </summary>
        public static PlayerEnergyOfflineRefresh InitializeFromClock(
            PlayerProfileData profile,
            long utcNowTicks,
            long deviceElapsedRealtimeMilliseconds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            var safeNowTicks = Math.Max(0L, utcNowTicks);
            if (profile.playerEnergyLastWallClockUtcTicks <= 0L &&
                profile.playerEnergyHighestWallClockUtcTicks <= 0L)
            {
                profile.playerEnergyLastWallClockUtcTicks = safeNowTicks;
                profile.playerEnergyHighestWallClockUtcTicks = safeNowTicks;
                if (deviceElapsedRealtimeMilliseconds >= 0L)
                {
                    profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = deviceElapsedRealtimeMilliseconds;
                }

                return new PlayerEnergyOfflineRefresh(
                    energyChanged: false,
                    clockRollbackDetected: false,
                    anchorInitialized: true);
            }

            return RefreshFromOfflineClock(profile, safeNowTicks, deviceElapsedRealtimeMilliseconds);
        }

        /// <summary>
        /// Advances a saved profile from a wall-clock/device-uptime sample. A wall clock moving
        /// backwards never awards energy and the highest observed wall-clock time is retained.
        /// </summary>
        public static PlayerEnergyOfflineRefresh RefreshFromOfflineClock(
            PlayerProfileData profile,
            long utcNowTicks,
            long deviceElapsedRealtimeMilliseconds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            var safeNowTicks = Math.Max(0L, utcNowTicks);
            var highestObservedTicks = Math.Max(
                profile.playerEnergyHighestWallClockUtcTicks,
                profile.playerEnergyLastWallClockUtcTicks);

            if (highestObservedTicks <= 0L)
            {
                return InitializeFromClock(profile, safeNowTicks, deviceElapsedRealtimeMilliseconds);
            }

            var previousDeviceElapsed = profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds;
            var hasReliableDeviceElapsed = deviceElapsedRealtimeMilliseconds >= 0L &&
                                            previousDeviceElapsed >= 0L &&
                                            deviceElapsedRealtimeMilliseconds >= previousDeviceElapsed;
            var rollbackDetected = false;
            long elapsedMilliseconds;

            if (hasReliableDeviceElapsed)
            {
                elapsedMilliseconds = deviceElapsedRealtimeMilliseconds - previousDeviceElapsed;
            }
            else if (safeNowTicks >= highestObservedTicks)
            {
                elapsedMilliseconds = TicksToRechargeMilliseconds(safeNowTicks - highestObservedTicks);
            }
            else
            {
                // Without a same-boot monotonic timer, accepting a backward wall clock would
                // make changing the phone time a source of repeated free energy.
                elapsedMilliseconds = 0L;
                rollbackDetected = true;
            }

            var energyChanged = AdvanceByMilliseconds(profile, elapsedMilliseconds);
            profile.playerEnergyLastWallClockUtcTicks = Math.Max(
                profile.playerEnergyLastWallClockUtcTicks,
                safeNowTicks);
            profile.playerEnergyHighestWallClockUtcTicks = Math.Max(highestObservedTicks, safeNowTicks);
            if (deviceElapsedRealtimeMilliseconds >= 0L)
            {
                profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = deviceElapsedRealtimeMilliseconds;
            }

            return new PlayerEnergyOfflineRefresh(energyChanged, rollbackDetected, anchorInitialized: false);
        }

        /// <summary>Records a new persisted time anchor after live, monotonic runtime progress.</summary>
        public static void RecordClockAnchor(
            PlayerProfileData profile,
            long utcNowTicks,
            long deviceElapsedRealtimeMilliseconds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            var safeNowTicks = Math.Max(0L, utcNowTicks);
            profile.playerEnergyLastWallClockUtcTicks = Math.Max(
                profile.playerEnergyLastWallClockUtcTicks,
                safeNowTicks);
            profile.playerEnergyHighestWallClockUtcTicks = Math.Max(
                profile.playerEnergyHighestWallClockUtcTicks,
                safeNowTicks);
            if (deviceElapsedRealtimeMilliseconds >= 0L)
            {
                profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = deviceElapsedRealtimeMilliseconds;
            }
        }

        /// <summary>Applies trusted elapsed time captured during the current application session.</summary>
        public static bool AdvanceByMilliseconds(PlayerProfileData profile, long elapsedMilliseconds)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            if (profile.playerEnergy >= MaximumEnergy || elapsedMilliseconds <= 0L)
            {
                return false;
            }

            var remainingEnergy = MaximumEnergy - profile.playerEnergy;
            var remainingCapacityMilliseconds = Math.Max(
                0L,
                remainingEnergy * RechargeIntervalMilliseconds - profile.playerEnergyRechargeRemainderMilliseconds);
            var usableElapsedMilliseconds = Math.Min(elapsedMilliseconds, remainingCapacityMilliseconds);
            if (usableElapsedMilliseconds <= 0L)
            {
                return false;
            }

            var accumulatedMilliseconds = profile.playerEnergyRechargeRemainderMilliseconds + usableElapsedMilliseconds;
            var energyGained = (int)(accumulatedMilliseconds / RechargeIntervalMilliseconds);
            if (energyGained > 0)
            {
                profile.playerEnergy = Mathf.Min(MaximumEnergy, profile.playerEnergy + energyGained);
            }

            profile.playerEnergyRechargeRemainderMilliseconds = profile.playerEnergy >= MaximumEnergy
                ? 0L
                : accumulatedMilliseconds % RechargeIntervalMilliseconds;
            return energyGained > 0;
        }

        /// <summary>Attempts to pay energy for an action such as starting a new match.</summary>
        public static bool TrySpend(PlayerProfileData profile, int amount)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            if (amount <= 0 || profile.playerEnergy < amount)
            {
                return false;
            }

            profile.playerEnergy -= amount;
            return true;
        }

        /// <summary>Returns a failed scene-load charge, capped to the player's maximum energy.</summary>
        public static void Refund(PlayerProfileData profile, int amount)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            if (amount <= 0)
            {
                return;
            }

            profile.playerEnergy = Mathf.Min(MaximumEnergy, profile.playerEnergy + amount);
            if (profile.playerEnergy >= MaximumEnergy)
            {
                profile.playerEnergyRechargeRemainderMilliseconds = 0L;
            }
        }

        /// <summary>Builds presentation data without exposing mutable profile fields.</summary>
        public static PlayerEnergySnapshot CreateSnapshot(PlayerProfileData profile, bool clockRollbackDetected = false)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SanitizeState(profile);
            if (profile.playerEnergy >= MaximumEnergy)
            {
                return new PlayerEnergySnapshot(
                    profile.playerEnergy,
                    MaximumEnergy,
                    nextEnergyInMilliseconds: 0L,
                    fullRechargeInMilliseconds: 0L,
                    clockRollbackDetected);
            }

            var nextEnergyInMilliseconds = RechargeIntervalMilliseconds - profile.playerEnergyRechargeRemainderMilliseconds;
            var fullRechargeInMilliseconds = nextEnergyInMilliseconds +
                                             (MaximumEnergy - profile.playerEnergy - 1) * RechargeIntervalMilliseconds;
            return new PlayerEnergySnapshot(
                profile.playerEnergy,
                MaximumEnergy,
                nextEnergyInMilliseconds,
                fullRechargeInMilliseconds,
                clockRollbackDetected);
        }

        private static void SanitizeState(PlayerProfileData profile)
        {
            profile.playerEnergy = Mathf.Clamp(profile.playerEnergy, 0, MaximumEnergy);
            profile.playerEnergyRechargeRemainderMilliseconds = profile.playerEnergy >= MaximumEnergy
                ? 0L
                : Math.Max(
                    0L,
                    Math.Min(RechargeIntervalMilliseconds - 1L, profile.playerEnergyRechargeRemainderMilliseconds));
            profile.playerEnergyLastWallClockUtcTicks = Math.Max(0L, profile.playerEnergyLastWallClockUtcTicks);
            profile.playerEnergyHighestWallClockUtcTicks = Math.Max(
                profile.playerEnergyLastWallClockUtcTicks,
                profile.playerEnergyHighestWallClockUtcTicks);
            profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds = Math.Max(
                -1L,
                profile.playerEnergyLastDeviceElapsedRealtimeMilliseconds);
        }

        private static long TicksToRechargeMilliseconds(long ticks)
        {
            if (ticks <= 0L)
            {
                return 0L;
            }

            // Any amount past a complete refill has the same gameplay result, and capping also
            // prevents malformed/corrupted timestamps from overflowing elapsed-time arithmetic.
            return Math.Min(FullRechargeMilliseconds, ticks / TimeSpan.TicksPerMillisecond);
        }
    }

    /// <summary>Immutable player-energy data used by menu and modal presentation.</summary>
    public readonly struct PlayerEnergySnapshot
    {
        public PlayerEnergySnapshot(
            int currentEnergy,
            int maximumEnergy,
            long nextEnergyInMilliseconds,
            long fullRechargeInMilliseconds,
            bool clockRollbackDetected)
        {
            CurrentEnergy = currentEnergy;
            MaximumEnergy = maximumEnergy;
            NextEnergyInMilliseconds = nextEnergyInMilliseconds;
            FullRechargeInMilliseconds = fullRechargeInMilliseconds;
            ClockRollbackDetected = clockRollbackDetected;
        }

        public int CurrentEnergy { get; }
        public int MaximumEnergy { get; }
        public long NextEnergyInMilliseconds { get; }
        public long FullRechargeInMilliseconds { get; }
        public bool ClockRollbackDetected { get; }
        public bool IsFull => CurrentEnergy >= MaximumEnergy;
        public bool CanStartNewGame => CurrentEnergy >= PlayerEnergyProgression.GameStartEnergyCost;
    }

    /// <summary>Result metadata for an offline reconciliation attempt.</summary>
    public readonly struct PlayerEnergyOfflineRefresh
    {
        public PlayerEnergyOfflineRefresh(bool energyChanged, bool clockRollbackDetected, bool anchorInitialized)
        {
            EnergyChanged = energyChanged;
            ClockRollbackDetected = clockRollbackDetected;
            AnchorInitialized = anchorInitialized;
        }

        public bool EnergyChanged { get; }
        public bool ClockRollbackDetected { get; }
        public bool AnchorInitialized { get; }
    }

    /// <summary>
    /// Supplies the strongest local clock available without introducing a backend dependency.
    /// Android's elapsed realtime survives process suspension and ignores wall-clock edits for
    /// the current device boot; other platforms safely fall back to the profile high-water mark.
    /// </summary>
    internal static class PlayerEnergyClock
    {
        public static long UtcNowTicks => DateTime.UtcNow.Ticks;

        public static long GetDeviceElapsedRealtimeMilliseconds()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var systemClock = new AndroidJavaClass("android.os.SystemClock"))
                {
                    return systemClock.CallStatic<long>("elapsedRealtime");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"TAD could not read Android elapsed realtime for player energy: {exception.Message}");
            }
#endif
            return -1L;
        }
    }
}
