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
            Assert.That(defenderBank.StartingGold, Is.GreaterThan(attackerBank.StartingGold));
            Assert.That(defenderBank.StartingGold / unarmedWeapon.Cost, Is.GreaterThanOrEqualTo(6),
                "The defender should be able to establish a real opening line.");
            Assert.That(unarmedWeapon.Damage * unarmedWeapon.AttackSpeed, Is.GreaterThanOrEqualTo(12f));

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
