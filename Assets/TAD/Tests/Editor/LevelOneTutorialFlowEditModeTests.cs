using System;
using System.Collections.Generic;
using NUnit.Framework;
using TAD.Match;
using TAD.Tutorials;
using TAD.Waves;
using UnityEngine;

namespace TAD.Tests.Editor
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class LevelOneTutorialFlowEditModeTests
    {
        [Test]
        public void AttackerTutorial_ExplainsBothSidesAndGatesTheEssentialAttackerActions()
        {
            var steps = LevelOneTutorialFlow.GetSteps(PlayerRole.Attacker);

            Assert.That(steps.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.QueueAttackers), Is.True);
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.OpenAttackerInfo), Is.True);
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.DispatchWave), Is.True);
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.ObserveAttackPhase), Is.True);
            Assert.That(ContainsText(steps, "Defender"), Is.True);
            Assert.That(ContainsText(steps, "day/night"), Is.True);
            Assert.That(AllStepsHaveUsableCopy(steps), Is.True);
        }

        [Test]
        public void DefenderTutorial_ExplainsBothSidesAndGatesTheEssentialDefenderActions()
        {
            var steps = LevelOneTutorialFlow.GetSteps(PlayerRole.Defender);

            Assert.That(steps.Count, Is.GreaterThanOrEqualTo(7));
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.ConfirmFarmHandPlacement), Is.True);
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.OpenDefenderInfo), Is.True);
            Assert.That(HasRequirement(steps, LevelOneTutorialRequirement.ObserveAttackPhase), Is.True);
            Assert.That(ContainsText(steps, "Attacker"), Is.True);
            Assert.That(ContainsText(steps, "day/night"), Is.True);
            Assert.That(AllStepsHaveUsableCopy(steps), Is.True);
        }

        [Test]
        public void TutorialHold_FreezesOnlyTheDayCycleClockState()
        {
            var gameObject = new GameObject("Tutorial Day Cycle Test");
            var dayCycle = gameObject.AddComponent<DayCycleController>();
            var previousTimeScale = Time.timeScale;

            try
            {
                Time.timeScale = 0.75f;
                dayCycle.SetTutorialHeld(true);
                Assert.That(dayCycle.IsTutorialHeld, Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(0.75f));

                dayCycle.SetTutorialHeld(false);
                Assert.That(dayCycle.IsTutorialHeld, Is.False);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static bool HasRequirement(
            IReadOnlyList<LevelOneTutorialStep> steps,
            LevelOneTutorialRequirement requirement)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                if (steps[index].Requirement == requirement)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsText(IReadOnlyList<LevelOneTutorialStep> steps, string text)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                if (step.Heading.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    step.Body.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllStepsHaveUsableCopy(IReadOnlyList<LevelOneTutorialStep> steps)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                if (string.IsNullOrWhiteSpace(step.Heading) ||
                    string.IsNullOrWhiteSpace(step.Body) ||
                    string.IsNullOrWhiteSpace(step.RequirementText))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
