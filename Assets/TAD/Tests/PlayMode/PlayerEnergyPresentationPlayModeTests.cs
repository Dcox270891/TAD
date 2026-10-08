using System.Collections;
using NUnit.Framework;
using TAD.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    public sealed class PlayerEnergyPresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayerEnergyMenuPanel_DisplaysEnergyAndOpensItsInformationOverlay()
        {
            GameObject canvasObject = null;
            try
            {
                canvasObject = CreateCanvasWithButtonTemplate(out var buttonTemplate);
                var panel = PlayerEnergyMenuPanel.Ensure(canvasObject.transform, buttonTemplate);
                yield return null;

                var energyText = panel.transform.Find("EnergyText")?.GetComponent<Text>();
                var timerText = panel.transform.Find("NextEnergyText")?.GetComponent<Text>();
                var informationButton = panel.transform.Find("EnergyInfoButton")?.GetComponent<Button>();
                Assert.That(energyText, Is.Not.Null);
                Assert.That(energyText.text, Does.StartWith("ENERGY"));
                Assert.That(timerText, Is.Not.Null);
                Assert.That(timerText.text, Is.Not.Empty);
                Assert.That(informationButton, Is.Not.Null);

                informationButton.onClick.Invoke();
                yield return null;

                var overlayTransform = canvasObject.transform.Find("PlayerEnergyOverlay");
                Assert.That(overlayTransform, Is.Not.Null);
                Assert.That(overlayTransform.gameObject.activeInHierarchy, Is.True);
                Assert.That(overlayTransform.Find("EnergyPanel/TitleText")?.GetComponent<Text>()?.text, Is.EqualTo("YOUR ENERGY"));
                Assert.That(overlayTransform.Find("EnergyPanel/GetMoreEnergyButton"), Is.Not.Null);

                overlayTransform.Find("EnergyPanel/CloseButton").GetComponent<Button>().onClick.Invoke();
                yield return null;
                Assert.That(overlayTransform.gameObject.activeSelf, Is.False);
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
                "TAD Test Energy Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

            var buttonObject = new GameObject(
                "ButtonTemplate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(canvasObject.transform, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.18f, 0.42f, 0.25f, 1f);
            buttonTemplate = buttonObject.GetComponent<Button>();
            buttonTemplate.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return canvasObject;
        }
    }
}
