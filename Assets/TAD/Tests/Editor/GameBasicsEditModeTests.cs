using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Match;
using TAD.Progression;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameBasicsEditModeTests
    {
        private const string PlayerProfileKey = "TAD.PlayerProfile";

        [Test]
        public void DefenderRewardCalculator_SharesAssistExperienceAndKeepsTheKillReward()
        {
            var reward = DefenderRewardCalculator.Calculate(expGiven: 10, sendSize: 5, eligibleAssistCount: 1);

            Assert.That(reward.TotalExperience, Is.EqualTo(10f));
            Assert.That(reward.AssistExperiencePerRecipient, Is.EqualTo(1f));
            Assert.That(reward.TotalAssistExperience, Is.EqualTo(1f));
            Assert.That(reward.KillerExperience, Is.EqualTo(9f));
        }

        [Test]
        public void DefenderRewardCalculator_AppliesSendSizeBeforeTheTestingMultiplier()
        {
            var reward = DefenderRewardCalculator.Calculate(expGiven: 1, sendSize: 5, eligibleAssistCount: 1);

            Assert.That(reward.TotalExperience, Is.EqualTo(1f));
            Assert.That(reward.AssistExperiencePerRecipient, Is.EqualTo(0.1f));
            Assert.That(reward.KillerExperience, Is.EqualTo(0.9f));
        }

        [Test]
        public void DefenderRewardCalculator_NeverAllocatesMoreThanHalfToAssists()
        {
            var reward = DefenderRewardCalculator.Calculate(expGiven: 10, sendSize: 5, eligibleAssistCount: 10);

            Assert.That(reward.TotalAssistExperience, Is.LessThanOrEqualTo(reward.TotalExperience * 0.5f));
            Assert.That(reward.KillerExperience, Is.GreaterThanOrEqualTo(reward.TotalExperience * 0.5f));
            Assert.That(reward.TotalAssistExperience + reward.KillerExperience, Is.EqualTo(reward.TotalExperience).Within(0.001f));
        }

        [Test]
        public void AttackerCombatRating_CombinesAuthoredValuesPerSentAttacker()
        {
            var rating = AttackerCombatRating.Calculate(
                maximumHealth: 20f,
                movementSpeed: 3f,
                sendCost: 10,
                waveIncome: 2,
                sendSize: 5);

            Assert.That(rating, Is.EqualTo(7f));
            Assert.That(
                AttackerCombatRating.Calculate(
                    maximumHealth: 20f,
                    movementSpeed: 3f,
                    sendCost: 10,
                    waveIncome: 2,
                    sendSize: 0),
                Is.EqualTo(35f),
                "An invalid zero send size must not cause a divide-by-zero rating.");
        }

        [Test]
        public void DefenderLevelProgression_UsesOpeningMilestonesAndGentleLaterGrowth()
        {
            var requiredExperience = new int[9];
            for (var level = 1; level <= 9; level++)
            {
                requiredExperience[level - 1] = DefenderLevelProgression.GetExperienceRequiredForNextLevel(
                    level,
                    DefenderLevelProgression.DefaultLevelOneToTwoExperience);
            }

            CollectionAssert.AreEqual(new[] { 10, 12, 15, 17, 19, 22, 24, 27, 30 }, requiredExperience);
            Assert.That(
                DefenderLevelProgression.GetTotalExperienceRequired(
                    startingLevel: 1,
                    targetLevel: 10,
                    DefenderLevelProgression.DefaultLevelOneToTwoExperience),
                Is.EqualTo(176));
        }

        [Test]
        public void AttackerAiPlanner_AvoidsImmediateRepeatsWhenDifferentRouteReadyAttackersExist()
        {
            var basic = Load<AttackerDefinition>("ChickenDefinition");
            var fast = Load<AttackerDefinition>("GreyhoundDefinition");
            var strong = Load<AttackerDefinition>("BullDefinition");
            var route = new[] { new Vector3(-4f, 0f), new Vector3(4f, 0f) };
            var candidates = new List<AttackerAiCandidate>
            {
                new(basic, route),
                new(fast, route),
                new(strong, route)
            };

            Assert.That(
                AttackerWavePlanner.TryChoose(
                    candidates,
                    new List<AttackerAiDefenderThreat>(),
                    new List<AttackerDefinition> { basic },
                    day: 1,
                    groupIndex: 0,
                    out var decision),
                Is.True);

            Assert.That(decision.IsValid, Is.True);
            Assert.That(decision.Definition, Is.Not.EqualTo(basic));
            Assert.That(decision.Reason, Is.Not.Empty);
        }

        [Test]
        public void DefenderAiPlanner_UsesQueuedRoutesForPlacementAndAWeaponDecision()
        {
            var flying = Load<AttackerDefinition>("EagleDefinition");
            var unarmed = Load<WeaponDefinition>("UnarmedWeaponDefinition");
            var airRifle = Load<WeaponDefinition>("AirRifleWeaponDefinition");
            var route = new[] { new Vector3(-5f, 0f), new Vector3(0f, 0f), new Vector3(5f, 0f) };
            var plans = new List<DefenderAiRoutePlan>
            {
                new(flying, attackerCount: 5, route)
            };

            Assert.That(DefenderAiPlanner.GetSuggestedFarmHandCount(plans, maximumFarmHands: 5), Is.GreaterThan(0));
            Assert.That(
                DefenderAiPlanner.TryChoosePlacement(
                    plans,
                    new List<Vector2> { new(-4f, 3f), new(0f, 0f), new(4f, 3f) },
                    new List<DefenderAiCoverage>(),
                    farmHandRange: 1.5f,
                    out var placement),
                Is.True);
            Assert.That(placement.IsValid, Is.True);
            Assert.That(placement.Position, Is.EqualTo(Vector2.zero));

            Assert.That(
                DefenderAiPlanner.TryChooseWeapon(
                    Vector2.zero,
                    progressionDamageMultiplier: 1f,
                    new List<WeaponDefinition> { unarmed, airRifle },
                    plans,
                    out var weapon),
                Is.True);
            Assert.That(weapon.IsValid, Is.True);
            Assert.That(weapon.Weapon, Is.EqualTo(airRifle));
            Assert.That(weapon.Reason, Does.Contain("ranged"));
        }

        [Test]
        public void DefenderAiPlanner_RelocatesOnlyForMeaningfullyBetterRouteCoverage()
        {
            var chicken = Load<AttackerDefinition>("ChickenDefinition");
            var route = new[] { new Vector3(-5f, 0f), new Vector3(0f, 0f), new Vector3(5f, 0f) };
            var plans = new List<DefenderAiRoutePlan>
            {
                new(chicken, attackerCount: 5, route)
            };

            Assert.That(
                DefenderAiPlanner.TryChooseRelocation(
                    plans,
                    new List<Vector2> { new(-4f, 3f), Vector2.zero, new(4f, 3f) },
                    new List<DefenderAiCoverage>(),
                    currentPosition: new Vector2(-4f, 3f),
                    farmHandRange: 1.5f,
                    minimumImprovementMultiplier: 1.2f,
                    minimumMoveDistance: 0.4f,
                    out var relocation),
                Is.True);
            Assert.That(relocation.IsValid, Is.True);
            Assert.That(relocation.Position, Is.EqualTo(Vector2.zero));
            Assert.That(relocation.Score, Is.GreaterThan(relocation.CurrentScore * 1.2f));

            Assert.That(
                DefenderAiPlanner.TryChooseRelocation(
                    plans,
                    new List<Vector2> { new(0.1f, 0f), new(-0.1f, 0f) },
                    new List<DefenderAiCoverage>(),
                    currentPosition: Vector2.zero,
                    farmHandRange: 1.5f,
                    minimumImprovementMultiplier: 1.2f,
                    minimumMoveDistance: 0.4f,
                    out _),
                Is.False,
                "Near-identical positions should not make the AI shuffle a farmhand.");
        }

        [Test]
        public void PlayerProfileStore_MigratesLegacyAudioAndDisplaySettingsWithoutLosingPreferences()
        {
            var backup = CaptureProfileBackup();
            try
            {
                PlayerPrefs.SetString(
                    PlayerProfileKey,
                    "{\"version\":1,\"musicEnabled\":true,\"musicVolume\":0.35,\"videoQuality\":999,\"notificationsEnabled\":false,\"controlHintsEnabled\":false}");
                PlayerPrefs.Save();

                var profile = PlayerProfileStore.Load();

                Assert.That(profile.version, Is.EqualTo(PlayerProfileStore.CurrentVersion));
                Assert.That(profile.masterVolume, Is.EqualTo(0.35f).Within(0.001f));
                Assert.That(profile.attackerVolume, Is.EqualTo(1f));
                Assert.That(profile.defenderVolume, Is.EqualTo(1f));
                Assert.That(profile.projectileVolume, Is.EqualTo(1f));
                Assert.That(profile.ambientGameVolume, Is.EqualTo(1f));
                Assert.That(profile.showAttackerHealthBars, Is.True);
                Assert.That(profile.showDefenderExperience, Is.True);
                Assert.That(profile.notificationsEnabled, Is.False);
                Assert.That(profile.controlHintsEnabled, Is.False);
                Assert.That(profile.videoQuality, Is.InRange(0, Mathf.Max(0, QualitySettings.names.Length - 1)));
            }
            finally
            {
                RestoreProfileBackup(backup);
            }
        }

        [Test]
        public void PlayerProfileStore_PersistsAndSanitizesTheExpandedSettings()
        {
            var backup = CaptureProfileBackup();
            try
            {
                PlayerProfileStore.Save(new PlayerProfileData
                {
                    masterVolume = 2f,
                    attackerVolume = -1f,
                    defenderVolume = 0.25f,
                    projectileVolume = 1.5f,
                    ambientGameVolume = 0.6f,
                    showAttackerHealthBars = false,
                    showDefenderExperience = false,
                    notificationsEnabled = false,
                    controlHintsEnabled = false,
                    videoQuality = -20
                });

                var profile = PlayerProfileStore.Load();

                Assert.That(profile.masterVolume, Is.EqualTo(1f));
                Assert.That(profile.attackerVolume, Is.EqualTo(0f));
                Assert.That(profile.defenderVolume, Is.EqualTo(0.25f));
                Assert.That(profile.projectileVolume, Is.EqualTo(1f));
                Assert.That(profile.ambientGameVolume, Is.EqualTo(0.6f));
                Assert.That(profile.showAttackerHealthBars, Is.False);
                Assert.That(profile.showDefenderExperience, Is.False);
                Assert.That(profile.notificationsEnabled, Is.False);
                Assert.That(profile.controlHintsEnabled, Is.False);
                Assert.That(profile.videoQuality, Is.EqualTo(0));
            }
            finally
            {
                RestoreProfileBackup(backup);
            }
        }

        private static ProfileBackup CaptureProfileBackup()
        {
            return PlayerPrefs.HasKey(PlayerProfileKey)
                ? new ProfileBackup(true, PlayerPrefs.GetString(PlayerProfileKey))
                : new ProfileBackup(false, string.Empty);
        }

        private static void RestoreProfileBackup(ProfileBackup backup)
        {
            if (backup.Exists)
            {
                PlayerPrefs.SetString(PlayerProfileKey, backup.Value);
            }
            else
            {
                PlayerPrefs.DeleteKey(PlayerProfileKey);
            }

            PlayerPrefs.Save();
        }

        private readonly struct ProfileBackup
        {
            public ProfileBackup(bool exists, string value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public string Value { get; }
        }

        private static T Load<T>(string assetName) where T : Object
        {
            var asset = Resources.Load<T>("TADTests/" + assetName);
            Assert.That(asset, Is.Not.Null, "Missing deterministic game-test asset " + assetName + ".");
            return asset;
        }
    }
}
