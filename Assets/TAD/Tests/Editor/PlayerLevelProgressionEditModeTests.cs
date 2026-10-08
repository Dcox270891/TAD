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
    public sealed class PlayerLevelProgressionEditModeTests
    {
        [Test]
        public void PlayerLevelProgression_ThreeStandardWinsAdvanceOneLevelEachThenRequireTwoWinsAtLevelFour()
        {
            var profile = new PlayerProfileData();

            var firstWin = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
            Assert.That(firstWin.LevelsGained, Is.EqualTo(1));
            Assert.That(profile.playerLevel, Is.EqualTo(2));
            Assert.That(profile.playerExp, Is.EqualTo(25f));

            var secondWin = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
            Assert.That(secondWin.LevelsGained, Is.EqualTo(1));
            Assert.That(profile.playerLevel, Is.EqualTo(3));
            Assert.That(profile.playerExp, Is.EqualTo(25f));

            var thirdWin = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
            Assert.That(thirdWin.LevelsGained, Is.EqualTo(1));
            Assert.That(profile.playerLevel, Is.EqualTo(4));
            Assert.That(profile.playerExp, Is.EqualTo(25f));
            Assert.That(PlayerLevelProgression.GetExperienceRequiredForNextLevel(profile.playerLevel), Is.EqualTo(200f));

            PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
            Assert.That(profile.playerLevel, Is.EqualTo(4));
            PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
            Assert.That(profile.playerLevel, Is.EqualTo(5));
        }

        [Test]
        public void PlayerLevelProgression_ThreeStandardLossesReachTheFirstLevelAtTwentyFivePercentReward()
        {
            var profile = new PlayerProfileData();

            for (var loss = 0; loss < PlayerLevelProgression.LossesForFirstLevel; loss++)
            {
                var reward = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: false);
                Assert.That(reward.AwardedExperience, Is.EqualTo(25f));
            }

            Assert.That(profile.playerLevel, Is.EqualTo(2));
            Assert.That(profile.playerExp, Is.EqualTo(0f));
            Assert.That(profile.totalPlayerExperience, Is.EqualTo(75f));
        }

        [Test]
        public void PlayerLevelProgression_RewardRetainsTheProgressShownBeforeTheMatch()
        {
            var profile = new PlayerProfileData
            {
                playerLevel = 2,
                playerExp = 40f,
                totalPlayerExperience = 40f
            };

            var reward = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);

            Assert.That(reward.StartingLevel, Is.EqualTo(2));
            Assert.That(reward.StartingExperience, Is.EqualTo(40f));
            Assert.That(reward.StartingExperienceRequired, Is.EqualTo(100f));
            Assert.That(reward.AwardedExperience, Is.EqualTo(100f));
            Assert.That(reward.ResultingLevel, Is.EqualTo(3));
            Assert.That(reward.CurrentExperience, Is.EqualTo(40f));
        }

        [Test]
        public void MatchExperienceLedger_UsesActualCombatExperienceForDispatchedAttackersAndTheSelectedSide()
        {
            var attacker = ScriptableObject.CreateInstance<AttackerDefinition>();
            var ledger = new MatchExperienceLedger();
            var expectedPerAttacker = DefenderRewardCalculator.Calculate(
                attacker.ExpGiven,
                attacker.SendSize,
                eligibleAssistCount: 0).TotalExperience;

            ledger.RecordAttackerDispatched(attacker);
            ledger.RecordDefenderExperience(7.5f);

            Assert.That(ledger.AttackerExperienceSent, Is.EqualTo(expectedPerAttacker));
            Assert.That(ledger.GetExperienceFor(PlayerRole.Attacker), Is.EqualTo(expectedPerAttacker));
            Assert.That(ledger.GetExperienceFor(PlayerRole.Defender), Is.EqualTo(7.5f));

            Object.DestroyImmediate(attacker);
        }

        [Test]
        public void MatchExperienceLedger_UsesTheEffectiveAttackerExperienceWhenProvided()
        {
            var attacker = ScriptableObject.CreateInstance<AttackerDefinition>();
            var ledger = new MatchExperienceLedger();
            const int effectiveExperienceGiven = 6;
            var expectedPerAttacker = DefenderRewardCalculator.Calculate(
                effectiveExperienceGiven,
                attacker.SendSize,
                eligibleAssistCount: 0).TotalExperience;

            ledger.RecordAttackerDispatched(attacker, effectiveExperienceGiven);

            Assert.That(ledger.AttackerExperienceSent, Is.EqualTo(expectedPerAttacker));
            Object.DestroyImmediate(attacker);
        }
    }
}
