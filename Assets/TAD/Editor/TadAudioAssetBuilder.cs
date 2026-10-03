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
                var path = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Entry.wav";
                WriteWav(path, BuildAnimalEntry(attacker.DisplayName, index));
            }

            for (var index = 0; index < projectiles.Length; index++)
            {
                var projectile = projectiles[index];
                var path = $"{ProjectileAudioRoot}/{Sanitize(projectile.name)}_Fire.wav";
                WriteWav(path, BuildProjectileFire(projectile, index));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureGeneratedImporters(AttackerAudioRoot);
            ConfigureGeneratedImporters(ProjectileAudioRoot);
            ConfigureGeneratedImporters(VoiceAudioRoot);

            AssignAttackerCues(attackers);
            AssignProjectileCues(projectiles);
            AssignFarmHandVoiceCues();
            AssetDatabase.SaveAssets();
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
                var path = $"{AttackerAudioRoot}/{Sanitize(attacker.DisplayName)}_Entry.wav";
                AssignObjectReference(attacker, "entrySound", AssetDatabase.LoadAssetAtPath<AudioClip>(path));
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

        private static void AssignFarmHandVoiceCues()
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
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var time = sampleIndex / (float)SampleRate;
                var envelope = Envelope(time, duration, 0.018f, 0.07f);
                var noise = DeterministicNoise(sampleIndex, index) * 0.12f;
                var value = 0f;

                if (name is "chicken" or "duck")
                {
                    var pulse = Mathf.Clamp01(Mathf.Sin(time * Mathf.PI * 9f));
                    var frequency = name == "chicken" ? 730f - (time * 260f) : 310f - (time * 65f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * pulse + noise;
                }
                else if (name is "crow" or "hawk" or "eagle")
                {
                    var frequency = 430f + (index * 25f) + Mathf.Sin(time * 40f) * 120f;
                    value = (Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.55f) + noise * 1.6f;
                }
                else if (name is "hare" or "fox" or "greyhound")
                {
                    var frequency = 570f + (index * 38f) - (time * 180f);
                    value = Mathf.Sin(time * Mathf.PI * 2f * frequency) * 0.6f +
                            Mathf.Sin(time * Mathf.PI * 2f * frequency * 2f) * 0.18f + noise;
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

        private static float[] BuildProjectileFire(ProjectileDefinition definition, int index)
        {
            var duration = definition.ProjectileBehavior switch
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

                samples[sampleIndex] = value * envelope * 0.5f;
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
