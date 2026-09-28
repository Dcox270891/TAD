using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Environment;
using TAD.Match;
using TAD.Pooling;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
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
            yield return GameBasicsTestSupport.DestroyAllTestObjects();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.maximumDeltaTime = originalMaximumDeltaTime;
            Time.timeScale = 1f;
            MatchRoleSelection.Select(PlayerRole.Defender);
            yield return GameBasicsTestSupport.DestroyAllTestObjects();
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
        public IEnumerator Defenders_LevelAtTheCorrectThresholdChooseAtLevelThreeAndStopAtLevelTen()
        {
            GameBasicsTestSupport.CreateBank(BankSide.Defender);
            var defender = GameBasicsTestSupport.CreateFarmHand();
            var attackerDefinition = ScriptableObject.CreateInstance<AttackerDefinition>();
            var upgrade = ScriptableObject.CreateInstance<WeaponDefinition>();

            for (var index = 0; index < 2; index++)
            {
                var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
                Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            }

            yield return null;
            Assert.That(defender.CurrentLevel, Is.EqualTo(3));
            Assert.That(defender.IsAwaitingLevelThreeUpgrade, Is.True);
            Assert.That(defender.TryChooseLevelThreeUpgrade(upgrade), Is.True);
            Assert.That(defender.WeaponDefinition, Is.EqualTo(upgrade));
            Assert.That(defender.HasSelectedLevelThreeUpgrade, Is.True);

            for (var index = defender.CurrentLevel; index < defender.MaxLevel + 5; index++)
            {
                var attacker = GameBasicsTestSupport.CreateAttacker(attackerDefinition);
                Assert.That(defender.TryDamageAttacker(attacker, attacker.RemainingHealth), Is.True);
            }

            Assert.That(defender.CurrentLevel, Is.EqualTo(defender.MaxLevel));
            Assert.That(defender.IsAtMaxLevel, Is.True);
            Assert.That(defender.CurrentExp, Is.EqualTo(0f));
            Assert.That(defender.TotalExperience, Is.GreaterThanOrEqualTo(defender.ExpMax * (defender.MaxLevel - 1)));

            Object.Destroy(attackerDefinition);
            Object.Destroy(upgrade);
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
            defender.SetRangeIndicatorVisible(true);
            defender.SetTargetingMode(DefenderTargetingMode.HighestHealth);
            defenderObject.GetComponent<PooledInstance>().ReturnToPool();
            var reusedDefender = pool.Spawn(PooledObjectType.Defender, Vector3.zero, Quaternion.identity).GetComponent<FarmHand>();
            Assert.That(reusedDefender.CurrentLevel, Is.EqualTo(1));
            Assert.That(reusedDefender.CurrentExp, Is.EqualTo(0f));
            Assert.That(reusedDefender.TotalExperience, Is.EqualTo(0f));
            Assert.That(reusedDefender.IsRangeIndicatorVisible, Is.False);
            Assert.That(reusedDefender.TargetingMode, Is.EqualTo(DefenderTargetingMode.FirstEnemyInRange));

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
            Assert.That(farmHand.PlacementCollider.bounds.size.x,
                Is.EqualTo(farmHand.Visual.bounds.size.x).Within(0.01f));
            Assert.That(farmHand.PlacementCollider.bounds.size.y,
                Is.EqualTo(farmHand.Visual.bounds.size.y).Within(0.01f));
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
            var greyhound = Resources.Load<AttackerDefinition>("TADTests/GreyhoundDefinition");
            var eagle = Resources.Load<AttackerDefinition>("TADTests/EagleDefinition");
            Assert.That(bull, Is.Not.Null);
            Assert.That(greyhound, Is.Not.Null);
            Assert.That(eagle, Is.Not.Null);

            var sensorWeapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            var farmHand = GameBasicsTestSupport.CreateFarmHand();
            farmHand.SetWeapon(sensorWeapon);

            var firstBull = GameBasicsTestSupport.CreateAttacker(bull);
            var secondBull = GameBasicsTestSupport.CreateAttacker(bull);
            var firstGreyhound = GameBasicsTestSupport.CreateAttacker(greyhound);
            var secondGreyhound = GameBasicsTestSupport.CreateAttacker(greyhound);
            var firstEagle = GameBasicsTestSupport.CreateAttacker(eagle);
            var secondEagle = GameBasicsTestSupport.CreateAttacker(eagle);
            var attackers = new[]
            {
                firstBull,
                secondBull,
                firstGreyhound,
                secondGreyhound,
                firstEagle,
                secondEagle
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
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstGreyhound));

            farmHand.SetTargetingMode(DefenderTargetingMode.HighestKillGain);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstEagle));

            farmHand.SetTargetingMode(DefenderTargetingMode.LastEnemyInRange);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondEagle));
            secondEagle.transform.position = new Vector2(3f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(firstEagle));

            firstEagle.TakeDamage(firstEagle.RemainingHealth);
            Assert.That(farmHand.CurrentTarget, Is.EqualTo(secondGreyhound));

            Object.Destroy(sensorWeapon);
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

        [UnityTest]
        public IEnumerator AttackerQueue_RefundsRemovedGroupsAndKeepsWaveIncomePersistent()
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
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome));

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
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(definition.WaveIncome));
            Assert.That(panel.IsShowing, Is.False);

            var incomeSnapshot = fixture.AttackerBank.CurrentIncome;
            var goldBeforeIncome = fixture.AttackerBank.CurrentGold;
            Assert.That(fixture.AttackerBank.PayIncome(incomeSnapshot), Is.EqualTo(incomeSnapshot));
            Assert.That(fixture.AttackerBank.CurrentGold, Is.EqualTo(goldBeforeIncome + incomeSnapshot));
            Assert.That(fixture.AttackerBank.CurrentIncome, Is.EqualTo(incomeSnapshot));
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
    }
}
