#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAD.Defenders;
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
        private const int DirectionalColumns = 8;
        private const int DirectionalRows = 4;
        private const int FiringRows = 6;
        private const int ProjectileColumns = 6;
        private const int SpritePixelsPerUnit = 24;

        private const string ArtDirectory = "Assets/TAD/Art/Farmhands/Directional";
        private const string ProjectileArtDirectory = "Assets/TAD/Art/Farmhands/Projectiles";
        private const string VisualDataDirectory = "Assets/TAD/Data/Defenders/Visuals";
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";

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
                "Assets/TAD/Data/Defenders/AirRifleWeaponDefinition.asset")
        };

        private static readonly ProjectileVisualSpec[] ProjectileSpecs =
        {
            new("Assets/TAD/Prefabs/Projectiles/FarmHandProjectile.prefab", "farmhand_projectile_unarmed"),
            new("Assets/TAD/Prefabs/Projectiles/PoleSweepProjectile.prefab", "farmhand_projectile_pole"),
            new("Assets/TAD/Prefabs/Projectiles/RollingProjectile.prefab", "farmhand_projectile_rolling"),
            new("Assets/TAD/Prefabs/Projectiles/TrapProjectile.prefab", "farmhand_projectile_trap_closed"),
            new("Assets/TAD/Prefabs/Projectiles/AirRifleBulletProjectile.prefab", "farmhand_projectile_air_rifle")
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

            var bodySprites = ImportDirectionalSheet(
                ArtDirectory + "/FarmhandBody_Default_Move.png",
                "farmhand_body",
                "move",
                MovementFrameCount,
                DirectionalRows);
            var hairSprites = ImportDirectionalSheet(
                ArtDirectory + "/FarmhandHair_ShortBrown_Move.png",
                "farmhand_hair",
                "move",
                MovementFrameCount,
                DirectionalRows);
            var outfitSprites = ImportDirectionalSheet(
                ArtDirectory + "/FarmhandOutfit_BlueOveralls_Move.png",
                "farmhand_outfit",
                "move",
                MovementFrameCount,
                DirectionalRows);

            var body = CreateOrUpdateLayerDefinition<FarmHandBodyVisualDefinition>(
                VisualDataDirectory + "/FarmHandBody_Default.asset",
                "farmhand_body",
                bodySprites);
            var hair = CreateOrUpdateLayerDefinition<FarmHandHairVisualDefinition>(
                VisualDataDirectory + "/FarmHandHair_ShortBrown.asset",
                "farmhand_hair",
                hairSprites);
            var outfit = CreateOrUpdateLayerDefinition<FarmHandOutfitVisualDefinition>(
                VisualDataDirectory + "/FarmHandOutfit_BlueOveralls.asset",
                "farmhand_outfit",
                outfitSprites);
            var appearance = CreateOrUpdateAppearance(body, hair, outfit);

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
                weaponVisuals.Add(CreateOrUpdateWeaponVisual(spec, moveSprites, fireSprites));
            }

            var projectileSprites = ImportProjectileSheet();
            UpdateFarmHandPrefab(appearance, weaponVisuals);
            UpdateProjectilePrefabs(projectileSprites);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("TAD rebuilt the modular farmhand, weapon-firing, and projectile visual set.");
        }

        private static void EnsureRequiredFiles()
        {
            var requiredPaths = new List<string>
            {
                ArtDirectory + "/FarmhandBody_Default_Move.png",
                ArtDirectory + "/FarmhandHair_ShortBrown_Move.png",
                ArtDirectory + "/FarmhandOutfit_BlueOveralls_Move.png",
                ProjectileArtDirectory + "/FarmhandProjectiles.png"
            };

            foreach (var spec in WeaponSpecs)
            {
                requiredPaths.Add(ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Move.png");
                requiredPaths.Add(ArtDirectory + $"/FarmhandWeapon_{spec.SheetTitle}_Fire.png");
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

        private static FarmHandAppearanceDefinition CreateOrUpdateAppearance(
            FarmHandBodyVisualDefinition body,
            FarmHandHairVisualDefinition hair,
            FarmHandOutfitVisualDefinition outfit)
        {
            var assetPath = VisualDataDirectory + "/FarmHandAppearance_Default.asset";
            var appearance = LoadOrCreateAsset<FarmHandAppearanceDefinition>(assetPath);
            var serialized = new SerializedObject(appearance);
            serialized.FindProperty("displayName").stringValue = "Default Farmhand";
            serialized.FindProperty("body").objectReferenceValue = body;
            serialized.FindProperty("hair").objectReferenceValue = hair;
            serialized.FindProperty("outfit").objectReferenceValue = outfit;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(appearance);
            return appearance;
        }

        private static FarmHandWeaponVisualDefinition CreateOrUpdateWeaponVisual(
            WeaponVisualSpec spec,
            IReadOnlyDictionary<string, Sprite> moveSprites,
            IReadOnlyDictionary<string, Sprite> fireSprites)
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

        private static void UpdateFarmHandPrefab(
            FarmHandAppearanceDefinition appearance,
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

                bodyRenderer.sprite = appearance.Body.GetMovementSprite(0, 0);
                bodyRenderer.enabled = true;
                var hairRenderer = GetOrCreateLayerRenderer(root.transform, "HairVisual", bodyRenderer, 1);
                var outfitRenderer = GetOrCreateLayerRenderer(root.transform, "OutfitVisual", bodyRenderer, 2);
                var weaponRenderer = GetOrCreateLayerRenderer(root.transform, "WeaponVisual", bodyRenderer, 3);
                hairRenderer.sprite = appearance.Hair.GetMovementSprite(0, 0);
                outfitRenderer.sprite = appearance.Outfit.GetMovementSprite(0, 0);
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
                controllerSerialized.FindProperty("appearance").objectReferenceValue = appearance;
                var weaponVisualsProperty = controllerSerialized.FindProperty("weaponVisuals");
                weaponVisualsProperty.arraySize = weaponVisuals.Count;
                for (var index = 0; index < weaponVisuals.Count; index++)
                {
                    weaponVisualsProperty.GetArrayElementAtIndex(index).objectReferenceValue = weaponVisuals[index];
                }

                controllerSerialized.FindProperty("aimTransitionDuration").floatValue = 0.16f;
                controllerSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bodyRenderer);
                EditorUtility.SetDirty(farmHand);
                EditorUtility.SetDirty(controller);
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

        private readonly struct WeaponVisualSpec
        {
            public WeaponVisualSpec(string sheetTitle, string key, string weaponPath)
            {
                SheetTitle = sheetTitle;
                Key = key;
                WeaponPath = weaponPath;
            }

            public string SheetTitle { get; }
            public string Key { get; }
            public string WeaponPath { get; }
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
    }
}
#endif
