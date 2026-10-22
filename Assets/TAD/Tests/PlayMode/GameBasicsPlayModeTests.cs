using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Environment;
using TAD.Match;
using TAD.Navigation;
using TAD.Pooling;
using TAD.Progression;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameBasicsPlayModeTests
    {
        private float originalMaximumDeltaTime;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalMaximumDeltaTime = Time.maximumDeltaTime;
            Time.timeScale = 1f;
            MatchRoleSelection.Select(PlayerRole.Defender);
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
            // The production profile is persistent, so override it with a clean in-memory one.
            // This keeps progression multipliers out of deterministic combat expectations.
            PlayerSettingsService.SetEditorTestProfileOverride(new PlayerProfileData());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.maximumDeltaTime = originalMaximumDeltaTime;
            Time.timeScale = 1f;
            MatchRoleSelection.Select(PlayerRole.Defender);
            PlayerSettingsService.ClearEditorTestProfileOverride();
            yield return GameBasicsTestSupport.ResetToIsolatedTestScene();
        }

        [UnityTest]
        public IEnumerator FoodStore_DepletesAndShowsAttackerWinOverlay()
        {
            var foodStore = GameBasicsTestSupport.CreateFoodStore();
            var overlay = GameBasicsTestSupport.CreateAttackerWinOverlay();
            overlay.Bind(foodStore);

            Assert.That(foodStore.ConsumeFood(foodStore.CurrentFood), Is.EqualTo(foodStore.StartingFood));
            yield return null;

            Assert.That(foodStore.IsDepleted, Is.True);
            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator DefenderCombat_AwardsAssistsKillExperienceAndResources()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var assistant = GameBasicsTestSupport.CreateFarmHand();
            var killer = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);

            Assert.That(assistant.TryDamageAttacker(attacker, 1f), Is.True);
            Assert.That(killer.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            yield return null;

            Assert.That(assistant.TotalExperience, Is.EqualTo(1f));
            Assert.That(killer.TotalExperience, Is.EqualTo(9f));
            Assert.That(killer.TotalExperience, Is.GreaterThanOrEqualTo(assistant.TotalExperience));
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(defenderBank.StartingGold + attackerDefinition.KillGain));
            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator DayCycle_TracksCombinedDefenderExperienceFromFarmHandCombat()
        {
            GameBasicsTestSupport.CreateFoodStore();
            var dayCycle = GameBasicsTestSupport.CreateDayCycle();
            yield return null;

            GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);

            Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            yield return null;

            Assert.That(defender.TotalExperience, Is.GreaterThan(0f));
            Assert.That(dayCycle.DefenderExperienceEarned, Is.EqualTo(defender.TotalExperience));
            Assert.That(dayCycle.PlayerMatchExperience, Is.EqualTo(defender.TotalExperience));

            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator Defenders_UseProgressiveExperienceChooseAtLevelTwoAndStopAtLevelTen()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var upgrade = Resources.Load<WeaponDefinition>("TADTests/UnarmedWeaponDefinition");
            Assert.That(upgrade, Is.Not.Null);
            defender.SetWeapon(upgrade);
            var baseScale = defender.transform.localScale;

            var firstAttacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(defender.TryDamageAttacker(firstAttacker, firstAttacker.RemainingHealth), Is.True);

            yield return null;
            Assert.That(defender.CurrentLevel, Is.EqualTo(2));
            Assert.That(defender.CurrentExp, Is.EqualTo(0f));
            Assert.That(defender.ExpMax, Is.EqualTo(12));
            Assert.That(defender.IsAwaitingWeaponUpgrade, Is.True);
            Assert.That(defender.PendingWeaponUpgradeLevel, Is.EqualTo(2));
            Assert.That(defender.IsFreeWeaponUpgradePending, Is.True);
            Assert.That(defender.Range, Is.EqualTo(upgrade.Range * 1.035f).Within(0.0001f));
            Assert.That(defender.Damage, Is.EqualTo(upgrade.Damage * 1.07f).Within(0.0001f));
            Assert.That(defender.transform.localScale.x, Is.EqualTo(baseScale.x * 1.05f).Within(0.0001f));
            var goldBeforeFreeUpgrade = defenderBank.CurrentGold;
            Assert.That(defender.TryChooseWeaponUpgrade(upgrade), Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeFreeUpgrade));
            Assert.That(defender.WeaponDefinition, Is.EqualTo(upgrade));
            Assert.That(defender.HasSelectedWeaponUpgrade, Is.True);

            var secondAttacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(defender.TryDamageAttacker(secondAttacker, secondAttacker.RemainingHealth), Is.True);
            Assert.That(defender.CurrentLevel, Is.EqualTo(2));
            Assert.That(defender.CurrentExp, Is.EqualTo(10f));

            var thirdAttacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(defender.TryDamageAttacker(thirdAttacker, thirdAttacker.RemainingHealth), Is.True);
            Assert.That(defender.CurrentLevel, Is.EqualTo(3));
            Assert.That(defender.CurrentExp, Is.EqualTo(8f));
            Assert.That(defender.ExpMax, Is.EqualTo(15));

            var levelFiveSafetyCounter = 0;
            while (defender.CurrentLevel < 5 && levelFiveSafetyCounter++ < 20)
            {
                var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
                Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            }

            Assert.That(defender.CurrentLevel, Is.EqualTo(5));
            Assert.That(defender.IsAwaitingWeaponUpgrade, Is.True);
            Assert.That(defender.PendingWeaponUpgradeLevel, Is.EqualTo(5));
            Assert.That(defender.IsPaidWeaponUpgradePending, Is.True);
            var goldBeforeEvolution = defenderBank.CurrentGold;
            Assert.That(defender.TryChooseWeaponUpgrade(upgrade), Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeEvolution - upgrade.Cost));
            Assert.That(defender.HasSelectedWeaponEvolution, Is.True);

            var safetyCounter = 0;
            while (!defender.IsAtMaxLevel && safetyCounter++ < 30)
            {
                var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
                Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            }

            Assert.That(defender.CurrentLevel, Is.EqualTo(defender.MaxLevel));
            Assert.That(defender.IsAtMaxLevel, Is.True);
            Assert.That(defender.CurrentExp, Is.EqualTo(0f));
            Assert.That(defender.TotalExperience, Is.GreaterThanOrEqualTo(
                DefenderLevelProgression.GetTotalExperienceRequired(
                    startingLevel: 1,
                    targetLevel: defender.MaxLevel,
                    defender.BaseExperienceRequirement)));

            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator DefenderFinalEvolution_RequiresTheExactLevelFiveParentAndChargesAtLevelEight()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender, startingGold: 1000);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var projectile = ScriptableObject.CreateInstance<ProjectileDefinition>();
            var baseWeapon = CreateTestWeapon("Base", projectile);
            var levelTwoWeapon = CreateTestWeapon("Level Two", projectile);
            var levelFiveWeapon = CreateTestWeapon("Level Five", projectile, cost: 20, unlocksFinalEvolution: true);
            var otherLevelFiveWeapon = CreateTestWeapon("Other Level Five", projectile, unlocksFinalEvolution: true);
            var wrongFinalWeapon = CreateTestWeapon("Wrong Final", projectile, cost: 55, requiredPreviousWeapon: otherLevelFiveWeapon);
            var validFinalWeapon = CreateTestWeapon("Valid Final", projectile, cost: 75, requiredPreviousWeapon: levelFiveWeapon);
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();

            defender.SetWeapon(baseWeapon);
            Assert.That(DefeatAttackersUntilLevel(defender, attackerDefinition, 2), Is.True);
            Assert.That(defender.PendingWeaponUpgradeLevel, Is.EqualTo(2));
            var goldBeforeEarlyFinalAttempt = defenderBank.CurrentGold;
            Assert.That(defender.TryChooseWeaponUpgrade(validFinalWeapon), Is.False,
                "A final asset must not bypass the free level-two branch.");
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeEarlyFinalAttempt));
            Assert.That(defender.TryChooseWeaponUpgrade(levelTwoWeapon), Is.True);

            Assert.That(DefeatAttackersUntilLevel(defender, attackerDefinition, 5), Is.True);
            Assert.That(defender.PendingWeaponUpgradeLevel, Is.EqualTo(5));
            var goldBeforeLevelFiveFinalAttempt = defenderBank.CurrentGold;
            Assert.That(defender.TryChooseWeaponUpgrade(validFinalWeapon), Is.False,
                "A final asset must not bypass its exact level-five parent selection.");
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeLevelFiveFinalAttempt));
            Assert.That(defender.TryChooseWeaponUpgrade(levelFiveWeapon), Is.True);

            Assert.That(DefeatAttackersUntilLevel(defender, attackerDefinition, 8), Is.True);
            yield return null;

            Assert.That(defender.CurrentLevel, Is.EqualTo(8));
            Assert.That(defender.PendingWeaponUpgradeLevel, Is.EqualTo(8));
            Assert.That(defender.IsPaidWeaponUpgradePending, Is.True);
            var goldBeforeFinalChoice = defenderBank.CurrentGold;
            Assert.That(defender.TryChooseWeaponUpgrade(wrongFinalWeapon), Is.False);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeFinalChoice));
            Assert.That(defender.TryChooseWeaponUpgrade(validFinalWeapon), Is.True);
            Assert.That(defender.WeaponDefinition, Is.EqualTo(validFinalWeapon));
            Assert.That(defender.HasSelectedFinalWeaponEvolution, Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeFinalChoice - validFinalWeapon.Cost));

            Object.Destroy(attackerDefinition);
            Object.Destroy(projectile);
            Object.Destroy(baseWeapon);
            Object.Destroy(levelTwoWeapon);
            Object.Destroy(levelFiveWeapon);
            Object.Destroy(otherLevelFiveWeapon);
            Object.Destroy(wrongFinalWeapon);
            Object.Destroy(validFinalWeapon);
        }

        [UnityTest]
        public IEnumerator FarmHandActivationSession_RejectsStaleDelayedDamageAfterReuse()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            var originalSession = defender.ActivationSessionId;
            var healthBeforeReuse = attacker.RemainingHealth;

            defender.gameObject.SetActive(false);
            yield return null;
            defender.gameObject.SetActive(true);
            yield return null;

            Assert.That(defender.ActivationSessionId, Is.Not.EqualTo(originalSession));
            Assert.That(defender.IsActivationSessionCurrent(originalSession), Is.False);
            var goldBeforeStaleDamage = defenderBank.CurrentGold;
            Assert.That(defender.TryDamageAttacker(attacker, healthBeforeReuse, originalSession), Is.False);
            Assert.That(attacker.RemainingHealth, Is.EqualTo(healthBeforeReuse));
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeStaleDamage));

            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator Projectiles_RespectFullAndHalfCoverAfterTheyHaveBeenFired()
        {
            var environmentLayer = LayerMask.NameToLayer("Environment");
            Assert.That(environmentLayer, Is.GreaterThanOrEqualTo(0));

            var defender = GameBasicsTestSupport.CreateFarmHand();
            defender.transform.position = new Vector2(-1f, 0f);
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            attacker.transform.position = new Vector2(10f, 0f);
            var projectileDefinition = CreateTestProjectileDefinition(ProjectileType.Ranged);
            var projectile = CreateTestProjectile();

            var wallObject = new GameObject("TAD Test Full Cover Projectile Wall");
            wallObject.layer = environmentLayer;
            var wallCollider = wallObject.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.25f, 3f);
            var wall = wallObject.AddComponent<FarmWall>();
            SetPrivateField(wall, "cover", FarmWallCover.Full);
            Physics2D.SyncTransforms();

            Assert.That(projectile.Launch(defender, defender.transform.position, attacker, projectileDefinition, 20f, 1f), Is.True);
            for (var index = 0; index < 12 && projectile.IsLaunched; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(projectile.IsLaunched, Is.False, "Full cover must stop a ranged projectile already in flight.");

            SetPrivateField(wall, "cover", FarmWallCover.Half);
            projectile.gameObject.SetActive(true);
            Assert.That(projectile.Launch(defender, defender.transform.position, attacker, projectileDefinition, 20f, 1f), Is.True);
            for (var index = 0; index < 12; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(projectile.IsLaunched, Is.True, "Ranged projectiles must retain the ability to cross half cover.");
            projectile.Cancel();

            SetPrivateField(projectileDefinition, "projectileType", ProjectileType.Melee);
            projectile.gameObject.SetActive(true);
            Assert.That(projectile.Launch(defender, defender.transform.position, attacker, projectileDefinition, 20f, 1f), Is.True);
            for (var index = 0; index < 12 && projectile.IsLaunched; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(projectile.IsLaunched, Is.False, "Melee projectiles must not cross half cover.");
            Object.Destroy(attackerDefinition);
            Object.Destroy(projectileDefinition);
        }

        [UnityTest]
        public IEnumerator BouncingProjectile_AppliesTheFarmhandsLevelScaledWeaponDamageAtEveryImpact()
        {
            GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            attacker.transform.position = new Vector2(0.2f, 0f);

            var bounceDefinition = ScriptableObject.CreateInstance<ProjectileDefinition>();
            SetPrivateField(bounceDefinition, "projectileType", ProjectileType.Melee);
            SetPrivateField(bounceDefinition, "projectileBehavior", ProjectileBehavior.Bouncing);
            SetPrivateField(bounceDefinition, "projectileLifetimeMode", ProjectileLifetimeMode.Timed);
            SetPrivateField(bounceDefinition, "projectileSpeed", 10f);
            SetPrivateField(bounceDefinition, "projectileLifetime", 1f);
            SetPrivateField(bounceDefinition, "impactAreaRadius", 0.5f);
            SetPrivateField(bounceDefinition, "impactAreaDamage", 0f);
            SetPrivateField(bounceDefinition, "bounceDistance", 0.05f);
            SetPrivateField(bounceDefinition, "maximumBounceCount", 1);

            var bounceWeapon = CreateTestWeapon("Balanced Bounce", bounceDefinition);
            SetPrivateField(bounceWeapon, "damage", 4f);
            defender.SetWeapon(bounceWeapon);
            SetPrivateField(defender, "currentLevel", 8);
            var expectedDamage = bounceWeapon.Damage * Mathf.Pow(1.07f, 7);
            var projectile = CreateTestRollingProjectile();

            Physics2D.SyncTransforms();
            Assert.That(projectile.Launch(
                defender,
                Vector2.zero,
                attacker,
                bounceDefinition,
                bounceWeapon.Range,
                defender.Damage), Is.True);

            yield return new WaitForFixedUpdate();

            Assert.That(attacker.RemainingHealth,
                Is.EqualTo(attacker.MaximumHealth - expectedDamage).Within(0.001f),
                "Bounce must use the launched damage, including the farmhand's level multiplier.");

            Object.Destroy(attackerDefinition);
            Object.Destroy(bounceDefinition);
            Object.Destroy(bounceWeapon);
        }

        [UnityTest]
        public IEnumerator PendingWeaponUpgrade_FarmhandStillDamagesButDoesNotEarnExperience()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var weapon = Resources.Load<WeaponDefinition>("TADTests/UnarmedWeaponDefinition");
            Assert.That(weapon, Is.Not.Null);
            defender.SetWeapon(weapon);

            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var levelUpAttacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(defender.TryDamageAttacker(levelUpAttacker, levelUpAttacker.RemainingHealth), Is.True);
            yield return null;

            Assert.That(defender.IsAwaitingWeaponUpgrade, Is.True);
            Assert.That(defender.GetComponent<FarmHandUpgradeIndicator>(), Is.Not.Null);
            var arrow = defender.transform.Find("Upgrade Available Arrow")?.GetComponent<SpriteRenderer>();
            Assert.That(arrow, Is.Not.Null);
            Assert.That(arrow.enabled, Is.True);
            Assert.That(arrow.color.r, Is.GreaterThan(0.8f));
            Assert.That(arrow.color.g, Is.LessThan(0.25f));
            Assert.That(arrow.color.b, Is.LessThan(0.25f));
            Assert.That(arrow.sortingLayerID, Is.EqualTo(defender.Visual.sortingLayerID));
            Assert.That(arrow.sortingOrder, Is.LessThan(defender.Visual.sortingOrder));
            Assert.That(arrow.bounds.size.x, Is.GreaterThan(defender.Visual.bounds.size.x));
            Assert.That(arrow.bounds.size.y, Is.GreaterThan(defender.Visual.bounds.size.y));

            var experienceBefore = defender.TotalExperience;
            var goldBefore = defenderBank.CurrentGold;
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            yield return null;

            Assert.That(defender.TotalExperience, Is.EqualTo(experienceBefore));
            Assert.That(defender.CurrentExp, Is.EqualTo(0f));
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBefore + attackerDefinition.KillGain));
            Assert.That(arrow.enabled, Is.True);

            Assert.That(defender.TryChooseWeaponUpgrade(weapon), Is.True);
            yield return null;
            Assert.That(arrow.enabled, Is.False);
            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator Pool_ReusesAttackerDefenderAndProjectileWithFreshRuntimeState()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            GameBasicsTestSupport.CreateBank(BankSide.Defender);
            yield return null;

            var attackerObject = pool.Spawn(PooledObjectType.Attacker, Vector3.zero, Quaternion.identity);
            var attacker = attackerObject.GetComponent<BasicAttacker>();
            var attackerInstance = attackerObject.GetComponent<PooledInstance>();
            var attackerId = attackerInstance.RuntimeInstanceId;
            attacker.TakeDamage(attacker.RemainingHealth);
            attackerInstance.ReturnToPool();

            var reusedAttackerObject = pool.Spawn(PooledObjectType.Attacker, Vector3.one, Quaternion.identity);
            var reusedAttacker = reusedAttackerObject.GetComponent<BasicAttacker>();
            Assert.That(reusedAttackerObject.GetComponent<PooledInstance>().RuntimeInstanceId, Is.EqualTo(attackerId));
            Assert.That(reusedAttacker.RemainingHealth, Is.EqualTo(reusedAttacker.MaximumHealth));
            Assert.That(reusedAttacker.IsDefeated, Is.False);
            Assert.That(reusedAttacker.HasReachedFoodStore, Is.False);

            var defenderObject = pool.Spawn(PooledObjectType.Defender, Vector3.zero, Quaternion.identity);
            var defender = defenderObject.GetComponent<FarmHand>();
            var defenderBaseScale = defender.transform.localScale;
            var progressionDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var progressionAttacker = GameBasicsTestSupport.CreateAttacker(progressionDefinition);
            Assert.That(defender.TryDamageAttacker(progressionAttacker, progressionAttacker.RemainingHealth), Is.True);
            yield return null;
            Assert.That(defender.CurrentLevel, Is.EqualTo(2));
            Assert.That(defender.transform.localScale.x, Is.GreaterThan(defenderBaseScale.x));
            var upgradeArrow = defender.transform.Find("Upgrade Available Arrow")?.GetComponent<SpriteRenderer>();
            Assert.That(upgradeArrow, Is.Not.Null);
            Assert.That(upgradeArrow.enabled, Is.True);
            defender.SetRangeIndicatorVisible(true);
            defender.SetTargetingMode(DefenderTargetingMode.HighestHealth);
            defenderObject.GetComponent<PooledInstance>().ReturnToPool();
            var reusedDefender = pool.Spawn(PooledObjectType.Defender, Vector3.zero, Quaternion.identity).GetComponent<FarmHand>();
            Assert.That(reusedDefender.CurrentLevel, Is.EqualTo(1));
            Assert.That(reusedDefender.CurrentExp, Is.EqualTo(0f));
            Assert.That(reusedDefender.TotalExperience, Is.EqualTo(0f));
            Assert.That(reusedDefender.IsRangeIndicatorVisible, Is.False);
            Assert.That(reusedDefender.TargetingMode, Is.EqualTo(DefenderTargetingMode.FirstEnemyInRange));
            Assert.That(reusedDefender.transform.localScale.x, Is.EqualTo(defenderBaseScale.x).Within(0.0001f));
            var reusedUpgradeArrow = reusedDefender.transform.Find("Upgrade Available Arrow")?.GetComponent<SpriteRenderer>();
            Assert.That(reusedUpgradeArrow, Is.Not.Null);
            Assert.That(reusedUpgradeArrow.enabled, Is.False);
            Object.Destroy(progressionDefinition);

            var projectileObject = pool.Spawn(PooledObjectType.Projectile, Vector3.zero, Quaternion.identity);
            var projectile = projectileObject.GetComponent<FarmHandProjectileBase>();
            projectileObject.GetComponent<PooledInstance>().ReturnToPool();
            var reusedProjectile = pool.Spawn(PooledObjectType.Projectile, Vector3.zero, Quaternion.identity)
                .GetComponent<FarmHandProjectileBase>();
            Assert.That(projectile.IsLaunched, Is.False);
            Assert.That(reusedProjectile.IsLaunched, Is.False);
            Assert.That(reusedProjectile.Source, Is.Null);
            Assert.That(reusedProjectile.ProjectileDefinition, Is.Null);
        }

        [UnityTest]
        public IEnumerator Pool_ResolvesEveryConfiguredAttackerDefenderAndProjectile()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var attackerDefinitions = new List<AttackerDefinition>();
            pool.CopyAvailableAttackerDefinitions(attackerDefinitions);
            Assert.That(attackerDefinitions, Has.Count.EqualTo(40),
                "The complete authored attacker roster must be available from the reusable Spawn Pool.");

            AttackerDefinition ramDefinition = null;
            foreach (var definition in attackerDefinitions)
            {
                Assert.That(definition, Is.Not.Null);
                var attackerObject = pool.SpawnAttacker(definition, Vector3.zero, Quaternion.identity);
                Assert.That(attackerObject, Is.Not.Null, $"{definition.DisplayName} did not resolve to a pooled prefab.");

                var attacker = attackerObject.GetComponent<BasicAttacker>();
                var instance = attackerObject.GetComponent<PooledInstance>();
                Assert.That(attacker, Is.Not.Null);
                Assert.That(instance, Is.Not.Null);
                Assert.That(attacker.Definition, Is.EqualTo(definition));
                instance.ReturnToPool();

                if (definition.AttackerId == "003")
                {
                    ramDefinition = definition;
                }
            }

            Assert.That(ramDefinition, Is.Not.Null);
            var deserializedRamCopy = Object.Instantiate(ramDefinition);
            var clonedRamObject = pool.SpawnAttacker(deserializedRamCopy, Vector3.zero, Quaternion.identity);
            Assert.That(clonedRamObject, Is.Not.Null,
                "An equivalent Ram definition copy should still resolve the canonical Ram pool bucket.");
            Assert.That(clonedRamObject.GetComponent<BasicAttacker>().Definition.AttackerId,
                Is.EqualTo(deserializedRamCopy.AttackerId));
            clonedRamObject.GetComponent<PooledInstance>().ReturnToPool();
            Object.Destroy(deserializedRamCopy);

            var defenderObject = pool.Spawn(PooledObjectType.Defender, Vector3.zero, Quaternion.identity);
            Assert.That(defenderObject, Is.Not.Null);
            Assert.That(defenderObject.GetComponent<FarmHand>(), Is.Not.Null);
            defenderObject.GetComponent<PooledInstance>().ReturnToPool();

            var projectilePrefabs = new List<GameObject>();
            pool.CopyAvailableProjectilePrefabs(projectilePrefabs);
            Assert.That(projectilePrefabs, Is.Not.Empty,
                "The Spawn Pool must expose its default and specialised projectile buckets.");
            foreach (var projectilePrefab in projectilePrefabs)
            {
                Assert.That(projectilePrefab, Is.Not.Null);
                var projectileObject = pool.SpawnProjectile(projectilePrefab, Vector3.zero, Quaternion.identity);
                Assert.That(projectileObject, Is.Not.Null, $"{projectilePrefab.name} did not resolve to a pooled projectile bucket.");
                Assert.That(projectileObject.GetComponent<FarmHandProjectileBase>(), Is.Not.Null);
                projectileObject.GetComponent<PooledInstance>().ReturnToPool();
            }
        }

        [UnityTest]
        public IEnumerator Pool_ReusedAttackerClearsBleedPoisonStunAndSlow()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var firstObject = pool.Spawn(PooledObjectType.Attacker, Vector3.zero, Quaternion.identity);
            var firstAttacker = firstObject.GetComponent<BasicAttacker>();
            var firstInstance = firstObject.GetComponent<PooledInstance>();
            Assert.That(firstAttacker, Is.Not.Null);
            Assert.That(firstInstance, Is.Not.Null);

            firstAttacker.ApplyBleed(null, 4f, 3f);
            firstAttacker.ApplyPoison(null, 3f, 3f);
            firstAttacker.ApplyStun(2f);
            firstAttacker.ApplySlow(0.5f, 2f);
            Assert.That(firstAttacker.IsBleeding, Is.True);
            Assert.That(firstAttacker.IsPoisoned, Is.True);
            Assert.That(firstAttacker.IsStunned, Is.True);
            Assert.That(firstAttacker.IsSlowed, Is.True);

            firstInstance.ReturnToPool();
            var reusedObject = pool.Spawn(PooledObjectType.Attacker, Vector3.one, Quaternion.identity);
            var reusedAttacker = reusedObject.GetComponent<BasicAttacker>();
            Assert.That(reusedAttacker.IsBleeding, Is.False);
            Assert.That(reusedAttacker.IsPoisoned, Is.False);
            Assert.That(reusedAttacker.IsStunned, Is.False);
            Assert.That(reusedAttacker.IsSlowed, Is.False);
            Assert.That(reusedAttacker.EffectiveMovementSpeed, Is.EqualTo(reusedAttacker.MovementSpeed));
        }

        [UnityTest]
        public IEnumerator TerrainSpeedModifiers_AffectGroundAttackersButNotFlyingAttackers()
        {
            var chicken = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            var eagle = Resources.Load<AttackerDefinition>("TADTests/PigeonDefinition");
            Assert.That(chicken, Is.Not.Null);
            Assert.That(eagle, Is.Not.Null);

            var mud = CreateTerrainSpeedModifier("Mud", TerrainSpeedEffect.Mud, new Vector2(-3f, 0f));
            var ice = CreateTerrainSpeedModifier("Ice", TerrainSpeedEffect.Ice, new Vector2(3f, 0f));
            var groundAttacker = GameBasicsTestSupport.CreateAttacker(chicken);
            var flyingAttacker = GameBasicsTestSupport.CreateAttacker(eagle);
            groundAttacker.GetComponent<Rigidbody2D>().gravityScale = 0f;
            flyingAttacker.GetComponent<Rigidbody2D>().gravityScale = 0f;

            groundAttacker.transform.position = mud.transform.position;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(groundAttacker.TerrainMovementMultiplier, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                groundAttacker.EffectiveMovementSpeed,
                Is.EqualTo(groundAttacker.MovementSpeed * 0.75f).Within(0.0001f));
            Assert.That(groundAttacker.IsSlowed, Is.True);

            groundAttacker.transform.position = ice.transform.position;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(groundAttacker.TerrainMovementMultiplier, Is.EqualTo(1.1f).Within(0.0001f));
            Assert.That(groundAttacker.IsSpeedBoosted, Is.True);

            groundAttacker.transform.position = new Vector2(8f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(groundAttacker.TerrainMovementMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(groundAttacker.EffectiveMovementSpeed, Is.EqualTo(groundAttacker.MovementSpeed));

            flyingAttacker.transform.position = mud.transform.position;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(flyingAttacker.IsFlying, Is.True);
            Assert.That(flyingAttacker.TerrainMovementMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(flyingAttacker.EffectiveMovementSpeed, Is.EqualTo(flyingAttacker.MovementSpeed));
        }

        [UnityTest]
        public IEnumerator Pool_ReusedAttackerClearsTerrainSpeedState()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            var mud = CreateTerrainSpeedModifier("Pool Mud", TerrainSpeedEffect.Mud, Vector2.zero);
            yield return null;

            var firstObject = pool.Spawn(PooledObjectType.Attacker, Vector3.zero, Quaternion.identity);
            var firstAttacker = firstObject.GetComponent<BasicAttacker>();
            var firstInstance = firstObject.GetComponent<PooledInstance>();
            firstAttacker.GetComponent<Rigidbody2D>().gravityScale = 0f;
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(firstAttacker.TerrainMovementMultiplier, Is.EqualTo(mud.MovementSpeedMultiplier).Within(0.0001f));

            firstInstance.ReturnToPool();
            var reusedAttacker = pool.Spawn(PooledObjectType.Attacker, new Vector3(8f, 0f), Quaternion.identity)
                .GetComponent<BasicAttacker>();
            Assert.That(reusedAttacker.TerrainMovementMultiplier, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(reusedAttacker.EffectiveMovementSpeed, Is.EqualTo(reusedAttacker.MovementSpeed));
        }

        [UnityTest]
        public IEnumerator FarmRouteMapper_UsesBeneficialIceForGroundButKeepsFlyingTerrainNeutral()
        {
            var chicken = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            var eagle = Resources.Load<AttackerDefinition>("TADTests/PigeonDefinition");
            Assert.That(chicken, Is.Not.Null);
            Assert.That(eagle, Is.Not.Null);

            GameBasicsTestSupport.CreateMainCamera();
            GameBasicsTestSupport.CreateFoodStore(new Vector2(5f, 0f));
            var helpfulIce = CreateTerrainSpeedModifier(
                "Helpful Ice",
                TerrainSpeedEffect.Ice,
                new Vector2(0f, 1f),
                new Vector2(9f, 2f));

            var mapperObject = new GameObject("TAD Test Terrain-Aware Route Mapper");
            mapperObject.SetActive(false);
            var mapper = mapperObject.AddComponent<FarmRouteMapper>();
            SetPrivateField(mapper, "attackerDefinitionsToPrecompute", new[] { chicken, eagle });
            mapperObject.SetActive(true);
            yield return null;

            Assert.That(mapper.RebuildRoutes(), Is.True);
            var groundRoute = new List<Vector3>();
            var flyingRoute = new List<Vector3>();
            Assert.That(
                mapper.TryGetFastestRoute(new Vector3(-5f, 0f), chicken, groundRoute, out var groundTiming),
                Is.True);
            Assert.That(
                mapper.TryGetFastestRoute(new Vector3(-5f, 0f), eagle, flyingRoute, out var flyingTiming),
                Is.True);

            Assert.That(groundTiming.UnitSpeedTravelCost, Is.LessThan(groundTiming.Distance),
                "A ground route should only lean into Ice when its faster movement repays the detour.");
            Assert.That(groundTiming.UnitSpeedTravelCost, Is.LessThan(flyingTiming.UnitSpeedTravelCost),
                "The ground attacker should receive the useful Ice time benefit.");
            Assert.That(flyingTiming.UnitSpeedTravelCost, Is.EqualTo(flyingTiming.Distance).Within(0.02f),
                "Flying attackers must ignore Ice entirely when mapping their path.");

            Object.Destroy(helpfulIce.gameObject);
            yield return null;
            CreateTerrainSpeedModifier(
                "Distant Ice",
                TerrainSpeedEffect.Ice,
                new Vector2(0f, 4f),
                new Vector2(8f, 1f));

            Assert.That(mapper.RebuildRoutes(), Is.True);
            groundRoute.Clear();
            Assert.That(
                mapper.TryGetFastestRoute(new Vector3(-5f, 0f), chicken, groundRoute, out var distantIceTiming),
                Is.True);

            Assert.That(distantIceTiming.UnitSpeedTravelCost, Is.EqualTo(distantIceTiming.Distance).Within(0.02f),
                "The ground route should remain dry when a distant Ice detour would be slower overall.");
        }

        [UnityTest]
        public IEnumerator DefenderReuse_DoesNotCreditStaleBleedToItsNewActivation()
        {
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            var originalActivationSession = defender.ActivationSessionId;

            attacker.ApplyBleed(defender, originalActivationSession, 50f, 1f);
            Assert.That(attacker.IsBleeding, Is.True);

            defender.gameObject.SetActive(false);
            defender.gameObject.SetActive(true);
            Assert.That(defender.ActivationSessionId, Is.Not.EqualTo(originalActivationSession));

            for (var index = 0; index < 20; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(attacker.IsBleeding, Is.False);
            Assert.That(defender.TotalExperience, Is.EqualTo(0f));
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(defenderBank.StartingGold));
            Assert.That(attacker.RemainingHealth, Is.EqualTo(attacker.MaximumHealth));
            Object.Destroy(attackerDefinition);
        }

        [UnityTest]
        public IEnumerator DefenderPlacement_UsesSpriteSizedFootprintAndPaidRelocation()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            var placementController = fixture.SpawnPool.GetComponent<FarmHandPlacementController>();
            Assert.That(placementController, Is.Not.Null);

            var initialPosition = new Vector2(0f, 0f);
            var goldBeforePlacement = defenderBank.CurrentGold;
            Assert.That(placementController.TryPlaceFarmHandAt(initialPosition), Is.True);
            yield return null;
            Physics2D.SyncTransforms();

            var farmHand = Object.FindAnyObjectByType<FarmHand>();
            Assert.That(farmHand, Is.Not.Null);
            Assert.That(farmHand.PlacementCollider, Is.Not.Null);
            Assert.That(farmHand.PlacementCollider.isTrigger, Is.True);
            var expectedPlacementBounds = GetSpritePhysicsBounds(farmHand.Visual);
            Assert.That(farmHand.PlacementCollider.bounds.size.x,
                Is.EqualTo(expectedPlacementBounds.size.x).Within(0.01f));
            Assert.That(farmHand.PlacementCollider.bounds.size.y,
                Is.EqualTo(expectedPlacementBounds.size.y).Within(0.01f));
            Assert.That(farmHand.TargetingMode, Is.EqualTo(DefenderTargetingMode.FirstEnemyInRange));
            farmHand.SetTargetingMode(DefenderTargetingMode.Fastest);
            Assert.That(defenderBank.CurrentGold,
                Is.EqualTo(goldBeforePlacement - farmHand.WeaponDefinition.Cost));

            Assert.That(placementController.TryPlaceFarmHandAt(initialPosition), Is.False);
            Assert.That(defenderBank.CurrentGold,
                Is.EqualTo(goldBeforePlacement - farmHand.WeaponDefinition.Cost));

            var relocationCost = farmHand.RelocationCost;
            var targetPosition = (Vector2)farmHand.transform.position + Vector2.up;
            Assert.That(placementController.TryRelocateFarmHandAt(farmHand, targetPosition), Is.True);
            Assert.That(farmHand.IsRelocating, Is.True);
            Assert.That(defenderBank.CurrentGold,
                Is.EqualTo(goldBeforePlacement - farmHand.WeaponDefinition.Cost - relocationCost));

            yield return new WaitForSeconds(1.2f);
            Assert.That(farmHand.IsRelocating, Is.False);
            Assert.That(Vector2.Distance(farmHand.transform.position, targetPosition), Is.LessThan(0.02f));
            Assert.That(farmHand.TargetingMode, Is.EqualTo(DefenderTargetingMode.Fastest));

            placementController.SetPlacementInputEnabled(false);
            var aiRequestedPosition = (Vector2)farmHand.transform.position + Vector2.right;
            Assert.That(
                placementController.CanRelocateFarmHandTo(farmHand, aiRequestedPosition, out var aiTargetPosition),
                Is.True);
            var goldBeforeAiRelocation = defenderBank.CurrentGold;
            Assert.That(placementController.TryRelocateFarmHandForAi(farmHand, aiRequestedPosition), Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeAiRelocation - farmHand.RelocationCost));

            yield return new WaitForSeconds(1.2f);
            Assert.That(farmHand.IsRelocating, Is.False);
            Assert.That(Vector2.Distance(farmHand.transform.position, aiTargetPosition), Is.LessThan(0.02f));
        }

        [UnityTest]
        public IEnumerator DefenderRelocation_FollowsGroundRouteAroundEveryWallType()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            yield return null;

            var environmentLayer = LayerMask.NameToLayer("Environment");
            Assert.That(environmentLayer, Is.GreaterThanOrEqualTo(0));

            var wallObject = new GameObject("TAD Test Defender Relocation Half Cover Wall");
            wallObject.layer = environmentLayer;
            wallObject.transform.position = Vector2.zero;
            var wallCollider = wallObject.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.5f, 2f);
            var wall = wallObject.AddComponent<FarmWall>();
            SetPrivateField(wall, "cover", FarmWallCover.Half);
            Physics2D.SyncTransforms();
            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);

            var placementController = fixture.SpawnPool.GetComponent<FarmHandPlacementController>();
            var initialPosition = new Vector2(-3f, 0f);
            var destination = new Vector2(3f, 0f);
            Assert.That(placementController.TryPlaceFarmHandAt(initialPosition), Is.True);
            yield return null;

            var farmHand = Object.FindAnyObjectByType<FarmHand>();
            Assert.That(farmHand, Is.Not.Null);
            SetPrivateField(farmHand, "relocationSpeed", 10f);
            var goldBeforeRelocation = defenderBank.CurrentGold;

            Assert.That(placementController.TryRelocateFarmHandAt(farmHand, destination), Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeRelocation - farmHand.RelocationCost));

            var maximumVerticalOffset = 0f;
            var movementClearance = farmHand.RelocationPathDiameter * 0.5f;
            for (var step = 0; step < 180 && farmHand.IsRelocating; step++)
            {
                yield return new WaitForFixedUpdate();
                maximumVerticalOffset = Mathf.Max(maximumVerticalOffset, Mathf.Abs(farmHand.transform.position.y));
                Assert.That(
                    wall.BlocksCircle(farmHand.transform.position, movementClearance),
                    Is.False,
                    "A farmhand relocation route must never pass through a Half or Full cover wall.");
            }

            // Rigidbody2D applies the final waypoint during the last fixed tick; let Unity carry
            // that position through to the visual Transform before checking the authored target.
            yield return null;
            Assert.That(farmHand.IsRelocating, Is.False);
            Assert.That(Vector2.Distance(farmHand.transform.position, destination), Is.LessThan(0.02f));
            Assert.That(maximumVerticalOffset, Is.GreaterThan(wallCollider.bounds.extents.y),
                "The route should visibly bend around the wall instead of moving straight through it.");
        }

        [UnityTest]
        public IEnumerator FarmHandPlacement_ValidatesPurchaseAndRelocationCostBeforeOpeningAPreview()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender, startingGold: 0);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            var placementController = fixture.SpawnPool.GetComponent<FarmHandPlacementController>();
            Assert.That(placementController, Is.Not.Null);

            Assert.That(placementController.BeginPlacement(), Is.False);
            Assert.That(placementController.IsPlacementActive, Is.False);
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.True);

            var warning = Object.FindAnyObjectByType<ModalOverlayMessage>();
            Assert.That(warning, Is.Not.Null);
            warning.CompleteChoice();
            yield return null;
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);

            defenderBank.Configure(BankSide.Defender, initialGold: 100);
            Assert.That(placementController.TryPlaceFarmHandAt(Vector2.zero), Is.True);
            yield return null;

            var farmHand = Object.FindAnyObjectByType<FarmHand>();
            Assert.That(farmHand, Is.Not.Null);
            Assert.That(defenderBank.TrySpendGold(defenderBank.CurrentGold), Is.True);
            var goldBeforeRelocationAttempt = defenderBank.CurrentGold;

            Assert.That(placementController.BeginRelocation(farmHand), Is.False);
            Assert.That(placementController.IsPlacementActive, Is.False);
            Assert.That(farmHand.IsRelocating, Is.False);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforeRelocationAttempt));
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.True);

            warning.CompleteChoice();
            yield return null;
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);
        }

        [UnityTest]
        public IEnumerator FarmHandPlacement_RejectsOccupiedPreviewPositionBeforeConfirmation()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var defenderBank = GameBasicsTestSupport.CreateBank(BankSide.Defender);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            var placementController = fixture.SpawnPool.GetComponent<FarmHandPlacementController>();
            Assert.That(placementController, Is.Not.Null);
            Assert.That(placementController.TryPlaceFarmHandAt(Vector2.zero), Is.True);
            yield return null;
            Physics2D.SyncTransforms();

            var farmHand = Object.FindAnyObjectByType<FarmHand>();
            Assert.That(farmHand, Is.Not.Null);
            var goldBeforePreview = defenderBank.CurrentGold;
            Assert.That(placementController.BeginPlacement(), Is.True);
            Assert.That(placementController.TrySetPlacementPreviewPosition((Vector2)farmHand.transform.position), Is.False);
            Assert.That(placementController.IsPlacementActive, Is.True);
            Assert.That(defenderBank.CurrentGold, Is.EqualTo(goldBeforePreview));
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);

            var warning = Object.FindAnyObjectByType<ModalOverlayMessage>();
            var canvasGroup = warning != null ? warning.GetComponent<CanvasGroup>() : null;
            Assert.That(canvasGroup, Is.Not.Null);
            Assert.That(canvasGroup.alpha, Is.EqualTo(1f));
            Assert.That(canvasGroup.interactable, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.False);

            placementController.CancelPlacement();
        }

        [UnityTest]
        public IEnumerator DefenderDetails_TargetingControlIsEditableForDefendersAndReadOnlyForAttackers()
        {
            var farmHand = GameBasicsTestSupport.CreateFarmHand();
            var panelObject = new GameObject("TAD Test Defender Details Panel", typeof(RectTransform));
            panelObject.SetActive(false);
            var cardObject = new GameObject("Card", typeof(RectTransform));
            cardObject.transform.SetParent(panelObject.transform, worldPositionStays: false);
            var panel = panelObject.AddComponent<DefenderDetailsPanel>();
            panelObject.SetActive(true);

            panel.Show(farmHand);
            yield return null;

            var dropdown = panelObject.transform.Find("Card/TargetingDropdown")?.GetComponent<Dropdown>();
            var readOnlyText = panelObject.transform.Find("Card/TargetingReadOnly")?.GetComponent<Text>();
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(readOnlyText, Is.Not.Null);
            Assert.That(dropdown.gameObject.activeSelf, Is.True);
            Assert.That(readOnlyText.gameObject.activeSelf, Is.False);
            var expectedModes = new[]
            {
                DefenderTargetingMode.FirstEnemyInRange,
                DefenderTargetingMode.HighestHealth,
                DefenderTargetingMode.Fastest,
                DefenderTargetingMode.LastEnemyInRange,
                DefenderTargetingMode.HighestKillGain
            };
            Assert.That(FarmHand.AvailableTargetingModes.Count, Is.EqualTo(expectedModes.Length));
            Assert.That(dropdown.options.Count, Is.EqualTo(expectedModes.Length));
            for (var index = 0; index < expectedModes.Length; index++)
            {
                Assert.That(dropdown.options[index].text,
                    Is.EqualTo($"Target Enemy: {FarmHand.GetTargetingModeDisplayName(expectedModes[index])}"));
                dropdown.value = index;
                Assert.That(farmHand.TargetingMode, Is.EqualTo(expectedModes[index]));
            }

            MatchRoleSelection.Select(PlayerRole.Attacker);
            panel.Show(farmHand);
            yield return null;

            Assert.That(dropdown.gameObject.activeSelf, Is.False);
            Assert.That(readOnlyText.gameObject.activeSelf, Is.True);
            Assert.That(readOnlyText.text, Is.EqualTo("Target Enemy: Highest resources"));
        }

        [UnityTest]
        public IEnumerator DefenderTargeting_PrioritizesLiveValuesAndEntryOrder()
        {
            var bull = Resources.Load<AttackerDefinition>("TADTests/BullDefinition");
            var rabbit = Resources.Load<AttackerDefinition>("TADTests/RabbitDefinition");
            var pigeon = Resources.Load<AttackerDefinition>("TADTests/PigeonDefinition");
            Assert.That(bull, Is.Not.Null);
            Assert.That(rabbit, Is.Not.Null);
            Assert.That(pigeon, Is.Not.Null);

            var sensorWeapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            var farmHand = GameBasicsTestSupport.CreateFarmHand();
            farmHand.SetWeapon(sensorWeapon);

            var firstBull = GameBasicsTestSupport.CreateAttacker(bull);
            var secondBull = GameBasicsTestSupport.CreateAttacker(bull);
            var firstRabbit = GameBasicsTestSupport.CreateAttacker(rabbit);
            var secondRabbit = GameBasicsTestSupport.CreateAttacker(rabbit);
            var firstPigeon = GameBasicsTestSupport.CreateAttacker(pigeon);
            var secondPigeon = GameBasicsTestSupport.CreateAttacker(pigeon);
            var attackers = new[]
            {
                firstBull,
                secondBull,
                firstRabbit,
                secondRabbit,
                firstPigeon,
                secondPigeon
            };

            for (var index = 0; index < attackers.Length; index++)
            {
                attackers[index].transform.position = new Vector2(2f + index, 0f);
                attackers[index].GetComponent<Rigidbody2D>().gravityScale = 0f;
            }

            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();

            var entryPositions = new[]
            {
                new Vector2(-0.6f, 0f),
                new Vector2(-0.4f, 0f),
                new Vector2(-0.2f, 0f),
                Vector2.zero,
                new Vector2(0.2f, 0f),
                new Vector2(0.4f, 0f)
            };
            for (var index = 0; index < attackers.Length; index++)
            {
                attackers[index].transform.position = entryPositions[index];
                Physics2D.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }

            Assert.That(farmHand.AttackersInRangeCount, Is.EqualTo(attackers.Length));

            farmHand.SetTargetingMode(DefenderTargetingMode.HighestHealth);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstBull));
            firstBull.TakeDamage(1f);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondBull));

            farmHand.SetTargetingMode(DefenderTargetingMode.Fastest);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstRabbit));

            // The default sensor weapon has no projectile definition, so it uses the safe melee fallback.
            // Flying animals remain tracked in range, but the fallback can only select grounded targets.
            farmHand.SetTargetingMode(DefenderTargetingMode.HighestKillGain);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstBull));

            farmHand.SetTargetingMode(DefenderTargetingMode.LastEnemyInRange);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondRabbit));

            var unarmed = Resources.Load<WeaponDefinition>("TADTests/UnarmedWeaponDefinition");
            Assert.That(unarmed, Is.Not.Null);
            farmHand.SetWeapon(unarmed);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();

            // Unarmed is the deliberate melee exception: it can acquire airborne attackers without
            // inheriting ranged wall-piercing behaviour. Airborne targets become eligible; they do
            // not displace an eligible target with a larger KillGain.
            farmHand.SetTargetingMode(DefenderTargetingMode.HighestKillGain);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstBull));
            farmHand.SetTargetingMode(DefenderTargetingMode.LastEnemyInRange);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondPigeon));

            var airRifle = Resources.Load<WeaponDefinition>("TADTests/AirRifleWeaponDefinition");
            Assert.That(airRifle, Is.Not.Null);
            farmHand.SetWeapon(airRifle);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();

            // Ranged weapons retain the existing ability to select flying animals, while preserving
            // the selected targeting rule's priority order.
            farmHand.SetTargetingMode(DefenderTargetingMode.HighestKillGain);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstBull));
            farmHand.SetTargetingMode(DefenderTargetingMode.LastEnemyInRange);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondPigeon));
            secondPigeon.transform.position = new Vector2(farmHand.Range + 1f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstPigeon));

            firstPigeon.TakeDamage(firstPigeon.RemainingHealth);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondRabbit));

            Object.Destroy(sensorWeapon);
        }

        [UnityTest]
        public IEnumerator ProjectileTargetingRules_AllowRangedAndExplicitlyConfiguredMeleeProjectilesToTargetFlyingAnimals()
        {
            var bull = Resources.Load<AttackerDefinition>("TADTests/BullDefinition");
            var eagle = Resources.Load<AttackerDefinition>("TADTests/PigeonDefinition");
            Assert.That(bull, Is.Not.Null);
            Assert.That(eagle, Is.Not.Null);

            var groundAttacker = GameBasicsTestSupport.CreateAttacker(bull);
            var flyingAttacker = GameBasicsTestSupport.CreateAttacker(eagle);
            yield return null;

            Assert.That(groundAttacker.IsFlying, Is.False);
            Assert.That(flyingAttacker.IsFlying, Is.True);
            Assert.That(ProjectileDefinition.CanTarget(ProjectileType.Melee, groundAttacker), Is.True);
            Assert.That(ProjectileDefinition.CanTarget(ProjectileType.Melee, flyingAttacker), Is.False);
            Assert.That(ProjectileDefinition.CanTarget(ProjectileType.Ranged, flyingAttacker), Is.True);

            var unarmed = Resources.Load<WeaponDefinition>("TADTests/UnarmedWeaponDefinition");
            Assert.That(unarmed, Is.Not.Null);
            Assert.That(unarmed.ProjectileDefinition.CanTarget(flyingAttacker), Is.True);
        }

        [UnityTest]
        public IEnumerator AttackerCombatRating_AppearsOnSelectionQueueAndDetailsImages()
        {
            var definition = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(definition, Is.Not.Null);

            var selectionObject = new GameObject(
                "TAD Test Combat Rating Selection",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var selectionImage = CreateCombatRatingTestImage(selectionObject.transform, new Vector2(100f, 90f));
            var selectionButton = selectionObject.AddComponent<AttackerSelectionButton>();
            selectionButton.Bind(definition, null, null, null);

            var queueObject = new GameObject(
                "TAD Test Combat Rating Queue",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var queueImage = CreateCombatRatingTestImage(queueObject.transform, new Vector2(64f, 64f));
            var queueButton = queueObject.AddComponent<AttackerQueueGroupButton>();
            var displayGroup = new AttackerQueuedGroupInfo(
                id: 1,
                definition,
                definition.SendSize,
                definition.SendSize,
                isDispatching: false,
                isCurrentGroup: false);
            queueButton.Bind(displayGroup, null, null, false);

            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            yield return null;
            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            var groups = new List<AttackerQueuedGroupInfo>();
            fixture.WaveQueue.CopyDisplayGroups(groups);
            var panel = GameBasicsTestSupport.CreateAttackerDetailsPanel(
                out _,
                out _,
                out _,
                out _);
            panel.Show(fixture.WaveQueue, groups[0], null);

            yield return null;

            AssertCombatRatingBadge(selectionImage, definition);
            AssertCombatRatingBadge(queueImage, definition);
            var detailsImage = panel.transform.Find("Card/AttackerImage")?.GetComponent<Image>();
            Assert.That(detailsImage, Is.Not.Null);
            AssertCombatRatingBadge(detailsImage, definition);
        }

        [UnityTest]
        public IEnumerator TransientPlacementWarning_IsOpaqueClickThroughAndExpires()
        {
            var warningObject = new GameObject("TAD Test Transient Placement Warning");
            warningObject.SetActive(false);
            var canvasGroup = warningObject.AddComponent<CanvasGroup>();
            warningObject.AddComponent<ModalOverlay>();
            var message = warningObject.AddComponent<ModalOverlayMessage>();
            warningObject.SetActive(true);
            yield return null;

            message.ShowTransientMessage("Another farmhand is already stationed there.", 0.05f);

            Assert.That(canvasGroup.alpha, Is.EqualTo(1f));
            Assert.That(canvasGroup.interactable, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.False);
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);

            yield return new WaitForSecondsRealtime(0.1f);

            Assert.That(canvasGroup.alpha, Is.EqualTo(0f));
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);
        }

        private static Image CreateCombatRatingTestImage(Transform parent, Vector2 size)
        {
            var imageObject = new GameObject(
                "AttackerImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(parent, worldPositionStays: false);
            imageObject.GetComponent<RectTransform>().sizeDelta = size;
            return imageObject.GetComponent<Image>();
        }

        private static void AssertCombatRatingBadge(Image attackerImage, AttackerDefinition definition)
        {
            var badge = attackerImage.transform.Find("CombatRatingBadge")
                ?.GetComponent<AttackerCombatRatingBadge>();
            Assert.That(badge, Is.Not.Null);
            Assert.That(badge.RatingLabel, Is.EqualTo("CR:" + definition.CombatRating.ToString("0.#")));
            Assert.That(badge.SendSizeLabel, Is.EqualTo("x" + definition.SendSize));
        }

        [UnityTest]
        public IEnumerator AttackerQueue_SelectionIncomeIsReversedWhenAnUnsentGroupIsRemoved()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = Resources.Load<AttackerDefinition>("TADTests/ChickenDefinition");
            Assert.That(definition, Is.Not.Null);
            Assert.That(fixture.SpawnPool, Is.Not.Null);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            Assert.That(fixture.WaveQueue.CanQueueAttackers, Is.True);

            var goldBeforeQueue = fixture.AttackerBank.CurrentGold;
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            var groups = new List<AttackerQueuedGroupInfo>();
            fixture.WaveQueue.CopyDisplayGroups(groups);
            Assert.That(groups, Has.Count.EqualTo(1));
            var group = groups[0];
            Assert.That(group.TotalCount, Is.EqualTo(definition.SendSize));
            Assert.That(group.RemainingAlive, Is.EqualTo(definition.SendSize));
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue - definition.SendCost));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome),
                "Selecting an attacker card must immediately add its authored income.");

            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            fixture.WaveQueue.CopyDisplayGroups(groups);
            Assert.That(groups, Has.Count.EqualTo(2));
            var retainedGroup = groups[1];
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue - (definition.SendCost * 2)));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 2));

            var panel = GameBasicsTestSupport.CreateAttackerDetailsPanel(
                out Text heading,
                out Text status,
                out Text stats,
                out Text route);
            MatchRoleSelection.Select(PlayerRole.Attacker);
            panel.Show(fixture.WaveQueue, group, null);
            Assert.That(panel.IsShowing, Is.True);
            Assert.That(heading.text, Is.EqualTo(definition.DisplayName));
            Assert.That(status.text, Does.Contain("Alive: " + definition.SendSize + "/" + definition.SendSize));
            Assert.That(stats.text, Does.Contain("Cost: " + definition.SendCost));
            Assert.That(route.text, Does.Contain("Route highlighted"));

            var routePreviewObject = new GameObject("TAD Test Route Preview", typeof(AttackerRoutePreview));
            var routePreview = routePreviewObject.GetComponent<AttackerRoutePreview>();
            Assert.That(routePreview.Show(
                fixture.SpawnPool.RouteMapper,
                fixture.WaveController.SpawnPosition,
                definition),
                Is.True);
            Assert.That(routePreview.GetComponent<LineRenderer>().positionCount, Is.GreaterThan(1));

            var removeButton = panel.transform.Find("Card/RemoveFromQueue")?.GetComponent<Button>();
            Assert.That(removeButton, Is.Not.Null);
            Assert.That(removeButton.gameObject.activeSelf, Is.True);
            Assert.That(removeButton.interactable, Is.True);
            removeButton.onClick.Invoke();

            Assert.That(fixture.WaveQueue.TryGetDisplayGroup(group.Id, out _), Is.False);
            Assert.That(fixture.WaveQueue.TryGetDisplayGroup(retainedGroup.Id, out _), Is.True);
            Assert.That(fixture.WaveQueue.QueuedAttackerCount, Is.EqualTo(definition.SendSize));
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue - definition.SendCost));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome),
                "Removing an unsent card must reverse only that card's selection income.");
            Assert.That(panel.IsShowing, Is.False);
        }

        [UnityTest]
        public IEnumerator AttackerWave_SelectionIncomeStacksWithEachFoodStoreArrivalAndRefundsOnce()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = GetSpawnableAttackerDefinition(fixture.SpawnPool);
            Assert.That(definition, Is.Not.Null);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            SetPrivateField(fixture.WaveQueue, "spawnInterval", 0f);
            var goldBeforeQueue = fixture.AttackerBank.CurrentGold;
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue - definition.SendCost));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome));
            Assert.That(fixture.WaveQueue.TrySendQueuedWaves(null), Is.True);

            var deadline = Time.realtimeSinceStartup + 1f;
            while (FindActivePoolAttackers(fixture.SpawnPool).Count < definition.SendSize &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var attackers = FindActivePoolAttackers(fixture.SpawnPool);
            Assert.That(attackers, Has.Count.EqualTo(definition.SendSize));
            var firstCollider = attackers[0].GetComponent<Collider2D>();
            fixture.FoodStore.gameObject.SendMessage("OnTriggerEnter2D", firstCollider, SendMessageOptions.RequireReceiver);
            yield return null;

            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue),
                "The first animal from a group refunds that group's original send cost exactly once.");
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 2));

            // Replaying the same trigger cannot grant another refund or income entry.
            fixture.FoodStore.gameObject.SendMessage("OnTriggerEnter2D", firstCollider, SendMessageOptions.RequireReceiver);
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 2));

            attackers = FindActivePoolAttackers(fixture.SpawnPool);
            var secondCollider = attackers[0].GetComponent<Collider2D>();
            fixture.FoodStore.gameObject.SendMessage("OnTriggerEnter2D", secondCollider, SendMessageOptions.RequireReceiver);
            yield return null;

            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue),
                "Later arrivals from the same group earn income but never a second cost refund.");
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 3));

            fixture.WaveController.ResetForNewLevelSession();
            fixture.WaveQueue.ResetForNewLevelSession();
        }

        [UnityTest]
        public IEnumerator BasicAttacker_ExhaustedCachedRouteUsesFlowFieldWithoutIndexingPastTheRoute()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = GetSpawnableAttackerDefinition(fixture.SpawnPool);
            Assert.That(definition, Is.Not.Null);
            yield return null;

            var mapper = fixture.SpawnPool.RouteMapper;
            Assert.That(mapper, Is.Not.Null);
            Assert.That(mapper.RebuildRoutes(), Is.True);

            var attacker = GameBasicsTestSupport.CreateAttacker(definition);
            attacker.SetRouteMapper(mapper);

            var cachedRoute = GetPrivateField<List<Vector3>>(attacker, "routePoints");
            Assert.That(cachedRoute, Has.Count.GreaterThanOrEqualTo(2));

            // Reaching the final sparse waypoint intentionally switches to FarmRouteMapper's live
            // flow field while the Food Store trigger finishes the arrival. This used to leave the
            // index at Count and FixedUpdate then indexed beyond the cached route.
            SetPrivateField(attacker, "nextRoutePointIndex", cachedRoute.Count);
            var fixedUpdate = typeof(BasicAttacker).GetMethod(
                "FixedUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fixedUpdate, Is.Not.Null);

            Assert.DoesNotThrow(
                () => fixedUpdate.Invoke(attacker, null),
                "An attacker that has consumed its cached waypoints must keep moving on the flow field.");
            Assert.That(attacker.MoveDirection.sqrMagnitude, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator AttackerWave_TerminalArrivalSettlesEconomyBeforeFoodDepletion()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = GetSpawnableAttackerDefinition(fixture.SpawnPool);
            Assert.That(definition, Is.Not.Null);
            yield return null;

            SetPrivateField(fixture.FoodStore, "startingFood", 1f);
            fixture.FoodStore.ResetFood();
            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            SetPrivateField(fixture.WaveQueue, "spawnInterval", 0f);
            var goldBeforeQueue = fixture.AttackerBank.CurrentGold;
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.WaveQueue.TrySendQueuedWaves(null), Is.True);

            var deadline = Time.realtimeSinceStartup + 1f;
            while (FindActivePoolAttackers(fixture.SpawnPool).Count == 0 &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var attacker = FindActivePoolAttackers(fixture.SpawnPool)[0];
            fixture.FoodStore.gameObject.SendMessage(
                "OnTriggerEnter2D",
                attacker.GetComponent<Collider2D>(),
                SendMessageOptions.RequireReceiver);
            yield return null;

            Assert.That(fixture.FoodStore.IsDepleted, Is.True);
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue),
                "The animal that ends the match must still refund its group's send cost.");
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 2),
                "The terminal food-store arrival must add income on top of the card-selection income before cancellation.");

            fixture.WaveQueue.CancelAllOrders();
            fixture.WaveController.ResetForNewLevelSession();
        }

        [UnityTest]
        public IEnumerator AttackerWave_OverlapsSnapshotGroupsAndDefersLaterOrdersToTheNextCycle()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = GetSpawnableAttackerDefinition(fixture.SpawnPool);
            Assert.That(definition, Is.Not.Null);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            SetPrivateField(fixture.WaveQueue, "spawnInterval", 0f);
            var startedWaves = new List<int>();
            fixture.WaveController.WaveStarted += (waveNumber, _) => startedWaves.Add(waveNumber);

            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.WaveQueue.TrySendQueuedWaves(null), Is.True);

            // This card is selected after the day's snapshot. It must not be pulled into either
            // of the two groups already entering the map.
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);

            var deadline = Time.realtimeSinceStartup + 1f;
            while (startedWaves.Count < 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(startedWaves, Has.Count.EqualTo(2));
            Assert.That(fixture.WaveController.ActiveAttackerCount, Is.EqualTo(definition.SendSize * 2),
                "The second group must enter while the first group is still active on the map.");

            var pendingGroups = new List<AttackerQueuedGroupInfo>();
            fixture.WaveQueue.CopyPendingGroups(pendingGroups);
            Assert.That(pendingGroups, Has.Count.EqualTo(1));
            Assert.That(pendingGroups[0].TotalCount, Is.EqualTo(definition.SendSize));
            Assert.That(fixture.WaveQueue.QueuedWaveCount, Is.EqualTo(1),
                "Orders added during combat remain for the following day/night cycle.");

            fixture.WaveController.ResetForNewLevelSession();
            fixture.WaveQueue.ResetForNewLevelSession();
        }

        [UnityTest]
        public IEnumerator AttackerWave_DeferredSelectionIncomeWaitsForItsOwnDispatchPayout()
        {
            var fixture = GameBasicsTestSupport.CreateQueueFixture();
            var definition = GetSpawnableAttackerDefinition(fixture.SpawnPool);
            Assert.That(definition, Is.Not.Null);
            yield return null;

            Assert.That(fixture.SpawnPool.RouteMapper.RebuildRoutes(), Is.True);
            SetPrivateField(fixture.WaveQueue, "spawnInterval", 0f);
            var goldBeforeQueue = fixture.AttackerBank.CurrentGold;

            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.WaveQueue.TrySendQueuedWaves(null), Is.True);

            // This selection is visible in the persistent-income HUD immediately, but is not
            // eligible for the active day's gold payout because it belongs to tomorrow's queue.
            Assert.That(fixture.WaveQueue.TryQueueAttackers(definition), Is.True);
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome * 2));

            var deadline = Time.realtimeSinceStartup + 1f;
            while (FindActivePoolAttackers(fixture.SpawnPool).Count < definition.SendSize &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var activeAttackers = new List<BasicAttacker>(FindActivePoolAttackers(fixture.SpawnPool));
            Assert.That(activeAttackers, Has.Count.EqualTo(definition.SendSize));
            foreach (var attacker in activeAttackers)
            {
                fixture.FoodStore.gameObject.SendMessage(
                    "OnTriggerEnter2D",
                    attacker.GetComponent<Collider2D>(),
                    SendMessageOptions.RequireReceiver);
                yield return null;
            }

            deadline = Time.realtimeSinceStartup + 1f;
            while (fixture.WaveQueue.IsSending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(fixture.WaveQueue.IsSending, Is.False);
            var resolvedGroupIncome = definition.WaveIncome * (definition.SendSize + 1);
            Assert.That(fixture.AttackerBank.CurrentGold,
                Is.EqualTo(goldBeforeQueue - definition.SendCost + resolvedGroupIncome),
                "The next-day selection must not be paid during the current day's resolution.");

            var pendingGroups = new List<AttackerQueuedGroupInfo>();
            fixture.WaveQueue.CopyPendingGroups(pendingGroups);
            Assert.That(pendingGroups, Has.Count.EqualTo(1));
            Assert.That(fixture.WaveQueue.TryRemoveQueuedGroup(pendingGroups[0].Id), Is.True);
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeQueue + resolvedGroupIncome));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(resolvedGroupIncome),
                "Removing the unplayed card must leave only the resolved group's selection and arrival income.");

            fixture.WaveController.ResetForNewLevelSession();
            fixture.WaveQueue.ResetForNewLevelSession();
        }

        [UnityTest]
        public IEnumerator DayCycle_EnforcesPreparationAndMinimumEmptyDayDuration()
        {
            GameBasicsTestSupport.CreateFoodStore();
            var dayCycle = GameBasicsTestSupport.CreateDayCycle();
            yield return null;

            Assert.That(dayCycle.IsPreparing, Is.True);
            Assert.That(dayCycle.CurrentDay, Is.EqualTo(0));

            Time.maximumDeltaTime = 10f;
            // Unity 6 caps editor time scale at 100. This still advances the deterministic
            // preparation/empty-day test quickly without emitting an unexpected error log.
            Time.timeScale = 100f;
            var deadline = Time.realtimeSinceStartup + 3f;
            while (dayCycle.CurrentDay < 1 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(dayCycle.CurrentDay, Is.EqualTo(1));
            Assert.That(dayCycle.IsPreparing, Is.True);
        }

        [UnityTest]
        public IEnumerator DefenderVictory_AfterTenDaysShowsTheDefenderWinOverlay()
        {
            GameBasicsTestSupport.CreateFoodStore();
            var dayCycle = GameBasicsTestSupport.CreateDayCycle();
            var overlay = GameBasicsTestSupport.CreateDefenderWinOverlay();
            yield return null;

            overlay.Bind(dayCycle);
            Time.maximumDeltaTime = 10f;
            // Keep within Unity 6's editor time-scale limit while accelerating the ten-day test.
            Time.timeScale = 100f;
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!overlay.IsVisible && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(dayCycle.CurrentDay, Is.EqualTo(10));
            Assert.That(dayCycle.IsComplete, Is.True);
            Assert.That(overlay.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator PauseOverlay_PausesAndResumesTheGameClock()
        {
            var pauseOverlay = GameBasicsTestSupport.CreatePauseOverlay();
            yield return null;

            Assert.That(pauseOverlay.ShowPause(null), Is.True);
            Assert.That(pauseOverlay.IsVisible, Is.True);
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            pauseOverlay.Play();
            yield return null;

            Assert.That(pauseOverlay.IsVisible, Is.False);
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        [Category("Stress")]
        public IEnumerator LongGamePoolingStress_ReusesObjectsWithoutLeakingState()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            var seenIds = new HashSet<long>();
            for (var index = 0; index < 300; index++)
            {
                var attackerObject = pool.Spawn(PooledObjectType.Attacker, Vector3.zero, Quaternion.identity);
                var instance = attackerObject.GetComponent<PooledInstance>();
                var attacker = attackerObject.GetComponent<BasicAttacker>();
                seenIds.Add(instance.RuntimeInstanceId);
                attacker.SetMoveDirection(Vector2.up);
                attacker.TakeDamage(attacker.RemainingHealth);
                instance.ReturnToPool();

                if (index % 25 == 0)
                {
                    yield return null;
                }
            }

            Assert.That(seenIds, Has.Count.EqualTo(1));
        }

        [UnityTest]
        [Category("Stress")]
        public IEnumerator LargeWavePoolingStress_CreatesAndRecyclesAFullWave()
        {
            var pool = GameBasicsTestSupport.CreateSpawnPool();
            Assert.That(pool, Is.Not.Null);
            yield return null;

            const int waveSize = 100;
            var firstWave = new List<GameObject>();
            var firstWaveIds = new HashSet<long>();
            for (var index = 0; index < waveSize; index++)
            {
                var attackerObject = pool.Spawn(PooledObjectType.Attacker, new Vector3(index * 0.02f, 0f), Quaternion.identity);
                firstWave.Add(attackerObject);
                Assert.That(firstWaveIds.Add(attackerObject.GetComponent<PooledInstance>().RuntimeInstanceId), Is.True);
            }

            foreach (var attackerObject in firstWave)
            {
                attackerObject.GetComponent<PooledInstance>().ReturnToPool();
            }

            var secondWave = new List<GameObject>();
            var secondWaveIds = new HashSet<long>();
            for (var index = 0; index < waveSize; index++)
            {
                var attackerObject = pool.Spawn(PooledObjectType.Attacker, Vector3.zero, Quaternion.identity);
                secondWaveIds.Add(attackerObject.GetComponent<PooledInstance>().RuntimeInstanceId);
                secondWave.Add(attackerObject);
            }

            foreach (var attackerObject in secondWave)
            {
                attackerObject.GetComponent<PooledInstance>().ReturnToPool();
            }

            Assert.That(secondWaveIds.SetEquals(firstWaveIds), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DefenderWeaponUpgradeModal_WaitsForThePlayerAndShowsOnlyItsTwoChoices()
        {
            GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var firstOption = Resources.Load<WeaponDefinition>("TADTests/UnarmedWeaponDefinition");
            var secondOption = Resources.Load<WeaponDefinition>("TADTests/AirRifleWeaponDefinition");
            Assert.That(firstOption, Is.Not.Null);
            Assert.That(secondOption, Is.Not.Null);

            var existingEventSystem = EventSystem.current;
            var overlayObject = new GameObject("TAD Test Defender Upgrade Overlay");
            overlayObject.SetActive(false);
            overlayObject.AddComponent<CanvasGroup>();
            var modal = overlayObject.AddComponent<ModalOverlay>();
            var overlay = overlayObject.AddComponent<DefenderLevelUpgradeOverlay>();
            var buttons = new Button[4];
            var labels = new Text[4];
            for (var index = 0; index < buttons.Length; index++)
            {
                var buttonObject = new GameObject(
                    $"Option{index}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                buttonObject.transform.SetParent(overlayObject.transform, worldPositionStays: false);
                buttons[index] = buttonObject.GetComponent<Button>();

                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(buttonObject.transform, worldPositionStays: false);
                labels[index] = labelObject.GetComponent<Text>();
            }

            overlay.Configure(modal, null, null, buttons, labels);
            overlayObject.SetActive(true);

            var bootstrapObject = new GameObject("TAD Test Defender Upgrade Bootstrap");
            bootstrapObject.SetActive(false);
            var bootstrap = bootstrapObject.AddComponent<DefenderLevelUpgradeBootstrap>();
            bootstrap.Configure(
                overlay,
                new[] { firstOption, secondOption },
                new[] { firstOption, firstOption },
                new[] { secondOption, secondOption });
            bootstrapObject.SetActive(true);

            var farmHand = GameBasicsTestSupport.CreateFarmHand();
            farmHand.SetWeapon(firstOption);
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
            Assert.That(farmHand.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            yield return null;

            Assert.That(farmHand.IsAwaitingWeaponUpgrade, Is.True);
            Assert.That(overlay.IsVisible, Is.False, "Reaching an upgrade must not interrupt gameplay automatically.");
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.False);
            Assert.That(bootstrap.TryShowUpgradeSelection(farmHand), Is.True);
            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(ModalOverlay.IsGameplayInputBlocked, Is.True);
            Assert.That(buttons[0].gameObject.activeSelf, Is.True);
            Assert.That(buttons[1].gameObject.activeSelf, Is.True);
            Assert.That(buttons[2].gameObject.activeSelf, Is.False);
            Assert.That(buttons[3].gameObject.activeSelf, Is.False);

            modal.Hide();
            Object.Destroy(attackerDefinition);
            if (existingEventSystem == null && EventSystem.current != null)
            {
                Object.Destroy(EventSystem.current.gameObject);
            }
        }

        [UnityTest]
        [Order(999)]
        public IEnumerator SceneTransition_LoadingOverlayLoadsTheConfiguredMainMenuScene()
        {
            var overlayObject = new GameObject("TAD Test Loading Overlay");
            overlayObject.SetActive(false);
            overlayObject.AddComponent<CanvasGroup>();
            var overlay = overlayObject.AddComponent<LevelLoadingOverlay>();
            overlayObject.SetActive(true);

            var loadedSceneName = string.Empty;
            void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                loadedSceneName = scene.name;
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            Assert.That(overlay.TryLoadScene(0, "MainMenu"), Is.True);
            var deadline = Time.realtimeSinceStartup + 10f;
            while (string.IsNullOrEmpty(loadedSceneName) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Assert.That(loadedSceneName, Is.EqualTo("MainMenu"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
        }

        [UnityTest]
        [Order(1000)]
        public IEnumerator ShippedGameplayScenes_LoadWithTheirRequiredGameplaySystems()
        {
            var shippedSceneNames = new[] { "Level1", "Level2", "Level3", "Level4" };

            foreach (var sceneName in shippedSceneNames)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                Assert.That(loadOperation, Is.Not.Null, $"{sceneName} must be loadable from Build Settings.");

                var deadline = Time.realtimeSinceStartup + 15f;
                while (!loadOperation.isDone && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(loadOperation.isDone, Is.True, $"{sceneName} did not finish loading in time.");
                yield return null;

                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
                Assert.That(Object.FindAnyObjectByType<FoodStore>(), Is.Not.Null,
                    $"{sceneName} is missing its Food Store.");
                Assert.That(Object.FindAnyObjectByType<SpawnPool>(), Is.Not.Null,
                    $"{sceneName} is missing its Spawn Pool.");
                Assert.That(Object.FindAnyObjectByType<FarmRouteMapper>(), Is.Not.Null,
                    $"{sceneName} is missing its route mapper.");
                Assert.That(Object.FindAnyObjectByType<WaveController>(), Is.Not.Null,
                    $"{sceneName} is missing its Wave Controller.");
                Assert.That(Object.FindAnyObjectByType<AttackerWaveQueue>(), Is.Not.Null,
                    $"{sceneName} is missing its attacker wave queue.");
                Assert.That(Object.FindAnyObjectByType<DayCycleController>(), Is.Not.Null,
                    $"{sceneName} is missing its day/night controller.");
                Assert.That(Object.FindObjectsByType<ResourceBank>(FindObjectsInactive.Exclude).Length,
                    Is.GreaterThanOrEqualTo(2), $"{sceneName} must have attacker and defender resource banks.");
                Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Not.Null,
                    $"{sceneName} is missing its EventSystem.");
            }
        }

        [UnityTest]
        [Order(1001)]
        public IEnumerator ShippedGameplayScenes_BuildVisibleSpawnToFoodRoutesForTheirConfiguredRoster()
        {
            var shippedSceneNames = new[] { "Level1", "Level2", "Level3", "Level4" };
            var attackerDefinitions = new List<AttackerDefinition>();
            var routePoints = new List<Vector3>();

            foreach (var sceneName in shippedSceneNames)
            {
                var loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                Assert.That(loadOperation, Is.Not.Null, $"{sceneName} must be loadable from Build Settings.");

                var deadline = Time.realtimeSinceStartup + 15f;
                while (!loadOperation.isDone && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(loadOperation.isDone, Is.True, $"{sceneName} did not finish loading in time.");
                // Allow Start on the pool, route mapper, and day-cycle owner to complete before
                // examining exactly the same flow fields used by a queued wave.
                yield return null;
                yield return new WaitForFixedUpdate();

                var spawnPool = Object.FindAnyObjectByType<SpawnPool>();
                var waveController = Object.FindAnyObjectByType<WaveController>();
                var routeMapper = Object.FindAnyObjectByType<FarmRouteMapper>();
                var dayCycle = Object.FindAnyObjectByType<DayCycleController>();
                Assert.That(spawnPool, Is.Not.Null, $"{sceneName} is missing its Spawn Pool.");
                Assert.That(waveController, Is.Not.Null, $"{sceneName} is missing its Wave Controller.");
                Assert.That(routeMapper, Is.Not.Null, $"{sceneName} is missing its route mapper.");
                Assert.That(dayCycle, Is.Not.Null, $"{sceneName} is missing its day-cycle controller.");
                // Use the same entry point as the queue. This is where the controller makes
                // sure the gameplay camera covers both route endpoints before the mapper
                // caches its visible-only flow field.
                Assert.That(waveController.CanQueuePlayerWave, Is.True,
                    $"{sceneName} cannot prepare a visible spawn-to-food route for a queued wave.");

                attackerDefinitions.Clear();
                spawnPool.CopyAvailableAttackerDefinitions(attackerDefinitions);
                Assert.That(attackerDefinitions, Is.Not.Empty, $"{sceneName} has no pooled attacker definitions.");

                foreach (var definition in attackerDefinitions)
                {
                    routePoints.Clear();
                    Assert.That(
                        routeMapper.TryGetFastestRoute(waveController.SpawnPosition, definition, routePoints),
                        Is.True,
                        $"{sceneName} has no visible spawn-to-food route for {definition.DisplayName}.");
                    Assert.That(routePoints, Is.Not.Empty,
                        $"{sceneName} produced an empty route for {definition.DisplayName}.");
                }

                // Level 1 intentionally pauses its clock behind the guided tutorial. Every other
                // shipped level must enter its normal preparation phase as soon as it loads.
                if (sceneName != "Level1")
                {
                    Assert.That(dayCycle.IsTutorialHeld, Is.False, $"{sceneName} should not hold its day cycle for a tutorial.");
                    Assert.That(dayCycle.IsPreparing, Is.True, $"{sceneName} did not start its preparation cycle.");
                }
            }
        }

        private static bool DefeatAttackersUntilLevel(
            FarmHand defender,
            AttackerDefinition attackerDefinition,
            int targetLevel)
        {
            var safetyCounter = 0;
            while (defender.CurrentLevel < targetLevel && safetyCounter++ < 80)
            {
                var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
                if (!defender.TryDamageAttacker(attacker, attacker.RemainingHealth))
                {
                    return false;
                }
            }

            return defender.CurrentLevel == targetLevel;
        }

        private static TerrainSpeedModifier CreateTerrainSpeedModifier(
            string name,
            TerrainSpeedEffect effect,
            Vector2 position,
            Vector2? size = null)
        {
            var terrainObject = new GameObject("TAD Test " + name + " Terrain");
            terrainObject.layer = LayerMask.NameToLayer("Environment");
            terrainObject.transform.position = position;
            terrainObject.SetActive(false);
            var terrainCollider = terrainObject.AddComponent<PolygonCollider2D>();
            var halfSize = (size ?? new Vector2(2f, 2f)) * 0.5f;
            terrainCollider.SetPath(0, new[]
            {
                new Vector2(-halfSize.x, -halfSize.y),
                new Vector2(-halfSize.x, halfSize.y),
                new Vector2(halfSize.x, halfSize.y),
                new Vector2(halfSize.x, -halfSize.y)
            });
            var modifier = terrainObject.AddComponent<TerrainSpeedModifier>();
            modifier.Configure(effect);
            terrainObject.SetActive(true);
            return modifier;
        }

        private static WeaponDefinition CreateTestWeapon(
            string displayName,
            ProjectileDefinition projectile,
            int cost = 0,
            WeaponDefinition requiredPreviousWeapon = null,
            bool unlocksFinalEvolution = false)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            SetPrivateField(weapon, "displayName", displayName);
            SetPrivateField(weapon, "cost", cost);
            SetPrivateField(weapon, "range", 1f);
            SetPrivateField(weapon, "attackSpeed", 1f);
            SetPrivateField(weapon, "damage", 1f);
            SetPrivateField(weapon, "projectileDefinition", projectile);
            SetPrivateField(weapon, "requiredPreviousWeapon", requiredPreviousWeapon);
            SetPrivateField(weapon, "unlocksFinalEvolution", unlocksFinalEvolution);
            return weapon;
        }

        private static ProjectileDefinition CreateTestProjectileDefinition(ProjectileType projectileType)
        {
            var definition = ScriptableObject.CreateInstance<ProjectileDefinition>();
            SetPrivateField(definition, "projectileType", projectileType);
            SetPrivateField(definition, "projectileBehavior", ProjectileBehavior.Direct);
            SetPrivateField(definition, "projectileLifetimeMode", ProjectileLifetimeMode.Timed);
            SetPrivateField(definition, "projectileSpeed", 10f);
            SetPrivateField(definition, "projectileLifetime", 3f);
            return definition;
        }

        private static FarmHandProjectile CreateTestProjectile()
        {
            var projectileObject = new GameObject("TAD Test Cover Projectile");
            projectileObject.SetActive(false);
            projectileObject.AddComponent<SpriteRenderer>();
            projectileObject.AddComponent<Rigidbody2D>();
            projectileObject.AddComponent<CircleCollider2D>();
            var projectile = projectileObject.AddComponent<FarmHandProjectile>();
            projectileObject.SetActive(true);
            return projectile;
        }

        private static RollingProjectile CreateTestRollingProjectile()
        {
            var projectileObject = new GameObject("TAD Test Bounce Projectile");
            projectileObject.SetActive(false);
            projectileObject.AddComponent<SpriteRenderer>();
            projectileObject.AddComponent<Rigidbody2D>();
            projectileObject.AddComponent<CircleCollider2D>();
            var projectile = projectileObject.AddComponent<RollingProjectile>();
            projectileObject.SetActive(true);
            return projectile;
        }

        private static Bounds GetSpritePhysicsBounds(SpriteRenderer visual)
        {
            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.sprite, Is.Not.Null);
            Assert.That(visual.sprite.GetPhysicsShapeCount(), Is.GreaterThan(0));

            var sourcePath = new List<Vector2>();
            var hasPoint = false;
            var bounds = new Bounds();
            for (var pathIndex = 0; pathIndex < visual.sprite.GetPhysicsShapeCount(); pathIndex++)
            {
                sourcePath.Clear();
                visual.sprite.GetPhysicsShape(pathIndex, sourcePath);
                foreach (var point in sourcePath)
                {
                    var worldPoint = visual.transform.TransformPoint(point);
                    if (!hasPoint)
                    {
                        bounds = new Bounds(worldPoint, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(worldPoint);
                    }
                }
            }

            Assert.That(hasPoint, Is.True, "The visual sprite needs a usable generated physics outline.");
            return bounds;
        }

        private static List<BasicAttacker> FindActivePoolAttackers(SpawnPool owner)
        {
            var attackers = new List<BasicAttacker>();
            foreach (var attacker in Object.FindObjectsByType<BasicAttacker>(FindObjectsInactive.Exclude))
            {
                if (attacker != null && attacker.TryGetComponent<PooledInstance>(out var pooledInstance) &&
                    pooledInstance.Owner == owner)
                {
                    attackers.Add(attacker);
                }
            }

            return attackers;
        }

        private static AttackerDefinition GetSpawnableAttackerDefinition(SpawnPool pool)
        {
            if (pool == null)
            {
                return null;
            }

            var definitions = new List<AttackerDefinition>();
            pool.CopyAvailableAttackerDefinitions(definitions);
            return definitions.Find(definition => definition != null && definition.SendSize > 0);
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private test field: {fieldName}");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private test field: {fieldName}");
            return (T)field.GetValue(target);
        }
    }
}
