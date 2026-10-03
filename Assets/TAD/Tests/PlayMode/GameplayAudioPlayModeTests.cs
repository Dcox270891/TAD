using System.Collections;
using NUnit.Framework;
using TAD.Progression;
using UnityEngine;
using UnityEngine.TestTools;

namespace TAD.Tests.PlayMode
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class GameplayAudioPlayModeTests
    {
        [UnityTest]
        public IEnumerator SfxPlayer_CreatesReusableChannelAwareSourcesForGameplayCues()
        {
            var clip = AudioClip.Create("TAD Test Cue", 2205, 1, 22050, stream: false);
            GameObject playerObject = null;
            try
            {
                TadSfxPlayer.Play(clip, TadAudioChannelType.Projectile, Vector3.zero);
                yield return null;

                var player = Object.FindAnyObjectByType<TadSfxPlayer>();
                Assert.That(player, Is.Not.Null);
                Assert.That(player.GetComponentsInChildren<AudioSource>(true).Length, Is.GreaterThanOrEqualTo(20));
                Assert.That(player.GetComponentsInChildren<TadAudioChannel>(true).Length, Is.GreaterThanOrEqualTo(20));
                playerObject = player.gameObject;
            }
            finally
            {
                Object.Destroy(clip);
                if (playerObject != null)
                {
                    Object.Destroy(playerObject);
                }
            }

            yield return null;
        }

        [Test]
        public void FoodStoreHapticFeedback_IsSafeWhenNoSupportedDeviceIsPresent()
        {
            Assert.DoesNotThrow(TadHapticFeedback.PulseFoodStoreReached);
        }
    }
}
