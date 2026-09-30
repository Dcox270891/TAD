using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Environment;
using TAD.Match;
using TAD.Navigation;
using TAD.Progression;
using UnityEditor;
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
        public void UnarmedProjectileDefinition_IsTheOnlyMeleeWeaponConfiguredToTargetFlyingAttackers()
        {
            var unarmed = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/UnarmedWeaponDefinition.asset");
            var stick = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/StickWeaponDefinition.asset");
            var pole = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/PoleWeaponDefinition.asset");

            Assert.That(unarmed.ProjectileDefinition.ProjectileType, Is.EqualTo(ProjectileType.Melee));
            Assert.That(unarmed.ProjectileDefinition.CanTargetFlying, Is.True);
            Assert.That(stick.ProjectileDefinition.ProjectileType, Is.EqualTo(ProjectileType.Melee));
            Assert.That(stick.ProjectileDefinition.CanTargetFlying, Is.False);
            Assert.That(pole.ProjectileDefinition.ProjectileType, Is.EqualTo(ProjectileType.Melee));
            Assert.That(pole.ProjectileDefinition.CanTargetFlying, Is.False);
        }

        [Test]
        public void TerrainSpeedPrefabs_AreEnvironmentTriggersWithExpectedModifiers()
        {
            var mudPrefab = LoadAsset<GameObject>("Assets/TAD/Prefabs/Environment/Mud.prefab");
            var icePrefab = LoadAsset<GameObject>("Assets/TAD/Prefabs/Environment/Ice.prefab");
            var environmentLayer = LayerMask.NameToLayer("Environment");

            Assert.That(environmentLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(mudPrefab.layer, Is.EqualTo(environmentLayer));
            Assert.That(icePrefab.layer, Is.EqualTo(environmentLayer));

            var mud = mudPrefab.GetComponent<TerrainSpeedModifier>();
            var ice = icePrefab.GetComponent<TerrainSpeedModifier>();
            Assert.That(mud, Is.Not.Null);
            Assert.That(ice, Is.Not.Null);
            Assert.That(mudPrefab.GetComponent<BoxCollider2D>().isTrigger, Is.True);
            Assert.That(icePrefab.GetComponent<BoxCollider2D>().isTrigger, Is.True);
            Assert.That(mud.TerrainType, Is.EqualTo(TerrainSpeedEffect.Mud));
            Assert.That(mud.MovementSpeedMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(ice.TerrainType, Is.EqualTo(TerrainSpeedEffect.Ice));
            Assert.That(ice.MovementSpeedMultiplier, Is.EqualTo(1.1f).Within(0.0001f));
        }

        [Test]
        public void DefenderAiPlanner_PrefersAPlacedFarmhandCoveringTheQueuedMudCrossing()
        {
            var chicken = Load<AttackerDefinition>("ChickenDefinition");
            var route = new[]
            {
                new Vector3(-5f, 0f),
                new Vector3(-3f, 0f),
                new Vector3(-1f, 0f),
                new Vector3(1f, 0f),
                new Vector3(3f, 0f),
                new Vector3(5f, 0f)
            };
            // The middle two world units represent a 75%-speed Mud crossing. The geometry is
            // intentionally symmetric, so the centre can only win when the planner uses time in range.
            var timing = new FarmRouteTiming(route, new[] { 2f, 2f, 2f / 0.75f, 2f, 2f });
            var plans = new List<DefenderAiRoutePlan>
            {
                new(chicken, attackerCount: 5, route, timing)
            };

            Assert.That(
                DefenderAiPlanner.TryChoosePlacement(
                    plans,
                    new List<Vector2> { new(-4f, 0f), Vector2.zero },
                    new List<DefenderAiCoverage>(),
                    farmHandRange: 0.75f,
                    out var decision),
                Is.True);

            Assert.That(decision.Position, Is.EqualTo(Vector2.zero),
                "A Mud-covered route segment should be worth more defender coverage time than an equal dry segment.");
        }

        [Test]
        public void AttackerAiCandidate_UsesTerrainTimingForDefenderExposure()
        {
            var chicken = Load<AttackerDefinition>("ChickenDefinition");
            var route = new[]
            {
                new Vector3(-2f, 0f),
                new Vector3(0f, 0f),
                new Vector3(2f, 0f)
            };
            var dryCandidate = new AttackerAiCandidate(
                chicken,
                route,
                new FarmRouteTiming(route, new[] { 2f, 2f }));
            var iceCandidate = new AttackerAiCandidate(
                chicken,
                route,
                new FarmRouteTiming(route, new[] { 2f / 1.1f, 2f / 1.1f }));

            Assert.That(
                iceCandidate.GetTravelTimeInsideRange(Vector2.zero, 5f),
                Is.LessThan(dryCandidate.GetTravelTimeInsideRange(Vector2.zero, 5f)),
                "The attacker planner must see the reduced defender exposure created by an Ice route.");
        }

        [Test]
        public void DefenderAiPlanner_ChoosesUnarmedOverStandardMeleeAgainstFlyingAttackers()
        {
            var flying = Load<AttackerDefinition>("EagleDefinition");
            var unarmed = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/UnarmedWeaponDefinition.asset");
            var stick = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/StickWeaponDefinition.asset");
            var route = new[] { new Vector3(-2f, 0f), new Vector3(2f, 0f) };
            var plans = new List<DefenderAiRoutePlan>
            {
                new(flying, attackerCount: 1, route)
            };

            Assert.That(
                DefenderAiPlanner.TryChooseWeapon(
                    Vector2.zero,
                    progressionDamageMultiplier: 1f,
                    new List<WeaponDefinition> { stick, unarmed },
                    plans,
                    out var weapon),
                Is.True);

            Assert.That(weapon.IsValid, Is.True);
            Assert.That(weapon.Weapon, Is.EqualTo(unarmed));
            Assert.That(weapon.Reason, Does.Contain("airborne-capable"));
        }

        [Test]
        public void FinalLevelEightWeaponAssets_HaveExactParentsAndConfiguredGameplayEffects()
        {
            var pole = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/PoleWeaponDefinition.asset");
            var rolling = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/RollingWeaponDefinition.asset");
            var airRifle = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/AirRifleWeaponDefinition.asset");
            var trapper = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/TrapperWeaponDefinition.asset");
            var rake = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/RakeWeaponDefinition.asset");
            var shovel = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/ShovelWeaponDefinition.asset");
            var heavyRoll = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/HeavyRollWeaponDefinition.asset");
            var bounce = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/BounceWeaponDefinition.asset");
            var semiAutomatic = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/SemiAutomaticWeaponDefinition.asset");
            var pistol = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/PistolWeaponDefinition.asset");
            var poisonTrapper = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/PoisonTrapperWeaponDefinition.asset");
            var slowTrapper = LoadAsset<WeaponDefinition>("Assets/TAD/Data/Defenders/FinalUpgrades/SlowTrapperWeaponDefinition.asset");

            Assert.That(pole.UnlocksFinalEvolution, Is.True);
            Assert.That(rolling.UnlocksFinalEvolution, Is.True);
            Assert.That(airRifle.UnlocksFinalEvolution, Is.True);
            Assert.That(trapper.UnlocksFinalEvolution, Is.True);
            Assert.That(rake.RequiredPreviousWeapon, Is.EqualTo(pole));
            Assert.That(shovel.RequiredPreviousWeapon, Is.EqualTo(pole));
            Assert.That(heavyRoll.RequiredPreviousWeapon, Is.EqualTo(rolling));
            Assert.That(bounce.RequiredPreviousWeapon, Is.EqualTo(rolling));
            Assert.That(semiAutomatic.RequiredPreviousWeapon, Is.EqualTo(airRifle));
            Assert.That(pistol.RequiredPreviousWeapon, Is.EqualTo(airRifle));
            Assert.That(poisonTrapper.RequiredPreviousWeapon, Is.EqualTo(trapper));
            Assert.That(slowTrapper.RequiredPreviousWeapon, Is.EqualTo(trapper));
            Assert.That(rake.ProjectileDefinition.BleedDuration, Is.GreaterThan(0f));
            Assert.That(shovel.ProjectileDefinition.StunDuration, Is.GreaterThan(0f));
            Assert.That(heavyRoll.ProjectileDefinition.ProjectileScaleMultiplier, Is.GreaterThan(1f));
            Assert.That(bounce.ProjectileDefinition.ProjectileBehavior, Is.EqualTo(ProjectileBehavior.Bouncing));
            Assert.That(bounce.ProjectileDefinition.ImpactAreaRadius, Is.GreaterThan(0f));
            Assert.That(semiAutomatic.ShotsPerMagazine, Is.GreaterThan(1));
            Assert.That(semiAutomatic.ReloadDuration, Is.GreaterThan(pistol.ReloadDuration));
            Assert.That(poisonTrapper.ProjectileDefinition.PoisonDuration, Is.GreaterThan(0f));
            Assert.That(slowTrapper.ProjectileDefinition.SlowDuration, Is.GreaterThan(0f));
        }

        [Test]
        public void FarmHandProjectile_CanBeConstructedInEditMode()
        {
            var projectileObject = new GameObject("EditorProjectileConstructionTest");
            try
            {
                Assert.DoesNotThrow(() => projectileObject.AddComponent<FarmHandProjectile>());
                Assert.That(projectileObject.GetComponent<FarmHandProjectile>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(projectileObject);
            }
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
                Assert.That(profile.playerLevel, Is.EqualTo(PlayerLevelProgression.StartingLevel));
                Assert.That(profile.playerExp, Is.EqualTo(0f));
                Assert.That(profile.totalPlayerExperience, Is.EqualTo(0f));
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
        public void PlayerProfileStore_MigratesVersionTwoProfilesToAccountProgressionDefaults()
        {
            var backup = CaptureProfileBackup();
            try
            {
                PlayerPrefs.SetString(
                    PlayerProfileKey,
                    "{\"version\":2,\"masterVolume\":0.4,\"attackerVolume\":0.6,\"showAttackerHealthBars\":false}");
                PlayerPrefs.Save();

                var profile = PlayerProfileStore.Load();

                Assert.That(profile.version, Is.EqualTo(PlayerProfileStore.CurrentVersion));
                Assert.That(profile.masterVolume, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(profile.attackerVolume, Is.EqualTo(0.6f).Within(0.001f));
                Assert.That(profile.showAttackerHealthBars, Is.False);
                Assert.That(profile.playerLevel, Is.EqualTo(PlayerLevelProgression.StartingLevel));
                Assert.That(profile.playerExp, Is.EqualTo(0f));
                Assert.That(profile.totalPlayerExperience, Is.EqualTo(0f));
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
                    playerExp = 24.56f,
                    playerLevel = 4,
                    totalPlayerExperience = 456.78f,
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
                Assert.That(profile.playerExp, Is.EqualTo(24.6f));
                Assert.That(profile.playerLevel, Is.EqualTo(4));
                Assert.That(profile.totalPlayerExperience, Is.EqualTo(456.8f));
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

        private static T LoadAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, "Missing authored asset " + path + ".");
            return asset;
        }
    }
}
