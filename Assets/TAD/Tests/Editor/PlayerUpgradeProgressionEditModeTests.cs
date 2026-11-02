using System.Collections.Generic;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Progression;

namespace TAD.Tests.Editor
{
    public sealed class PlayerUpgradeProgressionEditModeTests
    {
        [Test]
        public void PlayerLevelProgression_CapsAtLevelOneHundredWithoutRolloverProgress()
        {
            var profile = new PlayerProfileData
            {
                playerLevel = PlayerLevelProgression.MaximumLevel - 1,
                playerExp = PlayerLevelProgression.GetExperienceRequiredForNextLevel(
                    PlayerLevelProgression.MaximumLevel - 1) - 1f
            };

            var reward = PlayerLevelProgression.AwardMatchExperience(profile, 100000f, playerWon: true);

            Assert.That(reward.ResultingLevel, Is.EqualTo(PlayerLevelProgression.MaximumLevel));
            Assert.That(profile.playerLevel, Is.EqualTo(PlayerLevelProgression.MaximumLevel));
            Assert.That(profile.playerExp, Is.EqualTo(0f));
            Assert.That(PlayerLevelProgression.GetExperienceRequiredForNextLevel(profile.playerLevel), Is.EqualTo(0f));
        }

        [Test]
        public void UpgradeChoices_LevelTwoCreatesOneOrderedAttackerRandomDefenderChoice()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 2);

            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(1));
            var standard = FindChoice(profile, PlayerUpgradeRewardSlot.Standard);
            Assert.That(standard.offeredUpgradeIds, Has.Count.EqualTo(3));
            Assert.That(standard.offeredUpgradeIds, Is.Unique);
            Assert.That(
                PlayerUpgradeProgression.TryGetDefinition(
                    standard.offeredUpgradeIds[0],
                    out var attackerDefinition),
                Is.True);
            Assert.That(attackerDefinition.Category, Is.EqualTo(PlayerUpgradeCategory.Attacker));
            Assert.That(
                PlayerUpgradeProgression.TryGetDefinition(standard.offeredUpgradeIds[1], out var randomDefinition),
                Is.True);
            Assert.That(
                randomDefinition.Category,
                Is.EqualTo(PlayerUpgradeCategory.Attacker).Or.EqualTo(PlayerUpgradeCategory.Defender));
            Assert.That(
                PlayerUpgradeProgression.TryGetDefinition(standard.offeredUpgradeIds[2], out var defenderDefinition),
                Is.True);
            Assert.That(defenderDefinition.Category, Is.EqualTo(PlayerUpgradeCategory.Defender));
        }

        [Test]
        public void UpgradeChoices_LevelTwentyCreatesAStandardChoiceThenOneGeneralChoice()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 20);

            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(2));
            var pending = new List<PendingPlayerUpgradeChoiceData>();
            Assert.That(PlayerUpgradeProgression.CopyPendingChoicesForLevel(profile, 20, pending), Is.EqualTo(2));
            Assert.That(pending[0].rewardSlot, Is.EqualTo(PlayerUpgradeRewardSlot.Standard));
            Assert.That(pending[1].rewardSlot, Is.EqualTo(PlayerUpgradeRewardSlot.General));
            var general = FindChoice(profile, PlayerUpgradeRewardSlot.General);
            Assert.That(general.offeredUpgradeIds, Has.Count.GreaterThan(0));
            Assert.That(PlayerUpgradeProgression.TryGetDefinition(general.offeredUpgradeIds[0], out var definition), Is.True);
            Assert.That(definition.Category, Is.EqualTo(PlayerUpgradeCategory.General));
        }

        [Test]
        public void SelectingOneStandardCardIncreasesItsRankAndConsumesTheWholeChoice()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 2);
            var standardCard = FindChoice(profile, PlayerUpgradeRewardSlot.Standard);
            var selectedId = standardCard.offeredUpgradeIds[1];

            Assert.That(
                PlayerUpgradeProgression.TryChooseUpgrade(
                    profile,
                    standardCard.playerLevel,
                    standardCard.rewardSlot,
                    selectedId),
                Is.True);
            Assert.That(PlayerUpgradeProgression.GetRank(profile, selectedId), Is.EqualTo(1));
            Assert.That(profile.pendingPlayerUpgradeChoices, Is.Empty);
        }

        [Test]
        public void LegacyPendingChoices_MigrateToOneOrderedStandardChoiceWithoutChangingRanks()
        {
            var profile = new PlayerProfileData
            {
                pendingPlayerUpgradeChoices = new List<PendingPlayerUpgradeChoiceData>
                {
                    new()
                    {
                        playerLevel = 2,
                        rewardSlot = PlayerUpgradeRewardSlot.Attacker,
                        offeredUpgradeIds = new List<string>
                        {
                            PlayerUpgradeProgression.GetAttackerUpgradeId(
                                AttackerArchetype.Basic,
                                AttackerUpgradeStat.Health)
                        }
                    },
                    new()
                    {
                        playerLevel = 2,
                        rewardSlot = PlayerUpgradeRewardSlot.Random,
                        offeredUpgradeIds = new List<string>
                        {
                            PlayerUpgradeProgression.GetDefenderUpgradeId(
                                ProjectileType.Melee,
                                DefenderUpgradeStat.Damage)
                        }
                    },
                    new()
                    {
                        playerLevel = 2,
                        rewardSlot = PlayerUpgradeRewardSlot.Defender,
                        offeredUpgradeIds = new List<string>
                        {
                            PlayerUpgradeProgression.GetDefenderUpgradeId(
                                ProjectileType.Ranged,
                                DefenderUpgradeStat.Range)
                        }
                    }
                }
            };

            PlayerUpgradeProgression.MigrateLegacyPendingChoicesToStandard(profile);

            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(1));
            var standard = FindChoice(profile, PlayerUpgradeRewardSlot.Standard);
            Assert.That(standard.offeredUpgradeIds, Has.Count.EqualTo(3));
            Assert.That(PlayerUpgradeProgression.GetRank(profile, standard.offeredUpgradeIds[0]), Is.EqualTo(0));
        }

        [Test]
        public void PendingCardsDropUpgradesThatAreAlreadyAtMaximumRank()
        {
            var completedId = PlayerUpgradeProgression.GetAttackerUpgradeId(
                AttackerArchetype.Basic,
                AttackerUpgradeStat.Health);
            var availableId = PlayerUpgradeProgression.GetAttackerUpgradeId(
                AttackerArchetype.Basic,
                AttackerUpgradeStat.Speed);
            var profile = new PlayerProfileData
            {
                playerUpgradeRanks = new List<PlayerUpgradeRankData>
                {
                    new()
                    {
                        upgradeId = completedId,
                        rank = PlayerUpgradeProgression.AttackerMaximumRank
                    }
                },
                pendingPlayerUpgradeChoices = new List<PendingPlayerUpgradeChoiceData>
                {
                    new()
                    {
                        playerLevel = 2,
                        rewardSlot = PlayerUpgradeRewardSlot.Attacker,
                        offeredUpgradeIds = new List<string> { completedId, availableId }
                    }
                }
            };
            var pending = new List<PendingPlayerUpgradeChoiceData>();

            Assert.That(PlayerUpgradeProgression.CopyPendingChoicesForLevel(profile, 2, pending), Is.EqualTo(1));
            Assert.That(pending[0].offeredUpgradeIds, Does.Not.Contain(completedId));
            Assert.That(pending[0].offeredUpgradeIds, Does.Contain(availableId));
        }

        [Test]
        public void UpgradeMultipliers_UseTwoPercentPerRankAndRespectCategory()
        {
            var profile = new PlayerProfileData();
            var attackerId = PlayerUpgradeProgression.GetAttackerUpgradeId(
                AttackerArchetype.Fast,
                AttackerUpgradeStat.Speed);
            var defenderId = PlayerUpgradeProgression.GetDefenderUpgradeId(
                ProjectileType.Ranged,
                DefenderUpgradeStat.Damage);
            var migrationProfile = profile;
            for (var rank = 0; rank < 5; rank++)
            {
                PlayerUpgradeProgression.GrantUpgradeForMigration(migrationProfile, attackerId);
            }

            // Migration grants the completed rank in one operation. The values below verify both
            // the ten-percent attacker cap and a defender rank remaining independent from it.
            Assert.That(
                PlayerUpgradeProgression.GetAttackerMultiplier(
                    profile,
                    AttackerArchetype.Fast,
                    AttackerUpgradeStat.Speed),
                Is.EqualTo(1.10f).Within(0.0001f));
            Assert.That(
                PlayerUpgradeProgression.GetDefenderMultiplier(
                    profile,
                    ProjectileType.Ranged,
                    DefenderUpgradeStat.Damage),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PlayerUpgradeProgression.GetRank(profile, defenderId), Is.EqualTo(0));
        }

        [Test]
        public void ChallengeTierUnlocks_TierTwoAtTenAndTierThreeAtTwenty()
        {
            Assert.That(PlayerUpgradeProgression.GetUnlockedChallengeTier(1), Is.EqualTo(1));
            Assert.That(PlayerUpgradeProgression.GetUnlockedChallengeTier(9), Is.EqualTo(1));
            Assert.That(PlayerUpgradeProgression.GetUnlockedChallengeTier(10), Is.EqualTo(2));
            Assert.That(PlayerUpgradeProgression.GetUnlockedChallengeTier(20), Is.EqualTo(3));
            Assert.That(PlayerUpgradeProgression.GetUnlockedChallengeTier(100), Is.EqualTo(10));
        }

        [Test]
        public void ChoosingOneStandardRewardPerLevelConsumesTheCorrectNumberOfChoices()
        {
            var profile = new PlayerProfileData();
            var pending = new List<PendingPlayerUpgradeChoiceData>();
            var selectedChoiceCount = 0;

            for (var level = PlayerLevelProgression.StartingLevel + 1;
                 level <= PlayerLevelProgression.MaximumLevel;
                 level++)
            {
                PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, level);

                // One normal choice is consumed per level; milestone levels additionally retain
                // their general choice. Selecting deterministic cards proves neither group can
                // leave a stalled pending reward behind.
                while (PlayerUpgradeProgression.CopyPendingChoicesForLevel(profile, level, pending) > 0)
                {
                    var choice = pending[0];
                    Assert.That(choice.offeredUpgradeIds, Is.Not.Empty);
                    Assert.That(
                        PlayerUpgradeProgression.TryChooseUpgrade(
                            profile,
                            choice.playerLevel,
                            choice.rewardSlot,
                            choice.offeredUpgradeIds[0]),
                        Is.True);
                    selectedChoiceCount++;
                }
            }

            Assert.That(PlayerUpgradeProgression.GetPendingChoiceCount(profile), Is.EqualTo(0));
            Assert.That(
                selectedChoiceCount,
                Is.EqualTo((PlayerLevelProgression.MaximumLevel - PlayerLevelProgression.StartingLevel) + 5));

            var totalRanks = 0;
            foreach (var definition in PlayerUpgradeProgression.AllDefinitions)
            {
                var rank = PlayerUpgradeProgression.GetRank(profile, definition.Id);
                Assert.That(rank, Is.InRange(0, definition.MaxRank), definition.Id);
                totalRanks += rank;
            }

            Assert.That(totalRanks, Is.EqualTo(selectedChoiceCount));
        }

        private static PendingPlayerUpgradeChoiceData FindChoice(
            PlayerProfileData profile,
            PlayerUpgradeRewardSlot slot)
        {
            foreach (var choice in profile.pendingPlayerUpgradeChoices)
            {
                if (choice != null && choice.rewardSlot == slot)
                {
                    return choice;
                }
            }

            Assert.Fail($"Expected a pending {slot} choice.");
            return null;
        }
    }
}
