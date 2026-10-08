#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAD.Defenders;
using TAD.Pooling;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Imports the transparent low-poly Blender sheets, creates modular visual assets, and keeps
    /// the existing gameplay prefabs wired to presentation without changing their combat data.
    /// </summary>
    public static class FarmHandVisualAssetBuilder
    {
        private const int CellPixels = 192;
        private const int DirectionCount = 16;
        private const int MovementFrameCount = 2;
        private const int FiringFrameCount = 3;
        private const int ReloadFrameCount = 3;
        private const int DirectionalColumns = 8;
        private const int DirectionalRows = 4;
        private const int FiringRows = 6;
        private const int ProjectileColumns = 6;
        private const int UpgradeProjectileRows = 4;
        private const int SpritePixelsPerUnit = 24;

        private const string ArtDirectory = "Assets/TAD/Art/Farmhands/Directional";
        private const string ProjectileArtDirectory = "Assets/TAD/Art/Farmhands/Projectiles";
        private const string UpgradeProjectileArtPath = ProjectileArtDirectory + "/DefenderUpgradeProjectiles.png";
        private const string VisualDataDirectory = "Assets/TAD/Data/Defenders/Visuals";
        private const string AppearanceDataDirectory = VisualDataDirectory + "/Appearances";
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";
        private const string UpgradeProjectilePrefabDirectory = "Assets/TAD/Prefabs/Projectiles/Upgrades";

        // Each layer remains independently authored. The builder composes the small, reusable
        // layer library into every valid look so runtime randomisation stays allocation-free and
        // future artists can add another body, hair/hat, or outfit without touching combat code.
        private static readonly CosmeticLayerSpec[] BodySpecs =
        {
            new("Default", "farmhand_body", "Light skin"),
            new("Medium", "farmhand_body_medium", "Warm skin"),
            new("Deep", "farmhand_body_deep", "Deep skin")
        };

        private static readonly CosmeticLayerSpec[] HairSpecs =
        {
            new("ShortBrown", "farmhand_hair", "Short brown hair"),
            new("CurlyBlack", "farmhand_hair_curly", "Curly black hair"),
            new("StrawHat", "farmhand_hair_straw_hat", "Straw hat")
        };

        private static readonly CosmeticLayerSpec[] OutfitSpecs =
        {
            new("BlueOveralls", "farmhand_outfit", "Blue overalls"),
            new("RedPlaid", "farmhand_outfit_plaid", "Red plaid workwear"),
            new("GreenJacket", "farmhand_outfit_jacket", "Green field jacket")
        };

        private static readonly string[] FarmHandNames =
        {
            "Ada", "Bram", "Clover", "Daisy", "Ellis", "Finn", "Hazel", "Ivy",
            "June", "Lena", "Mabel", "Milo", "Nell", "Otis", "Poppy", "Rowan",
            "Rufus", "Sage", "Tilly", "Toby", "Wren", "Eddie", "Moss", "Betsy"
        };

        private static readonly WeaponVisualSpec[] WeaponSpecs =
        {
            new(
                "Unarmed",
                "unarmed",
                "Assets/TAD/Data/Defenders/UnarmedWeaponDefinition.asset"),
            new(
                "Pole",
                "pole",
                "Assets/TAD/Data/Defenders/PoleWeaponDefinition.asset"),
            new(
                "Rolling",
                "rolling",
                "Assets/TAD/Data/Defenders/RollingWeaponDefinition.asset"),
            new(
                "Trapper",
                "trapper",
                "Assets/TAD/Data/Defenders/TrapperWeaponDefinition.asset"),
            new(
                "AirRifle",
                "airrifle",
                "Assets/TAD/Data/Defenders/AirRifleWeaponDefinition.asset",
                true),
            new(
                "Stick",
                "stick",
                "Assets/TAD/Data/Defenders/StickWeaponDefinition.asset"),
            new(
                "Stone",
                "stone",
                "Assets/TAD/Data/Defenders/StoneWeaponDefinition.asset"),
            new(
                "Rake",
                "rake",
                "Assets/TAD/Data/Defenders/FinalUpgrades/RakeWeaponDefinition.asset"),
            new(
                "Shovel",
                "shovel",
                "Assets/TAD/Data/Defenders/FinalUpgrades/ShovelWeaponDefinition.asset"),
            new(
                "HeavyRoll",
                "heavyroll",
                "Assets/TAD/Data/Defenders/FinalUpgrades/HeavyRollWeaponDefinition.asset"),
            new(
                "Bounce",
                "bounce",
                "Assets/TAD/Data/Defenders/FinalUpgrades/BounceWeaponDefinition.asset"),
            new(
                "SemiAutomatic",
                "semiautomatic",
                "Assets/TAD/Data/Defenders/FinalUpgrades/SemiAutomaticWeaponDefinition.asset",
                true),
            new(
                "Pistol",
                "pistol",
                "Assets/TAD/Data/Defenders/FinalUpgrades/PistolWeaponDefinition.asset",
                true),
            new(
                "PoisonTrapper",
                "poisontrapper",
                "Assets/TAD/Data/Defenders/FinalUpgrades/PoisonTrapperWeaponDefinition.asset"),
            new(
                "SlowTrapper",
                "slowtrapper",
                "Assets/TAD/Data/Defenders/FinalUpgrades/SlowTrapperWeaponDefinition.asset")
        };

        private static readonly ProjectileVisualSpec[] ProjectileSpecs =
        {
            new("Assets/TAD/Prefabs/Projectiles/FarmHandProjectile.prefab", "farmhand_projectile_unarmed"),
            new("Assets/TAD/Prefabs/Projectiles/PoleSweepProjectile.prefab", "farmhand_projectile_pole"),
            new("Assets/TAD/Prefabs/Projectiles/RollingProjectile.prefab", "farmhand_projectile_rolling"),
            new("Assets/TAD/Prefabs/Projectiles/TrapProjectile.prefab", "farmhand_projectile_trap_closed"),
            new("Assets/TAD/Prefabs/Projectiles/AirRifleBulletProjectile.prefab", "farmhand_projectile_air_rifle")
        };

        private static readonly UpgradeProjectileVisualSpec[] UpgradeProjectileSpecs =
        {
            new(
                "StickProjectile",
                "Assets/TAD/Prefabs/Projectiles/FarmHandProjectile.prefab",
                "Assets/TAD/Data/Defenders/Projectiles/StickProjectileDefinition.asset",
                "upgrade_projectile_stick"),
            new(
                "StoneProjectile",
                "Assets/TAD/Prefabs/Projectiles/AirRifleBulletProjectile.prefab",
                "Assets/TAD/Data/Defenders/Projectiles/StoneProjectileDefinition.asset",
                "upgrade_projectile_stone"),
            new(
                "RakeProjectile",
                "Assets/TAD/Prefabs/Projectiles/PoleSweepProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/RakeProjectileDefinition.asset",
                "upgrade_projectile_rake"),
            new(
                "ShovelProjectile",
                "Assets/TAD/Prefabs/Projectiles/PoleSweepProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/ShovelProjectileDefinition.asset",
                "upgrade_projectile_shovel"),
            new(
                "HeavyRollProjectile",
                "Assets/TAD/Prefabs/Projectiles/RollingProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/HeavyRollProjectileDefinition.asset",
                "upgrade_projectile_heavy_roll"),
            new(
                "BounceProjectile",
                "Assets/TAD/Prefabs/Projectiles/RollingProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/BounceProjectileDefinition.asset",
                "upgrade_projectile_bounce",
                isBounce: true),
            new(
                "SemiAutomaticBulletProjectile",
                "Assets/TAD/Prefabs/Projectiles/AirRifleBulletProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/SemiAutomaticBulletProjectileDefinition.asset",
                "upgrade_projectile_semi_automatic"),
            new(
                "PistolBulletProjectile",
                "Assets/TAD/Prefabs/Projectiles/AirRifleBulletProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/PistolBulletProjectileDefinition.asset",
                "upgrade_projectile_pistol"),
            new(
                "PoisonTrapProjectile",
                "Assets/TAD/Prefabs/Projectiles/TrapProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/PoisonTrapProjectileDefinition.asset",
                "upgrade_projectile_poison_closed",
                isTrap: true,
                armedSpriteName: "upgrade_projectile_poison_armed",
                detonationPrefix: "upgrade_projectile_poison_explosion"),
            new(
                "SlowTrapProjectile",
                "Assets/TAD/Prefabs/Projectiles/TrapProjectile.prefab",
                "Assets/TAD/Data/Defenders/FinalUpgrades/Projectiles/SlowTrapProjectileDefinition.asset",
                "upgrade_projectile_slow_closed",
                isTrap: true,
                armedSpriteName: "upgrade_projectile_slow_armed",
                detonationPrefix: "upgrade_projectile_slow_explosion")
        };

        [MenuItem("TAD/Defenders/Rebuild Farmhand Visuals")]
        private static void RebuildFromMenu()
        {
            RebuildFromAutomation();
        }

        /// <summary>Entry point used after the Blender source renders its sprite sheets.</summary>
        public static void RebuildFromAutomation()
        {
            EnsureFolder(VisualDataDirectory);
            EnsureRequiredFiles();

            var bodies = ImportCosmeticLayers<FarmHandBodyVisualDefinition>(
                BodySpecs,
                "FarmhandBody",
                "FarmHandBody");
            var hairs = ImportCosmeticLayers<FarmHandHairVisualDefinition>(
                HairSpecs,
                "FarmhandHair",
                "FarmHandHair");
            var outfits = ImportCosmeticLayers<FarmHandOutfitVisualDefinition>(
                OutfitSpecs,
                "FarmhandOutfit",
                "FarmHandOutfit");
            var appearances = CreateOrUpdateAppearances(bodies, hairs, outfits);
            var defaultAppearance = appearances[0];

            var weaponVisuals = new List<FarmHandWeaponVisualDefinition>();
            foreach (var spec in WeaponSpecs)
            {
                var moveSprites = ImportDirectionalSheet(
                    ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Move.png",
                    spec.Key,
                    "move",
                    MovementFrameCount,
                    DirectionalRows);
                var fireSprites = ImportDirectionalSheet(
                    ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Fire.png",
                    spec.Key,
                    "fire",
                    FiringFrameCount,
                    FiringRows);
                var reloadSprites = spec.HasReloadAnimation
                    ? ImportDirectionalSheet(
                        ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Reload.png",
                        spec.Key,
                        "reload",
                        ReloadFrameCount,
                        FiringRows)
                    : null;
                weaponVisuals.Add(CreateOrUpdateWeaponVisual(spec, moveSprites, fireSprites, reloadSprites));
            }

            var projectileSprites = ImportProjectileSheet();
            var upgradeProjectileSprites = ImportUpgradeProjectileSheet();
            UpdateFarmHandPrefab(defaultAppearance, appearances, weaponVisuals);
            UpdateProjectilePrefabs(projectileSprites);
            UpdateUpgradeProjectilePrefabs(upgradeProjectileSprites);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TAD rebuilt the modular farmhand, weapon-firing, and projectile visual set.");
        }

        /// <summary>
        /// Queues the potentially long import on the Editor update loop. This keeps automation
        /// transport requests short while the Unity asset database slices the authored sheets.
        /// </summary>
        public static void ScheduleRebuildFromAutomation()
        {
            EditorApplication.delayCall -= RebuildFromAutomation;
            EditorApplication.delayCall += RebuildFromAutomation;
        }

        private static void EnsureRequiredFiles()
        {
            var requiredPaths = new List<string>
            {
                ProjectileArtDirectory + "/FarmhandProjectiles.png",
                UpgradeProjectileArtPath
            };

            AddCosmeticSourcePaths(requiredPaths, "FarmhandBody", BodySpecs);
            AddCosmeticSourcePaths(requiredPaths, "FarmhandHair", HairSpecs);
            AddCosmeticSourcePaths(requiredPaths, "FarmhandOutfit", OutfitSpecs);

            foreach (var spec in WeaponSpecs)
            {
                requiredPaths.Add(ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Move.png");
                requiredPaths.Add(ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Fire.png");
                if (spec.HasReloadAnimation)
                {
                    requiredPaths.Add(ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Reload.png");
                }
                requiredPaths.Add(spec.WeaponPath);
            }

            foreach (var path in requiredPaths)
            {
                if (!File.Exists(ToAbsolutePath(path)))
                {
                    throw new FileNotFoundException("Required farmhand visual source is missing.", path);
                }
            }
        }

        private static void AddCosmeticSourcePaths(
            ICollection<string> requiredPaths,
            string sheetPrefix,
            IReadOnlyList<CosmeticLayerSpec> specs)
        {
            foreach (var spec in specs)
            {
                requiredPaths.Add(GetCosmeticSheetPath(sheetPrefix, spec));
            }
        }

        private static string GetCosmeticSheetPath(string sheetPrefix, CosmeticLayerSpec spec)
        {
            return ArtDirectory + $"/{sheetPrefix}_{spec.SheetTitle}_Move.png";
        }

        private static Dictionary<string, Sprite> ImportDirectionalSheet(
            string sheetPath,
            string key,
            string sequence,
            int frameCount,
            int rowCount)
        {
            var spriteSpecs = new List<SpriteSpec>(DirectionCount * frameCount);
            for (var direction = 0; direction < DirectionCount; direction++)
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var index = (direction * frameCount) + frame;
                    spriteSpecs.Add(new SpriteSpec(
                        $"{key}_d{direction:D2}_{sequence}{frame}",
                        index % DirectionalColumns,
                        index / DirectionalColumns));
                }
            }

            return ImportAndSliceSheet(sheetPath, spriteSpecs, DirectionalColumns, rowCount);
        }

        private static Dictionary<string, Sprite> ImportProjectileSheet()
        {
            var specs = new List<SpriteSpec>
            {
                new("farmhand_projectile_unarmed", 0, 0),
                new("farmhand_projectile_pole", 1, 0),
                new("farmhand_projectile_rolling", 2, 0),
                new("farmhand_projectile_trap_closed", 3, 0),
                new("farmhand_projectile_trap_armed", 4, 0),
                new("farmhand_projectile_air_rifle", 5, 0)
            };

            return ImportAndSliceSheet(
                ProjectileArtDirectory + "/FarmhandProjectiles.png",
                specs,
                ProjectileColumns,
                1);
        }

        private static Dictionary<string, Sprite> ImportUpgradeProjectileSheet()
        {
            var specs = new List<SpriteSpec>
            {
                new("upgrade_projectile_stick", 0, 0),
                new("upgrade_projectile_stone", 1, 0),
                new("upgrade_projectile_rake", 2, 0),
                new("upgrade_projectile_shovel", 3, 0),
                new("upgrade_projectile_heavy_roll", 4, 0),
                new("upgrade_projectile_bounce", 5, 0),
                new("upgrade_projectile_semi_automatic", 0, 1),
                new("upgrade_projectile_pistol", 1, 1),
                new("upgrade_projectile_poison_closed", 2, 1),
                new("upgrade_projectile_poison_armed", 3, 1),
                new("upgrade_projectile_slow_closed", 4, 1),
                new("upgrade_projectile_slow_armed", 5, 1),
                new("upgrade_projectile_poison_explosion0", 0, 2),
                new("upgrade_projectile_poison_explosion1", 1, 2),
                new("upgrade_projectile_poison_explosion2", 2, 2),
                new("upgrade_projectile_slow_explosion0", 3, 2),
                new("upgrade_projectile_slow_explosion1", 4, 2),
                new("upgrade_projectile_slow_explosion2", 5, 2),
                new("upgrade_projectile_bounce_impact0", 0, 3),
                new("upgrade_projectile_bounce_impact1", 1, 3),
                new("upgrade_projectile_bounce_impact2", 2, 3)
            };

            return ImportAndSliceSheet(
                UpgradeProjectileArtPath,
                specs,
                ProjectileColumns,
                UpgradeProjectileRows);
        }

        private static Dictionary<string, Sprite> ImportAndSliceSheet(
            string sheetPath,
            IReadOnlyList<SpriteSpec> spriteSpecs,
            int columnCount,
            int rowCount)
        {
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"{sheetPath} is not a TextureImporter.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = SpritePixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 75;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException($"{sheetPath} does not support sprite editing.");
            }

            dataProvider.InitSpriteEditorDataProvider();
            var editCapability = dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException($"{sheetPath} has no sprite-frame editing capability.");
            }

            var capability = editCapability.GetEditCapability();
            if (!capability.HasCapability(EEditCapability.EditSpriteName) ||
                !capability.HasCapability(EEditCapability.EditSpriteRect) ||
                !capability.HasCapability(EEditCapability.EditPivot) ||
                !capability.HasCapability(EEditCapability.CreateAndDeleteSprite))
            {
                throw new InvalidOperationException($"{sheetPath} cannot safely create the required sprite slices.");
            }

            var rects = spriteSpecs
                .Select(spec => new SpriteRect
                {
                    name = spec.Name,
                    rect = new Rect(
                        spec.Column * CellPixels,
                        (rowCount - spec.Row - 1) * CellPixels,
                        CellPixels,
                        CellPixels),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = GUID.Generate()
                })
                .ToArray();
            dataProvider.SetSpriteRects(rects);
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                nameFileIdProvider.SetNameFileIdPairs(rects
                    .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            }

            dataProvider.Apply();
            importer.SaveAndReimport();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);
            if (sprites.Count != spriteSpecs.Count)
            {
                throw new InvalidOperationException(
                    $"{sheetPath} imported {sprites.Count} sprites; expected {spriteSpecs.Count}.");
            }

            return sprites;
        }

        private static T CreateOrUpdateLayerDefinition<T>(
            string assetPath,
            string key,
            IReadOnlyDictionary<string, Sprite> sprites)
            where T : FarmHandDirectionalLayerDefinition
        {
            var layer = LoadOrCreateAsset<T>(assetPath);
            var serialized = new SerializedObject(layer);
            AssignSpriteArray(serialized.FindProperty("movementSprites"), key, "move", MovementFrameCount, sprites);
            serialized.FindProperty("movementFrameDuration").floatValue = 0.16f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static List<T> ImportCosmeticLayers<T>(
            IReadOnlyList<CosmeticLayerSpec> specs,
            string sheetPrefix,
            string assetPrefix)
            where T : FarmHandDirectionalLayerDefinition
        {
            var layers = new List<T>(specs.Count);
            foreach (var spec in specs)
            {
                var sprites = ImportDirectionalSheet(
                    GetCosmeticSheetPath(sheetPrefix, spec),
                    spec.Key,
                    "move",
                    MovementFrameCount,
                    DirectionalRows);
                layers.Add(CreateOrUpdateLayerDefinition<T>(
                    VisualDataDirectory + $"/{assetPrefix}_{spec.SheetTitle}.asset",
                    spec.Key,
                    sprites));
            }

            return layers;
        }

        private static List<FarmHandAppearanceDefinition> CreateOrUpdateAppearances(
            IReadOnlyList<FarmHandBodyVisualDefinition> bodies,
            IReadOnlyList<FarmHandHairVisualDefinition> hairs,
            IReadOnlyList<FarmHandOutfitVisualDefinition> outfits)
        {
            if (bodies.Count != BodySpecs.Length || hairs.Count != HairSpecs.Length || outfits.Count != OutfitSpecs.Length)
            {
                throw new InvalidOperationException("Farmhand cosmetic layer counts no longer match their authored specifications.");
            }

            EnsureFolder(AppearanceDataDirectory);
            var appearances = new List<FarmHandAppearanceDefinition>(bodies.Count * hairs.Count * outfits.Count);
            for (var bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                for (var hairIndex = 0; hairIndex < hairs.Count; hairIndex++)
                {
                    for (var outfitIndex = 0; outfitIndex < outfits.Count; outfitIndex++)
                    {
                        var isDefault = bodyIndex == 0 && hairIndex == 0 && outfitIndex == 0;
                        var path = isDefault
                            ? VisualDataDirectory + "/FarmHandAppearance_Default.asset"
                            : AppearanceDataDirectory + $"/FarmHandAppearance_{BodySpecs[bodyIndex].SheetTitle}_" +
                              $"{HairSpecs[hairIndex].SheetTitle}_{OutfitSpecs[outfitIndex].SheetTitle}.asset";
                        var appearance = LoadOrCreateAsset<FarmHandAppearanceDefinition>(path);
                        var serialized = new SerializedObject(appearance);
                        serialized.FindProperty("displayName").stringValue = isDefault
                            ? "Default Farmhand"
                            : $"{BodySpecs[bodyIndex].DisplayName} / {HairSpecs[hairIndex].DisplayName} / " +
                              OutfitSpecs[outfitIndex].DisplayName;
                        serialized.FindProperty("body").objectReferenceValue = bodies[bodyIndex];
                        serialized.FindProperty("hair").objectReferenceValue = hairs[hairIndex];
                        serialized.FindProperty("outfit").objectReferenceValue = outfits[outfitIndex];
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(appearance);
                        appearances.Add(appearance);
                    }
                }
            }

            return appearances;
        }

        private static FarmHandWeaponVisualDefinition CreateOrUpdateWeaponVisual(
            WeaponVisualSpec spec,
            IReadOnlyDictionary<string, Sprite> moveSprites,
            IReadOnlyDictionary<string, Sprite> fireSprites,
            IReadOnlyDictionary<string, Sprite> reloadSprites)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(spec.WeaponPath);
            if (weapon == null)
            {
                throw new FileNotFoundException("Weapon Definition is missing.", spec.WeaponPath);
            }

            var assetPath = VisualDataDirectory + $"/FarmHandWeaponVisual_{spec.SheetTitle}.asset";
            var visual = LoadOrCreateAsset<FarmHandWeaponVisualDefinition>(assetPath);
            var serialized = new SerializedObject(visual);
            serialized.FindProperty("weapon").objectReferenceValue = weapon;
            AssignSpriteArray(serialized.FindProperty("movementSprites"), spec.Key, "move", MovementFrameCount, moveSprites);
            AssignSpriteArray(serialized.FindProperty("firingSprites"), spec.Key, "fire", FiringFrameCount, fireSprites);
            if (spec.HasReloadAnimation)
            {
                AssignSpriteArray(serialized.FindProperty("reloadSprites"), spec.Key, "reload", ReloadFrameCount, reloadSprites);
            }
            else
            {
                ClearSpriteArray(serialized.FindProperty("reloadSprites"), ReloadFrameCount);
            }
            serialized.FindProperty("firingFrameDuration").floatValue = 0.09f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visual);
            return visual;
        }

        private static void AssignSpriteArray(
            SerializedProperty property,
            string key,
            string sequence,
            int frameCount,
            IReadOnlyDictionary<string, Sprite> sprites)
        {
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized sprite array was not found.");
            }

            property.arraySize = DirectionCount * frameCount;
            for (var direction = 0; direction < DirectionCount; direction++)
            {
                for (var frame = 0; frame < frameCount; frame++)
                {
                    var spriteName = $"{key}_d{direction:D2}_{sequence}{frame}";
                    if (!sprites.TryGetValue(spriteName, out var sprite))
                    {
                        throw new InvalidOperationException($"Missing sliced sprite {spriteName}.");
                    }

                    property.GetArrayElementAtIndex((direction * frameCount) + frame).objectReferenceValue = sprite;
                }
            }
        }

        private static void ClearSpriteArray(SerializedProperty property, int frameCount)
        {
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized sprite array was not found.");
            }

            property.arraySize = DirectionCount * frameCount;
            for (var index = 0; index < property.arraySize; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = null;
            }
        }

        private static void UpdateFarmHandPrefab(
            FarmHandAppearanceDefinition defaultAppearance,
            IReadOnlyList<FarmHandAppearanceDefinition> appearances,
            IReadOnlyList<FarmHandWeaponVisualDefinition> weaponVisuals)
        {
            var root = PrefabUtility.LoadPrefabContents(FarmHandPrefabPath);
            try
            {
                var farmHand = root.GetComponent<FarmHand>();
                var bodyRenderer = root.GetComponent<SpriteRenderer>();
                if (farmHand == null || bodyRenderer == null)
                {
                    throw new InvalidOperationException("FarmHand prefab no longer has its root FarmHand and SpriteRenderer.");
                }

                bodyRenderer.sprite = defaultAppearance.Body.GetMovementSprite(0, 0);
                bodyRenderer.enabled = true;
                var hairRenderer = GetOrCreateLayerRenderer(root.transform, "HairVisual", bodyRenderer, 1);
                var outfitRenderer = GetOrCreateLayerRenderer(root.transform, "OutfitVisual", bodyRenderer, 2);
                var weaponRenderer = GetOrCreateLayerRenderer(root.transform, "WeaponVisual", bodyRenderer, 3);
                hairRenderer.sprite = defaultAppearance.Hair.GetMovementSprite(0, 0);
                outfitRenderer.sprite = defaultAppearance.Outfit.GetMovementSprite(0, 0);
                weaponRenderer.sprite = weaponVisuals[0].GetMovementSprite(0, 0);
                hairRenderer.enabled = hairRenderer.sprite != null;
                outfitRenderer.enabled = outfitRenderer.sprite != null;
                weaponRenderer.enabled = weaponRenderer.sprite != null;

                var controller = root.GetComponent<FarmHandVisualController>();
                if (controller == null)
                {
                    controller = root.AddComponent<FarmHandVisualController>();
                }

                var farmHandSerialized = new SerializedObject(farmHand);
                farmHandSerialized.FindProperty("visual").objectReferenceValue = bodyRenderer;
                farmHandSerialized.FindProperty("visualController").objectReferenceValue = controller;
                farmHandSerialized.ApplyModifiedPropertiesWithoutUndo();

                var controllerSerialized = new SerializedObject(controller);
                controllerSerialized.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
                controllerSerialized.FindProperty("hairRenderer").objectReferenceValue = hairRenderer;
                controllerSerialized.FindProperty("outfitRenderer").objectReferenceValue = outfitRenderer;
                controllerSerialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
                controllerSerialized.FindProperty("appearance").objectReferenceValue = defaultAppearance;
                var weaponVisualsProperty = controllerSerialized.FindProperty("weaponVisuals");
                weaponVisualsProperty.arraySize = weaponVisuals.Count;
                for (var index = 0; index < weaponVisuals.Count; index++)
                {
                    weaponVisualsProperty.GetArrayElementAtIndex(index).objectReferenceValue = weaponVisuals[index];
                }

                controllerSerialized.FindProperty("aimTransitionDuration").floatValue = 0.16f;
                controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

                var randomizer = root.GetComponent<FarmHandAppearanceRandomizer>();
                if (randomizer == null)
                {
                    randomizer = root.AddComponent<FarmHandAppearanceRandomizer>();
                }

                var randomizerSerialized = new SerializedObject(randomizer);
                SetObjectReference(randomizerSerialized, "farmHand", farmHand);
                SetObjectReference(randomizerSerialized, "visualController", controller);
                AssignObjectReferenceArray(randomizerSerialized.FindProperty("appearances"), appearances);
                AssignStringArray(randomizerSerialized.FindProperty("displayNames"), FarmHandNames);
                randomizerSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bodyRenderer);
                EditorUtility.SetDirty(farmHand);
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(randomizer);
                PrefabUtility.SaveAsPrefabAsset(root, FarmHandPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static SpriteRenderer GetOrCreateLayerRenderer(
            Transform root,
            string layerName,
            SpriteRenderer bodyRenderer,
            int sortingOffset)
        {
            var layerTransform = root.Find(layerName);
            if (layerTransform == null)
            {
                var layer = new GameObject(layerName, typeof(SpriteRenderer));
                layer.layer = root.gameObject.layer;
                layer.transform.SetParent(root, worldPositionStays: false);
                layerTransform = layer.transform;
            }

            layerTransform.gameObject.layer = root.gameObject.layer;
            layerTransform.localPosition = Vector3.zero;
            layerTransform.localRotation = Quaternion.identity;
            layerTransform.localScale = Vector3.one;
            var renderer = layerTransform.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = layerTransform.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sharedMaterial = bodyRenderer.sharedMaterial;
            renderer.color = bodyRenderer.color;
            renderer.flipX = bodyRenderer.flipX;
            renderer.flipY = bodyRenderer.flipY;
            renderer.sortingLayerID = bodyRenderer.sortingLayerID;
            renderer.sortingOrder = bodyRenderer.sortingOrder + sortingOffset;
            return renderer;
        }

        private static void UpdateProjectilePrefabs(IReadOnlyDictionary<string, Sprite> projectileSprites)
        {
            foreach (var spec in ProjectileSpecs)
            {
                var root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
                try
                {
                    var renderer = root.GetComponent<SpriteRenderer>();
                    if (renderer == null)
                    {
                        throw new InvalidOperationException($"{spec.PrefabPath} no longer has a root SpriteRenderer.");
                    }

                    if (!projectileSprites.TryGetValue(spec.SpriteName, out var sprite))
                    {
                        throw new InvalidOperationException($"Missing projectile sprite {spec.SpriteName}.");
                    }

                    renderer.sprite = sprite;
                    renderer.enabled = false;
                    EditorUtility.SetDirty(renderer);

                    if (root.TryGetComponent<TrapProjectile>(out var trap))
                    {
                        var trapVisual = root.GetComponent<TrapProjectileVisualController>();
                        if (trapVisual == null)
                        {
                            trapVisual = root.AddComponent<TrapProjectileVisualController>();
                        }

                        var trapSerialized = new SerializedObject(trapVisual);
                        trapSerialized.FindProperty("trapProjectile").objectReferenceValue = trap;
                        trapSerialized.FindProperty("visual").objectReferenceValue = renderer;
                        trapSerialized.FindProperty("closedSprite").objectReferenceValue =
                            projectileSprites["farmhand_projectile_trap_closed"];
                        trapSerialized.FindProperty("armedSprite").objectReferenceValue =
                            projectileSprites["farmhand_projectile_trap_armed"];
                        trapSerialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(trapVisual);
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        /// <summary>
        /// Builds compact prefab variants for every new gameplay projectile. The variants inherit the
        /// proven pooled movement/collision scripts from their base projectile, while their sprites and
        /// presentation-only effect controllers stay specific to the selected upgrade.
        /// </summary>
        private static void UpdateUpgradeProjectilePrefabs(IReadOnlyDictionary<string, Sprite> projectileSprites)
        {
            EnsureFolder(UpgradeProjectilePrefabDirectory);
            var variants = new List<GameObject>(UpgradeProjectileSpecs.Length);
            foreach (var spec in UpgradeProjectileSpecs)
            {
                var prefabPath = UpgradeProjectilePrefabDirectory + "/" + spec.PrefabName + ".prefab";
                var editablePath = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null
                    ? prefabPath
                    : spec.BasePrefabPath;
                var root = PrefabUtility.LoadPrefabContents(editablePath);
                try
                {
                    ConfigureUpgradeProjectilePrefab(root, spec, projectileSprites);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                var variant = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (variant == null)
                {
                    throw new InvalidOperationException("Unable to create upgrade projectile prefab: " + prefabPath);
                }

                variants.Add(variant);
                UpdateProjectileDefinitionPrefabReference(spec.ProjectileDefinitionPath, variant);
            }

            UpdateSpawnPoolProjectileVariants(variants);
        }

        private static void ConfigureUpgradeProjectilePrefab(
            GameObject root,
            UpgradeProjectileVisualSpec spec,
            IReadOnlyDictionary<string, Sprite> sprites)
        {
            var renderer = root.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException(spec.BasePrefabPath + " no longer has a root SpriteRenderer.");
            }

            renderer.sprite = GetRequiredSprite(sprites, spec.SpriteName);
            renderer.enabled = false;
            EditorUtility.SetDirty(renderer);

            if (spec.IsTrap)
            {
                ConfigureTrapVisual(root, renderer, spec, sprites);
            }

            if (spec.IsBounce)
            {
                ConfigureBounceVisual(root, renderer, sprites);
            }
        }

        private static void ConfigureTrapVisual(
            GameObject root,
            SpriteRenderer renderer,
            UpgradeProjectileVisualSpec spec,
            IReadOnlyDictionary<string, Sprite> sprites)
        {
            var trap = root.GetComponent<TrapProjectile>();
            if (trap == null)
            {
                throw new InvalidOperationException(spec.PrefabName + " must inherit a TrapProjectile.");
            }

            var visual = root.GetComponent<TrapProjectileVisualController>();
            if (visual == null)
            {
                visual = root.AddComponent<TrapProjectileVisualController>();
            }

            var trapSerialized = new SerializedObject(trap);
            SetFloat(trapSerialized, "detonationVisualDuration", 0.24f);
            trapSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(trap);

            var visualSerialized = new SerializedObject(visual);
            SetObjectReference(visualSerialized, "trapProjectile", trap);
            SetObjectReference(visualSerialized, "visual", renderer);
            SetObjectReference(visualSerialized, "closedSprite", GetRequiredSprite(sprites, spec.SpriteName));
            SetObjectReference(visualSerialized, "armedSprite", GetRequiredSprite(sprites, spec.ArmedSpriteName));
            AssignSpriteSequence(
                visualSerialized.FindProperty("detonationSprites"),
                new[]
                {
                    GetRequiredSprite(sprites, spec.DetonationPrefix + "0"),
                    GetRequiredSprite(sprites, spec.DetonationPrefix + "1"),
                    GetRequiredSprite(sprites, spec.DetonationPrefix + "2")
                });
            SetFloat(visualSerialized, "detonationFrameDuration", 0.08f);
            visualSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visual);
        }

        private static void ConfigureBounceVisual(
            GameObject root,
            SpriteRenderer renderer,
            IReadOnlyDictionary<string, Sprite> sprites)
        {
            var rolling = root.GetComponent<RollingProjectile>();
            if (rolling == null)
            {
                throw new InvalidOperationException("BounceProjectile must inherit a RollingProjectile.");
            }

            var impactRenderer = GetOrCreateProjectileChildRenderer(root.transform, "BounceImpactVisual", renderer, 1);
            var visual = root.GetComponent<RollingProjectileVisualController>();
            if (visual == null)
            {
                visual = root.AddComponent<RollingProjectileVisualController>();
            }

            var rollingSerialized = new SerializedObject(rolling);
            SetFloat(rollingSerialized, "finalBounceVisualDuration", 0.24f);
            rollingSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rolling);

            var visualSerialized = new SerializedObject(visual);
            SetObjectReference(visualSerialized, "rollingProjectile", rolling);
            SetObjectReference(visualSerialized, "impactRenderer", impactRenderer);
            AssignSpriteSequence(
                visualSerialized.FindProperty("bounceSprites"),
                new[]
                {
                    GetRequiredSprite(sprites, "upgrade_projectile_bounce_impact0"),
                    GetRequiredSprite(sprites, "upgrade_projectile_bounce_impact1"),
                    GetRequiredSprite(sprites, "upgrade_projectile_bounce_impact2")
                });
            SetFloat(visualSerialized, "bounceFrameDuration", 0.08f);
            visualSerialized.ApplyModifiedPropertiesWithoutUndo();
            impactRenderer.enabled = false;
            EditorUtility.SetDirty(impactRenderer);
            EditorUtility.SetDirty(visual);
        }

        private static void UpdateProjectileDefinitionPrefabReference(string definitionPath, GameObject projectilePrefab)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ProjectileDefinition>(definitionPath);
            if (definition == null)
            {
                // The final-upgrade builder is deliberately allowed to run after artwork import on a
                // clean checkout. It will resolve this visual prefab when it creates the data asset.
                return;
            }

            var serialized = new SerializedObject(definition);
            SetObjectReference(serialized, "projectilePrefab", projectilePrefab);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void UpdateSpawnPoolProjectileVariants(IReadOnlyList<GameObject> variants)
        {
            var spawnPoolPaths = new[]
            {
                "Assets/TAD/Prefabs/Pooling/SpawnPool.prefab",
                "Assets/Resources/TADTests/SpawnPool.prefab"
            };
            foreach (var prefabPath in spawnPoolPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var pool = root.GetComponent<SpawnPool>();
                    if (pool == null)
                    {
                        throw new InvalidOperationException("Spawn pool prefab is missing SpawnPool: " + prefabPath);
                    }

                    var serialized = new SerializedObject(pool);
                    var property = serialized.FindProperty("additionalProjectilePrefabs");
                    if (property == null)
                    {
                        throw new InvalidOperationException("SpawnPool no longer exposes additionalProjectilePrefabs.");
                    }

                    var configured = new List<UnityEngine.Object>(property.arraySize + variants.Count);
                    for (var index = 0; index < property.arraySize; index++)
                    {
                        var existing = property.GetArrayElementAtIndex(index).objectReferenceValue;
                        if (existing != null && !configured.Contains(existing))
                        {
                            configured.Add(existing);
                        }
                    }

                    foreach (var variant in variants)
                    {
                        if (variant != null && !configured.Contains(variant))
                        {
                            configured.Add(variant);
                        }
                    }

                    property.arraySize = configured.Count;
                    for (var index = 0; index < configured.Count; index++)
                    {
                        property.GetArrayElementAtIndex(index).objectReferenceValue = configured[index];
                    }

                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(pool);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static SpriteRenderer GetOrCreateProjectileChildRenderer(
            Transform root,
            string childName,
            SpriteRenderer sourceRenderer,
            int sortingOffset)
        {
            var child = root.Find(childName);
            if (child == null)
            {
                var gameObject = new GameObject(childName, typeof(SpriteRenderer));
                gameObject.layer = root.gameObject.layer;
                gameObject.transform.SetParent(root, worldPositionStays: false);
                child = gameObject.transform;
            }

            child.gameObject.layer = root.gameObject.layer;
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = child.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sharedMaterial = sourceRenderer.sharedMaterial;
            renderer.color = Color.white;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder + sortingOffset;
            return renderer;
        }

        private static Sprite GetRequiredSprite(IReadOnlyDictionary<string, Sprite> sprites, string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName) || !sprites.TryGetValue(spriteName, out var sprite) || sprite == null)
            {
                throw new InvalidOperationException("Missing sliced upgrade projectile sprite: " + spriteName);
            }

            return sprite;
        }

        private static void AssignSpriteSequence(SerializedProperty property, IReadOnlyList<Sprite> sprites)
        {
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized sprite sequence was not found.");
            }

            property.arraySize = sprites.Count;
            for (var index = 0; index < sprites.Count; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = sprites[index];
            }
        }

        private static void AssignObjectReferenceArray<T>(
            SerializedProperty property,
            IReadOnlyList<T> values)
            where T : UnityEngine.Object
        {
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized object-reference array was not found.");
            }

            property.arraySize = values.Count;
            for (var index = 0; index < values.Count; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }
        }

        private static void AssignStringArray(SerializedProperty property, IReadOnlyList<string> values)
        {
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized string array was not found.");
            }

            property.arraySize = values.Count;
            for (var index = 0; index < values.Count; index++)
            {
                property.GetArrayElementAtIndex(index).stringValue = values[index] ?? string.Empty;
            }
        }

        private static void SetObjectReference(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized property was not found: " + propertyName);
            }

            property.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException("Expected serialized property was not found: " + propertyName);
            }

            property.floatValue = value;
        }

        private static T LoadOrCreateAsset<T>(string assetPath)
            where T : ScriptableObject
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

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
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

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath).Replace('\\', '/');
        }

        private readonly struct SpriteSpec
        {
            public SpriteSpec(string name, int column, int row)
            {
                Name = name;
                Column = column;
                Row = row;
            }

            public string Name { get; }
            public int Column { get; }
            public int Row { get; }
        }

        private readonly struct CosmeticLayerSpec
        {
            public CosmeticLayerSpec(string sheetTitle, string key, string displayName)
            {
                SheetTitle = sheetTitle;
                Key = key;
                DisplayName = displayName;
            }

            public string SheetTitle { get; }
            public string Key { get; }
            public string DisplayName { get; }
        }

        private readonly struct WeaponVisualSpec
        {
            public WeaponVisualSpec(string sheetTitle, string key, string weaponPath, bool hasReloadAnimation = false)
            {
                SheetTitle = sheetTitle;
                Key = key;
                WeaponPath = weaponPath;
                HasReloadAnimation = hasReloadAnimation;
            }

            public string SheetTitle { get; }
            public string Key { get; }
            public string WeaponPath { get; }
            public bool HasReloadAnimation { get; }
        }

        private readonly struct ProjectileVisualSpec
        {
            public ProjectileVisualSpec(string prefabPath, string spriteName)
            {
                PrefabPath = prefabPath;
                SpriteName = spriteName;
            }

            public string PrefabPath { get; }
            public string SpriteName { get; }
        }

        private readonly struct UpgradeProjectileVisualSpec
        {
            public UpgradeProjectileVisualSpec(
                string prefabName,
                string basePrefabPath,
                string projectileDefinitionPath,
                string spriteName,
                bool isTrap = false,
                bool isBounce = false,
                string armedSpriteName = null,
                string detonationPrefix = null)
            {
                PrefabName = prefabName;
                BasePrefabPath = basePrefabPath;
                ProjectileDefinitionPath = projectileDefinitionPath;
                SpriteName = spriteName;
                IsTrap = isTrap;
                IsBounce = isBounce;
                ArmedSpriteName = armedSpriteName;
                DetonationPrefix = detonationPrefix;
            }

            public string PrefabName { get; }
            public string BasePrefabPath { get; }
            public string ProjectileDefinitionPath { get; }
            public string SpriteName { get; }
            public bool IsTrap { get; }
            public bool IsBounce { get; }
            public string ArmedSpriteName { get; }
            public string DetonationPrefix { get; }
        }
    }
}
#endif
