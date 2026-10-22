using System.Collections.Generic;
using NUnit.Framework;
using TAD.Defenders;
using TAD.Pooling;
using UnityEditor;
using UnityEngine;

namespace TAD.Tests.Editor
{
    /// <summary>
    /// Guards the authored visual contract for level-two and final defender upgrades. These checks
    /// deliberately inspect the assets that players see instead of duplicating projectile gameplay rules.
    /// </summary>
    [TestFixture]
    [Category("GameBasics")]
    public sealed class DefenderUpgradeVisualEditModeTests
    {
        private const string VisualDirectory = "Assets/TAD/Data/Defenders/Visuals";
        private const string UpgradeProjectileDirectory = "Assets/TAD/Prefabs/Projectiles/Upgrades";

        private static readonly WeaponVisualExpectation[] UpgradeVisuals =
        {
            new("Stick", "Assets/TAD/Data/Defenders/StickWeaponDefinition.asset", "StickProjectile"),
            new("Stone", "Assets/TAD/Data/Defenders/StoneWeaponDefinition.asset", "StoneProjectile"),
            new("Rake", "Assets/TAD/Data/Defenders/FinalUpgrades/RakeWeaponDefinition.asset", "RakeProjectile"),
            new("Shovel", "Assets/TAD/Data/Defenders/FinalUpgrades/ShovelWeaponDefinition.asset", "ShovelProjectile"),
            new("HeavyRoll", "Assets/TAD/Data/Defenders/FinalUpgrades/HeavyRollWeaponDefinition.asset", "HeavyRollProjectile"),
            new("Bounce", "Assets/TAD/Data/Defenders/FinalUpgrades/BounceWeaponDefinition.asset", "BounceProjectile"),
            new("SemiAutomatic", "Assets/TAD/Data/Defenders/FinalUpgrades/SemiAutomaticWeaponDefinition.asset", "SemiAutomaticBulletProjectile"),
            new("Pistol", "Assets/TAD/Data/Defenders/FinalUpgrades/PistolWeaponDefinition.asset", "PistolBulletProjectile"),
            new("PoisonTrapper", "Assets/TAD/Data/Defenders/FinalUpgrades/PoisonTrapperWeaponDefinition.asset", "PoisonTrapProjectile"),
            new("SlowTrapper", "Assets/TAD/Data/Defenders/FinalUpgrades/SlowTrapperWeaponDefinition.asset", "SlowTrapProjectile")
        };

        [Test]
        public void LevelTwoAndFinalUpgradeVisuals_HaveCompleteDirectionalHeldAndFiringSprites()
        {
            foreach (var expectation in UpgradeVisuals)
            {
                var weapon = LoadAsset<WeaponDefinition>(expectation.WeaponPath);
                var visual = LoadAsset<FarmHandWeaponVisualDefinition>(
                    $"{VisualDirectory}/FarmHandWeaponVisual_{expectation.Title}.asset");

                Assert.That(visual.Weapon, Is.EqualTo(weapon),
                    $"{expectation.Title} visual should be tied to its gameplay weapon.");
                Assert.That(visual.HasMovementSprites, Is.True,
                    $"{expectation.Title} needs a complete directional held/aim sequence.");
                Assert.That(visual.HasFiringSprites, Is.True,
                    $"{expectation.Title} needs a complete directional firing sequence.");
                Assert.That(visual.GetMovementSprite(0, 0), Is.Not.Null);
                Assert.That(
                    visual.GetMovementSprite(
                        FarmHandWeaponVisualDefinition.DirectionCount - 1,
                        FarmHandWeaponVisualDefinition.MovementFrameCount - 1),
                    Is.Not.Null,
                    $"{expectation.Title} should include its final movement direction and frame.");
                Assert.That(visual.GetFiringSprite(0, 0), Is.Not.Null);
                Assert.That(
                    visual.GetFiringSprite(
                        FarmHandWeaponVisualDefinition.DirectionCount - 1,
                        FarmHandWeaponVisualDefinition.FiringFrameCount - 1),
                    Is.Not.Null,
                    $"{expectation.Title} should include its final firing direction and frame.");
            }
        }

