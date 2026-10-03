using System.Collections.Generic;
using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Plays short gameplay cues through a small reusable AudioSource pool. Category volumes are
    /// delegated to <see cref="TadAudioChannel"/>, keeping attacker, defender, and projectile
    /// effects connected to the player's saved settings without adding AudioSources to every projectile.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class TadSfxPlayer : MonoBehaviour
    {
        private const string PlayerObjectName = "TAD SFX Player";
        private const int InitialSourceCount = 20;
        private const int MaximumSourceCount = 40;

        private sealed class SourceSlot
        {
            public AudioSource Source;
            public TadAudioChannel Channel;
            public float ReleaseAt;
        }

        private static TadSfxPlayer instance;

        private readonly List<SourceSlot> sourceSlots = new();

        /// <summary>Plays a non-looping cue, returning immediately when no clip is configured.</summary>
        public static void Play(
            AudioClip clip,
            TadAudioChannelType channel,
            Vector3 worldPosition,
            float volume = 1f,
            float pitch = 1f)
        {
            if (clip == null)
            {
                return;
            }

            EnsureInstance().PlayInternal(clip, channel, worldPosition, volume, pitch);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureSourceCount(InitialSourceCount);
        }

        private void Update()
        {
            var now = Time.unscaledTime;
            foreach (var slot in sourceSlots)
            {
                if (slot.Source != null && slot.ReleaseAt > 0f &&
                    !slot.Source.isPlaying && now >= slot.ReleaseAt)
                {
                    slot.ReleaseAt = 0f;
                }
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void PlayInternal(
            AudioClip clip,
            TadAudioChannelType channel,
            Vector3 worldPosition,
            float volume,
            float pitch)
        {
            var slot = GetAvailableSlot();
            if (slot == null || slot.Source == null)
            {
                return;
            }

            var safePitch = Mathf.Clamp(pitch, 0.1f, 3f);
            slot.Source.Stop();
            slot.Source.transform.position = worldPosition;
            slot.Source.clip = clip;
            slot.Source.pitch = safePitch;
            slot.Channel.Configure(channel, Mathf.Clamp01(volume));
            slot.Source.Play();
            slot.ReleaseAt = Time.unscaledTime + Mathf.Max(0.05f, clip.length / safePitch);
        }

        private SourceSlot GetAvailableSlot()
        {
            var now = Time.unscaledTime;
            SourceSlot earliestSlot = null;
            foreach (var slot in sourceSlots)
            {
                if (slot.Source == null || !slot.Source.isPlaying || slot.ReleaseAt <= now)
                {
                    return slot;
                }

                if (earliestSlot == null || slot.ReleaseAt < earliestSlot.ReleaseAt)
                {
                    earliestSlot = slot;
                }
            }

            if (sourceSlots.Count < MaximumSourceCount)
            {
                return CreateSourceSlot(sourceSlots.Count);
            }

            // In an exceptional burst, reuse the cue nearest to completion rather than allocating forever.
            return earliestSlot;
        }

        private void EnsureSourceCount(int requiredCount)
        {
            while (sourceSlots.Count < requiredCount)
            {
                CreateSourceSlot(sourceSlots.Count);
            }
        }

        private SourceSlot CreateSourceSlot(int index)
        {
            var sourceObject = new GameObject($"SFX Source {index + 1}");
            sourceObject.transform.SetParent(transform, worldPositionStays: false);

            var audioSource = sourceObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;

            var audioChannel = sourceObject.AddComponent<TadAudioChannel>();
            var slot = new SourceSlot
            {
                Source = audioSource,
                Channel = audioChannel
            };
            sourceSlots.Add(slot);
            return slot;
        }

        private static TadSfxPlayer EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            // Make channel volume application deterministic even if a cue is the first game object
            // created by a scene transition.
            _ = PlayerSettingsService.Instance;
            instance = FindAnyObjectByType<TadSfxPlayer>(FindObjectsInactive.Include);
            if (instance != null)
            {
                return instance;
            }

            var playerObject = new GameObject(PlayerObjectName);
            instance = playerObject.AddComponent<TadSfxPlayer>();
            return instance;
        }
    }
}
