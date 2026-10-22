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
    /// Rebuilds TAD's data-driven attacker roster. A compact static atlas is the preferred
    /// visual source for each archetype; the runtime controller gives each atlas member a compact
    /// sixteen-direction walk and four-step defeat sequence. Legacy directional sheets remain a
    /// safe authoring fallback for future bespoke sprite-sheet animation.
    /// It is editor-only so the production game never carries asset-authoring code.
    /// </summary>
    public static class AttackerRosterAssetBuilder
    {
        private const int CellPixels = 64;
        private const int DirectionCount = 16;
        private const int MovementFrameCount = 2;
        private const int DeathFrameCount = 4;
        private const string ArtDirectory = "Assets/TAD/Art/Attackers/Directional";
        private const string StaticAtlasDirectory = "Assets/TAD/Art/Attackers/Atlases";
        private const string EffectsDirectory = "Assets/TAD/Art/Effects";
        private const string DeathEffectPath = EffectsDirectory + "/TAD_AnimalDeathDust.png";
        private const int StaticAtlasColumns = 4;
        private const int StaticAtlasRows = 3;
        private const int StaticAtlasTierSlots = 10;
        private const int DeathEffectFrameCount = 4;
        // Atlas silhouettes occupy roughly 250 pixels inside a 362-pixel cell. At 512 PPU they
        // remain about 0.4–0.55 world units before AttackerVisualController's existing x4 scale,
        // keeping them comparable with the project's standard 2-unit wall module.
        private const float StaticAtlasPixelsPerUnit = 512f;
        private const string VisualDataDirectory = "Assets/TAD/Data/Attackers/Visuals";
        private const string AttackerDataDirectory = "Assets/TAD/Data/Attackers";
        private const string PrefabDirectory = "Assets/TAD/Prefabs/Attackers";
        private const string ChickenPrefabPath = PrefabDirectory + "/Chicken.prefab";
        private const string TestDataDirectory = "Assets/Resources/TADTests";
        private static readonly string[] SpawnPoolPrefabPaths =
        {
            "Assets/TAD/Prefabs/Pooling/SpawnPool.prefab",
            "Assets/Resources/TADTests/SpawnPool.prefab"
        };

        // These six assets previously occupied the same stable identifiers that the farm-only
        // roster now uses. Moving the asset instead of creating a replacement preserves the GUID
        // stored in existing level wave overrides, so authored Level 2 and Level 3 waves continue
        // to resolve to their equivalent farm animal after a roster rebuild.
        private static readonly KeyValuePair<string, string>[] LegacyDisplayNameMigrations =
        {
            new("Hare", "Rabbit"),
            new("Fox", "Fallow Deer"),
            new("Greyhound", "Red Deer"),
            new("Crow", "Pigeon"),
            new("Hawk", "Japanese Quail"),
            new("Eagle", "Muscovy Duck")
        };

        // Routing rounds sizes to three decimal places. Keeping the roster on these five authored
        // diameters prevents a forty-animal catalogue from needlessly producing forty cached route
        // fields at level start, while still making the smallest birds and the largest livestock read
        // differently in play.
        private const float SmallSize = 0.75f;
        private const float StandardSize = 1.0f;
        private const float MediumSize = 1.25f;
        private const float LargeSize = 1.5f;
        private const float HugeSize = 1.75f;

        private static readonly AttackerArchetype[] RosterArchetypes =
        {
            AttackerArchetype.Basic,
            AttackerArchetype.Fast,
            AttackerArchetype.Strong,
            AttackerArchetype.Flying
        };

        private static readonly AttackerSpec[] Specs =
        {
            // Every entry is a real animal kept, raised, or ranch-farmed somewhere in the world.
            // The identifier order deliberately preserves the twelve identifiers that shipped in the
            // first roster, including the three original flight slots now represented by farm birds.

            // Basic: dependable ground livestock.
            new("chicken", "Chicken", "001", 5f, 2.10f, SmallSize, 6, 1, 2, 1, 4, AttackerArchetype.Basic, 1, AttackerTraversalProfile.Ground, "chicken"),
            new("duck", "Duck", "002", 8f, 2.25f, SmallSize, 11, 2, 3, 2, 4, AttackerArchetype.Basic, 2, AttackerTraversalProfile.Ground, "duck"),
            new("ram", "Ram", "003", 12f, 2.40f, StandardSize, 17, 4, 5, 3, 5, AttackerArchetype.Basic, 3, AttackerTraversalProfile.Ground, "ram"),
            new("turkey", "Turkey", "013", 16f, 2.55f, StandardSize, 22, 5, 7, 4, 5, AttackerArchetype.Basic, 4, AttackerTraversalProfile.Ground, "duck"),
            new("goose", "Goose", "014", 20f, 2.70f, MediumSize, 28, 7, 9, 5, 5, AttackerArchetype.Basic, 5, AttackerTraversalProfile.Ground, "duck"),
            new("sheep", "Sheep", "015", 24f, 2.85f, MediumSize, 35, 9, 12, 7, 5, AttackerArchetype.Basic, 6, AttackerTraversalProfile.Ground, "ram"),
            new("goat", "Goat", "016", 29f, 3.00f, MediumSize, 43, 12, 15, 9, 6, AttackerArchetype.Basic, 7, AttackerTraversalProfile.Ground, "ram"),
            new("pig", "Pig", "017", 34f, 3.15f, LargeSize, 52, 15, 19, 11, 6, AttackerArchetype.Basic, 8, AttackerTraversalProfile.Ground, "boar"),
            new("alpaca", "Alpaca", "018", 39f, 3.30f, LargeSize, 62, 19, 24, 14, 6, AttackerArchetype.Basic, 9, AttackerTraversalProfile.Ground, "ram"),
            new("llama", "Llama", "019", 45f, 3.45f, LargeSize, 74, 24, 30, 18, 6, AttackerArchetype.Basic, 10, AttackerTraversalProfile.Ground, "ram"),

            // Fast: quick stock and animals raised on deer, game, ratite, and working farms.
            new("rabbit", "Rabbit", "004", 3.5f, 3.20f, SmallSize, 7, 1, 2, 1, 4, AttackerArchetype.Fast, 1, AttackerTraversalProfile.Ground, "hare"),
            new("fallow_deer", "Fallow Deer", "005", 5f, 3.55f, StandardSize, 12, 2, 3, 2, 4, AttackerArchetype.Fast, 2, AttackerTraversalProfile.Ground, "hare"),
            new("red_deer", "Red Deer", "006", 7f, 3.90f, MediumSize, 19, 4, 5, 3, 5, AttackerArchetype.Fast, 3, AttackerTraversalProfile.Ground, "greyhound"),
            new("reindeer", "Reindeer", "020", 9f, 4.20f, LargeSize, 25, 5, 7, 4, 5, AttackerArchetype.Fast, 4, AttackerTraversalProfile.Ground, "greyhound"),
            new("springbok", "Springbok", "021", 11.5f, 4.50f, StandardSize, 32, 8, 9, 5, 5, AttackerArchetype.Fast, 5, AttackerTraversalProfile.Ground, "greyhound"),
            new("impala", "Impala", "022", 14f, 4.75f, StandardSize, 40, 11, 12, 7, 5, AttackerArchetype.Fast, 6, AttackerTraversalProfile.Ground, "greyhound"),
            new("emu", "Emu", "023", 16.5f, 5.00f, MediumSize, 49, 14, 15, 9, 6, AttackerArchetype.Fast, 7, AttackerTraversalProfile.Ground, "greyhound"),
            new("greater_rhea", "Greater Rhea", "024", 19f, 5.20f, MediumSize, 59, 18, 19, 11, 6, AttackerArchetype.Fast, 8, AttackerTraversalProfile.Ground, "greyhound"),
            new("kudu", "Kudu", "025", 22f, 5.40f, LargeSize, 70, 23, 24, 14, 6, AttackerArchetype.Fast, 9, AttackerTraversalProfile.Ground, "greyhound"),
            new("ostrich", "Ostrich", "026", 25f, 5.65f, LargeSize, 84, 29, 30, 18, 6, AttackerArchetype.Fast, 10, AttackerTraversalProfile.Ground, "greyhound"),

            // Strong: progressively heavier farm and ranch animals.
            new("boar", "Boar", "007", 11f, 1.60f, MediumSize, 11, 2, 2, 3, 3, AttackerArchetype.Strong, 1, AttackerTraversalProfile.Ground, "boar"),
            new("bull", "Bull", "008", 16f, 1.78f, LargeSize, 18, 4, 4, 4, 3, AttackerArchetype.Strong, 2, AttackerTraversalProfile.Ground, "bull"),
            new("bison", "Bison", "009", 23f, 1.96f, HugeSize, 28, 6, 6, 6, 4, AttackerArchetype.Strong, 3, AttackerTraversalProfile.Ground, "bison"),
            new("highland_cow", "Highland Cow", "027", 31f, 2.14f, HugeSize, 39, 9, 9, 8, 4, AttackerArchetype.Strong, 4, AttackerTraversalProfile.Ground, "bull"),
            new("water_buffalo", "Water Buffalo", "028", 40f, 2.32f, HugeSize, 51, 13, 13, 11, 4, AttackerArchetype.Strong, 5, AttackerTraversalProfile.Ground, "bison"),
            new("yak", "Yak", "029", 50f, 2.50f, HugeSize, 68, 18, 18, 15, 5, AttackerArchetype.Strong, 6, AttackerTraversalProfile.Ground, "bison"),
            new("zebu_bull", "Zebu Bull", "030", 61f, 2.68f, HugeSize, 85, 24, 24, 20, 5, AttackerArchetype.Strong, 7, AttackerTraversalProfile.Ground, "bull"),
            new("dromedary_camel", "Dromedary Camel", "031", 73f, 2.86f, HugeSize, 104, 31, 31, 26, 5, AttackerArchetype.Strong, 8, AttackerTraversalProfile.Ground, "bison"),
            new("musk_ox", "Musk Ox", "032", 86f, 3.04f, HugeSize, 126, 40, 40, 34, 5, AttackerArchetype.Strong, 9, AttackerTraversalProfile.Ground, "bison"),
            new("bactrian_camel", "Bactrian Camel", "033", 100f, 3.22f, HugeSize, 150, 51, 51, 43, 5, AttackerArchetype.Strong, 10, AttackerTraversalProfile.Ground, "bison"),

            // Flying: farm-raised birds that can pass half cover, rather than wild crows or raptors.
            new("pigeon", "Pigeon", "010", 4f, 2.60f, SmallSize, 8, 1, 2, 1, 3, AttackerArchetype.Flying, 1, AttackerTraversalProfile.IgnoresHalfCover, "crow"),
            new("japanese_quail", "Japanese Quail", "011", 6f, 2.85f, SmallSize, 14, 3, 4, 2, 4, AttackerArchetype.Flying, 2, AttackerTraversalProfile.IgnoresHalfCover, "hawk"),
            new("muscovy_duck", "Muscovy Duck", "012", 9f, 3.10f, MediumSize, 22, 5, 6, 4, 4, AttackerArchetype.Flying, 3, AttackerTraversalProfile.IgnoresHalfCover, "duck"),
            new("mallard_duck", "Mallard Duck", "034", 12f, 3.30f, MediumSize, 29, 7, 8, 5, 4, AttackerArchetype.Flying, 4, AttackerTraversalProfile.IgnoresHalfCover, "duck"),
            new("pheasant", "Pheasant", "035", 15f, 3.50f, StandardSize, 37, 10, 11, 7, 4, AttackerArchetype.Flying, 5, AttackerTraversalProfile.IgnoresHalfCover, "hawk"),
            new("partridge", "Partridge", "036", 18f, 3.70f, StandardSize, 46, 13, 14, 9, 5, AttackerArchetype.Flying, 6, AttackerTraversalProfile.IgnoresHalfCover, "hawk"),
            new("chukar_partridge", "Chukar Partridge", "037", 21f, 3.90f, StandardSize, 56, 17, 18, 12, 5, AttackerArchetype.Flying, 7, AttackerTraversalProfile.IgnoresHalfCover, "hawk"),
            new("guinea_fowl", "Guinea Fowl", "038", 25f, 4.10f, MediumSize, 67, 22, 23, 15, 5, AttackerArchetype.Flying, 8, AttackerTraversalProfile.IgnoresHalfCover, "crow"),
            new("mandarin_duck", "Mandarin Duck", "039", 29f, 4.30f, MediumSize, 79, 28, 29, 19, 5, AttackerArchetype.Flying, 9, AttackerTraversalProfile.IgnoresHalfCover, "duck"),
            new("peafowl", "Peafowl", "040", 34f, 4.50f, MediumSize, 93, 35, 36, 24, 5, AttackerArchetype.Flying, 10, AttackerTraversalProfile.IgnoresHalfCover, "eagle")
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
            EnsureFolder(EffectsDirectory);
            EnsureFolder(VisualDataDirectory);
            EnsureFolder(AttackerDataDirectory);
            EnsureFolder(PrefabDirectory);

            // Discover art copied into the project by an artist before deciding whether a legacy
            // directional seed must be copied. The atlas source itself stays immutable; only its
            // importer settings and generated visual-definition references are maintained here.
            AssetDatabase.Refresh();
            MigrateLegacyRosterAssets();
            var atlasSprites = ImportStaticAtlasSprites();
            var deathEffectSprites = ImportAndSliceDeathEffect();
            EnsureDirectionalSheets(atlasSprites.Keys);

            var visuals = new Dictionary<string, AttackerVisualDefinition>();
            foreach (var spec in Specs)
            {
                if (atlasSprites.TryGetValue(spec.Archetype, out var sprites) &&
                    sprites.TryGetValue(spec.ChallengeTier, out var staticSprite))
                {
                    visuals.Add(spec.Key, CreateOrUpdateVisualDefinition(spec, staticSprite, deathEffectSprites));
                    continue;
                }

                var directionalSprites = ImportAndSliceSheet(spec);
                visuals.Add(spec.Key, CreateOrUpdateVisualDefinition(spec, directionalSprites, deathEffectSprites));
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
            RemoveRetiredAttackerAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            TadAudioAssetBuilder.GenerateAndAssign();
            ConfigureTestFixtureDefinitions(definitions);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"TAD rebuilt {Specs.Length} attacker visual sets and reusable attacker prefabs " +
                $"({atlasSprites.Count} static archetype atlases; remaining archetypes use directional fallbacks).");
        }

        private static void EnsureDirectionalSheets(IEnumerable<AttackerArchetype> archetypesWithStaticAtlas)
        {
            var staticAtlasArchetypes = new HashSet<AttackerArchetype>(archetypesWithStaticAtlas);
            foreach (var spec in Specs)
            {
                if (staticAtlasArchetypes.Contains(spec.Archetype))
                {
                    continue;
                }

                var sheetPath = ArtDirectory + "/" + spec.Key + "_directions.png";
                if (File.Exists(ToAbsolutePath(sheetPath)))
                {
                    continue;
                }

                var seedPath = ArtDirectory + "/" + spec.ArtSeedKey + "_directions.png";
                if (!File.Exists(ToAbsolutePath(seedPath)))
                {
                    throw new FileNotFoundException(
                        "The directional sheet and its configured reusable art seed are both missing.",
                        sheetPath + " (seed: " + seedPath + ")");
                }

                // A generated Blender sheet is preferred when art is available. Until bespoke art is
                // rendered, copy the closest existing directional sheet so every new data asset,
                // prefab, and pool slot stays valid and the art build is reproducible on a clean clone.
                File.Copy(ToAbsolutePath(seedPath), ToAbsolutePath(sheetPath), overwrite: false);
                AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        /// <summary>
        /// Imports each archetype's 4x3 visual catalogue. Slots zero through nine map in reading
        /// order to challenge tiers one through ten; the final two cells are deliberately spare so
        /// an artist can keep layout room without adding a phantom roster entry.
        /// </summary>
        private static Dictionary<AttackerArchetype, Dictionary<int, Sprite>> ImportStaticAtlasSprites()
        {
            var result = new Dictionary<AttackerArchetype, Dictionary<int, Sprite>>();
            foreach (var archetype in RosterArchetypes)
            {
                var atlasPath = GetStaticAtlasPath(archetype);
                if (!File.Exists(ToAbsolutePath(atlasPath)))
                {
                    continue;
                }

                result.Add(archetype, ImportAndSliceStaticAtlas(atlasPath, archetype));
            }

            return result;
        }

        private static string GetStaticAtlasPath(AttackerArchetype archetype)
        {
            return StaticAtlasDirectory + "/TAD_" + archetype + "RosterAtlas.png";
        }

        private static Dictionary<int, Sprite> ImportAndSliceStaticAtlas(
            string atlasPath,
            AttackerArchetype archetype)
        {
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"{atlasPath} is not a TextureImporter.");
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            if (texture == null || texture.width % StaticAtlasColumns != 0 || texture.height % StaticAtlasRows != 0)
            {
                throw new InvalidOperationException(
                    $"{atlasPath} must be a {StaticAtlasColumns}x{StaticAtlasRows} atlas with evenly sized cells.");
            }

            var cellWidth = texture.width / StaticAtlasColumns;
            var cellHeight = texture.height / StaticAtlasRows;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = StaticAtlasPixelsPerUnit;
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
                throw new InvalidOperationException($"{atlasPath} does not support sprite editing. Operation aborted.");
            }

            dataProvider.InitSpriteEditorDataProvider();
            var editCapability = dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException($"{atlasPath} has no sprite-frame edit capability. Operation aborted.");
            }

            var capability = editCapability.GetEditCapability();
            if (!capability.HasCapability(EEditCapability.EditSpriteName) ||
                !capability.HasCapability(EEditCapability.EditSpriteRect) ||
                !capability.HasCapability(EEditCapability.EditPivot) ||
                !capability.HasCapability(EEditCapability.CreateAndDeleteSprite))
            {
                throw new InvalidOperationException($"{atlasPath} cannot safely create the required atlas sprites. Operation aborted.");
            }

            var atlasName = Path.GetFileNameWithoutExtension(atlasPath);
            var rects = new List<SpriteRect>(StaticAtlasColumns * StaticAtlasRows);
            for (var row = 0; row < StaticAtlasRows; row++)
            {
                for (var column = 0; column < StaticAtlasColumns; column++)
                {
                    var slot = (row * StaticAtlasColumns) + column;
                    rects.Add(new SpriteRect
                    {
                        name = atlasName + "_slot" + slot.ToString("D2"),
                        rect = new Rect(
                            column * cellWidth,
                            (StaticAtlasRows - row - 1) * cellHeight,
                            cellWidth,
                            cellHeight),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                        spriteID = GUID.Generate()
                    });
                }
            }

            dataProvider.SetSpriteRects(rects.ToArray());
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                nameFileIdProvider.SetNameFileIdPairs(rects
                    .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            }

            dataProvider.Apply();
            importer.SaveAndReimport();
            var spritesByName = AssetDatabase.LoadAllAssetsAtPath(atlasPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);
            if (spritesByName.Count != StaticAtlasColumns * StaticAtlasRows)
            {
                throw new InvalidOperationException(
                    $"{atlasPath} imported {spritesByName.Count} sprites; expected {StaticAtlasColumns * StaticAtlasRows}.");
            }

            var tiers = new Dictionary<int, Sprite>(StaticAtlasTierSlots);
            for (var tier = 1; tier <= StaticAtlasTierSlots; tier++)
            {
                var spriteName = atlasName + "_slot" + (tier - 1).ToString("D2");
                if (!spritesByName.TryGetValue(spriteName, out var sprite))
                {
                    throw new InvalidOperationException(
                        $"{atlasPath} is missing the visual for {archetype} challenge tier {tier}.");
                }

                tiers.Add(tier, sprite);
            }

            return tiers;
        }

        /// <summary>
        /// Imports the shared four-frame feather-and-dust defeat effect. It is deliberately one
        /// reusable sheet rather than forty copies, keeping the 40-animal roster mobile friendly.
        /// </summary>
        private static Sprite[] ImportAndSliceDeathEffect()
        {
            if (!File.Exists(ToAbsolutePath(DeathEffectPath)))
            {
                Debug.LogWarning("TAD attacker death-effect sheet is missing: " + DeathEffectPath);
                return Array.Empty<Sprite>();
            }

            AssetDatabase.ImportAsset(DeathEffectPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(DeathEffectPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(DeathEffectPath + " is not a TextureImporter.");
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DeathEffectPath);
            if (texture == null || texture.width % DeathEffectFrameCount != 0)
            {
                throw new InvalidOperationException(
                    DeathEffectPath + " must contain " + DeathEffectFrameCount + " evenly-sized horizontal frames.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = StaticAtlasPixelsPerUnit;
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
                throw new InvalidOperationException(DeathEffectPath + " does not support sprite editing.");
            }

            dataProvider.InitSpriteEditorDataProvider();
            var editCapability = dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException(DeathEffectPath + " has no sprite-frame edit capability.");
            }

            var capability = editCapability.GetEditCapability();
            if (!capability.HasCapability(EEditCapability.EditSpriteName) ||
                !capability.HasCapability(EEditCapability.EditSpriteRect) ||
                !capability.HasCapability(EEditCapability.EditPivot) ||
                !capability.HasCapability(EEditCapability.CreateAndDeleteSprite))
            {
                throw new InvalidOperationException(DeathEffectPath + " cannot safely create the death-effect frames.");
            }

            var effectName = Path.GetFileNameWithoutExtension(DeathEffectPath);
            var frameWidth = texture.width / DeathEffectFrameCount;
            var rects = new List<SpriteRect>(DeathEffectFrameCount);
            for (var frame = 0; frame < DeathEffectFrameCount; frame++)
            {
                rects.Add(new SpriteRect
                {
                    name = effectName + "_frame" + frame,
                    rect = new Rect(frame * frameWidth, 0f, frameWidth, texture.height),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = GUID.Generate()
                });
            }

            dataProvider.SetSpriteRects(rects.ToArray());
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                nameFileIdProvider.SetNameFileIdPairs(rects
                    .Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID))
                    .ToList());
            }

            dataProvider.Apply();
            importer.SaveAndReimport();
            var spritesByName = AssetDatabase.LoadAllAssetsAtPath(DeathEffectPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);
            if (spritesByName.Count != DeathEffectFrameCount)
            {
                throw new InvalidOperationException(
                    DeathEffectPath + " imported " + spritesByName.Count + " sprites; expected " + DeathEffectFrameCount + ".");
            }

            var frames = new Sprite[DeathEffectFrameCount];
            for (var frame = 0; frame < DeathEffectFrameCount; frame++)
            {
                var frameName = effectName + "_frame" + frame;
                if (!spritesByName.TryGetValue(frameName, out var sprite))
                {
                    throw new InvalidOperationException(DeathEffectPath + " is missing " + frameName + ".");
                }

                frames[frame] = sprite;
            }

            return frames;
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
            IReadOnlyDictionary<string, Sprite> sprites,
            IReadOnlyList<Sprite> deathEffectSprites)
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
            AssignDeathEffectSpriteArray(serialized.FindProperty("deathEffectSprites"), deathEffectSprites);
            serialized.FindProperty("movementFrameDuration").floatValue = 0.16f;
            serialized.FindProperty("deathFrameDuration").floatValue = 0.12f;
            serialized.FindProperty("animationPresentation").enumValueIndex =
                (int)AttackerVisualAnimationPresentation.DirectionalSpriteSheets;
            serialized.FindProperty("atlasFacingOffsetDegrees").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(visual);
            return visual;
        }

        /// <summary>
        /// Static atlas art is intentionally repeated through every 16-direction movement/death
        /// slot. The runtime uses the procedural presentation mode to turn, bob, fall, and fade
        /// those compact portraits without multiplying texture memory for all forty species.
        /// </summary>
        private static AttackerVisualDefinition CreateOrUpdateVisualDefinition(
            AttackerSpec spec,
            Sprite staticSprite,
            IReadOnlyList<Sprite> deathEffectSprites)
        {
            var assetPath = VisualDataDirectory + "/" + spec.DisplayName + "VisualDefinition.asset";
            var visual = AssetDatabase.LoadAssetAtPath<AttackerVisualDefinition>(assetPath);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<AttackerVisualDefinition>();
                AssetDatabase.CreateAsset(visual, assetPath);
            }

            var serialized = new SerializedObject(visual);
            AssignStaticSpriteArray(serialized.FindProperty("movementSprites"), staticSprite, MovementFrameCount);
            AssignStaticSpriteArray(serialized.FindProperty("deathSprites"), staticSprite, DeathFrameCount);
            AssignDeathEffectSpriteArray(serialized.FindProperty("deathEffectSprites"), deathEffectSprites);
            serialized.FindProperty("movementFrameDuration").floatValue = 0.16f;
            serialized.FindProperty("deathFrameDuration").floatValue = 0.12f;
            serialized.FindProperty("animationPresentation").enumValueIndex =
                (int)AttackerVisualAnimationPresentation.ProceduralAtlas;
            serialized.FindProperty("atlasFacingOffsetDegrees").floatValue = 0f;
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

        private static void AssignStaticSpriteArray(SerializedProperty property, Sprite sprite, int frameCount)
        {
            if (sprite == null)
            {
                throw new ArgumentNullException(nameof(sprite));
            }

            property.arraySize = DirectionCount * frameCount;
            for (var index = 0; index < property.arraySize; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = sprite;
            }
        }

        private static void AssignDeathEffectSpriteArray(
            SerializedProperty property,
            IReadOnlyList<Sprite> sprites)
        {
            property.arraySize = DeathEffectFrameCount;
            for (var frame = 0; frame < DeathEffectFrameCount; frame++)
            {
                property.GetArrayElementAtIndex(frame).objectReferenceValue =
                    sprites != null && frame < sprites.Count ? sprites[frame] : null;
            }
        }

        private static AttackerDefinition CreateOrUpdateAttackerDefinition(
            AttackerSpec spec,
            AttackerVisualDefinition visuals)
        {
            var assetPath = AttackerDataDirectory + "/" + spec.DisplayName + "Definition.asset";
            var definition = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(assetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<AttackerDefinition>();
                AssetDatabase.CreateAsset(definition, assetPath);
            }

            var serialized = new SerializedObject(definition);
            ApplyDefinitionValues(serialized, spec, visuals, entrySound: null, deathSound: null);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void ApplyDefinitionValues(
            SerializedObject serialized,
            AttackerSpec spec,
            AttackerVisualDefinition visuals,
            AudioClip entrySound,
            AudioClip deathSound)
        {
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
            if (entrySound != null)
            {
                serialized.FindProperty("entrySound").objectReferenceValue = entrySound;
            }

            if (deathSound != null)
            {
                serialized.FindProperty("deathSound").objectReferenceValue = deathSound;
            }
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

                var attackerSerialized = new SerializedObject(attacker);
                attackerSerialized.FindProperty("definition").objectReferenceValue = definition;
                attackerSerialized.ApplyModifiedPropertiesWithoutUndo();

                var controller = visualTransform.GetComponent<AttackerVisualController>();
                if (controller == null)
                {
                    controller = visualTransform.gameObject.AddComponent<AttackerVisualController>();
                }

                var deathPuffTransform = visualTransform.Find("DeathPuff");
                if (deathPuffTransform == null)
                {
                    var deathPuffObject = new GameObject("DeathPuff", typeof(SpriteRenderer));
                    deathPuffTransform = deathPuffObject.transform;
                    deathPuffTransform.SetParent(visualTransform, worldPositionStays: false);
                    deathPuffTransform.localPosition = Vector3.zero;
                    deathPuffTransform.localRotation = Quaternion.identity;
                    deathPuffTransform.localScale = Vector3.one * 0.78f;
                }

                var deathPuffRenderer = deathPuffTransform.GetComponent<SpriteRenderer>();
                deathPuffRenderer.sortingLayerID = renderer.sortingLayerID;
                deathPuffRenderer.sortingOrder = renderer.sortingOrder + 1;
                deathPuffRenderer.enabled = false;

                var controllerSerialized = new SerializedObject(controller);
                controllerSerialized.FindProperty("spriteRenderer").objectReferenceValue = renderer;
                controllerSerialized.FindProperty("deathEffectRenderer").objectReferenceValue = deathPuffRenderer;
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
            foreach (var prefabPath in SpawnPoolPrefabPaths)
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
                    var prefabs = serialized.FindProperty("additionalAttackerPrefabs");
                    prefabs.arraySize = Specs.Length - 1;
                    for (var index = 1; index < Specs.Length; index++)
                    {
                        prefabs.GetArrayElementAtIndex(index - 1).objectReferenceValue =
                            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDirectory + "/" + Specs[index].DisplayName + ".prefab");
                    }

                    serialized.FindProperty("additionalAttackerInitialCapacity").intValue = 5;
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

        private static void ConfigureTestFixtureDefinitions(IReadOnlyDictionary<string, AttackerDefinition> productionDefinitions)
        {
            // PlayMode fixtures intentionally exercise one member of each archetype. Keep their
            // authored data in sync with the production roster without making the tests depend on
            // production Resources paths.
            var fixtureKeys = new[] { "chicken", "rabbit", "bull", "pigeon" };
            MigrateTestFixtureAsset("Eagle", "Pigeon");
            MigrateTestFixtureAsset("Greyhound", "Rabbit");

            foreach (var key in fixtureKeys)
            {
                var spec = Specs.Single(candidate => candidate.Key == key);
                var testPath = TestDataDirectory + "/" + spec.DisplayName + "Definition.asset";
                var fixture = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(testPath);
                if (fixture == null)
                {
                    fixture = ScriptableObject.CreateInstance<AttackerDefinition>();
                    AssetDatabase.CreateAsset(fixture, testPath);
                }

                if (!productionDefinitions.TryGetValue(spec.Key, out var productionDefinition) ||
                    productionDefinition == null)
                {
                    throw new InvalidOperationException("Missing production definition for test fixture " + spec.DisplayName);
                }

                fixture.name = spec.DisplayName + "Definition";
                var serialized = new SerializedObject(fixture);
                ApplyDefinitionValues(
                    serialized,
                    spec,
                    productionDefinition.VisualDefinition,
                    productionDefinition.EntrySound,
                    productionDefinition.DeathSound);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(fixture);
            }
        }

        private static void MigrateTestFixtureAsset(string oldDisplayName, string newDisplayName)
        {
            var oldPath = TestDataDirectory + "/" + oldDisplayName + "Definition.asset";
            var newPath = TestDataDirectory + "/" + newDisplayName + "Definition.asset";
            var oldAsset = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(oldPath);
            var newAsset = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(newPath);
            if (oldAsset != null && newAsset == null)
            {
                var error = AssetDatabase.MoveAsset(oldPath, newPath);
                if (!string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException("Could not migrate test fixture " + oldDisplayName + ": " + error);
                }

                return;
            }

            if (oldAsset != null && newAsset != null)
            {
                AssetDatabase.DeleteAsset(oldPath);
            }
        }

        private static void MigrateLegacyRosterAssets()
        {
            foreach (var migration in LegacyDisplayNameMigrations)
            {
                var oldDisplayName = migration.Key;
                var newDisplayName = migration.Value;

                // Definitions are the critical move because level wave overrides serialize this
                // asset's GUID. The associated assets move too, keeping prefab and visual links
                // stable for any saved scene or pool that still has an older reference.
                MoveAssetPreservingGuid(
                    AttackerDataDirectory + "/" + oldDisplayName + "Definition.asset",
                    AttackerDataDirectory + "/" + newDisplayName + "Definition.asset");
                MoveAssetPreservingGuid(
                    VisualDataDirectory + "/" + oldDisplayName + "VisualDefinition.asset",
                    VisualDataDirectory + "/" + newDisplayName + "VisualDefinition.asset");
                MoveAssetPreservingGuid(
                    PrefabDirectory + "/" + oldDisplayName + ".prefab",
                    PrefabDirectory + "/" + newDisplayName + ".prefab");
                MoveAssetPreservingGuid(
                    "Assets/TAD/Audio/Generated/Attackers/" + oldDisplayName + "_Entry.wav",
                    "Assets/TAD/Audio/Generated/Attackers/" + newDisplayName + "_Entry.wav");
            }
        }

        private static void MoveAssetPreservingGuid(string oldPath, string newPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(oldPath) == null)
            {
                return;
            }

            // A partially completed previous rebuild may have created the target. The legacy
            // asset is the canonical one for these stable IDs because existing level data points
            // to its GUID, so replace the incomplete target before moving it into place.
            if (AssetDatabase.LoadMainAssetAtPath(newPath) != null)
            {
                AssetDatabase.DeleteAsset(newPath);
            }

            var error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(
                    "Could not migrate roster asset from " + oldPath + " to " + newPath + ": " + error);
            }
        }

        private static void RemoveRetiredAttackerAssets()
        {
            // The builder owns the complete production roster folders. Removing data that no
            // longer belongs to a declarative spec prevents retired wild birds or superseded
            // placeholders appearing as a forty-first selection card after a rebuild. Source
            // directional sheets are deliberately retained as visual fallback seeds.
            var liveDisplayNames = new HashSet<string>(Specs.Select(spec => spec.DisplayName));
            foreach (var definitionPath in AssetDatabase.FindAssets(
                         "t:AttackerDefinition",
                         new[] { AttackerDataDirectory })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                var definition = AssetDatabase.LoadAssetAtPath<AttackerDefinition>(definitionPath);
                if (definition == null || liveDisplayNames.Contains(definition.DisplayName))
                {
                    continue;
                }

                var retiredName = definition.DisplayName;
                AssetDatabase.DeleteAsset(definitionPath);
                AssetDatabase.DeleteAsset(VisualDataDirectory + "/" + retiredName + "VisualDefinition.asset");
                AssetDatabase.DeleteAsset(PrefabDirectory + "/" + retiredName + ".prefab");
                AssetDatabase.DeleteAsset("Assets/TAD/Audio/Generated/Attackers/" + retiredName + "_Entry.wav");
                AssetDatabase.DeleteAsset("Assets/TAD/Audio/Generated/Attackers/" + retiredName + "_Death.wav");
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
                string artSeedKey)
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
                ArtSeedKey = artSeedKey;
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
            public string ArtSeedKey { get; }
        }
    }
}
#endif
