using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Environment;
using UnityEditor;
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
        public void ProductionBalance_DefenderHasOpeningSafetyAndEveryArchetypeHasThreeTiers()
        {
            var attackers = LoadProductionAttackers();
            var foodStore = LoadComponent<FoodStore>(FoodStorePrefabPath);
            var waveController = AssetDatabase.LoadAssetAtPath<GameObject>(WaveControllerPrefabPath);
            var unarmedWeapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(UnarmedWeaponPath);

            Assert.That(attackers, Has.Count.EqualTo(12));
            Assert.That(foodStore, Is.Not.Null);
            Assert.That(waveController, Is.Not.Null);
            Assert.That(unarmedWeapon, Is.Not.Null);

            AssertThreeProgressiveTiers(attackers, AttackerArchetype.Basic);
            AssertThreeProgressiveTiers(attackers, AttackerArchetype.Fast);
            AssertThreeProgressiveTiers(attackers, AttackerArchetype.Strong);
            AssertThreeProgressiveTiers(attackers, AttackerArchetype.Flying);

            for (var tier = 1; tier <= 3; tier++)
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
            var maximumOpeningFoodPressure = attackerBank.StartingGold * attackers.Max(
                definition => definition.MaximumHealth * definition.SendSize / definition.SendCost);
            Assert.That(maximumOpeningFoodPressure, Is.LessThan(foodStore.StartingFood));
        }

        private static List<AttackerDefinition> LoadProductionAttackers()
        {
            return AssetDatabase.FindAssets("t:AttackerDefinition", new[] { AttackerDataFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AttackerDefinition>)
                .Where(definition => definition != null)
                .ToList();
        }

        private static void AssertThreeProgressiveTiers(
            IReadOnlyCollection<AttackerDefinition> attackers,
            AttackerArchetype archetype)
        {
            var tiers = attackers
                .Where(definition => definition.Archetype == archetype)
                .OrderBy(definition => definition.ChallengeTier)
                .ToArray();

            Assert.That(tiers, Has.Length.EqualTo(3), $"{archetype} needs exactly three challenge tiers.");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, tiers.Select(definition => definition.ChallengeTier));

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
            var expectedValues = new Dictionary<string, (int Cost, int Income, int Experience)>
            {
                ["Chicken"] = (6, 1, 2),
                ["Duck"] = (11, 2, 3),
                ["Ram"] = (17, 4, 5),
                ["Hare"] = (7, 1, 2),
                ["Fox"] = (12, 2, 3),
                ["Greyhound"] = (19, 4, 5),
                ["Boar"] = (11, 2, 2),
                ["Bull"] = (18, 4, 4),
                ["Bison"] = (28, 6, 6),
                ["Crow"] = (8, 1, 2),
                ["Hawk"] = (14, 3, 4),
                ["Eagle"] = (22, 5, 6)
            };

            Assert.That(attackers.Select(definition => definition.DisplayName),
                Is.EquivalentTo(expectedValues.Keys));
            foreach (var definition in attackers)
            {
                var expected = expectedValues[definition.DisplayName];
                Assert.That(definition.SendCost, Is.EqualTo(expected.Cost), definition.DisplayName + " send cost");
                Assert.That(definition.WaveIncome, Is.EqualTo(expected.Income), definition.DisplayName + " wave income");
                Assert.That(definition.ExpGiven, Is.EqualTo(expected.Experience), definition.DisplayName + " experience reward");
            }
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
