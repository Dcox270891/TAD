using System.Collections;
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
    public sealed class PlayerMatchExperiencePresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator MatchExperiencePresentation_SkipPausesForLevelUpThenSettlesAtSavedProgress()
        {
            GameObject canvasObject = null;

            try
            {
                var profile = new PlayerProfileData
                {
                    playerLevel = 1,
                    playerExp = 60f,
                    totalPlayerExperience = 60f
                };
                var reward = PlayerLevelProgression.AwardMatchExperience(profile, 100f, playerWon: true);
                canvasObject = CreateCanvas();
                var presentation = canvasObject.AddComponent<PlayerMatchExperiencePresentation>();

                Assert.That(presentation.Present(reward, null), Is.True);
                yield return null;

                var resultsRoot = canvasObject.transform.Find("PlayerExperienceResults");
                Assert.That(resultsRoot, Is.Not.Null);
                var summaryPanel = canvasObject.transform.Find("AttackerWinPanel/PlayerExperienceSummaryPanel");
                Assert.That(summaryPanel, Is.Not.Null);
                Assert.That(resultsRoot.Find("PlayerExperienceSummaryPanel"), Is.Null,
                    "Match information belongs inside the win/loss card; only the XP bar remains below it.");
                var winPanelRect = canvasObject.transform.Find("AttackerWinPanel").GetComponent<RectTransform>();
                Assert.That(winPanelRect.rect.height, Is.GreaterThanOrEqualTo(570f));
                Assert.That(winPanelRect.anchoredPosition.y, Is.GreaterThan(0f),
                    "The expanded win/loss card should sit above the persistent XP bar rather than behind it.");
                Assert.That(summaryPanel.Find("StartingExperienceText").GetComponent<Text>().text, Does.Contain("LV 1"));
                Assert.That(summaryPanel.Find("EarnedText").GetComponent<Text>().text, Does.Contain("+100"));
                Assert.That(resultsRoot.Find("PlayerLevelStarBadge/StarLevelText").GetComponent<Text>().text,
                    Is.EqualTo("LV\n1"));

                presentation.SkipAnimation();
                yield return null;

                Assert.That(presentation.IsWaitingForLevelUp, Is.True);
                var levelUpModal = canvasObject.transform.Find("PlayerLevelUpModal");
                Assert.That(levelUpModal, Is.Not.Null);
                Assert.That(levelUpModal.gameObject.activeSelf, Is.True);
                Assert.That(levelUpModal.Find("PlayerLevelUpCard/TitleText").GetComponent<Text>().text,
                    Is.EqualTo("LEVEL UP!"));

                levelUpModal.Find("PlayerLevelUpCard/ContinueLevelUpButton")
                    .GetComponent<Button>()
                    .onClick
                    .Invoke();
                yield return null;

                Assert.That(presentation.IsAnimating, Is.False);
                Assert.That(presentation.IsWaitingForLevelUp, Is.False);
                Assert.That(presentation.DisplayedLevel, Is.EqualTo(profile.playerLevel));
                Assert.That(presentation.DisplayedExperience, Is.EqualTo(profile.playerExp));
                Assert.That(resultsRoot.Find("PlayerExperienceBarBackground/PlayerExperienceProgressText").GetComponent<Text>().text,
                    Does.Contain("85 / 100"));
                Assert.That(summaryPanel.Find("CurrentExperienceText").GetComponent<Text>().text,
                    Does.Contain("CURRENT: LV 2  85 / 100"));
                Assert.That(resultsRoot.Find("PlayerLevelStarBadge/StarLevelText").GetComponent<Text>().text,
                    Is.EqualTo("LV\n2"));
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

        private static GameObject CreateCanvas()
        {
            var canvasObject = new GameObject(
                "TAD Test Player XP Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panelObject = new GameObject(
                "AttackerWinPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            panelObject.transform.SetParent(canvasObject.transform, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 400f);
            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(46, 46, 40, 40);
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = new GameObject("TitleText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(LayoutElement));
            title.transform.SetParent(panelObject.transform, false);
            title.GetComponent<LayoutElement>().preferredHeight = 90f;
            return canvasObject;
        }
    }
}
