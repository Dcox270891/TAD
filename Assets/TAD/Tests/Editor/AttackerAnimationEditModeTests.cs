using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>Guards the compact directional-animation contract shared by the full animal roster.</summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class AttackerAnimationEditModeTests
    {
        private const string AttackerFolder = "Assets/TAD/Data/Attackers";
        private const string DeathEffectPath = "Assets/TAD/Art/Effects/TAD_AnimalDeathDust.png";

        [Test]
        public void EveryProductionAnimal_HasACompleteProceduralDirectionalAndDeathPresentation()
        {
            var attackers = AssetDatabase.FindAssets("t:AttackerDefinition", new[] { AttackerFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AttackerDefinition>)
                .Where(definition => definition != null)
                .ToArray();

            Assert.That(attackers, Has.Length.EqualTo(40));
            foreach (var attacker in attackers)
            {
                var visuals = attacker.VisualDefinition;
                Assert.That(visuals, Is.Not.Null, attacker.DisplayName + " is missing visual data.");
                Assert.That(visuals.HasMovementSprites, Is.True, attacker.DisplayName + " is missing movement frames.");
                Assert.That(visuals.HasDeathSprites, Is.True, attacker.DisplayName + " is missing death frames.");
                Assert.That(visuals.HasDeathEffectSprites, Is.True, attacker.DisplayName + " is missing death-effect frames.");
                Assert.That(visuals.UsesProceduralDirectionalAnimation, Is.True,
                    attacker.DisplayName + " must use the atlas directional-pose presentation.");
            }
        }

        [Test]
        public void SharedDeathEffectSheet_ProvidesFourDistinctFrames()
        {
            var frames = AssetDatabase.LoadAllAssetsAtPath(DeathEffectPath)
                .OfType<Sprite>()
                .ToArray();

            Assert.That(frames, Has.Length.EqualTo(AttackerVisualDefinition.DeathFrameCount));
            Assert.That(frames.Select(frame => frame.name).Distinct().Count(), Is.EqualTo(frames.Length));
        }
    }
}
