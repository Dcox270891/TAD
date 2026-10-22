using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TAD.Attackers;
using TAD.Defenders;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Creates the project's lightweight original placeholder SFX and wires them into the reusable
    /// attacker, projectile, and farmhand data. Voice lines are generated separately from the
    /// local British-English system voice, then assigned here with the rest of the cue library.
    /// </summary>
    public static class TadAudioAssetBuilder
    {
        private const string AudioRoot = "Assets/TAD/Audio/Generated";
        private const string AttackerAudioRoot = AudioRoot + "/Attackers";
        private const string ProjectileAudioRoot = AudioRoot + "/Projectiles";
        private const string VoiceAudioRoot = AudioRoot + "/FarmhandVoice";
        private const string FarmHandPrefabPath = "Assets/TAD/Prefabs/Defenders/FarmHand.prefab";
        private const int SampleRate = 44100;

        [MenuItem("TAD/Audio/Generate Original Gameplay Cues")]
        public static void GenerateAndAssign()
        {
            Directory.CreateDirectory(AttackerAudioRoot);
            Directory.CreateDirectory(ProjectileAudioRoot);
            Directory.CreateDirectory(VoiceAudioRoot);

            var attackers = FindAssets<AttackerDefinition>("Assets/TAD/Data/Attackers")
                .OrderBy(definition => definition.AttackerId, StringComparer.Ordinal)
                .ToArray();
            var projectiles = FindAssets<ProjectileDefinition>("Assets/TAD/Data/Defenders")
                .OrderBy(definition => AssetDatabase.GetAssetPath(definition), StringComparer.Ordinal)
                .ToArray();

            for (var index = 0; index < attackers.Length; index++)
            {
                var attacker = attackers[index];
                var cryPath = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Entry.wav";
                var deathPath = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Death.wav";
                WriteWav(cryPath, BuildAnimalEntry(attacker.DisplayName, index));
                WriteWav(deathPath, BuildAnimalDeath(attacker.DisplayName, index));
            }

            GenerateDefenderAudioAssets(projectiles);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureGeneratedImporters(AttackerAudioRoot);
            ConfigureGeneratedImporters(ProjectileAudioRoot);
            ConfigureGeneratedImporters(VoiceAudioRoot);

            AssignAttackerCues(attackers);
            AssignProjectileCues(projectiles);
            AssignFarmHandAudioCues();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Rebuilds only defender and weapon presentation audio. Keeping this separate means art
        /// and defender-foley iteration never rewrites the animal cry library.
        /// </summary>
        [MenuItem("TAD/Audio/Refresh Defender Foley")]
        public static void RefreshDefenderAudio()
        {
            Directory.CreateDirectory(ProjectileAudioRoot);
            Directory.CreateDirectory(VoiceAudioRoot);

            var projectiles = FindAssets<ProjectileDefinition>("Assets/TAD/Data/Defenders")
                .OrderBy(definition => AssetDatabase.GetAssetPath(definition), StringComparer.Ordinal)
                .ToArray();
            GenerateDefenderAudioAssets(projectiles);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureGeneratedImporters(ProjectileAudioRoot);
            ConfigureGeneratedImporters(VoiceAudioRoot);
            AssignProjectileCues(projectiles);
            AssignFarmHandAudioCues();
            AssetDatabase.SaveAssets();
        }

        private static void GenerateDefenderAudioAssets(IReadOnlyList<ProjectileDefinition> projectiles)
        {
            for (var index = 0; index < projectiles.Count; index++)
            {
                var projectile = projectiles[index];
                var path = $"{ProjectileAudioRoot}/{Sanitize(projectile.name)}_Fire.wav";
                WriteWav(path, BuildProjectileFire(projectile, index));
            }

            WriteWav($"{VoiceAudioRoot}/Farmhand_Footstep.wav", BuildFarmHandFootstep());
            WriteWav($"{VoiceAudioRoot}/Farmhand_UpgradeReady.wav", BuildUpgradeReadyCue());
            WriteWav($"{VoiceAudioRoot}/Farmhand_UpgradeSelected.wav", BuildUpgradeSelectedCue());
        }

        private static T[] FindAssets<T>(string root) where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void AssignAttackerCues(IEnumerable<AttackerDefinition> attackers)
        {
            foreach (var attacker in attackers)
            {
                var cryPath = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Entry.wav";
                var deathPath = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Death.wav";
                AssignObjectReference(attacker, "entrySound", AssetDatabase.LoadAssetAtPath<AudioClip>(cryPath));
                AssignObjectReference(attacker, "deathSound", AssetDatabase.LoadAssetAtPath<AudioClip>(deathPath));
            }
        }

        private static void AssignProjectileCues(IEnumerable<ProjectileDefinition> projectiles)
        {
            foreach (var projectile in projectiles)
            {
                var path = $"{ProjectileAudioRoot}/{Sanitize(projectile.name)}_Fire.wav";
                AssignObjectReference(projectile, "firingSound", AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
        }

        private static void AssignFarmHandAudioCues()
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(FarmHandPrefabPath);
            try
            {
                var controller = prefabRoot.GetComponent<FarmHandAudioController>();
                if (controller == null)
                {
                    controller = prefabRoot.AddComponent<FarmHandAudioController>();
                }

                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("placementGreeting").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_Hello.wav");
                serializedController.FindProperty("relocationLine").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_UhOhGottaMove.wav");
                serializedController.FindProperty("levelUpLine").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_Wahey.wav");
                serializedController.FindProperty("relocationFootstep").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_Footstep.wav");
                serializedController.FindProperty("upgradeReadyCue").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_UpgradeReady.wav");
                serializedController.FindProperty("upgradeSelectedCue").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceAudioRoot}/Farmhand_UpgradeSelected.wav");
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, FarmHandPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void AssignObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serializedTarget = new SerializedObject(target);
            var property = serializedTarget.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.name} no longer has a '{propertyName}' audio property.");
            }

            property.objectReferenceValue = value;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureGeneratedImporters(string root)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.forceToMono = true;
                importer.loadInBackground = false;
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.45f;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static void WriteWav(string assetPath, float[] samples)
        {
            var fullPath = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? AudioRoot);

            using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            const short channels = 1;
            const short bitsPerSample = 16;
            var byteRate = SampleRate * channels * bitsPerSample / 8;
            var dataLength = samples.Length * sizeof(short);

            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataLength);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(SampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataLength);

            foreach (var sample in samples)
            {
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }

        private static float[] BuildAnimalEntry(string displayName, int index)
        {
            var name = displayName.ToLowerInvariant();
            var duration = 0.34f + (index % 3 * 0.04f);
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            var isWaterfowl = HasAny(name, "duck", "goose", "mandarin");
            var isPoultry = HasAny(name, "chicken", "turkey", "quail", "pheasant", "partridge", "guinea", "peafowl");
            var isSmallMammal = HasAny(name, "rabbit");
            var isFastUngulate = HasAny(name, "deer", "springbok", "impala", "kudu", "reindeer");
            var isRatite = HasAny(name, "emu", "rhea", "ostrich");
            var isBird = HasAny(name, "pigeon") || isPoultry || isWaterfowl;
            var isBovine = HasAny(name, "bull", "bison", "cow", "buffalo", "yak", "zebu", "musk ox");
            var isCamelid = HasAny(name, "alpaca", "llama", "camel");
            var isSwine = HasAny(name, "boar", "pig");
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var envelope = Envelope(time, duration, 0.018f, 0.07f);
                var noise = DeterministicNoise(sampleIndex, index) * 0.12f;
                var value = 0f;

                if (isWaterfowl)
                {
                    var frequency = 260f + (index * 11f) - (time * 58f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.72f + noise * 1.45f;
                }
                else if (isPoultry)
                {
                    var pulse = Mathf.Clamp01(Mathf.Sin(time * Mathf.PI * (8f + index % 4)));
                    var frequency = 630f + (index * 17f) - (time * 210f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * pulse + noise * 1.15f;
                }
                else if (isBird)
                {
                    var frequency = 430f + (index * 19f) + Mathf.Sin(time * 40f) * 105f;
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.55f + noise * 1.5f;
                }
                else if (isSmallMammal || isFastUngulate)
                {
                    var frequency = 480f + (index * 23f) - (time * 155f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.56f +
                            Mathf.Sin(time * Mathf.PI * 2f * frequency * 2f) * 0.16f + noise;
                }
                else if (isRatite)
                {
                    var frequency = 175f + (index * 9f) + Mathf.Sin(time * 18f) * 23f;
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.64f + noise * 1.25f;
                }
                else if (isCamelid)
                {
                    var frequency = 135f + (index * 7f) + Mathf.Sin(time * 13f) * 12f;
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.71f +
                            Mathf.Sin(time * Mathf.PI * 2f * frequency * 0.5f) * 0.18f + noise;
                }
                else if (isSwine)
                {
                    var frequency = 175f + (index * 8f) - (time * 35f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.62f + noise * 1.7f;
                }
                else if (isBovine)
                {
                    var frequency = 88f + (index * 5f) + Mathf.Sin(time * 12f) * 14f;
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.8f + noise * 1.25f;
                }
                else
                {
                    var frequency = 105f + (index * 11f) + Mathf.Sin(time * 17f) * 16f;
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.74f + noise * 1.45f;
                }

                samples[sampleIndex] = value * envelope * 0.45f;
            }

            return samples;
        }

        private static float[] BuildAnimalDeath(string displayName, int index)
        {
            var name = displayName.ToLowerInvariant();
            var duration = 0.22f + (index % 4 * 0.025f);
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            var isBird = HasAny(name, "chicken", "duck", "goose", "turkey", "quail", "pheasant", "partridge", "guinea", "pigeon", "peafowl", "emu", "rhea", "ostrich");
            var isSmall = HasAny(name, "rabbit", "deer", "springbok", "impala", "kudu");
            var isSwine = HasAny(name, "boar", "pig");
            var isCamelid = HasAny(name, "alpaca", "llama", "camel");
            var baseFrequency = isBird
                ? 500f + (index * 17f)
                : isSmall
                    ? 330f + (index * 13f)
                    : isSwine
                        ? 195f + (index * 9f)
                        : isCamelid
                            ? 145f + (index * 6f)
                            : 112f + (index * 5f);

            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var progress = time / duration;
                var envelope = Envelope(time, duration, 0.005f, duration * 0.72f);
                var frequency = Mathf.Lerp(baseFrequency, baseFrequency * 0.48f, progress);
                var noise = DeterministicNoise(sampleIndex, index + 401) * (isBird ? 0.18f : 0.1f);
                var voice = Mathf.Sin(time * Mathf.PI * 2f * frequency) * (isBird ? 0.45f : 0.66f);
                var overtone = Mathf.Sin(time * Mathf.PI * 2f * frequency * 1.85f) * (isBird ? 0.17f : 0.09f);
                samples[sampleIndex] = (voice + overtone + noise) * envelope * 0.42f;
            }

            return samples;
        }

        private static bool HasAny(string value, params string[] fragments)
        {
            return fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
        }

        private static float[] BuildProjectileFire(ProjectileDefinition definition, int index)
        {
            var key = (AssetDatabase.GetAssetPath(definition) + " " + definition.name).ToLowerInvariant();
            var isUnarmed = HasAny(key, "unarmed");
            var isWoodTool = HasAny(key, "stick", "pole", "rake", "shovel");
            var isThrownStone = HasAny(key, "stone");
            var isRoller = HasAny(key, "rolling", "heavyroll", "bounce");
            var isTrap = HasAny(key, "trap");
            var isAirWeapon = HasAny(key, "airrifle", "semi", "pistol");
            var duration = isUnarmed ? 0.11f
                : isWoodTool ? 0.19f
                : isThrownStone ? 0.16f
                : isRoller ? 0.31f
                : isTrap ? 0.18f
                : isAirWeapon ? 0.13f
                : definition.ProjectileBehavior switch
                {
                    ProjectileBehavior.Trap => 0.16f,
                    ProjectileBehavior.Rolling => 0.28f,
                    ProjectileBehavior.Bouncing => 0.22f,
                    ProjectileBehavior.PoleSweep => 0.2f,
                    _ => 0.12f
                };
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            var baseFrequency = 220f + (index * 37f);
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var envelope = Envelope(time, duration, 0.003f, duration * 0.62f);
                var noise = DeterministicNoise(sampleIndex, index + 83);
                float value;
                if (isUnarmed)
                {
                    // A warm mitt-like thump with a tiny air swish rather than a weapon clang.
                    value = Mathf.Sin(time * Mathf.PI * 2f * (118f - time * 160f)) * 0.72f +
                            noise * 0.55f;
                }
                else if (isWoodTool)
                {
                    // Wooden handle whoosh with a distinct metal head tick for rake and shovel.
                    var swish = Mathf.Sin(time * Mathf.PI * 2f * (240f + time * 940f)) * 0.38f;
                    var wood = noise * 0.44f;
                    var headTick = HasAny(key, "rake", "shovel") && time < 0.035f
                        ? Mathf.Sin(time * Mathf.PI * 2f * 1680f) * 0.48f
                        : 0f;
                    value = swish + wood + headTick;
                }
                else if (isThrownStone)
                {
                    value = Mathf.Sin(time * Mathf.PI * 2f * (330f + time * 1430f)) * 0.32f +
                            noise * (0.66f - time * 1.6f);
                }
                else if (isRoller)
                {
                    var rumble = Mathf.Sin(time * Mathf.PI * 2f * (76f + time * 42f)) * 0.62f;
                    var rattle = noise * 0.42f * Mathf.Clamp01(time / 0.035f);
                    var bounceClick = HasAny(key, "bounce") && time < 0.045f
                        ? Mathf.Sin(time * Mathf.PI * 2f * 1120f) * 0.32f
                        : 0f;
                    value = rumble + rattle + bounceClick;
                }
                else if (isTrap)
                {
                    // A mechanical set-and-latch clack; poison and slow traps still differ by seed.
                    var latch = Mathf.Sign(Mathf.Sin(time * Mathf.PI * 2f * (510f + index * 19f))) * 0.37f;
                    value = latch + noise * 0.38f;
                }
                else if (isAirWeapon)
                {
                    // Short pneumatic crack, pitched a little differently per rifle/pistol variant.
                    var crack = Mathf.Sin(time * Mathf.PI * 2f * (960f + index * 31f + time * 1350f)) * 0.5f;
                    value = crack + noise * 0.34f;
                }
                else
                {
                    switch (definition.ProjectileBehavior)
                    {
                    case ProjectileBehavior.PoleSweep:
                        value = Mathf.Sin(time * Mathf.PI * 2f * (baseFrequency + time * 880f)) * 0.32f + noise * 0.42f;
                        break;
                    case ProjectileBehavior.Rolling:
                        value = Mathf.Sin(time * Mathf.PI * 2f * (baseFrequency * 0.38f)) * 0.42f + noise * 0.5f;
                        break;
                    case ProjectileBehavior.Bouncing:
                        value = Mathf.Sin(time * Mathf.PI * 2f * (baseFrequency * 0.7f + time * 500f)) * 0.45f + noise * 0.32f;
                        break;
                    case ProjectileBehavior.Trap:
                        value = Mathf.Sign(Mathf.Sin(time * Mathf.PI * 2f * baseFrequency * 1.7f)) * 0.35f + noise * 0.18f;
                        break;
                    default:
                        value = Mathf.Sin(time * Mathf.PI * 2f * (baseFrequency + time * 1100f)) * 0.55f + noise * 0.22f;
                        break;
                    }
                }

                samples[sampleIndex] = value * envelope * 0.5f;
            }

            return samples;
        }

        private static float[] BuildFarmHandFootstep()
        {
            const float duration = 0.14f;
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var envelope = Envelope(time, duration, 0.002f, 0.105f);
                var thump = Mathf.Sin(time * Mathf.PI * 2f * (92f - time * 250f)) * 0.58f;
                var dirt = DeterministicNoise(sampleIndex, 991) * 0.48f;
                samples[sampleIndex] = (thump + dirt) * envelope * 0.42f;
            }

            return samples;
        }

        private static float[] BuildUpgradeReadyCue()
        {
            const float duration = 0.42f;
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var firstNote = Mathf.Sin(time * Mathf.PI * 2f * 659.25f) * Envelope(time, 0.21f, 0.01f, 0.13f);
                var secondTime = Mathf.Max(0f, time - 0.12f);
                var secondNote = Mathf.Sin(secondTime * Mathf.PI * 2f * 987.77f) * Envelope(secondTime, 0.30f, 0.008f, 0.20f);
                samples[sampleIndex] = (firstNote * 0.33f) + (secondNote * 0.38f);
            }

            return samples;
        }

        private static float[] BuildUpgradeSelectedCue()
        {
            const float duration = 0.29f;
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var envelope = Envelope(time, duration, 0.006f, 0.17f);
                var tone = Mathf.Sin(time * Mathf.PI * 2f * (523.25f + time * 460f)) * 0.42f;
                var sparkle = Mathf.Sin(time * Mathf.PI * 2f * 1569f) * 0.10f;
                samples[sampleIndex] = (tone + sparkle) * envelope;
            }

            return samples;
        }

        private static float Envelope(float time, float duration, float attack, float release)
        {
            var attackMultiplier = attack <= 0f ? 1f : Mathf.Clamp01(time / attack);
            var releaseStart = Mathf.Max(attack, duration - release);
            var releaseMultiplier = release <= 0f ? 1f : Mathf.Clamp01((duration - time) / Mathf.Max(0.001f, duration - releaseStart));
            return attackMultiplier * releaseMultiplier;
        }

        private static float DeterministicNoise(int sampleIndex, int seed)
        {
            unchecked
            {
                var value = sampleIndex * 1103515245 + seed * 12345;
                value ^= value >> 13;
                value *= 1274126177;
                return ((value & 0x7fffffff) / 1073741824f) - 1f;
            }
        }

        private static string Sanitize(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
        }
    }
}
