using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TAD.Progression;
using TAD.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    [TestFixture]
    [Category("GameBasics")]
    public sealed class PlayerSettingsPlayModeTests
    {
        private const string PlayerProfileKey = "TAD.PlayerProfile";

        [UnityTest]
        public IEnumerator PlayerSettingsOverlay_ProvidesRequestedPagesAndAppliesSavedAudioChannels()
        {
            var backup = CaptureProfileBackup();
            var settingsService = PlayerSettingsService.Instance;
            GameObject canvasObject = null;
            GameObject audioObject = null;

            try
            {
                canvasObject = CreateCanvasWithButtonTemplate(out var buttonTemplate);
                var canvas = canvasObject.GetComponent<Canvas>();
                var overlay = PlayerSettingsOverlay.Show(canvas.transform, buttonTemplate, buttonTemplate.gameObject);
                yield return null;

                var content = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content, Is.Not.Null);
                Assert.That(content.Find("Show attacker health barsRow/Toggle"), Is.Not.Null);
                Assert.That(content.Find("Show defender experienceRow/Toggle"), Is.Not.Null);

                var healthToggle = content.Find("Show attacker health barsRow/Toggle").GetComponent<Toggle>();
                var requestedHealthBarState = !settingsService.Profile.showAttackerHealthBars;
                healthToggle.isOn = requestedHealthBarState;
                yield return null;

                Assert.That(settingsService.Profile.showAttackerHealthBars, Is.EqualTo(requestedHealthBarState));
                Assert.That(PlayerProfileStore.Load().showAttackerHealthBars, Is.EqualTo(requestedHealthBarState));

                overlay.transform.Find("SettingsPanel/Tabs/AudioTab").GetComponent<Button>().onClick.Invoke();
                yield return null;
                content = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content.GetComponentsInChildren<Slider>().Length, Is.EqualTo(5));
                var contentPanel = overlay.transform.Find("SettingsPanel/ContentPanel");
                var scrollRect = contentPanel.GetComponent<ScrollRect>();
                Assert.That(scrollRect, Is.Not.Null);
                Assert.That(scrollRect.viewport, Is.Not.Null);
                Assert.That(scrollRect.content, Is.EqualTo(content.GetComponent<RectTransform>()));
                Assert.That(contentPanel.Find("Viewport").GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(contentPanel.Find("Viewport").GetComponent<Mask>(), Is.Null);
                Assert.That(content.GetComponent<ContentSizeFitter>(), Is.Not.Null);
                Assert.That(scrollRect.viewport.rect.height, Is.GreaterThan(0f));
                Assert.That(content.GetComponent<RectTransform>().rect.height, Is.GreaterThan(scrollRect.viewport.rect.height));
                Assert.That(content.Find("Master volumeRow/Label").GetComponent<RectTransform>().rect.width, Is.GreaterThan(200f));
                Assert.That(content.Find("Master volumeRow/Slider").GetComponent<RectTransform>().rect.width, Is.GreaterThan(300f));

                var headerTitle = overlay.transform.Find("SettingsPanel/Header/TitleText").GetComponent<RectTransform>();
                Assert.That(headerTitle.rect.width, Is.GreaterThan(300f));

                var panelRect = overlay.transform.Find("SettingsPanel").GetComponent<RectTransform>();
                var overlayRect = overlay.GetComponent<RectTransform>();
                Assert.That(panelRect.rect.width, Is.LessThanOrEqualTo(overlayRect.rect.width - 72f));
                Assert.That(panelRect.rect.height, Is.LessThanOrEqualTo(overlayRect.rect.height - 72f));

                audioObject = new GameObject("TAD Test Attacker Audio", typeof(AudioSource));
                var audioSource = audioObject.GetComponent<AudioSource>();
                audioSource.volume = 1f;
                var audioChannel = audioObject.AddComponent<TadAudioChannel>();
                audioChannel.Configure(TadAudioChannelType.Attacker, 1f);
                settingsService.SetAttackerVolume(0.42f);
                yield return null;
                Assert.That(audioSource.volume, Is.EqualTo(0.42f).Within(0.001f));

                overlay.transform.Find("SettingsPanel/Tabs/NotificationsTab").GetComponent<Button>().onClick.Invoke();
                yield return null;
                content = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content.Find("Enable in-game notificationsRow/Toggle"), Is.Not.Null);

                overlay.transform.Find("SettingsPanel/Tabs/VideoTab").GetComponent<Button>().onClick.Invoke();
                yield return null;
                content = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content.Find("Video qualityRow"), Is.Not.Null);

                overlay.transform.Find("SettingsPanel/Tabs/ControlsTab").GetComponent<Button>().onClick.Invoke();
                yield return null;
                content = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content.Find("ControlGuide"), Is.Not.Null);
                Assert.That(content.Find("Show control hintsRow/Toggle"), Is.Not.Null);

                var closeButton = overlay.transform.Find("SettingsPanel/Header/CloseButton").GetComponent<Button>();
                Assert.That(closeButton, Is.Not.Null);
                closeButton.onClick.Invoke();
                yield return null;
                Assert.That(overlay.gameObject.activeSelf, Is.False);
            }
            finally
            {
                RestoreProfileBackup(backup);
                RestoreServiceSettings(settingsService, PlayerProfileStore.Load());
                RestoreProfileBackup(backup);

                if (audioObject != null)
                {
                    Object.Destroy(audioObject);
                }

                if (canvasObject != null)
                {
                    Object.Destroy(canvasObject);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerSettingsOverlay_ReopensWithVisibleSettingsAfterRuntimeCacheIsLost()
        {
            GameObject canvasObject = null;

            try
            {
                canvasObject = CreateCanvasWithButtonTemplate(out var buttonTemplate);
                var canvas = canvasObject.GetComponent<Canvas>();
                var overlay = PlayerSettingsOverlay.Show(canvas.transform, buttonTemplate, buttonTemplate.gameObject);
                yield return null;

                var audioTab = overlay.transform.Find("SettingsPanel/Tabs/AudioTab").GetComponent<Button>();
                var closeButton = overlay.transform.Find("SettingsPanel/Header/CloseButton").GetComponent<Button>();
                var originalPanel = overlay.transform.Find("SettingsPanel");
                var originalViewport = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport");
                audioTab.onClick.RemoveAllListeners();
                closeButton.onClick.RemoveAllListeners();
                overlay.gameObject.SetActive(false);
                ClearRuntimeLayoutCache(overlay);

                var reopenedOverlay = PlayerSettingsOverlay.Show(canvas.transform, buttonTemplate, buttonTemplate.gameObject);
                yield return null;

                Assert.That(reopenedOverlay, Is.SameAs(overlay));
                Assert.That(reopenedOverlay.gameObject.activeInHierarchy, Is.True);
                Assert.That(reopenedOverlay.transform.Find("SettingsPanel"), Is.SameAs(originalPanel));
                Assert.That(reopenedOverlay.GetComponentsInChildren<ScrollRect>(true).Length, Is.EqualTo(1));

                var content = reopenedOverlay.transform.Find("SettingsPanel/ContentPanel/Viewport/Content");
                Assert.That(content, Is.Not.Null);
                var reopenedViewport = reopenedOverlay.transform.Find("SettingsPanel/ContentPanel/Viewport");
                Assert.That(reopenedViewport, Is.SameAs(originalViewport));
                Assert.That(reopenedViewport.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(reopenedViewport.GetComponent<Mask>(), Is.Null);
                Assert.That(content.Find("Show attacker health barsRow/Toggle"), Is.Not.Null);

                reopenedOverlay.transform.Find("SettingsPanel/Tabs/AudioTab").GetComponent<Button>().onClick.Invoke();
                yield return null;
                Assert.That(content.GetComponentsInChildren<Slider>().Length, Is.EqualTo(5));

                reopenedOverlay.transform.Find("SettingsPanel/Header/CloseButton").GetComponent<Button>().onClick.Invoke();
                yield return null;
                Assert.That(reopenedOverlay.gameObject.activeSelf, Is.False);
            }
            finally
            {
                if (canvasObject != null)
                {
                    Object.Destroy(canvasObject);
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerSettingsOverlay_RebuildsAStaleStencilMaskedViewport()
        {
            GameObject canvasObject = null;

            try
            {
                canvasObject = CreateCanvasWithButtonTemplate(out var buttonTemplate);
                var canvas = canvasObject.GetComponent<Canvas>();
                var overlay = PlayerSettingsOverlay.Show(canvas.transform, buttonTemplate, buttonTemplate.gameObject);
                yield return null;

                overlay.gameObject.SetActive(false);
                var staleViewport = overlay.transform.Find("SettingsPanel/ContentPanel/Viewport");
                Object.Destroy(staleViewport.GetComponent<RectMask2D>());
                staleViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                yield return null;

                var repairedOverlay = PlayerSettingsOverlay.Show(canvas.transform, buttonTemplate, buttonTemplate.gameObject);
                yield return null;

                var repairedViewport = repairedOverlay.transform.Find("SettingsPanel/ContentPanel/Viewport");
                Assert.That(repairedViewport, Is.Not.SameAs(staleViewport));
                Assert.That(repairedViewport.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(repairedViewport.GetComponent<Mask>(), Is.Null);
                Assert.That(repairedViewport.Find("Content/Show attacker health barsRow/Toggle"), Is.Not.Null);
            }
            finally
            {
                if (canvasObject != null)
                {
                    Object.Destroy(canvasObject);
                }
            }

            yield return null;
        }

        private static GameObject CreateCanvasWithButtonTemplate(out Button buttonTemplate)
        {
            var canvasObject = new GameObject(
                "TAD Test Settings Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

            var templateObject = new GameObject(
                "ButtonTemplate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            templateObject.transform.SetParent(canvasObject.transform, false);
            var image = templateObject.GetComponent<Image>();
            image.color = new Color(0.18f, 0.42f, 0.25f, 1f);
            buttonTemplate = templateObject.GetComponent<Button>();
            buttonTemplate.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(templateObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return canvasObject;
        }

        private static void ClearRuntimeLayoutCache(PlayerSettingsOverlay overlay)
        {
            const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(PlayerSettingsOverlay).GetField("contentRoot", InstancePrivate)?.SetValue(overlay, null);
            typeof(PlayerSettingsOverlay).GetField("contentScrollRect", InstancePrivate)?.SetValue(overlay, null);
            typeof(PlayerSettingsOverlay).GetField("panelRect", InstancePrivate)?.SetValue(overlay, null);
            typeof(PlayerSettingsOverlay).GetField("firstTabButton", InstancePrivate)?.SetValue(overlay, null);
        }

        private static ProfileBackup CaptureProfileBackup()
        {
            return PlayerPrefs.HasKey(PlayerProfileKey)
                ? new ProfileBackup(true, PlayerPrefs.GetString(PlayerProfileKey))
                : new ProfileBackup(false, string.Empty);
        }

        private static void RestoreProfileBackup(ProfileBackup backup)
        {
            if (backup.Exists)
            {
                PlayerPrefs.SetString(PlayerProfileKey, backup.Value);
            }
            else
            {
                PlayerPrefs.DeleteKey(PlayerProfileKey);
            }

            PlayerPrefs.Save();
        }

        private static void RestoreServiceSettings(PlayerSettingsService settingsService, PlayerProfileData profile)
        {
            settingsService.SetAttackerHealthBarsVisible(profile.showAttackerHealthBars);
            settingsService.SetDefenderExperienceVisible(profile.showDefenderExperience);
            settingsService.SetMasterVolume(profile.masterVolume);
            settingsService.SetAttackerVolume(profile.attackerVolume);
            settingsService.SetDefenderVolume(profile.defenderVolume);
            settingsService.SetProjectileVolume(profile.projectileVolume);
            settingsService.SetAmbientGameVolume(profile.ambientGameVolume);
            settingsService.SetNotificationsEnabled(profile.notificationsEnabled);
            settingsService.SetControlHintsEnabled(profile.controlHintsEnabled);
            settingsService.SetVideoQuality(profile.videoQuality);
        }

        private readonly struct ProfileBackup
        {
            public ProfileBackup(bool exists, string value)
            {
                Exists = exists;
                Value = value;
            }

            public bool Exists { get; }
            public string Value { get; }
        }
    }
}
