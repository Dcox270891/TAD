using TAD.Progression;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Presentation-only farmhand audio. It observes successful gameplay events so pool activation,
    /// failed shots, and rejected moves never produce a voice line or weapon sound.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmHand))]
    public sealed class FarmHandAudioController : MonoBehaviour
    {
        [Header("Farmer voice")]
        [SerializeField] private AudioClip placementGreeting;
        [SerializeField] private AudioClip relocationLine;
        [SerializeField] private AudioClip levelUpLine;

        [Header("Farmhand feedback")]
        [Tooltip("A quiet boot-on-soil cue paced while this farmhand is walking to a relocation destination.")]
        [SerializeField] private AudioClip relocationFootstep;
        [Tooltip("A short cue played once when this farmhand is ready for a weapon choice.")]
        [SerializeField] private AudioClip upgradeReadyCue;
        [Tooltip("A short confirmation cue after a waiting weapon choice has been made.")]
        [SerializeField] private AudioClip upgradeSelectedCue;
        [SerializeField, Min(0.1f)] private float footstepInterval = 0.36f;

        private FarmHand farmHand;
        private bool wasAwaitingWeaponUpgrade;
        private bool isSubscribed;
        private float nextFootstepTime;

        private void Awake()
        {
            farmHand = GetComponent<FarmHand>();
        }

        private void OnEnable()
        {
            farmHand ??= GetComponent<FarmHand>();
            if (farmHand == null || isSubscribed)
            {
                return;
            }

            farmHand.Fired += HandleFired;
            farmHand.LeveledUp += HandleLeveledUp;
            farmHand.RelocationStarted += HandleRelocationStarted;
            farmHand.ProgressionChanged += HandleProgressionChanged;
            isSubscribed = true;
            wasAwaitingWeaponUpgrade = farmHand.IsAwaitingWeaponUpgrade;
            nextFootstepTime = Time.time;
        }

        private void OnDisable()
        {
            if (farmHand == null || !isSubscribed)
            {
                return;
            }

            farmHand.Fired -= HandleFired;
            farmHand.LeveledUp -= HandleLeveledUp;
            farmHand.RelocationStarted -= HandleRelocationStarted;
            farmHand.ProgressionChanged -= HandleProgressionChanged;
            isSubscribed = false;
        }

        private void Update()
        {
            if (farmHand == null || !farmHand.IsRelocating)
            {
                nextFootstepTime = Time.time;
                return;
            }

            if (Time.time < nextFootstepTime)
            {
                return;
            }

            var alternatingPitch = (farmHand.InstanceId & 1L) == 0L ? 0.96f : 1.04f;
            TadSfxPlayer.Play(
                relocationFootstep,
                TadAudioChannelType.Defender,
                transform.position,
                volume: 0.42f,
                pitch: alternatingPitch);
            nextFootstepTime = Time.time + Mathf.Max(0.1f, footstepInterval);
        }

        /// <summary>Called after the bank has paid for a successfully spawned farmhand.</summary>
        public void PlayPlacementGreeting()
        {
            PlayDefenderVoice(placementGreeting);
        }

        private void HandleFired(Vector2 _, WeaponDefinition weapon)
        {
            var firingSound = weapon != null && weapon.ProjectileDefinition != null
                ? weapon.ProjectileDefinition.FiringSound
                : null;
            TadSfxPlayer.Play(firingSound, TadAudioChannelType.Projectile, transform.position);
        }

        private void HandleLeveledUp()
        {
            PlayDefenderVoice(levelUpLine);
        }

        private void HandleRelocationStarted()
        {
            PlayDefenderVoice(relocationLine);
            // A first footstep immediately confirms the accepted move, then Update paces the rest.
            nextFootstepTime = Time.time;
        }

        private void HandleProgressionChanged()
        {
            if (farmHand == null)
            {
                return;
            }

            var isAwaitingWeaponUpgrade = farmHand.IsAwaitingWeaponUpgrade;
            if (!wasAwaitingWeaponUpgrade && isAwaitingWeaponUpgrade)
            {
                TadSfxPlayer.Play(upgradeReadyCue, TadAudioChannelType.Defender, transform.position, volume: 0.7f);
            }
            else if (wasAwaitingWeaponUpgrade && !isAwaitingWeaponUpgrade)
            {
                TadSfxPlayer.Play(upgradeSelectedCue, TadAudioChannelType.Defender, transform.position, volume: 0.78f);
            }

            wasAwaitingWeaponUpgrade = isAwaitingWeaponUpgrade;
        }

        private void PlayDefenderVoice(AudioClip clip)
        {
            TadSfxPlayer.Play(clip, TadAudioChannelType.Defender, transform.position);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            footstepInterval = Mathf.Max(0.1f, footstepInterval);
        }
#endif
    }
}
