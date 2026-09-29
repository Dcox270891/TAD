using TAD.Defenders;
using TAD.UI;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Creates the data-only level-eight defender evolutions and wires them into both reusable
    /// SpawnPool prefabs. Running this command is safe to repeat: authored final-upgrade assets
    /// are updated in place while the rest of the project is left untouched.
    /// </summary>
    [InitializeOnLoad]
    public static class FinalDefenderUpgradeAssetBuilder
    {
        private const string DefenderDataDirectory = "Assets/TAD/Data/Defenders";
        private const string ProjectileDataDirectory = DefenderDataDirectory + "/Projectiles";
        private const string FinalDataDirectory = DefenderDataDirectory + "/FinalUpgrades";
        private const string FinalProjectileDataDirectory = FinalDataDirectory + "/Projectiles";
        private const string UpgradeProjectilePrefabDirectory = "Assets/TAD/Prefabs/Projectiles/Upgrades";

        private const string PoleWeaponPath = DefenderDataDirectory + "/PoleWeaponDefinition.asset";
        private const string RollingWeaponPath = DefenderDataDirectory + "/RollingWeaponDefinition.asset";
        private const string AirRifleWeaponPath = DefenderDataDirectory + "/AirRifleWeaponDefinition.asset";
        private const string TrapperWeaponPath = DefenderDataDirectory + "/TrapperWeaponDefinition.asset";
        private const string PoleProjectilePath = ProjectileDataDirectory + "/PoleSweepProjectileDefinition.asset";
        private const string RollingProjectilePath = ProjectileDataDirectory + "/RollingProjectileDefinition.asset";
        private const string AirRifleProjectilePath = ProjectileDataDirectory + "/AirRifleBulletProjectileDefinition.asset";
        private const string TrapProjectilePath = ProjectileDataDirectory + "/TrapProjectileDefinition.asset";

        private static readonly string[] SpawnPoolPaths =
        {
            "Assets/TAD/Prefabs/Pooling/SpawnPool.prefab",
            "Assets/Resources/TADTests/SpawnPool.prefab"
        };

        static FinalDefenderUpgradeAssetBuilder()
        {
            // The project is already open for this feature pass. Queue one safe initialisation
            // after compilation so the data appears without anyone hand-editing a prefab YAML file.
            EditorApplication.delayCall += EnsureInitialAssets;
        }

        [MenuItem("TAD/Build Defender/Build Final Level 8 Upgrades")]
        public static void BuildFinalUpgrades()
        {
            EnsureFolder(FinalDataDirectory);
            EnsureFolder(FinalProjectileDataDirectory);

            var poleWeapon = RequireAsset<WeaponDefinition>(PoleWeaponPath);
            var rollingWeapon = RequireAsset<WeaponDefinition>(RollingWeaponPath);
            var airRifleWeapon = RequireAsset<WeaponDefinition>(AirRifleWeaponPath);
            var trapperWeapon = RequireAsset<WeaponDefinition>(TrapperWeaponPath);
            var poleProjectile = RequireAsset<ProjectileDefinition>(PoleProjectilePath);
            var rollingProjectile = RequireAsset<ProjectileDefinition>(RollingProjectilePath);
            var airRifleProjectile = RequireAsset<ProjectileDefinition>(AirRifleProjectilePath);
            var trapProjectile = RequireAsset<ProjectileDefinition>(TrapProjectilePath);

            // The level-five Trap becomes a small reliable area hit, and every level-five Air Rifle
            // now reloads after its one-shot magazine as specified.
            ConfigureProjectile(
                trapProjectile,
                ProjectileType.Ranged,
                ProjectileBehavior.Trap,
                ProjectileLifetimeMode.UntilActivated,
                trapProjectile.ProjectilePrefab,
                13f,
                0f,
                3,
                1f,
                0f,
                180f,
                impactRadius: 0.75f,
                impactDamage: 8f,
                projectileScale: 1f);
            ConfigureWeapon(
                airRifleWeapon,
                "Air Rifle",
                "A high-cost ranged single-shot rifle. It reaches over half cover, then reloads after every round.",
                70,
                7f,
                1.6f,
                25f,
                airRifleProjectile,
                requiredPreviousWeapon: null,
                shotsPerMagazine: 1,
                reloadDuration: 1.15f,
                unlocksFinalEvolution: true);
            ConfigureFinalEvolutionParent(poleWeapon);
            ConfigureFinalEvolutionParent(rollingWeapon);
            ConfigureFinalEvolutionParent(trapperWeapon);

            var rakeProjectile = ConfigureProjectile(
                "RakeProjectileDefinition",
                ProjectileType.Melee,
                ProjectileBehavior.PoleSweep,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("RakeProjectile", poleProjectile.ProjectilePrefab),
                7f,
                0.90f,
                1,
                1f,
                0f,
                200f,
                bleedDamagePerSecond: 4.5f,
                bleedDuration: 3.5f,
                projectileScale: 1.15f);
            var shovelProjectile = ConfigureProjectile(
                "ShovelProjectileDefinition",
                ProjectileType.Melee,
                ProjectileBehavior.PoleSweep,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("ShovelProjectile", poleProjectile.ProjectilePrefab),
                6.5f,
                0.95f,
                1,
                1f,
                0f,
                145f,
                stunDuration: 1.25f,
                projectileScale: 1.25f);
            var heavyRollProjectile = ConfigureProjectile(
                "HeavyRollProjectileDefinition",
                ProjectileType.Melee,
                ProjectileBehavior.Rolling,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("HeavyRollProjectile", rollingProjectile.ProjectilePrefab),
                4.5f,
                4.75f,
                2,
                0.15f,
                5.75f,
                180f,
                projectileScale: 1.45f);
            var bounceProjectile = ConfigureProjectile(
                "BounceProjectileDefinition",
                ProjectileType.Melee,
                ProjectileBehavior.Bouncing,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("BounceProjectile", rollingProjectile.ProjectilePrefab),
                3.1f,
                4.5f,
                1,
                1f,
                0f,
                180f,
                impactRadius: 1f,
                impactDamage: 18f,
                projectileScale: 1.15f,
                bounceDistance: 1f,
                bounceAngle: 20f,
                maximumBounces: 4);
            var poisonTrapProjectile = ConfigureProjectile(
                "PoisonTrapProjectileDefinition",
                ProjectileType.Ranged,
                ProjectileBehavior.Trap,
                ProjectileLifetimeMode.UntilActivated,
                ResolveUpgradeProjectilePrefab("PoisonTrapProjectile", trapProjectile.ProjectilePrefab),
                13f,
                0f,
                3,
                1f,
                0f,
                180f,
                impactRadius: 0.95f,
                poisonDamagePerSecond: 3.5f,
                poisonDuration: 5f,
                projectileScale: 1f);
            var slowTrapProjectile = ConfigureProjectile(
                "SlowTrapProjectileDefinition",
                ProjectileType.Ranged,
                ProjectileBehavior.Trap,
                ProjectileLifetimeMode.UntilActivated,
                ResolveUpgradeProjectilePrefab("SlowTrapProjectile", trapProjectile.ProjectilePrefab),
                13f,
                0f,
                3,
                1f,
                0f,
                180f,
                impactRadius: 1f,
                impactDamage: 7f,
                slowMultiplier: 0.55f,
                slowDuration: 2.5f,
                projectileScale: 1f);

            var semiAutomaticProjectile = ConfigureProjectile(
                "SemiAutomaticBulletProjectileDefinition",
                ProjectileType.Ranged,
                ProjectileBehavior.Direct,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("SemiAutomaticBulletProjectile", airRifleProjectile.ProjectilePrefab),
                26f,
                0.42f,
                0,
                1f,
                0f,
                180f,
                projectileScale: 0.9f);
            var pistolProjectile = ConfigureProjectile(
                "PistolBulletProjectileDefinition",
                ProjectileType.Ranged,
                ProjectileBehavior.Direct,
                ProjectileLifetimeMode.Timed,
                ResolveUpgradeProjectilePrefab("PistolBulletProjectile", airRifleProjectile.ProjectilePrefab),
                24f,
                0.38f,
                0,
                1f,
                0f,
                180f,
                projectileScale: 0.85f);

            var rake = ConfigureWeapon(
                "RakeWeaponDefinition",
                "Rake",
                "A broad final pole evolution. Each sweep damages a target and leaves a heavy bleed behind.",
                90,
                2.2f,
                1.55f,
                18f,
                rakeProjectile,
                poleWeapon);
            var shovel = ConfigureWeapon(
                "ShovelWeaponDefinition",
                "Shovel",
                "A powerful final pole evolution. Its compact swing hits hard and stuns the attacker it connects with.",
                96,
                2f,
                1.15f,
                27f,
                shovelProjectile,
                poleWeapon);
            var heavyRoll = ConfigureWeapon(
                "HeavyRollWeaponDefinition",
                "Heavy Roll",
                "A larger final roller that keeps piercing through the line, with greater damage, scale, and lifetime.",
                112,
                3.35f,
                0.85f,
                33f,
                heavyRollProjectile,
                rollingWeapon);
            var bounce = ConfigureWeapon(
                "BounceWeaponDefinition",
                "Bounce",
                "A final rolling evolution that repeatedly strikes the ground, dealing area damage with every bounce along its route.",
                118,
                3.05f,
                0.75f,
                18f,
                bounceProjectile,
                rollingWeapon);
            var semiAutomatic = ConfigureWeapon(
                "SemiAutomaticWeaponDefinition",
                "Semi Automatic",
                "A final rifle evolution: a very fast five-round burst followed by a long reload.",
                135,
                8f,
                5.5f,
                20f,
                semiAutomaticProjectile,
                airRifleWeapon,
                shotsPerMagazine: 5,
                reloadDuration: 3f);
            var pistol = ConfigureWeapon(
                "PistolWeaponDefinition",
                "Pistol",
                "A steady final rifle evolution with an eight-round magazine and a much faster reload.",
                128,
                7.25f,
                2.75f,
                28f,
                pistolProjectile,
                airRifleWeapon,
                shotsPerMagazine: 8,
                reloadDuration: 1.1f);
            var poisonTrapper = ConfigureWeapon(
                "PoisonTrapperWeaponDefinition",
                "Poison Trapper",
                "A final trap evolution. Only the animal that triggers it takes the initial hit, while nearby animals are poisoned.",
                98,
                2.65f,
                0.80f,
                11f,
                poisonTrapProjectile,
                trapperWeapon);
            var slowTrapper = ConfigureWeapon(
                "SlowTrapperWeaponDefinition",
                "Slow Trapper",
                "A final trap evolution that deals a small area hit and slows every nearby attacker.",
                96,
                2.9f,
                0.90f,
                13f,
                slowTrapProjectile,
                trapperWeapon);

            foreach (var spawnPoolPath in SpawnPoolPaths)
            {
                ConfigureSpawnPool(
                    spawnPoolPath,
                    new[] { rake, shovel },
                    new[] { heavyRoll, bounce },
                    new[] { semiAutomatic, pistol },
                    new[] { poisonTrapper, slowTrapper });
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TAD created and configured the eight level-eight final defender evolutions.");
        }

        private static void EnsureInitialAssets()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || !NeedsInitialBuild())
            {
                return;
            }

            BuildFinalUpgrades();
        }

        private static bool NeedsInitialBuild()
        {
            var finalWeaponNames = new[]
            {
                "RakeWeaponDefinition",
                "ShovelWeaponDefinition",
                "HeavyRollWeaponDefinition",
                "BounceWeaponDefinition",
                "SemiAutomaticWeaponDefinition",
                "PistolWeaponDefinition",
                "PoisonTrapperWeaponDefinition",
                "SlowTrapperWeaponDefinition"
            };
            foreach (var name in finalWeaponNames)
            {
                if (AssetDatabase.LoadAssetAtPath<WeaponDefinition>(FinalDataDirectory + "/" + name + ".asset") == null)
                {
                    return true;
                }
            }

            var finalProjectileNames = new[]
            {
                "SemiAutomaticBulletProjectileDefinition",
                "PistolBulletProjectileDefinition"
            };
            foreach (var name in finalProjectileNames)
            {
                if (AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(FinalProjectileDataDirectory + "/" + name + ".asset") == null)
                {
                    return true;
                }
            }

            foreach (var prefabPath in SpawnPoolPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var bootstrap = prefab != null ? prefab.GetComponent<DefenderLevelUpgradeBootstrap>() : null;
                if (bootstrap == null || !HasCompleteFinalBranches(bootstrap))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCompleteFinalBranches(DefenderLevelUpgradeBootstrap bootstrap)
        {
            var serialized = new SerializedObject(bootstrap);
            return HasTwoOptions(serialized.FindProperty("poleLevelEightUpgradeOptions")) &&
                   HasTwoOptions(serialized.FindProperty("rollingLevelEightUpgradeOptions")) &&
                   HasTwoOptions(serialized.FindProperty("airRifleLevelEightUpgradeOptions")) &&
                   HasTwoOptions(serialized.FindProperty("trapperLevelEightUpgradeOptions"));
        }

        private static bool HasTwoOptions(SerializedProperty property)
        {
            return property != null && property.arraySize == 2 &&
                   property.GetArrayElementAtIndex(0).objectReferenceValue != null &&
                   property.GetArrayElementAtIndex(1).objectReferenceValue != null;
        }

        private static WeaponDefinition ConfigureWeapon(
            string assetName,
            string displayName,
            string description,
            int cost,
            float range,
            float attackSpeed,
            float damage,
            ProjectileDefinition projectile,
            WeaponDefinition requiredPreviousWeapon,
            int shotsPerMagazine = 1,
            float reloadDuration = 0f,
            bool unlocksFinalEvolution = false)
        {
            var asset = LoadOrCreateAsset<WeaponDefinition>(FinalDataDirectory + "/" + assetName + ".asset");
            return ConfigureWeapon(
                asset,
                displayName,
                description,
                cost,
                range,
                attackSpeed,
                damage,
                projectile,
                requiredPreviousWeapon,
                shotsPerMagazine,
                reloadDuration,
                unlocksFinalEvolution);
        }

        private static WeaponDefinition ConfigureWeapon(
            WeaponDefinition asset,
            string displayName,
            string description,
            int cost,
            float range,
            float attackSpeed,
            float damage,
            ProjectileDefinition projectile,
            WeaponDefinition requiredPreviousWeapon,
            int shotsPerMagazine,
            float reloadDuration,
            bool unlocksFinalEvolution = false)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("cost").intValue = Mathf.Max(0, cost);
            serialized.FindProperty("range").floatValue = Mathf.Max(0f, range);
            serialized.FindProperty("attackSpeed").floatValue = Mathf.Max(0.01f, attackSpeed);
            serialized.FindProperty("damage").floatValue = Mathf.Max(0f, damage);
            serialized.FindProperty("projectileDefinition").objectReferenceValue = projectile;
            serialized.FindProperty("requiredPreviousWeapon").objectReferenceValue = requiredPreviousWeapon;
            serialized.FindProperty("unlocksFinalEvolution").boolValue = unlocksFinalEvolution;
            serialized.FindProperty("shotsPerMagazine").intValue = Mathf.Max(1, shotsPerMagazine);
            serialized.FindProperty("reloadDuration").floatValue = Mathf.Max(0f, reloadDuration);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void ConfigureFinalEvolutionParent(WeaponDefinition asset)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("unlocksFinalEvolution").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static ProjectileDefinition ConfigureProjectile(
            string assetName,
            ProjectileType projectileType,
            ProjectileBehavior projectileBehavior,
            ProjectileLifetimeMode lifetimeMode,
            GameObject projectilePrefab,
            float speed,
            float lifetime,
            int maximumActiveProjectiles,
            float initialSpeedMultiplier,
            float rollingAcceleration,
            float poleSweepAngle,
            float impactRadius = 0f,
            float impactDamage = 0f,
            float bleedDamagePerSecond = 0f,
            float bleedDuration = 0f,
            float poisonDamagePerSecond = 0f,
            float poisonDuration = 0f,
            float stunDuration = 0f,
            float slowMultiplier = 1f,
            float slowDuration = 0f,
            float projectileScale = 1f,
            float bounceDistance = 1f,
            float bounceAngle = 20f,
            int maximumBounces = 3)
        {
            var asset = LoadOrCreateAsset<ProjectileDefinition>(FinalProjectileDataDirectory + "/" + assetName + ".asset");
            ConfigureProjectile(
                asset,
                projectileType,
                projectileBehavior,
                lifetimeMode,
                projectilePrefab,
                speed,
                lifetime,
                maximumActiveProjectiles,
                initialSpeedMultiplier,
                rollingAcceleration,
                poleSweepAngle,
                impactRadius,
                impactDamage,
                bleedDamagePerSecond,
                bleedDuration,
                poisonDamagePerSecond,
                poisonDuration,
                stunDuration,
                slowMultiplier,
                slowDuration,
                projectileScale,
                bounceDistance,
                bounceAngle,
                maximumBounces);
            return asset;
        }

        private static void ConfigureProjectile(
            ProjectileDefinition asset,
            ProjectileType projectileType,
            ProjectileBehavior projectileBehavior,
            ProjectileLifetimeMode lifetimeMode,
            GameObject projectilePrefab,
            float speed,
            float lifetime,
            int maximumActiveProjectiles,
            float initialSpeedMultiplier,
            float rollingAcceleration,
            float poleSweepAngle,
            float impactRadius = 0f,
            float impactDamage = 0f,
            float bleedDamagePerSecond = 0f,
            float bleedDuration = 0f,
            float poisonDamagePerSecond = 0f,
            float poisonDuration = 0f,
            float stunDuration = 0f,
            float slowMultiplier = 1f,
            float slowDuration = 0f,
            float projectileScale = 1f,
            float bounceDistance = 1f,
            float bounceAngle = 20f,
            int maximumBounces = 3)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("projectileType").enumValueIndex = (int)projectileType;
            serialized.FindProperty("projectileBehavior").enumValueIndex = (int)projectileBehavior;
            serialized.FindProperty("projectileLifetimeMode").enumValueIndex = (int)lifetimeMode;
            serialized.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
            serialized.FindProperty("projectileSpeed").floatValue = Mathf.Max(0f, speed);
            serialized.FindProperty("projectileLifetime").floatValue = Mathf.Max(0f, lifetime);
            serialized.FindProperty("maximumActiveProjectiles").intValue = Mathf.Max(0, maximumActiveProjectiles);
            serialized.FindProperty("initialSpeedMultiplier").floatValue = Mathf.Clamp01(initialSpeedMultiplier);
            serialized.FindProperty("rollingAcceleration").floatValue = Mathf.Max(0f, rollingAcceleration);
            serialized.FindProperty("poleSweepAngle").floatValue = Mathf.Clamp(poleSweepAngle, 1f, 360f);
            serialized.FindProperty("impactAreaRadius").floatValue = Mathf.Max(0f, impactRadius);
            serialized.FindProperty("impactAreaDamage").floatValue = Mathf.Max(0f, impactDamage);
            serialized.FindProperty("bleedDamagePerSecond").floatValue = Mathf.Max(0f, bleedDamagePerSecond);
            serialized.FindProperty("bleedDuration").floatValue = Mathf.Max(0f, bleedDuration);
            serialized.FindProperty("poisonDamagePerSecond").floatValue = Mathf.Max(0f, poisonDamagePerSecond);
            serialized.FindProperty("poisonDuration").floatValue = Mathf.Max(0f, poisonDuration);
            serialized.FindProperty("stunDuration").floatValue = Mathf.Max(0f, stunDuration);
            serialized.FindProperty("slowMovementMultiplier").floatValue = Mathf.Clamp(slowMultiplier, 0.05f, 1f);
            serialized.FindProperty("slowDuration").floatValue = Mathf.Max(0f, slowDuration);
            serialized.FindProperty("projectileScaleMultiplier").floatValue = Mathf.Max(0.01f, projectileScale);
            serialized.FindProperty("bounceDistance").floatValue = Mathf.Max(0.05f, bounceDistance);
            serialized.FindProperty("bounceAngle").floatValue = Mathf.Clamp(bounceAngle, 0f, 89f);
            serialized.FindProperty("maximumBounceCount").intValue = Mathf.Max(1, maximumBounces);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureSpawnPool(
            string prefabPath,
            WeaponDefinition[] poleOptions,
            WeaponDefinition[] rollingOptions,
            WeaponDefinition[] airRifleOptions,
            WeaponDefinition[] trapperOptions)
        {
            var prefab = RequireAsset<GameObject>(prefabPath);
            var bootstrap = prefab.GetComponent<DefenderLevelUpgradeBootstrap>();
            if (bootstrap == null)
            {
                throw new System.InvalidOperationException(
                    "Spawn Pool is missing DefenderLevelUpgradeBootstrap: " + prefabPath);
            }

            var serialized = new SerializedObject(bootstrap);
            AssignOptionArray(serialized.FindProperty("poleLevelEightUpgradeOptions"), poleOptions);
            AssignOptionArray(serialized.FindProperty("rollingLevelEightUpgradeOptions"), rollingOptions);
            AssignOptionArray(serialized.FindProperty("airRifleLevelEightUpgradeOptions"), airRifleOptions);
            AssignOptionArray(serialized.FindProperty("trapperLevelEightUpgradeOptions"), trapperOptions);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static void AssignOptionArray(SerializedProperty property, WeaponDefinition[] options)
        {
            if (property == null || options == null || options.Length != 2)
            {
                throw new System.ArgumentException("A final-upgrade branch needs exactly two weapon definitions.");
            }

            property.arraySize = options.Length;
            for (var index = 0; index < options.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = options[index];
            }
        }

        private static T RequireAsset<T>(string assetPath) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new System.IO.FileNotFoundException("Required TAD asset is missing.", assetPath);
            }

            return asset;
        }

        /// <summary>
        /// Visual variants are built after their gameplay data exists. A final-data rebuild therefore
        /// preserves a bespoke projectile prefab when it has been authored, while still working on a
        /// clean checkout that only has the reusable base projectile prefabs.
        /// </summary>
        private static GameObject ResolveUpgradeProjectilePrefab(string prefabName, GameObject fallback)
        {
            var visualVariant = AssetDatabase.LoadAssetAtPath<GameObject>(
                UpgradeProjectilePrefabDirectory + "/" + prefabName + ".prefab");
            return visualVariant != null ? visualVariant : fallback;
        }

        private static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }
    }
}
