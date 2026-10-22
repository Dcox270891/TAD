using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TAD.Attackers;
using TAD.Defenders;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>Guards the authored sound library so new data entries cannot silently become mute.</summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameplayAudioEditModeTests
    {
        private const string AttackerFolder = "Assets/TAD/Data/Attackers";
        private const string DefenderFolder = "Assets/TAD/Data/Defenders";
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";

        [Test]
        public void EveryProductionAttackerAndProjectile_HasItsOwnSoundCue()
        {
            var attackers = LoadAssets<AttackerDefinition>(AttackerFolder);
            var projectiles = LoadAssets<ProjectileDefinition>(DefenderFolder);

            Assert.That(attackers, Has.Count.EqualTo(40));
            Assert.That(projectiles, Has.Count.EqualTo(15));
            Assert.That(attackers.All(definition => definition.EntrySound != null), Is.True,
                "Every animal must have its reusable cue/spawn/food-store cry.");
            Assert.That(attackers.All(definition => definition.DeathSound != null), Is.True,
                "Every animal must have a defeat sound.");
            Assert.That(projectiles.All(definition => definition.FiringSound != null), Is.True,
                "Every projectile must have a firing cue.");

            var attackerPaths = attackers
                .Select(definition => AssetDatabase.GetAssetPath(definition.EntrySound))
                .ToArray();
            var attackerDeathPaths = attackers
                .Select(definition => AssetDatabase.GetAssetPath(definition.DeathSound))
                .ToArray();
            var projectilePaths = projectiles
                .Select(definition => AssetDatabase.GetAssetPath(definition.FiringSound))
                .ToArray();
            Assert.That(attackerPaths.Distinct().Count(), Is.EqualTo(attackers.Count),
                "Each animal needs a distinct cry clip.");
            Assert.That(attackerDeathPaths.Distinct().Count(), Is.EqualTo(attackers.Count),
                "Each animal needs a distinct death clip.");
            Assert.That(attackerPaths.Intersect(attackerDeathPaths), Is.Empty,
                "Cry and death clips must remain distinct for every animal.");
            Assert.That(projectilePaths.Distinct().Count(), Is.EqualTo(projectiles.Count),
                "Each projectile needs a distinct fire clip.");
        }

        [Test]
        public void FarmHandPrefab_HasTheRequestedFarmerVoiceAndActionCues()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FarmHandPrefabPath);
            var audioController = prefab != null ? prefab.GetComponent<FarmHandAudioController>() : null;

            Assert.That(audioController, Is.Not.Null);
            var serializedController = new SerializedObject(audioController);
            Assert.That(serializedController.FindProperty("placementGreeting").objectReferenceValue, Is.Not.Null,
                "A placed farmhand should say Hello.");
            Assert.That(serializedController.FindProperty("relocationLine").objectReferenceValue, Is.Not.Null,
                "A moving farmhand should say Uh oh, gotta move.");
            Assert.That(serializedController.FindProperty("levelUpLine").objectReferenceValue, Is.Not.Null,
                "A levelling farmhand should say Wahey.");
            Assert.That(serializedController.FindProperty("relocationFootstep").objectReferenceValue, Is.Not.Null,
                "A relocating farmhand needs a paced movement cue.");
            Assert.That(serializedController.FindProperty("upgradeReadyCue").objectReferenceValue, Is.Not.Null,
                "A waiting weapon choice needs a clear ready cue.");
            Assert.That(serializedController.FindProperty("upgradeSelectedCue").objectReferenceValue, Is.Not.Null,
                "Selecting a waiting weapon choice needs a confirmation cue.");
        }

        private static List<T> LoadAssets<T>(string folder) where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }
    }
}
