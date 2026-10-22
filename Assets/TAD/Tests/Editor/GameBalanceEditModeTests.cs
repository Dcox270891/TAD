using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Environment;
using TAD.Waves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>
    /// Guards the authored production values that make a ten-day defence achievable without
    /// removing the attacker's meaningful archetype and tier choices.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameBalanceEditModeTests
    {
        private const string AttackerDataFolder = "Assets/TAD/Data/Attackers";
        private const string FoodStorePrefabPath = "Assets/TAD/Prefabs/Environment/FoodStore.prefab";
        private const string WaveControllerPrefabPath = "Assets/TAD/Prefabs/Waves/WaveController.prefab";
        private const string UnarmedWeaponPath = "Assets/TAD/Data/Defenders/UnarmedWeaponDefinition.asset";

        [Test]
        public void ProductionBalance_DefenderHasOpeningSafetyAndEveryArchetypeHasTenTiers()
        {
            var attackers = LoadProductionAttackers();
            var foodStore = LoadComponent<FoodStore>(FoodStorePrefabPath);
            var waveController = AssetDatabase.LoadAssetAtPath<GameObject>(WaveControllerPrefabPath);
            var unarmedWeapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(UnarmedWeaponPath);

            Assert.That(attackers, Has.Count.EqualTo(40));
            Assert.That(foodStore, Is.Not.Null);
            Assert.That(waveController, Is.Not.Null);
            Assert.That(unarmedWeapon, Is.Not.Null);

            AssertTenProgressiveTiers(attackers, AttackerArchetype.Basic);
            AssertTenProgressiveTiers(attackers, AttackerArchetype.Fast);
            AssertTenProgressiveTiers(attackers, AttackerArchetype.Strong);
            AssertTenProgressiveTiers(attackers, AttackerArchetype.Flying);

            for (var tier = 1; tier <= 10; tier++)
            {
                var basic = FindTier(attackers, AttackerArchetype.Basic, tier);
                var fast = FindTier(attackers, AttackerArchetype.Fast, tier);
                var strong = FindTier(attackers, AttackerArchetype.Strong, tier);
                var flying = FindTier(attackers, AttackerArchetype.Flying, tier);

                Assert.That(fast.MovementSpeed, Is.GreaterThan(basic.MovementSpeed),
                    $"Fast tier {tier} must remain quicker than Basic.");
                Assert.That(strong.MaximumHealth, Is.GreaterThan(basic.MaximumHealth),
                    $"Strong tier {tier} must remain tougher than Basic.");
                Assert.That(strong.MovementSpeed, Is.LessThan(basic.MovementSpeed),
                    $"Strong tier {tier} must remain slower than Basic.");
                Assert.That(flying.MovementSpeed, Is.GreaterThan(basic.MovementSpeed),
                    $"Flying tier {tier} must retain its route-skipping speed advantage.");
                Assert.That(flying.MaximumHealth, Is.LessThanOrEqualTo(basic.MaximumHealth),
                    $"Flying tier {tier} should trade durability for cover traversal.");
            }

            var banks = waveController.GetComponentsInChildren<ResourceBank>(true);
            var attackerBank = banks.Single(bank => bank.BankSide == BankSide.Attacker);
            var defenderBank = banks.Single(bank => bank.BankSide == BankSide.Defender);

            Assert.That(foodStore.StartingFood, Is.GreaterThanOrEqualTo(300f));
            Assert.That(attackerBank.StartingGold, Is.EqualTo(80));
            Assert.That(defenderBank.StartingGold, Is.EqualTo(150));
            Assert.That(defenderBank.StartingGold, Is.GreaterThan(attackerBank.StartingGold));
            Assert.That(defenderBank.StartingGold / unarmedWeapon.Cost, Is.GreaterThanOrEqualTo(6),
                "The defender should be able to establish a real opening line.");
            Assert.That(unarmedWeapon.Damage * unarmedWeapon.AttackSpeed,
                Is.EqualTo(6.25f).Within(0.0001f),
                "Opening defender damage is intentionally restrained so the late income curve can matter.");

            AssertAttackerEconomyAndExperience(attackers);

            var levelTenExperience = DefenderLevelProgression.GetTotalExperienceRequired(
                startingLevel: 1,
                targetLevel: 10,
                DefenderLevelProgression.DefaultLevelOneToTwoExperience);
            var chicken = FindTier(attackers, AttackerArchetype.Basic, 1);
            var soloFarmHandExperiencePerChickenGroup = DefenderRewardCalculator.Calculate(
                chicken.ExpGiven,
                chicken.SendSize,
                eligibleAssistCount: 0).TotalExperience * chicken.SendSize;
            Assert.That(soloFarmHandExperiencePerChickenGroup * 18, Is.GreaterThanOrEqualTo(levelTenExperience),
                "A focused farmhand must still reach level 10 against the larger number of lighter entry-level groups.");

            // Even if every animal reached the food untouched, spending the whole opening attacker
            // budget on the most food-efficient card cannot immediately deplete the store.
            var maximumOpeningFoodPressure = attackers
                .Where(definition => definition.SendCost <= attackerBank.StartingGold)
                .Max(definition =>
                    (attackerBank.StartingGold / definition.SendCost) *
                    definition.MaximumHealth *
                    definition.SendSize);
            Assert.That(maximumOpeningFoodPressure, Is.LessThan(foodStore.StartingFood));
        }

        [TestCase("Assets/Scenes/Levels/Level2.unity")]
        [TestCase("Assets/Scenes/Levels/Level3.unity")]
        [TestCase("Assets/Scenes/Levels/Level4.unity")]
        public void AuthoredLevelWavesKeepResolvedProductionAttackerDefinitions(string scenePath)
        {
            var productionAttackers = new HashSet<AttackerDefinition>(LoadProductionAttackers());
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                // Open the complete level by itself. Loading it additively beside the test runner's
                // current scene can register two URP 2D global lights and turn a valid data check
                // into an unrelated rendering error.
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var waveControllers = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<WaveController>(true))
                    .ToArray();
                Assert.That(waveControllers, Is.Not.Empty, scenePath + " needs a wave controller.");

                var configuredGroups = 0;
                foreach (var controller in waveControllers)
                {
                    var waves = new SerializedObject(controller).FindProperty("waves");
                    Assert.That(waves, Is.Not.Null, controller.name + " has no serialized waves collection.");
                    for (var waveIndex = 0; waveIndex < waves.arraySize; waveIndex++)
                    {
                        var groups = waves.GetArrayElementAtIndex(waveIndex)
                            .FindPropertyRelative("attackerGroups");
                        if (groups == null)
                        {
                            continue;
                        }

                        for (var groupIndex = 0; groupIndex < groups.arraySize; groupIndex++)
                        {
                            configuredGroups++;
                            var definition = groups.GetArrayElementAtIndex(groupIndex)
                                .FindPropertyRelative("attackerDefinition")
                                .objectReferenceValue as AttackerDefinition;
                            Assert.That(definition, Is.Not.Null,
                                $"{scenePath} wave {waveIndex + 1}, group {groupIndex + 1} lost its attacker definition.");
                            Assert.That(productionAttackers.Contains(definition), Is.True,
                                $"{scenePath} wave {waveIndex + 1}, group {groupIndex + 1} must reference the production roster.");
                        }
                    }
                }

                Assert.That(configuredGroups, Is.GreaterThan(0), scenePath + " needs at least one authored attacker group.");
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }
        }

        private static List<AttackerDefinition> LoadProductionAttackers()
        {
            return AssetDatabase.FindAssets("t:AttackerDefinition", new[] { AttackerDataFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AttackerDefinition>)
                .Where(definition => definition != null)
                .ToList();
        }

        private static void AssertTenProgressiveTiers(
            IReadOnlyCollection<AttackerDefinition> attackers,
            AttackerArchetype archetype)
        {
            var tiers = attackers
                .Where(definition => definition.Archetype == archetype)
                .OrderBy(definition => definition.ChallengeTier)
                .ToArray();

            Assert.That(tiers, Has.Length.EqualTo(10), $"{archetype} needs exactly ten challenge tiers.");
            CollectionAssert.AreEqual(Enumerable.Range(1, 10), tiers.Select(definition => definition.ChallengeTier));

            for (var index = 1; index < tiers.Length; index++)
            {
                Assert.That(tiers[index].MaximumHealth, Is.GreaterThan(tiers[index - 1].MaximumHealth));
                Assert.That(tiers[index].MovementSpeed, Is.GreaterThan(tiers[index - 1].MovementSpeed));
                Assert.That(tiers[index].SendCost, Is.GreaterThan(tiers[index - 1].SendCost));
                Assert.That(tiers[index].SendSize, Is.GreaterThanOrEqualTo(tiers[index - 1].SendSize));
                Assert.That(tiers[index].WaveIncome, Is.GreaterThan(tiers[index - 1].WaveIncome));
                Assert.That(tiers[index].ExpGiven, Is.GreaterThan(tiers[index - 1].ExpGiven));
                Assert.That(tiers[index].KillGain, Is.GreaterThan(tiers[index - 1].KillGain));
            }
        }

        private static void AssertAttackerEconomyAndExperience(IReadOnlyCollection<AttackerDefinition> attackers)
        {
            var expectedIds = Enumerable.Range(1, 40)
                .Select(value => value.ToString("D3"))
                .ToArray();
            CollectionAssert.AreEqual(
                expectedIds,
                attackers.Select(definition => definition.AttackerId).OrderBy(id => id));
            Assert.That(attackers.Select(definition => definition.DisplayName).Distinct().Count(), Is.EqualTo(40));

            var displaySizeBuckets = new[] { 0.75f, 1f, 1.25f, 1.5f, 1.75f };
            var routeCacheSizes = new[] { 0.225f, 0.3f, 0.375f, 0.45f, 0.525f };
            Assert.That(attackers.Select(definition => definition.Size).Distinct().Count(), Is.LessThanOrEqualTo(5));

            foreach (var definition in attackers)
            {
                Assert.That(definition.MaximumHealth, Is.GreaterThan(0f), definition.DisplayName + " health");
                Assert.That(definition.MovementSpeed, Is.GreaterThan(0f), definition.DisplayName + " speed");
                Assert.That(definition.SendCost, Is.GreaterThan(0), definition.DisplayName + " send cost");
                Assert.That(definition.WaveIncome, Is.GreaterThan(0), definition.DisplayName + " wave income");
                Assert.That(definition.ExpGiven, Is.GreaterThan(0), definition.DisplayName + " experience reward");
                Assert.That(definition.KillGain, Is.GreaterThan(0), definition.DisplayName + " kill reward");
                Assert.That(definition.SendSize, Is.GreaterThan(0), definition.DisplayName + " send size");
                Assert.That(displaySizeBuckets.Any(size => Mathf.Approximately(size, definition.Size)), Is.True,
                    definition.DisplayName + " must use a shared display-size bucket.");
                Assert.That(routeCacheSizes.Any(size => Mathf.Approximately(size, definition.NavigationDiameter)), Is.True,
                    definition.DisplayName + " must use a shared route-clearance bucket.");
            }

            Assert.That(FindTier(attackers, AttackerArchetype.Fast, 10).DisplayName, Is.EqualTo("Ostrich"));
            Assert.That(attackers.Any(definition =>
                definition.DisplayName is "Crow" or "Hawk" or "Eagle"), Is.False,
                "Wild birds were replaced by farm-raised flying animals in the playable roster.");
        }

        private static AttackerDefinition FindTier(
            IReadOnlyCollection<AttackerDefinition> attackers,
            AttackerArchetype archetype,
            int tier)
        {
            return attackers.Single(definition =>
                definition.Archetype == archetype &&
                definition.ChallengeTier == tier);
        }

        private static T LoadComponent<T>(string prefabPath) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefab != null ? prefab.GetComponent<T>() : null;
        }
    }
}
