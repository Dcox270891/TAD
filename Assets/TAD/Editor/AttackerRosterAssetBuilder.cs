#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAD.Attackers;
using TAD.Pooling;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Rebuilds TAD's data-driven attacker roster from Blender's 16-direction sprite sheets.
    /// It is editor-only so the production game never carries asset-authoring code.
    /// </summary>
    public static class AttackerRosterAssetBuilder
    {
        private const int CellPixels = 64;
        private const int DirectionCount = 16;
        private const int MovementFrameCount = 2;
        private const int DeathFrameCount = 4;
        private const string ArtDirectory = "Assets/TAD/Art/Attackers/Directional";
        private const string VisualDataDirectory = "Assets/TAD/Data/Attackers/Visuals";
        private const string AttackerDataDirectory = "Assets/TAD/Data/Attackers";
        private const string PrefabDirectory = "Assets/TAD/Prefabs/Attackers";
        private const string ChickenDefinitionPath = AttackerDataDirectory + "/ChickenDefinition.asset";
        private const string ChickenPrefabPath = PrefabDirectory + "/Chicken.prefab";
        private const string SpawnPoolPrefabPath = "Assets/TAD/Prefabs/Pooling/SpawnPool.prefab";

        private static readonly AttackerSpec[] Specs =
        {
            new("chicken", "Chicken", "001", 10f, 3f, 1f, 5, 1, 10, 10, 5, AttackerArchetype.Basic, 1, AttackerTraversalProfile.Ground, false),
            new("duck", "Duck", "002", 16f, 3.15f, 1.05f, 7, 2, 12, 12, 5, AttackerArchetype.Basic, 2, AttackerTraversalProfile.Ground, true),
            new("ram", "Ram", "003", 24f, 3.1f, 1.2f, 10, 3, 16, 19, 6, AttackerArchetype.Basic, 3, AttackerTraversalProfile.Ground, true),
            new("hare", "Hare", "004", 6f, 4.4f, 0.75f, 6, 2, 9, 8, 5, AttackerArchetype.Fast, 1, AttackerTraversalProfile.Ground, true),
            new("fox", "Fox", "005", 8f, 5.1f, 0.82f, 8, 3, 12, 13, 5, AttackerArchetype.Fast, 2, AttackerTraversalProfile.Ground, true),
            new("greyhound", "Greyhound", "006", 10f, 5.8f, 0.9f, 10, 4, 16, 18, 6, AttackerArchetype.Fast, 3, AttackerTraversalProfile.Ground, true),
            new("boar", "Boar", "007", 30f, 2.65f, 1.15f, 11, 3, 20, 22, 5, AttackerArchetype.Strong, 1, AttackerTraversalProfile.Ground, true),
            new("bull", "Bull", "008", 45f, 2.35f, 1.35f, 15, 5, 27, 32, 6, AttackerArchetype.Strong, 2, AttackerTraversalProfile.Ground, true),
            new("bison", "Bison", "009", 65f, 2.1f, 1.55f, 20, 7, 36, 45, 6, AttackerArchetype.Strong, 3, AttackerTraversalProfile.Ground, true),
            new("crow", "Crow", "010", 9f, 3.25f, 0.7f, 8, 2, 11, 11, 5, AttackerArchetype.Flying, 1, AttackerTraversalProfile.IgnoresHalfCover, true),
            new("hawk", "Hawk", "011", 14f, 3.35f, 0.82f, 12, 4, 18, 21, 5, AttackerArchetype.Flying, 2, AttackerTraversalProfile.IgnoresHalfCover, true),
            new("eagle", "Eagle", "012", 21f, 3.45f, 0.95f, 17, 6, 27, 35, 6, AttackerArchetype.Flying, 3, AttackerTraversalProfile.IgnoresHalfCover, true)
        };

        [MenuItem("TAD/Attackers/Rebuild Directional Roster")]
        private static void RebuildFromMenu()
        {
            RebuildFromAutomation();
        }

        /// <summary>Used by automated authoring after Blender has rendered the sprite sheets.</summary>
        public static void RebuildFromAutomation()
        {
            EnsureFolder(ArtDirectory);
            EnsureFolder(VisualDataDirectory);
            EnsureFolder(AttackerDataDirectory);
            EnsureFolder(PrefabDirectory);

            foreach (var spec in Specs)
            {
                var sheetPath = ArtDirectory + "/" + spec.Key + "_directions.png";
                if (!File.Exists(ToAbsolutePath(sheetPath)))
                {
                    throw new FileNotFoundException("The Blender directional sheet is missing.", sheetPath);
                }
            }

            var visuals = new Dictionary<string, AttackerVisualDefinition>();
            foreach (var spec in Specs)
            {
                var sprites = ImportAndSliceSheet(spec);
                visuals.Add(spec.Key, CreateOrUpdateVisualDefinition(spec, sprites));
            }

            var definitions = new Dictionary<string, AttackerDefinition>();
            foreach (var spec in Specs)
            {
                definitions.Add(spec.Key, CreateOrUpdateAttackerDefinition(spec, visuals[spec.Key]));
            }

            foreach (var spec in Specs)
            {
                CreateOrUpdatePrefab(spec, definitions[spec.Key], visuals[spec.Key]);
            }

            ConfigureAdditionalAttackerPools();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"TAD rebuilt {Specs.Length} directional attacker visual sets and reusable attacker prefabs.");
        }

        private static Dictionary<string, Sprite> ImportAndSliceSheet(AttackerSpec spec)
        {
            var sheetPath = ArtDirectory + "/" + spec.Key + "_directions.png";
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"{sheetPath} is not a TextureImporter.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = CellPixels;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 75;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();

            // Sprite rect edits use the provider API and capability checks mandated by the Sprite Editor contract.
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException($"{sheetPath} does not support sprite editing. Operation aborted.");
            }

            dataProvider.InitSpriteEditorDataProvider();
            var editCapability = dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException($"{sheetPath} has no sprite-frame edit capability. Operation aborted.");
            }

            var capability = editCapability.GetEditCapability();
            if (!capability.HasCapability(EEditCapability.EditSpriteName) ||
                !capability.HasCapability(EEditCapability.EditSpriteRect) ||
                !capability.HasCapability(EEditCapability.EditPivot) ||
                !capability.HasCapability(EEditCapability.CreateAndDeleteSprite))
            {
                throw new InvalidOperationException($"{sheetPath} cannot safely create the required directional sprites. Operation aborted.");
            }

            var rects = new List<SpriteRect>(DirectionCount * (MovementFrameCount + DeathFrameCount));
            for (var direction = 0; direction < DirectionCount; direction++)
            {
                for (var frame = 0; frame < MovementFrameCount; frame++)
                {
                    rects.Add(CreateSpriteRect(
                        $"{spec.Key}_d{direction:D2}_move{frame}",
                        frame,
                        direction));
                }

                for (var frame = 0; frame < DeathFrameCount; frame++)
                {
                    rects.Add(CreateSpriteRect(
                        $"{spec.Key}_d{direction:D2}_death{frame}",
                        MovementFrameCount + frame,
                        direction));
                }
            }

            dataProvider.SetSpriteRects(rects.ToArray());
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                var pairs = rects
                    .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList();
                nameFileIdProvider.SetNameFileIdPairs(pairs);
            }

            dataProvider.Apply();
            importer.SaveAndReimport();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);

            var expectedSpriteCount = DirectionCount * (MovementFrameCount + DeathFrameCount);
            if (sprites.Count != expectedSpriteCount)
            {
                throw new InvalidOperationException($"{sheetPath} imported {sprites.Count} sprites; expected {expectedSpriteCount}.");
            }

            return sprites;
        }

        private static SpriteRect CreateSpriteRect(string spriteName, int column, int directionRow)
        {
            return new SpriteRect
            {
                name = spriteName,
                rect = new Rect(
                    column * CellPixels,
                    (DirectionCount - directionRow - 1) * CellPixels,
                    CellPixels,
                    CellPixels),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero,
                spriteID = GUID.Generate()
            };
        }

        private static AttackerVisualDefinition CreateOrUpdateVisualDefinition(
            AttackerSpec spec,
            IReadOnlyDictionary<string, Sprite> sprites)
        {
            var assetPath = VisualDataDirectory + "/" + spec.DisplayName + "VisualDefinition.asset";
            var visual = AssetDatabase.LoadAssetAtPath<AttackerVisualDefinition>(assetPath);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<AttackerVisualDefinition>();
                AssetDatabase.CreateAsset(visual, assetPath);
            }

            var serialized = new SerializedObject(visual);
            AssignSpriteArray(serialized.FindProperty("movementSprites"), spec.Key, "move", MovementFrameCount, sprites);
            AssignSpriteArray(serialized.FindProperty("deathSprites"), spec.Key, "death", DeathFrameCount, sprites);
            serialized.FindProperty("movementFrameDuration").floatValue = 0.16f;
            serialized.FindProperty("deathFrameDuration").floatValue = 0.12f;
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

        private static AttackerDefinition CreateOrUpdateAttackerDefinition(
            AttackerSpec spec,
            AttackerVisualDefinition visuals)
        {
            if (!spec.UpdateDefinition)
            {
                var chicken = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(ChickenDefinitionPath);
                if (chicken == null)
                {
                    throw new FileNotFoundException("The existing Chicken attacker definition is missing.", ChickenDefinitionPath);
                }

                // Preserve the user's existing Chicken data asset exactly; Chicken's prefab uses a visual fallback.
                return chicken;
            }

            var assetPath = AttackerDataDirectory + "/" + spec.DisplayName + "Definition.asset";
            var definition = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(assetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<AttackerDefinition>();
                AssetDatabase.CreateAsset(definition, assetPath);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("attackerId").stringValue = spec.Id;
            serialized.FindProperty("displayName").stringValue = spec.DisplayName;
            serialized.FindProperty("maximumHealth").floatValue = spec.MaximumHealth;
            serialized.FindProperty("movementSpeed").floatValue = spec.MovementSpeed;
            serialized.FindProperty("size").floatValue = spec.Size;
            serialized.FindProperty("sendCost").intValue = spec.SendCost;
            serialized.FindProperty("waveIncome").intValue = spec.WaveIncome;
            serialized.FindProperty("expGiven").intValue = spec.ExpGiven;
            serialized.FindProperty("killGain").intValue = spec.KillGain;
            serialized.FindProperty("sendSize").intValue = spec.SendSize;
            serialized.FindProperty("archetype").enumValueIndex = (int)spec.Archetype;
            serialized.FindProperty("challengeTier").intValue = spec.ChallengeTier;
            serialized.FindProperty("traversalProfile").enumValueIndex = (int)spec.TraversalProfile;
            serialized.FindProperty("visualDefinition").objectReferenceValue = visuals;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void CreateOrUpdatePrefab(
            AttackerSpec spec,
            AttackerDefinition definition,
            AttackerVisualDefinition visuals)
        {
            var destinationPath = PrefabDirectory + "/" + spec.DisplayName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(ChickenPrefabPath);
            try
            {
                root.name = spec.DisplayName;
                var attacker = root.GetComponent<BasicAttacker>();
                var visualTransform = root.transform.Find("Visual");
                if (attacker == null || visualTransform == null ||
                    !visualTransform.TryGetComponent<SpriteRenderer>(out var renderer))
                {
                    throw new InvalidOperationException("Chicken prefab no longer has the expected attacker Visual setup.");
                }

                if (spec.UpdateDefinition)
                {
                    var attackerSerialized = new SerializedObject(attacker);
                    attackerSerialized.FindProperty("definition").objectReferenceValue = definition;
                    attackerSerialized.ApplyModifiedPropertiesWithoutUndo();
                }

                var controller = visualTransform.GetComponent<AttackerVisualController>();
                if (controller == null)
                {
                    controller = visualTransform.gameObject.AddComponent<AttackerVisualController>();
                }

                var controllerSerialized = new SerializedObject(controller);
                controllerSerialized.FindProperty("spriteRenderer").objectReferenceValue = renderer;
                controllerSerialized.FindProperty("fallbackVisualDefinition").objectReferenceValue = visuals;
                controllerSerialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.sprite = visuals.GetMovementSprite(0, 0);
                EditorUtility.SetDirty(renderer);
                PrefabUtility.SaveAsPrefabAsset(root, destinationPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureAdditionalAttackerPools()
        {
            var root = PrefabUtility.LoadPrefabContents(SpawnPoolPrefabPath);
            try
            {
                var serialized = new SerializedObject(root.GetComponent<SpawnPool>());
                var prefabs = serialized.FindProperty("additionalAttackerPrefabs");
                prefabs.arraySize = Specs.Length - 1;
                for (var index = 1; index < Specs.Length; index++)
                {
                    prefabs.GetArrayElementAtIndex(index - 1).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDirectory + "/" + Specs[index].DisplayName + ".prefab");
                }

                serialized.FindProperty("additionalAttackerInitialCapacity").intValue = 5;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, SpawnPoolPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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

        private readonly struct AttackerSpec
        {
            public AttackerSpec(
                string key,
                string displayName,
                string id,
                float maximumHealth,
                float movementSpeed,
                float size,
                int sendCost,
                int waveIncome,
                int expGiven,
                int killGain,
                int sendSize,
                AttackerArchetype archetype,
                int challengeTier,
                AttackerTraversalProfile traversalProfile,
                bool updateDefinition)
            {
                Key = key;
                DisplayName = displayName;
                Id = id;
                MaximumHealth = maximumHealth;
                MovementSpeed = movementSpeed;
                Size = size;
                SendCost = sendCost;
                WaveIncome = waveIncome;
                ExpGiven = expGiven;
                KillGain = killGain;
                SendSize = sendSize;
                Archetype = archetype;
                ChallengeTier = challengeTier;
                TraversalProfile = traversalProfile;
                UpdateDefinition = updateDefinition;
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string Id { get; }
            public float MaximumHealth { get; }
            public float MovementSpeed { get; }
            public float Size { get; }
            public int SendCost { get; }
            public int WaveIncome { get; }
            public int ExpGiven { get; }
            public int KillGain { get; }
            public int SendSize { get; }
            public AttackerArchetype Archetype { get; }
            public int ChallengeTier { get; }
            public AttackerTraversalProfile TraversalProfile { get; }
            public bool UpdateDefinition { get; }
        }
    }
}
#endif