        [Test]
        public void ReloadingWeaponVisuals_HaveThreeFrameDirectionalReloadAnimations()
        {
            var reloadVisualTitles = new[] { "AirRifle", "SemiAutomatic", "Pistol" };

            foreach (var title in reloadVisualTitles)
            {
                var visual = LoadAsset<FarmHandWeaponVisualDefinition>(
                    $"{VisualDirectory}/FarmHandWeaponVisual_{title}.asset");

                Assert.That(visual.HasReloadSprites, Is.True,
                    $"{title} reloads during combat and needs dedicated reload artwork.");
                Assert.That(visual.GetReloadSprite(0, 0f), Is.Not.Null);
                Assert.That(visual.GetReloadSprite(0, 0.5f), Is.Not.Null);
                Assert.That(
                    visual.GetReloadSprite(FarmHandWeaponVisualDefinition.DirectionCount - 1, 1f),
                    Is.Not.Null,
                    $"{title} should include the final reload frame in every direction.");
            }
        }

        [Test]
        public void UpgradeProjectileDefinitions_UseDedicatedVariantPrefabs()
        {
            foreach (var expectation in UpgradeVisuals)
            {
                var weapon = LoadAsset<WeaponDefinition>(expectation.WeaponPath);
                var projectile = weapon.ProjectileDefinition;
                var expectedPath = $"{UpgradeProjectileDirectory}/{expectation.ProjectilePrefabName}.prefab";

                Assert.That(projectile, Is.Not.Null,
                    $"{expectation.Title} needs an authored projectile definition.");
                Assert.That(projectile.ProjectilePrefab, Is.Not.Null,
                    $"{expectation.Title} needs a visible projectile prefab.");
                Assert.That(AssetDatabase.GetAssetPath(projectile.ProjectilePrefab), Is.EqualTo(expectedPath),
                    $"{expectation.Title} should not silently reuse another weapon's projectile artwork.");
            }
        }

        [Test]
        public void FarmHandPrefab_RegistersEveryUpgradeVisualForRuntimeWeaponSwaps()
        {
            var prefab = LoadAsset<GameObject>("Assets/TAD/Prefabs/Defenders/FarmHand.prefab");
            var controller = prefab.GetComponent<FarmHandVisualController>();
            Assert.That(controller, Is.Not.Null);

            var serialized = new SerializedObject(controller);
            var visuals = serialized.FindProperty("weaponVisuals");
            Assert.That(visuals, Is.Not.Null);
            Assert.That(visuals.arraySize, Is.GreaterThanOrEqualTo(UpgradeVisuals.Length),
                "The FarmHand visual controller must be able to find every newly equipped weapon.");

            foreach (var expectation in UpgradeVisuals)
            {
                var expectedVisual = LoadAsset<FarmHandWeaponVisualDefinition>(
                    $"{VisualDirectory}/FarmHandWeaponVisual_{expectation.Title}.asset");
                var found = false;
                for (var index = 0; index < visuals.arraySize; index++)
                {
                    found |= visuals.GetArrayElementAtIndex(index).objectReferenceValue == expectedVisual;
                }

                Assert.That(found, Is.True,
                    $"FarmHand prefab does not register the {expectation.Title} visual for runtime equipment changes.");
            }
        }

        [Test]
        public void FarmHandPrefab_UsesAnAuthoredUpgradeMarkerAndEveryWeaponPresentation()
        {
            var prefab = LoadAsset<GameObject>("Assets/TAD/Prefabs/Defenders/FarmHand.prefab");
            var controller = prefab.GetComponent<FarmHandVisualController>();
            var indicator = prefab.GetComponent<FarmHandUpgradeIndicator>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(indicator, Is.Not.Null);

            var indicatorSerialized = new SerializedObject(indicator);
            var marker = indicatorSerialized.FindProperty("authoredArrowSprite").objectReferenceValue as Sprite;
            Assert.That(marker, Is.Not.Null,
                "The upgrade-ready marker should use the authored low-poly asset rather than only a generated fallback.");
            Assert.That(
                AssetDatabase.GetAssetPath(marker),
                Is.EqualTo("Assets/TAD/Art/Farmhands/Indicators/FarmHandUpgradeReady.png"));

            var visuals = new SerializedObject(controller).FindProperty("weaponVisuals");
            Assert.That(visuals, Is.Not.Null);
            Assert.That(visuals.arraySize, Is.EqualTo(15),
                "Every current weapon branch should use an explicit matching held/firing visual.");
            for (var index = 0; index < visuals.arraySize; index++)
            {
                var visual = visuals.GetArrayElementAtIndex(index).objectReferenceValue as FarmHandWeaponVisualDefinition;
                Assert.That(visual, Is.Not.Null, $"Weapon visual slot {index} is missing.");
                Assert.That(visual.Weapon, Is.Not.Null, $"Weapon visual slot {index} is not tied to a gameplay weapon.");
                Assert.That(visual.HasMovementSprites, Is.True, $"Weapon visual slot {index} needs directional held sprites.");
                Assert.That(visual.HasFiringSprites, Is.True, $"Weapon visual slot {index} needs directional firing sprites.");
            }
        }

