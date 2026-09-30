using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Applies the shared farm-themed presentation to an existing uGUI canvas without changing
    /// gameplay bindings, layout ownership, or interaction behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmUiSkin : MonoBehaviour
    {
        [Header("Farm UI Artwork")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite menuGrassSprite;

        private static readonly Color PanelText = new(0.19f, 0.10f, 0.045f, 1f);
        private static readonly Color HudText = new(1f, 0.94f, 0.74f, 1f);
        private static readonly Color ButtonText = new(1f, 0.97f, 0.82f, 1f);
        private static readonly Color HeadingText = new(0.34f, 0.16f, 0.055f, 1f);
        private static readonly Color AlertText = new(0.55f, 0.13f, 0.055f, 1f);
        private static readonly Color TextShadow = new(0.16f, 0.07f, 0.02f, 0.88f);
        private static readonly Color BodyOutline = new(0.95f, 0.82f, 0.54f, 0.42f);
        private static readonly Color LightOutline = new(0.16f, 0.07f, 0.02f, 0.94f);
        private static readonly Color PanelOutline = new(0.16f, 0.08f, 0.025f, 0.70f);

        private enum TextTreatment
        {
            Body,
            Heading,
            Hud,
            Button,
            Badge,
            Alert
        }

        private IEnumerator Start()
        {
            // Dynamic content is often added by a bootstrapper in its own Start call.
            yield return null;
            ApplyTheme();
        }

        /// <summary>Reapplies the presentation to every existing child of this themed canvas.</summary>
        public void ApplyTheme()
        {
            ApplyThemeTo(transform);
        }

        /// <summary>Styles a dynamically-created subtree owned by this canvas.</summary>
        public void ApplyThemeTo(Transform subtree)
        {
            if (subtree == null)
            {
                return;
            }

            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StyleImage(image);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                EnsureHudBadge(text);
            }
        }

        /// <summary>
        /// Lets runtime-created overlays use the same visual language as the canvas that owns them.
        /// </summary>
        public static void ApplyFromCanvas(Transform canvasRoot, Transform subtree)
        {
            if (canvasRoot == null || subtree == null)
            {
                return;
            }

            var skin = canvasRoot.GetComponent<FarmUiSkin>();
            if (skin == null)
            {
                skin = canvasRoot.GetComponentInParent<FarmUiSkin>();
            }

            skin?.ApplyThemeTo(subtree);
        }

        private void StyleImage(Image image)
        {
            if (image == null || IsBlocker(image) || IsGameplayArtwork(image))
            {
                return;
            }

            // A deliberately small marker component keeps the new brass-free outline from
            // being replaced by the wider, decorative farm panel artwork below. It also makes
            // the treatment available to runtime-created controls without changing their
            // bindings or button behaviour.
            var thinWoodOutline = image.GetComponent<FarmUiThinWoodOutline>();
            if (thinWoodOutline != null)
            {
                thinWoodOutline.ApplyTo(image);
                return;
            }

            // Detail cards keep their plain parchment, raycastable surface for readable stats and to stop
            // a click inside the card from dismissing the panel. Their thin wood frame is a
            // non-interactive child overlay, added by ThinWoodOutlineUiAssetBuilder.
            if (IsInformationDetailCard(image))
            {
                return;
            }

            if (IsMainMenuBackground(image))
            {
                ApplySprite(image, menuGrassSprite, Image.Type.Tiled);
                image.color = Color.white;
                return;
            }

            if (IsSettingsContentPanel(image))
            {
                // The parent settings panel already provides the decorative wood-and-brass frame.
                // A quiet paper surface here keeps long settings labels inside their usable viewport.
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = new Color(0.95f, 0.87f, 0.66f, 0.98f);
                var contentOutline = image.GetComponent<Outline>() ?? image.gameObject.AddComponent<Outline>();
                contentOutline.effectColor = PanelOutline;
                contentOutline.effectDistance = new Vector2(2f, -2f);
                contentOutline.useGraphicAlpha = true;
                return;
            }

            if (!ShouldUsePanelSprite(image))
            {
                return;
            }

            ApplySprite(image, panelSprite, Image.Type.Sliced);
            image.color = Color.white;

            if (image.gameObject.name.Contains("Panel"))
            {
                var outline = image.GetComponent<Outline>() ?? image.gameObject.AddComponent<Outline>();
                outline.effectColor = PanelOutline;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                outline.useGraphicAlpha = true;
            }
        }

        private void StyleButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            var targetImage = button.targetGraphic as Image;
            if (targetImage == null)
            {
                targetImage = button.GetComponent<Image>();
                button.targetGraphic = targetImage;
            }

            if (targetImage != null)
            {
                var thinWoodOutline = button.GetComponent<FarmUiThinWoodOutline>();
                if (thinWoodOutline != null)
                {
                    thinWoodOutline.ApplyTo(targetImage);
                }
                else
                {
                    ApplySprite(targetImage, buttonSprite, Image.Type.Sliced);
                    targetImage.color = Color.white;
                }
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.88f, 1f);
            colors.pressedColor = new Color(0.72f, 0.84f, 0.55f, 1f);
            colors.selectedColor = new Color(0.94f, 1f, 0.74f, 1f);
            colors.disabledColor = new Color(0.46f, 0.46f, 0.42f, 0.75f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        private void StyleText(Text text)
        {
            if (text == null)
            {
                return;
            }

            switch (ResolveTextTreatment(text))
            {
                case TextTreatment.Button:
                    text.color = ButtonText;
                    text.fontStyle = FontStyle.Bold;
                    ApplyTextEffects(text, LightOutline, new Vector2(0.85f, -0.85f), new Vector2(1.6f, -1.6f));
                    break;

                case TextTreatment.Hud:
                    text.color = HudText;
                    text.fontStyle = FontStyle.Bold;
                    ApplyTextEffects(text, LightOutline, new Vector2(0.8f, -0.8f), new Vector2(1.45f, -1.45f));
                    break;

                case TextTreatment.Heading:
                    text.color = HeadingText;
                    text.fontStyle = IsTitleText(text) ? FontStyle.BoldAndItalic : FontStyle.Bold;
                    ApplyTextEffects(text, BodyOutline, new Vector2(0.8f, -0.8f), new Vector2(1.15f, -1.15f));
                    break;

                case TextTreatment.Alert:
                    text.color = AlertText;
                    text.fontStyle = FontStyle.Bold;
                    ApplyTextEffects(text, BodyOutline, new Vector2(0.8f, -0.8f), new Vector2(1.1f, -1.1f));
                    break;

                case TextTreatment.Badge:
                    // Combat-rating numerals sit over their own star artwork, so a bold ink
                    // treatment reads better than the cream text used on wood buttons.
                    text.color = HeadingText;
                    text.fontStyle = FontStyle.Bold;
                    ApplyTextEffects(text, BodyOutline, new Vector2(0.5f, -0.5f), new Vector2(0.75f, -0.75f), false);
                    break;

                default:
                    text.color = PanelText;
                    // Smaller stat and description copy stays crisp on parchment rather than
                    // receiving a heavy outline intended for signs and buttons.
                    ApplyTextEffects(text, BodyOutline, new Vector2(0.55f, -0.55f), new Vector2(0.9f, -0.9f), false);
                    break;
            }
        }

        private static TextTreatment ResolveTextTreatment(Text text)
        {
            if (text.GetComponentInParent<Button>(true) != null)
            {
                return TextTreatment.Button;
            }

            if (IsHudText(text))
            {
                return TextTreatment.Hud;
            }

            if (text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null)
            {
                return TextTreatment.Badge;
            }

            if (IsAlertText(text))
            {
                return TextTreatment.Alert;
            }

            return IsHeadingText(text) ? TextTreatment.Heading : TextTreatment.Body;
        }

        private static void ApplyTextEffects(
            Text text,
            Color outlineColor,
            Vector2 outlineDistance,
            Vector2 shadowDistance,
            bool includeOutline = true)
        {
            if (includeOutline)
            {
                var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
                outline.effectColor = outlineColor;
                outline.effectDistance = outlineDistance;
                outline.useGraphicAlpha = true;
            }

            // Outline inherits Shadow, so GetComponent<Shadow>() can return the outline we just
            // configured. Keep a distinct drop-shadow component for the sign-painted depth effect.
            Shadow shadow = null;
            foreach (var candidate in text.GetComponents<Shadow>())
            {
                if (candidate.GetType() == typeof(Shadow))
                {
                    shadow = candidate;
                    break;
                }
            }

            shadow ??= text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = TextShadow;
            shadow.effectDistance = shadowDistance;
            shadow.useGraphicAlpha = true;
        }

        private void EnsureHudBadge(Text text)
        {
            if (panelSprite == null || text == null || !NeedsHudBadge(text) || text.transform.parent == null)
            {
                return;
            }

            var parent = text.transform.parent;
            var badgeName = $"FarmUiBadge_{text.gameObject.name}";
            var badgeTransform = parent.Find(badgeName);
            var badge = badgeTransform != null
                ? badgeTransform.GetComponent<Image>()
                : null;
            if (badge == null)
            {
                var badgeObject = new GameObject(badgeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                badgeObject.layer = text.gameObject.layer;
                badgeObject.transform.SetParent(parent, false);
                badge = badgeObject.GetComponent<Image>();
                badge.raycastTarget = false;
            }

            var labelRect = text.rectTransform;
            var badgeRect = badge.rectTransform;
            badgeRect.anchorMin = labelRect.anchorMin;
            badgeRect.anchorMax = labelRect.anchorMax;
            badgeRect.pivot = labelRect.pivot;
            badgeRect.anchoredPosition = labelRect.anchoredPosition;
            badgeRect.localRotation = labelRect.localRotation;
            badgeRect.localScale = labelRect.localScale;
            // The HUD labels use generous layout rects so their text can update without clipping.
            // Size the decorative plaque from its visible content instead of that full layout rect.
            var desiredWidth = Mathf.Clamp(text.preferredWidth + 56f, 172f, 260f);
            var desiredHeight = Mathf.Max(labelRect.sizeDelta.y + 18f, text.preferredHeight + 18f);
            badgeRect.sizeDelta = new Vector2(desiredWidth, desiredHeight);
            ApplySprite(badge, panelSprite, Image.Type.Sliced);
            badge.color = Color.white;
            var labelIndex = text.transform.GetSiblingIndex();
            var badgeIndex = badge.transform.GetSiblingIndex();
            badge.transform.SetSiblingIndex(badgeIndex < labelIndex ? labelIndex - 1 : labelIndex);
        }

        private void ApplySprite(Image image, Sprite sprite, Image.Type imageType)
        {
            if (image == null || sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = imageType;
            image.preserveAspect = false;
        }

        private bool IsMainMenuBackground(Image image)
        {
            return menuGrassSprite != null &&
                   gameObject.name.Contains("MainMenu") &&
                   image.gameObject.name == "Background" &&
                   image.transform.parent == transform;
        }

        private static bool IsSettingsContentPanel(Image image)
        {
            return image.gameObject.name == "ContentPanel" &&
                   image.GetComponentInParent<PlayerSettingsOverlay>(true) != null;
        }

        private static bool IsInformationDetailCard(Image image)
        {
            if (image.gameObject.name != "Card")
            {
                return false;
            }

            return image.GetComponentInParent<AttackerDetailsPanel>(true) != null ||
                   image.GetComponentInParent<DefenderDetailsPanel>(true) != null;
        }

        private static bool ShouldUsePanelSprite(Image image)
        {
            if (image.GetComponent<Button>() != null ||
                image.GetComponentInParent<Button>(true) != null ||
                image.GetComponentInParent<Toggle>(true) != null ||
                image.GetComponentInParent<Slider>(true) != null ||
                image.GetComponentInParent<Scrollbar>(true) != null ||
                image.GetComponent<RectMask2D>() != null)
            {
                return false;
            }

            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("panel") ||
                   name.Contains("background") ||
                   name.Contains("card") ||
                   name.Contains("details") ||
                   name.Contains("window") ||
                   name.Contains("topcentrefoodbar") ||
                   name.Contains("selection") ||
                   name.Contains("queue") ||
                   (name.Contains("scrollview") &&
                    image.GetComponentInParent<AttackerQueuePanel>(true) != null);
        }

        private static bool IsBlocker(Image image)
        {
            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("blocker") || name.Contains("backdrop");
        }

        private static bool IsGameplayArtwork(Image image)
        {
            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("fill") ||
                   name.Contains("attackerimage") ||
                   name.Contains("defenderimage") ||
                   name.Contains("projectile") ||
                   name.Contains("health") ||
                   name.Contains("experience") ||
                   name.Contains("checkmark") ||
                   name.Contains("handle") ||
                   name == "viewport";
        }

        private static bool IsHudText(Text text)
        {
            var name = text.gameObject.name.ToLowerInvariant();
            return name.Contains("gold") ||
                   name.Contains("income") ||
                   name.Contains("day") ||
                   name.Contains("preparation") ||
                   name.Contains("amount") ||
                   name.Contains("count") ||
                   name.Contains("progress");
        }

        private static bool IsHeadingText(Text text)
        {
            var name = text.gameObject.name.ToLowerInvariant();
            return IsTitleText(text) ||
                   name.Contains("heading") ||
                   name.Contains("header") ||
                   name.Contains("name") ||
                   name.Contains("weapon") ||
                   name.Contains("upnext");
        }

        private static bool IsTitleText(Text text)
        {
            return IsMainMenuTitle(text) ||
                   text.gameObject.name.ToLowerInvariant().Contains("title");
        }

        private static bool IsAlertText(Text text)
        {
            var name = text.gameObject.name.ToLowerInvariant();
            return name.Contains("warning") ||
                   name.Contains("error") ||
                   name.Contains("message") ||
                   (text.GetComponentInParent<ModalOverlay>(true) != null &&
                    text.GetComponentInParent<Button>(true) == null);
        }

        private static bool NeedsHudBadge(Text text)
        {
            var name = text.gameObject.name;
            return name == "AttackerGoldText" ||
                   name == "DefenderGoldText" ||
                   name == "AttackerIncomeText";
        }

        private static bool IsMainMenuTitle(Text text)
        {
            if (text.gameObject.name != "TitleText")
            {
                return false;
            }

            for (var current = text.transform.parent; current != null; current = current.parent)
            {
                if (current.name == "MainMenuPanel")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
