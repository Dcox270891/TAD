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

            Assert.That(attackers, Has.Count.EqualTo(12));
            Assert.That(projectiles, Has.Count.EqualTo(15));
            Assert.That(attackers.All(definition => definition.EntrySound != null), Is.True,
                "Every animal must announce a successful entry into the level.");
            Assert.That(projectiles.All(definition => definition.FiringSound != null), Is.True,
                "Every projectile must have a firing cue.");

            var attackerPaths = attackers
                .Select(definition => AssetDatabase.GetAssetPath(definition.EntrySound))
                .ToArray();
            var projectilePaths = projectiles
                .Select(definition => AssetDatabase.GetAssetPath(definition.FiringSound))
                .ToArray();
            Assert.That(attackerPaths.Distinct().Count(), Is.EqualTo(attackers.Count),
                "Each animal needs a distinct entry clip.");
            Assert.That(projectilePaths.Distinct().Count(), Is.EqualTo(projectiles.Count),
                "Each projectile needs a distinct fire clip.");
        }

        [Test]
        public void FarmHandPrefab_HasTheThreeRequestedFarmerVoiceLines()
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
