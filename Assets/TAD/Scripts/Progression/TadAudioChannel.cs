using UnityEngine;

namespace TAD.Progression
{
    /// <summary>
    /// Assign this beside an AudioSource to opt it into a player-controlled TAD audio category.
    /// The source retains its authored loudness while the category volume is applied at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class TadAudioChannel : MonoBehaviour
    {
        [SerializeField] private TadAudioChannelType channel = TadAudioChannelType.Ambient;
        [SerializeField, Range(0f, 1f)] private float authoredVolume = 1f;

        private AudioSource audioSource;
        private PlayerSettingsService settingsService;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            settingsService = PlayerSettingsService.Instance;
            settingsService.SettingsChanged += HandleSettingsChanged;
            ApplyVolume();
        }

        private void OnDisable()
        {
            if (settingsService != null)
            {
                settingsService.SettingsChanged -= HandleSettingsChanged;
                settingsService = null;
            }
        }

        private void OnValidate()
        {
            authoredVolume = Mathf.Clamp01(authoredVolume);
        }

        /// <summary>Sets this source's category and its authored baseline volume at runtime.</summary>
        public void Configure(TadAudioChannelType audioChannel, float baseVolume)
        {
            channel = audioChannel;
            authoredVolume = Mathf.Clamp01(baseVolume);
            ApplyVolume();
        }

        private void HandleSettingsChanged(PlayerProfileData profile)
        {
            ApplyVolume();
        }

        private void ApplyVolume()
        {
            if (audioSource == null || settingsService == null)
            {
                return;
            }

            audioSource.volume = authoredVolume * settingsService.GetChannelVolume(channel);
        }
    }

    public enum TadAudioChannelType
    {
        Attacker,
        Defender,
        Projectile,
        Ambient
    }
}
