using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Match;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>
    /// Locks the intended final-match curve: a defend-favoured opening, more smaller attacking
    /// groups, and a late swing created by recurring attacker income rather than raw early stats.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class FinalBalanceEditModeTests
    {
        private const string AttackerFolder = "Assets/TAD/Data/Attackers";
        private const string DefenderFolder = "Assets/TAD/Data/Defenders";
        private const string ProjectileFolder = DefenderFolder + "/Projectiles";
        private const string FinalFolder = DefenderFolder + "/FinalUpgrades";
        private const string FinalProjectileFolder = FinalFolder + "/Projectiles";
        private const string WaveControllerPath = "Assets/TAD/Prefabs/Waves/WaveController.prefab";

        [Test]
        public void TenDayCurve_UsesMoreGroupsAndTurnsIncomeIntoLatePressure()
        {
            var waveController = AssetDatabase.LoadAssetAtPath<GameObject>(WaveControllerPath);
            var roleController = waveController != null
                ? waveController.GetComponent<MatchRoleController>()
                : null;
            var chicken = LoadAttacker("Chicken");

            Assert.That(roleController, Is.Not.Null);
            Assert.That(chicken, Is.Not.Null);

            var serializedRole = new SerializedObject(roleController);
            var baseGroups = serializedRole.FindProperty("baseAttackerGroupsPerDay").intValue;
            var additionalGroupEveryDays = serializedRole.FindProperty("attackerAiAdditionalGroupEveryDays").intValue;
            var dailyGroups = Enumerable.Range(0, 10)
                .Select(day => baseGroups + (day / additionalGroupEveryDays))
                .ToArray();

            CollectionAssert.AreEqual(new[] { 2, 2, 2, 3, 3, 3, 4, 4, 4, 5 }, dailyGroups);
            Assert.That(dailyGroups.Sum(), Is.EqualTo(32),
                "The final match should dispatch substantially more than the former eighteen AI groups.");
            for (var day = 0; day < 10; day++)
            {
                Assert.That(
                    AttackerWavePlanner.GetMaximumChallengeTierForAiDay(day),
                    Is.EqualTo(day + 1),
                    "The ten-day AI curve should unlock its matching roster tier on each day.");
            }

            var goldAfterEachDay = SimulateRecurringIncome(
                startingGold: 80,
                definition: chicken,
                dailyGroups: dailyGroups,
                out var finalIncome);

            Assert.That(finalIncome, Is.EqualTo(dailyGroups.Sum() * (chicken.SendSize + 1) * chicken.WaveIncome));
            Assert.That(finalIncome, Is.GreaterThan(dailyGroups[dailyGroups.Length - 1] * chicken.SendCost),
                "By the final day recurring income must cover another entry-level five-group attack.");
            Assert.That(goldAfterEachDay[8], Is.GreaterThan(goldAfterEachDay[7]),
                "The cheapest safe investment should turn cash-positive only in the late game.");
            Assert.That(goldAfterEachDay[9], Is.GreaterThan(goldAfterEachDay[8]));
        }

        [Test]
        public void FinalWeaponAndProjectileBalance_IsFullyAuthoredAndBounded()
        {
            var expectedWeapons = new Dictionary<string, (int Cost, float Range, float Speed, float Damage, int Magazine, float Reload)>
            {
                [DefenderFolder + "/UnarmedWeaponDefinition.asset"] = (25, 1.15f, 1.25f, 5f, 1, 0f),
                [DefenderFolder + "/StickWeaponDefinition.asset"] = (0, 1.55f, 1.35f, 6.5f, 1, 0f),
                [DefenderFolder + "/StoneWeaponDefinition.asset"] = (0, 4.2f, 0.85f, 6f, 1, 0f),
                [DefenderFolder + "/PoleWeaponDefinition.asset"] = (34, 1.75f, 1.1f, 9.5f, 1, 0f),
                [DefenderFolder + "/RollingWeaponDefinition.asset"] = (42, 2.35f, 0.7f, 12f, 1, 0f),
                [DefenderFolder + "/TrapperWeaponDefinition.asset"] = (36, 2.1f, 0.72f, 9f, 1, 0f),
                [DefenderFolder + "/AirRifleWeaponDefinition.asset"] = (55, 6.8f, 1.2f, 15f, 1, 1.25f),
                [FinalFolder + "/RakeWeaponDefinition.asset"] = (70, 2.1f, 1.2f, 13f, 1, 0f),
                [FinalFolder + "/ShovelWeaponDefinition.asset"] = (76, 1.95f, 0.95f, 18f, 1, 0f),
                [FinalFolder + "/HeavyRollWeaponDefinition.asset"] = (86, 3.1f, 0.7f, 22f, 1, 0f),
                [FinalFolder + "/BounceWeaponDefinition.asset"] = (90, 2.85f, 0.45f, 8f, 1, 0f),
                [FinalFolder + "/SemiAutomaticWeaponDefinition.asset"] = (104, 7.5f, 3.8f, 13f, 5, 2.5f),
                [FinalFolder + "/PistolWeaponDefinition.asset"] = (100, 6.9f, 1.8f, 14f, 8, 0.8f),
                [FinalFolder + "/PoisonTrapperWeaponDefinition.asset"] = (75, 2.5f, 0.65f, 8f, 1, 0f),
                [FinalFolder + "/SlowTrapperWeaponDefinition.asset"] = (78, 2.7f, 0.7f, 9f, 1, 0f)
            };

            foreach (var pair in expectedWeapons)
            {
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(pair.Key);
                Assert.That(weapon, Is.Not.Null, pair.Key);
                Assert.That(weapon.Cost, Is.EqualTo(pair.Value.Cost), pair.Key + " cost");
                Assert.That(weapon.Range, Is.EqualTo(pair.Value.Range).Within(0.0001f), pair.Key + " range");
                Assert.That(weapon.AttackSpeed, Is.EqualTo(pair.Value.Speed).Within(0.0001f), pair.Key + " attack speed");
                Assert.That(weapon.Damage, Is.EqualTo(pair.Value.Damage).Within(0.0001f), pair.Key + " damage");
                Assert.That(weapon.ShotsPerMagazine, Is.EqualTo(pair.Value.Magazine), pair.Key + " magazine");
                Assert.That(weapon.ReloadDuration, Is.EqualTo(pair.Value.Reload).Within(0.0001f), pair.Key + " reload");
            }

            var expectedProjectiles = new Dictionary<string, (float Speed, float Lifetime, int Cap)>
            {
                [ProjectileFolder + "/UnarmedHandProjectileDefinition.asset"] = (8f, 0.18f, 0),
                [ProjectileFolder + "/StickProjectileDefinition.asset"] = (9f, 0.25f, 0),
                [ProjectileFolder + "/StoneProjectileDefinition.asset"] = (9f, 0.55f, 0),
                [ProjectileFolder + "/PoleSweepProjectileDefinition.asset"] = (7f, 0.8f, 1),
                [ProjectileFolder + "/RollingProjectileDefinition.asset"] = (3.5f, 3.2f, 1),
                [ProjectileFolder + "/TrapProjectileDefinition.asset"] = (11f, 0f, 2),
                [ProjectileFolder + "/AirRifleBulletProjectileDefinition.asset"] = (16f, 0.55f, 0),
                [FinalProjectileFolder + "/RakeProjectileDefinition.asset"] = (7f, 0.8f, 1),
                [FinalProjectileFolder + "/ShovelProjectileDefinition.asset"] = (6.5f, 0.75f, 1),
                [FinalProjectileFolder + "/HeavyRollProjectileDefinition.asset"] = (4.1f, 4f, 1),
                [FinalProjectileFolder + "/BounceProjectileDefinition.asset"] = (2.5f, 3f, 1),
                [FinalProjectileFolder + "/PoisonTrapProjectileDefinition.asset"] = (11f, 0f, 2),
                [FinalProjectileFolder + "/SlowTrapProjectileDefinition.asset"] = (11f, 0f, 2),
                [FinalProjectileFolder + "/SemiAutomaticBulletProjectileDefinition.asset"] = (24f, 0.4f, 0),
                [FinalProjectileFolder + "/PistolBulletProjectileDefinition.asset"] = (22f, 0.42f, 0)
            };

            foreach (var pair in expectedProjectiles)
            {
                var projectile = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(pair.Key);
                Assert.That(projectile, Is.Not.Null, pair.Key);
                Assert.That(projectile.ProjectileSpeed, Is.EqualTo(pair.Value.Speed).Within(0.0001f), pair.Key + " speed");
                Assert.That(projectile.ProjectileLifetime, Is.EqualTo(pair.Value.Lifetime).Within(0.0001f), pair.Key + " lifetime");
                Assert.That(projectile.MaximumActiveProjectiles, Is.EqualTo(pair.Value.Cap), pair.Key + " active cap");
            }

            var unarmed = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DefenderFolder + "/UnarmedWeaponDefinition.asset");
            var rolling = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(ProjectileFolder + "/RollingProjectileDefinition.asset");
            var trap = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(ProjectileFolder + "/TrapProjectileDefinition.asset");
            var rake = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileFolder + "/RakeProjectileDefinition.asset");
            var shovel = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileFolder + "/ShovelProjectileDefinition.asset");
            var bounce = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileFolder + "/BounceProjectileDefinition.asset");
            var poisonTrap = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileFolder + "/PoisonTrapProjectileDefinition.asset");
            var slowTrap = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileFolder + "/SlowTrapProjectileDefinition.asset");

            Assert.That(unarmed.ProjectileDefinition.CanTargetFlying, Is.True);
            Assert.That(rolling.InitialSpeedMultiplier, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(rolling.RollingAcceleration, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(trap.ImpactAreaRadius, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(trap.ImpactAreaDamage, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(rake.BleedDamagePerSecond, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(rake.BleedDuration, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(shovel.StunDuration, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(bounce.ProjectileBehavior, Is.EqualTo(ProjectileBehavior.Bouncing));
            Assert.That(bounce.ImpactAreaRadius, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(bounce.ImpactAreaDamage, Is.EqualTo(0f),
                "Bounce damage must come from the launched weapon damage so level scaling applies.");
            Assert.That(poisonTrap.PoisonDamagePerSecond, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(poisonTrap.PoisonDuration, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(slowTrap.ImpactAreaDamage, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(slowTrap.SlowMovementMultiplier, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(slowTrap.SlowDuration, Is.EqualTo(1.8f).Within(0.0001f));
        }

        private static AttackerDefinition LoadAttacker(string name)
        {
            return AssetDatabase.LoadAssetAtPath<AttackerDefinition>(
                AttackerFolder + "/" + name + "Definition.asset");
        }

        private static IReadOnlyList<int> SimulateRecurringIncome(
            int startingGold,
            AttackerDefinition definition,
            IReadOnlyList<int> dailyGroups,
            out int recurringIncome)
        {
            var gold = startingGold;
            recurringIncome = 0;
            var balances = new List<int>(dailyGroups.Count);
            for (var day = 0; day < dailyGroups.Count; day++)
            {
                for (var group = 0; group < dailyGroups[day]; group++)
                {
                    Assert.That(gold, Is.GreaterThanOrEqualTo(definition.SendCost),
                        $"Entry-level attacker groups must remain affordable on day {day}.");
                    gold -= definition.SendCost;
                    // Selecting a card immediately adds recurring income. A group refunds its
                    // purchase cost once when its first animal reaches the Food Store, then every
                    // arriving animal contributes the same income again.
                    recurringIncome += definition.WaveIncome;
                    gold += definition.SendCost;
                    recurringIncome += definition.WaveIncome * definition.SendSize;
                }

                // ResourceBank.PayIncome pays the full stored income after every resolved day.
                gold += recurringIncome;
                balances.Add(gold);
            }

            return balances;
        }
    }
}
