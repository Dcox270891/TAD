using System;
using NUnit.Framework;
using TAD.Progression;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class PlayerEnergyProgressionEditModeTests
    {
        [Test]
        public void PlayerEnergy_NewProfileStartsFullAtThirty()
        {
            var profile = new PlayerProfileData();

            var energy = PlayerEnergyProgression.CreateSnapshot(profile);

            Assert.That(energy.CurrentEnergy, Is.EqualTo(30));
            Assert.That(energy.MaximumEnergy, Is.EqualTo(30));
            Assert.That(energy.IsFull, Is.True);
            Assert.That(energy.NextEnergyInMilliseconds, Is.EqualTo(0L));
        }

        [Test]
        public void PlayerEnergy_NewGameCostsFiveAndCannotSpendBelowTheRequiredAmount()
        {
            var profile = new PlayerProfileData { playerEnergy = 5 };

            Assert.That(
                PlayerEnergyProgression.TrySpend(profile, PlayerEnergyProgression.GameStartEnergyCost),
                Is.True);
            Assert.That(profile.playerEnergy, Is.EqualTo(0));
            Assert.That(
                PlayerEnergyProgression.TrySpend(profile, PlayerEnergyProgression.GameStartEnergyCost),
                Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(0));
        }

        [Test]
        public void PlayerEnergy_RechargesOnePointEverySixMinutesAndCapsAtThirty()
        {
            var profile = new PlayerProfileData { playerEnergy = 0 };

            Assert.That(
                PlayerEnergyProgression.AdvanceByMilliseconds(
                    profile,
                    PlayerEnergyProgression.RechargeIntervalMilliseconds - 1L),
                Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(0));
            Assert.That(profile.playerEnergyRechargeRemainderMilliseconds,
                Is.EqualTo(PlayerEnergyProgression.RechargeIntervalMilliseconds - 1L));

            Assert.That(PlayerEnergyProgression.AdvanceByMilliseconds(profile, 1L), Is.True);
            Assert.That(profile.playerEnergy, Is.EqualTo(1));
            Assert.That(profile.playerEnergyRechargeRemainderMilliseconds, Is.EqualTo(0L));

            Assert.That(
                PlayerEnergyProgression.AdvanceByMilliseconds(
                    profile,
                    PlayerEnergyProgression.FullRechargeMilliseconds),
                Is.True);
            Assert.That(profile.playerEnergy, Is.EqualTo(PlayerEnergyProgression.MaximumEnergy));
            Assert.That(profile.playerEnergyRechargeRemainderMilliseconds, Is.EqualTo(0L));
        }

        [Test]
        public void PlayerEnergy_RefillsAnEmptyReserveInThreeHoursWhileOffline()
        {
            var startUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc).Ticks;
            var profile = new PlayerProfileData
            {
                playerEnergy = 0,
                playerEnergyLastWallClockUtcTicks = startUtc,
                playerEnergyHighestWallClockUtcTicks = startUtc,
                playerEnergyLastDeviceElapsedRealtimeMilliseconds = -1L
            };

            var refresh = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                startUtc + TimeSpan.FromHours(3).Ticks,
                deviceElapsedRealtimeMilliseconds: -1L);

            Assert.That(refresh.ClockRollbackDetected, Is.False);
            Assert.That(refresh.EnergyChanged, Is.True);
            Assert.That(profile.playerEnergy, Is.EqualTo(PlayerEnergyProgression.MaximumEnergy));
            Assert.That(PlayerEnergyProgression.CreateSnapshot(profile).FullRechargeInMilliseconds, Is.EqualTo(0L));
        }

        [Test]
        public void PlayerEnergy_WallClockRollbackDoesNotGrantEnergyUntilTheHighWaterMarkIsReached()
        {
            var anchorUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc).Ticks;
            var profile = new PlayerProfileData
            {
                playerEnergy = 10,
                playerEnergyLastWallClockUtcTicks = anchorUtc,
                playerEnergyHighestWallClockUtcTicks = anchorUtc,
                playerEnergyLastDeviceElapsedRealtimeMilliseconds = -1L
            };

            var rollback = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc - TimeSpan.FromHours(1).Ticks,
                deviceElapsedRealtimeMilliseconds: -1L);

            Assert.That(rollback.ClockRollbackDetected, Is.True);
            Assert.That(rollback.EnergyChanged, Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(10));

            var recovered = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc + TimeSpan.FromMinutes(6).Ticks,
                deviceElapsedRealtimeMilliseconds: -1L);

            Assert.That(recovered.ClockRollbackDetected, Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(11));
        }

        [Test]
        public void PlayerEnergy_SameBootMonotonicClockIgnoresWallClockChanges()
        {
            var anchorUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc).Ticks;
            var profile = new PlayerProfileData
            {
                playerEnergy = 10,
                playerEnergyLastWallClockUtcTicks = anchorUtc,
                playerEnergyHighestWallClockUtcTicks = anchorUtc,
                playerEnergyLastDeviceElapsedRealtimeMilliseconds = 1000L
            };

            var refresh = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc - TimeSpan.FromDays(1).Ticks,
                deviceElapsedRealtimeMilliseconds: 1000L + PlayerEnergyProgression.RechargeIntervalMilliseconds);

            Assert.That(refresh.ClockRollbackDetected, Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(11));
        }

        [Test]
        public void PlayerEnergy_SameBootMonotonicClockRejectsForwardWallClockCheats()
        {
            var anchorUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc).Ticks;
            var profile = new PlayerProfileData
            {
                playerEnergy = 10,
                playerEnergyLastWallClockUtcTicks = anchorUtc,
                playerEnergyHighestWallClockUtcTicks = anchorUtc,
                playerEnergyLastDeviceElapsedRealtimeMilliseconds = 1000L
            };

            var refresh = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc + TimeSpan.FromHours(3).Ticks,
                deviceElapsedRealtimeMilliseconds: 1000L);

            Assert.That(refresh.ClockRollbackDetected, Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(10),
                "The same-boot elapsed clock, not a manually advanced wall clock, must determine recharge progress.");
        }

        [Test]
        public void PlayerEnergy_ResumeReconciliationIsIdempotentWhenFocusAndPauseCallbacksBothFire()
        {
            var anchorUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc).Ticks;
            var elapsedRealtimeAtPause = 1000L;
            var elapsedRealtimeAtResume = elapsedRealtimeAtPause +
                                         PlayerEnergyProgression.RechargeIntervalMilliseconds;
            var profile = new PlayerProfileData
            {
                playerEnergy = 10,
                playerEnergyLastWallClockUtcTicks = anchorUtc,
                playerEnergyHighestWallClockUtcTicks = anchorUtc,
                playerEnergyLastDeviceElapsedRealtimeMilliseconds = elapsedRealtimeAtPause
            };

            var firstResume = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc + TimeSpan.FromMinutes(6).Ticks,
                elapsedRealtimeAtResume);
            var duplicateResume = PlayerEnergyProgression.RefreshFromOfflineClock(
                profile,
                anchorUtc + TimeSpan.FromMinutes(6).Ticks,
                elapsedRealtimeAtResume);

            Assert.That(firstResume.EnergyChanged, Is.True);
            Assert.That(duplicateResume.EnergyChanged, Is.False);
            Assert.That(profile.playerEnergy, Is.EqualTo(11));
        }
    }
}
