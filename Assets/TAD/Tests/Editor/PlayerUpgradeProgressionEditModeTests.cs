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
        public void UpgradeChoices_LevelTwoCreatesAttackerDefenderAndRandomCards()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 2);

            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(3));
            Assert.That(FindChoice(profile, PlayerUpgradeRewardSlot.Attacker).offeredUpgradeIds, Has.Count.GreaterThan(0));
            Assert.That(FindChoice(profile, PlayerUpgradeRewardSlot.Defender).offeredUpgradeIds, Has.Count.GreaterThan(0));
            Assert.That(FindChoice(profile, PlayerUpgradeRewardSlot.Random).offeredUpgradeIds, Has.Count.GreaterThan(0));
            Assert.That(
                PlayerUpgradeProgression.TryGetDefinition(
                    FindChoice(profile, PlayerUpgradeRewardSlot.Random).offeredUpgradeIds[0],
                    out var randomDefinition),
                Is.True);
            Assert.That(randomDefinition.Category, Is.EqualTo(PlayerUpgradeCategory.Attacker));
        }

        [Test]
        public void UpgradeChoices_LevelTwentyAlsoCreatesOneGeneralCard()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 20);

            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(4));
            var general = FindChoice(profile, PlayerUpgradeRewardSlot.General);
            Assert.That(general.offeredUpgradeIds, Has.Count.GreaterThan(0));
            Assert.That(PlayerUpgradeProgression.TryGetDefinition(general.offeredUpgradeIds[0], out var definition), Is.True);
            Assert.That(definition.Category, Is.EqualTo(PlayerUpgradeCategory.General));
        }

        [Test]
        public void SelectingPersistedCardIncreasesItsRankAndRemovesOnlyThatCard()
        {
            var profile = new PlayerProfileData();
            PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, 2);
            var attackerCard = FindChoice(profile, PlayerUpgradeRewardSlot.Attacker);
            var selectedId = attackerCard.offeredUpgradeIds[0];

            Assert.That(
                PlayerUpgradeProgression.TryChooseUpgrade(
                    profile,
                    attackerCard.playerLevel,
                    attackerCard.rewardSlot,
                    selectedId),
                Is.True);
            Assert.That(PlayerUpgradeProgression.GetRank(profile, selectedId), Is.EqualTo(1));
            Assert.That(profile.pendingPlayerUpgradeChoices, Has.Count.EqualTo(2));
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
        public void ChoosingEveryAvailableRewardThroughLevelOneHundredCompletesTheEntireTree()
        {
            var profile = new PlayerProfileData();
            var pending = new List<PendingPlayerUpgradeChoiceData>();

            for (var level = PlayerLevelProgression.StartingLevel + 1;
                 level <= PlayerLevelProgression.MaximumLevel;
                 level++)
            {
                PlayerUpgradeProgression.EnqueueChoicesForLevel(profile, level);

                // A player may take any offered card. Selecting the first available option gives
                // this test a deterministic path while proving that capped ranks cannot trap a
                // later reward card or leave the account tree incomplete at level 100.
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
                }
            }

            Assert.That(PlayerUpgradeProgression.GetPendingChoiceCount(profile), Is.EqualTo(0));
            foreach (var definition in PlayerUpgradeProgression.AllDefinitions)
            {
                Assert.That(
                    PlayerUpgradeProgression.GetRank(profile, definition.Id),
                    Is.EqualTo(definition.MaxRank),
                    definition.Id);
            }
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
