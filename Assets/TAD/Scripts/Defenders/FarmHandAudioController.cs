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

        private FarmHand farmHand;

        private void Awake()
        {
            farmHand = GetComponent<FarmHand>();
        }

        private void OnEnable()
        {
            farmHand ??= GetComponent<FarmHand>();
            if (farmHand == null)
            {
                return;
            }

            farmHand.Fired += HandleFired;
            farmHand.LeveledUp += HandleLeveledUp;
            farmHand.RelocationStarted += HandleRelocationStarted;
        }

        private void OnDisable()
        {
            if (farmHand == null)
            {
                return;
            }

            farmHand.Fired -= HandleFired;
            farmHand.LeveledUp -= HandleLeveledUp;
            farmHand.RelocationStarted -= HandleRelocationStarted;
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
        }

        private void PlayDefenderVoice(AudioClip clip)
        {
            TadSfxPlayer.Play(clip, TadAudioChannelType.Defender, transform.position);
        }
    }
}