        [Test]
        public void FarmHandPrefab_AutomaticallyRandomizesThreeCosmeticTypesAndNames()
        {
            var prefab = LoadAsset<GameObject>("Assets/TAD/Prefabs/Defenders/FarmHand.prefab");
            var randomizer = prefab.GetComponent<FarmHandAppearanceRandomizer>();
            Assert.That(randomizer, Is.Not.Null,
                "The reusable farmhand prefab needs an automatic identity randomizer.");
            Assert.That(randomizer.AppearanceCount, Is.EqualTo(27),
                "Three body tones, three hair/hat styles, and three outfits should create every reusable combination.");
            Assert.That(randomizer.NameCount, Is.GreaterThanOrEqualTo(12),
                "Placed farmhands need a useful variety of friendly generated names.");

            var serialized = new SerializedObject(randomizer);
            var appearances = serialized.FindProperty("appearances");
            Assert.That(appearances, Is.Not.Null);
            var bodies = new HashSet<FarmHandBodyVisualDefinition>();
            var hairs = new HashSet<FarmHandHairVisualDefinition>();
            var outfits = new HashSet<FarmHandOutfitVisualDefinition>();
            for (var index = 0; index < appearances.arraySize; index++)
            {
                var appearance = appearances.GetArrayElementAtIndex(index)
                    .objectReferenceValue as FarmHandAppearanceDefinition;
                Assert.That(appearance, Is.Not.Null, $"Cosmetic appearance {index} is missing.");
                Assert.That(appearance.Body, Is.Not.Null);
                Assert.That(appearance.Hair, Is.Not.Null);
                Assert.That(appearance.Outfit, Is.Not.Null);
                bodies.Add(appearance.Body);
                hairs.Add(appearance.Hair);
                outfits.Add(appearance.Outfit);
            }

            Assert.That(bodies.Count, Is.EqualTo(3));
            Assert.That(hairs.Count, Is.EqualTo(3));
            Assert.That(outfits.Count, Is.EqualTo(3));

            var instance = Object.Instantiate(prefab);
            try
            {
                var instanceFarmHand = instance.GetComponent<FarmHand>();
                var instanceRandomizer = instance.GetComponent<FarmHandAppearanceRandomizer>();
                var instanceVisuals = instance.GetComponent<FarmHandVisualController>();
                ((IPoolLifecycle)instanceRandomizer).OnTakenFromPool();

                Assert.That(instanceRandomizer.AssignedAppearance, Is.Not.Null);
                Assert.That(instanceVisuals.Appearance, Is.EqualTo(instanceRandomizer.AssignedAppearance));
                Assert.That(instanceFarmHand.DisplayName, Is.Not.Empty);
                Assert.That(instanceFarmHand.DisplayName, Is.Not.EqualTo("Farmhand"),
                    "A configured pooled placement should receive a generated farmhand name.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SpawnPools_PrewarmTheDedicatedUpgradeProjectilePrefabs()
        {
            var poolPaths = new[]
            {
                "Assets/TAD/Prefabs/Pooling/SpawnPool.prefab",
                "Assets/Resources/TADTests/SpawnPool.prefab"
            };

            foreach (var poolPath in poolPaths)
            {
                var prefab = LoadAsset<GameObject>(poolPath);
                var pool = prefab.GetComponent<SpawnPool>();
                Assert.That(pool, Is.Not.Null);

                var serialized = new SerializedObject(pool);
                var projectiles = serialized.FindProperty("additionalProjectilePrefabs");
                Assert.That(projectiles, Is.Not.Null);
                foreach (var expectation in UpgradeVisuals)
                {
                    var expectedPrefab = LoadAsset<GameObject>(
                        $"{UpgradeProjectileDirectory}/{expectation.ProjectilePrefabName}.prefab");
                    var found = false;
                    for (var index = 0; index < projectiles.arraySize; index++)
                    {
                        found |= projectiles.GetArrayElementAtIndex(index).objectReferenceValue == expectedPrefab;
                    }

                    Assert.That(found, Is.True,
                        $"{poolPath} does not prepare the {expectation.Title} projectile variant.");
                }
            }
        }

        [Test]
        public void UpgradeTrapPrefabs_WireClosedArmedAndDetonationSprites()
        {
            AssertTrapVisualIsConfigured("PoisonTrapProjectile");
            AssertTrapVisualIsConfigured("SlowTrapProjectile");
        }

        [Test]
        public void BounceProjectilePrefab_WiresGroundImpactAnimation()
        {
            var prefab = LoadAsset<GameObject>($"{UpgradeProjectileDirectory}/BounceProjectile.prefab");
            var rollingProjectile = prefab.GetComponent<RollingProjectile>();
            var visual = prefab.GetComponent<RollingProjectileVisualController>();

            Assert.That(rollingProjectile, Is.Not.Null);
            Assert.That(visual, Is.Not.Null,
                "The bounce variant needs an impact visual controller rather than the generic rolling presentation.");

            var serialized = new SerializedObject(visual);
            Assert.That(serialized.FindProperty("rollingProjectile").objectReferenceValue, Is.EqualTo(rollingProjectile));
            Assert.That(serialized.FindProperty("impactRenderer").objectReferenceValue, Is.Not.Null);
            AssertSpriteArray(serialized.FindProperty("bounceSprites"), minimumSpriteCount: 3, "bounce impact");
            Assert.That(serialized.FindProperty("bounceFrameDuration").floatValue, Is.GreaterThan(0f));
        }

        private static void AssertTrapVisualIsConfigured(string prefabName)
        {
            var prefab = LoadAsset<GameObject>($"{UpgradeProjectileDirectory}/{prefabName}.prefab");
            var trapProjectile = prefab.GetComponent<TrapProjectile>();
            var visual = prefab.GetComponent<TrapProjectileVisualController>();
            var renderer = prefab.GetComponent<SpriteRenderer>();

            Assert.That(trapProjectile, Is.Not.Null);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(visual, Is.Not.Null,
                $"{prefabName} needs a visual controller for its armed state and detonation.");

            var serialized = new SerializedObject(visual);
            Assert.That(serialized.FindProperty("trapProjectile").objectReferenceValue, Is.EqualTo(trapProjectile));
            Assert.That(serialized.FindProperty("visual").objectReferenceValue, Is.EqualTo(renderer));
            Assert.That(serialized.FindProperty("closedSprite").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("armedSprite").objectReferenceValue, Is.Not.Null);
            AssertSpriteArray(serialized.FindProperty("detonationSprites"), minimumSpriteCount: 3, $"{prefabName} detonation");
            Assert.That(serialized.FindProperty("detonationFrameDuration").floatValue, Is.GreaterThan(0f));
        }

        private static void AssertSpriteArray(SerializedProperty property, int minimumSpriteCount, string label)
        {
            Assert.That(property, Is.Not.Null, $"Missing serialized {label} sprite array.");
            Assert.That(property.arraySize, Is.GreaterThanOrEqualTo(minimumSpriteCount),
                $"{label} should contain at least {minimumSpriteCount} animation frames.");
            for (var index = 0; index < property.arraySize; index++)
            {
                Assert.That(property.GetArrayElementAtIndex(index).objectReferenceValue, Is.Not.Null,
                    $"{label} frame {index} is not assigned.");
            }
        }

        private static T LoadAsset<T>(string path)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing authored asset {path}.");
            return asset;
        }

        private readonly struct WeaponVisualExpectation
        {
            public WeaponVisualExpectation(string title, string weaponPath, string projectilePrefabName)
            {
                Title = title;
                WeaponPath = weaponPath;
                ProjectilePrefabName = projectilePrefabName;
            }

            public string Title { get; }
            public string WeaponPath { get; }
            public string ProjectilePrefabName { get; }
        }
    }
}
