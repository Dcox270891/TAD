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
        private const string ButtonSurfaceObjectName = "FarmUiButtonSurface";
        private const float ButtonSurfaceInset = 7f;
        private const float HudBadgeHorizontalInset = 16f;
        private const float HudBadgeVerticalInset = 7f;
        private const float HudBadgeRowGap = 12f;

        [Header("Farm UI Artwork")]
        [SerializeField] private Sprite menuGrassSprite;
        [SerializeField] private Sprite thinWoodOutlineSprite;

        // A single dark ink palette keeps the small mobile UI crisp against its light parchment
        // surfaces. Bold, slightly slanted headings provide the playful hand-painted feel
        // without relying on blurred drop shadows or a device-specific system font.
        private static readonly Color InkBrown = new(0.24f, 0.105f, 0.03f, 1f);
        private static readonly Color PanelText = InkBrown;
        private static readonly Color HudText = InkBrown;
        private static readonly Color ButtonText = InkBrown;
        private static readonly Color HeadingText = InkBrown;
        private static readonly Color AlertText = new(0.43f, 0.115f, 0.035f, 1f);
        private static readonly Color PanelSurface = new(0.94f, 0.86f, 0.66f, 0.97f);
        private static readonly Color HudPanelSurface = new(0.94f, 0.82f, 0.53f, 0.98f);
        private static readonly Color MaskedButtonSurface = new(0.94f, 0.86f, 0.66f, 0.98f);
        private static readonly Color MaskedButtonRim = new(0.43f, 0.19f, 0.06f, 0.98f);

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

            var outlineSprite = ResolveThinWoodOutlineSprite();
            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StyleImage(image, outlineSprite);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, outlineSprite);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                EnsureHudBadge(text, outlineSprite);
            }

            ArrangeHudBadgeRows(subtree);
        }

        /// <summary>
        /// Used by the editor asset builder to apply the same visual rules to every shipped UI
        /// prefab. It deliberately avoids gameplay bindings and layout components.
        /// </summary>
        public static void ApplyThinWoodOutlineTheme(Transform subtree, Sprite outlineSprite)
        {
            if (subtree == null || outlineSprite == null)
            {
                return;
            }

            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StylePanelImage(image, outlineSprite);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, outlineSprite);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text);
            }
        }

        /// <summary>Assigns the shared thin-outline artwork for an editor-authored UI owner.</summary>
        public void ConfigureThinWoodOutline(Sprite sprite)
        {
            thinWoodOutlineSprite = sprite;
        }

        public Sprite ThinWoodOutlineSprite => thinWoodOutlineSprite;

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

        private void StyleImage(Image image, Sprite outlineSprite)
        {
            if (IsMainMenuBackground(image))
            {
                ApplySprite(image, menuGrassSprite, Image.Type.Tiled);
                image.color = Color.white;
                return;
            }

            StylePanelImage(image, outlineSprite);
        }

        private static void StylePanelImage(Image image, Sprite outlineSprite)
        {
            if (image == null ||
                image.GetComponent<Mask>() != null ||
                IsBlocker(image) ||
                IsFoodStoreBarBackground(image) ||
                IsGameplayArtwork(image) ||
                IsLikelyMainMenuBackground(image))
            {
                return;
            }

            var directOutline = image.GetComponent<FarmUiThinWoodOutline>();
            if (directOutline != null)
            {
                directOutline.Configure(outlineSprite != null ? outlineSprite : directOutline.OutlineSprite);
                return;
            }

            if (!ShouldUsePanelSurface(image))
            {
                return;
            }

            // A flat, readable interior lets the fine outline do the visual work without the
            // former wide timber border or brass corner pieces showing through underneath.
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.fillCenter = true;
            image.preserveAspect = false;
            image.color = ResolvePanelSurface(image);
            DisableLegacyImageOutline(image);
            FarmUiThinWoodOutline.EnsureFrame(image, outlineSprite);
        }

        private static void StyleButton(Button button, Sprite outlineSprite)
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
                // A Mask gets its clipping shape from this exact image. Applying an outline-only
                // sprite here turns the mask centre transparent, which hides every child (the
                // attacker image, count, and state in the queue are all children of this mask).
                if (targetImage.GetComponent<Mask>() != null)
                {
                    StyleMaskedButtonTarget(targetImage);
                }
                else
                {
                    var thinWoodOutline = targetImage.GetComponent<FarmUiThinWoodOutline>();
                    if (thinWoodOutline == null)
                    {
                        thinWoodOutline = targetImage.gameObject.AddComponent<FarmUiThinWoodOutline>();
                    }

                    thinWoodOutline.Configure(outlineSprite != null ? outlineSprite : thinWoodOutline.OutlineSprite);
                    DisableLegacyImageOutline(targetImage);
                    EnsureButtonSurface(targetImage);
                }

                targetImage.raycastTarget = true;
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

        /// <summary>
        /// Keeps dark ink labels readable on every ordinary thin-frame button without covering
        /// its narrow wood trim. Masked circular queue controls manage their own filled surface.
        /// </summary>
        private static void EnsureButtonSurface(Image targetImage)
        {
            if (targetImage == null)
            {
                return;
            }

            var surfaceTransform = targetImage.transform.Find(ButtonSurfaceObjectName);
            if (surfaceTransform == null)
            {
                var surfaceObject = new GameObject(
                    ButtonSurfaceObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                surfaceObject.layer = targetImage.gameObject.layer;
                surfaceObject.transform.SetParent(targetImage.transform, false);
                surfaceTransform = surfaceObject.transform;
            }

            var surfaceRect = surfaceTransform as RectTransform;
            if (surfaceRect != null)
            {
                surfaceRect.anchorMin = Vector2.zero;
                surfaceRect.anchorMax = Vector2.one;
                surfaceRect.pivot = new Vector2(0.5f, 0.5f);
                surfaceRect.offsetMin = Vector2.one * ButtonSurfaceInset;
                surfaceRect.offsetMax = Vector2.one * -ButtonSurfaceInset;
                surfaceRect.localScale = Vector3.one;
            }

            var surfaceImage = surfaceTransform.GetComponent<Image>();
            if (surfaceImage == null)
            {
                surfaceImage = surfaceTransform.gameObject.AddComponent<Image>();
            }

            surfaceImage.sprite = null;
            surfaceImage.type = Image.Type.Simple;
            surfaceImage.fillCenter = true;
            surfaceImage.preserveAspect = false;
            surfaceImage.color = PanelSurface;
            surfaceImage.raycastTarget = false;
            surfaceTransform.SetSiblingIndex(0);
        }

        /// <summary>Keeps an opaque circular clipping graphic behind the attacker queue's portrait and count.</summary>
        private static void StyleMaskedButtonTarget(Image targetImage)
        {
            if (targetImage == null)
            {
                return;
            }

            var directOutline = targetImage.GetComponent<FarmUiThinWoodOutline>();
            if (directOutline != null)
            {
                RemoveDirectThinWoodOutline(directOutline);
            }

            targetImage.type = Image.Type.Simple;
            targetImage.fillCenter = true;
            targetImage.preserveAspect = false;
            targetImage.color = MaskedButtonSurface;

            // A rectangular nine-slice frame would itself be clipped by the circular mask.
            // Keep a restrained circular rim instead, so this compact queue control still fits
            // the wood-and-ink UI treatment without risking its content visibility.
            var rim = targetImage.GetComponent<Outline>() ?? targetImage.gameObject.AddComponent<Outline>();
            rim.enabled = true;
            rim.effectColor = MaskedButtonRim;
            rim.effectDistance = new Vector2(0.8f, -0.8f);
            rim.useGraphicAlpha = true;
        }

        private static void RemoveDirectThinWoodOutline(FarmUiThinWoodOutline outline)
        {
            if (outline == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(outline);
                return;
            }
#endif

            Destroy(outline);
        }

        private static void StyleText(Text text)
        {
            if (text == null)
            {
                return;
            }

            RemoveTextEffects(text);
            text.alignByGeometry = true;
            switch (ResolveTextTreatment(text))
            {
                case TextTreatment.Button:
                    text.color = ButtonText;
                    text.fontStyle = FontStyle.BoldAndItalic;
                    // Preserve deliberate top/bottom placement (such as queue count badges),
                    // while always centring labels horizontally inside their own button region.
                    text.alignment = CenterHorizontally(text.alignment);
                    break;

                case TextTreatment.Hud:
                    text.color = HudText;
                    text.fontStyle = FontStyle.Bold;
                    text.alignment = TextAnchor.MiddleCenter;
                    ConfigureTextToShrinkBeforeItOverflows(text);
                    break;

                case TextTreatment.Heading:
                    text.color = HeadingText;
                    text.fontStyle = FontStyle.BoldAndItalic;
                    break;

                case TextTreatment.Alert:
                    text.color = AlertText;
                    text.fontStyle = FontStyle.BoldAndItalic;
                    break;

                case TextTreatment.Badge:
                    text.color = HeadingText;
                    text.fontStyle = FontStyle.Bold;
                    break;

                default:
                    text.color = PanelText;
                    text.fontStyle = FontStyle.Bold;
                    break;
            }
        }

        private static TextAnchor CenterHorizontally(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.UpperLeft or TextAnchor.UpperRight => TextAnchor.UpperCenter,
                TextAnchor.MiddleLeft or TextAnchor.MiddleRight => TextAnchor.MiddleCenter,
                TextAnchor.LowerLeft or TextAnchor.LowerRight => TextAnchor.LowerCenter,
                _ => alignment
            };
        }

        private static void ConfigureTextToShrinkBeforeItOverflows(Text text)
        {
            if (text == null)
            {
                return;
            }

            // Keep a single, centred HUD line. Best-fit can reduce the label if an unusually
            // large bank value would otherwise reach the thin wood frame.
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Min(12, Mathf.Max(1, text.fontSize));
            text.resizeTextMaxSize = Mathf.Max(12, text.fontSize);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
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

            if (text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null ||
                text.GetComponentInParent<AttackerArchetypeBadge>(true) != null)
            {
                return TextTreatment.Badge;
            }

            if (IsAlertText(text))
            {
                return TextTreatment.Alert;
            }

            return IsHeadingText(text) ? TextTreatment.Heading : TextTreatment.Body;
        }

        /// <summary>Removes legacy mesh effects that soften small text on the game's light UI.</summary>
        private static void RemoveTextEffects(Text text)
        {
            if (text == null)
            {
                return;
            }

            // Outline inherits Shadow, so this removes both old outlines and ordinary shadows.
            foreach (var effect in text.GetComponents<Shadow>())
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(effect);
                    continue;
                }
#endif

                effect.enabled = false;
                Destroy(effect);
            }
        }

        private void EnsureHudBadge(Text text, Sprite outlineSprite)
        {
            if (text == null || !NeedsHudBadge(text) || text.transform.parent == null)
            {
                return;
            }

            var badgeName = $"FarmUiBadge_{text.gameObject.name}";
            var textTransform = text.transform;
            var labelRect = text.rectTransform;
            var badge = textTransform.parent.name == badgeName
                ? textTransform.parent.GetComponent<Image>()
                : null;
            var parent = badge != null ? badge.transform.parent : textTransform.parent;

            if (parent == null)
            {
                return;
            }

            var isBadgeChild = badge != null;
            if (badge == null)
            {
                var badgeTransform = parent.Find(badgeName);
                badge = badgeTransform != null
                    ? badgeTransform.GetComponent<Image>()
                    : null;
            }

            if (badge == null)
            {
                var badgeObject = new GameObject(badgeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                badgeObject.layer = text.gameObject.layer;
                badgeObject.transform.SetParent(parent, false);
                badge = badgeObject.GetComponent<Image>();
                badge.raycastTarget = false;
            }

            var badgeRect = badge.rectTransform;
            if (!isBadgeChild)
            {
                // Capture the serialized HUD location once, then make the text a child of the
                // plaque so its centred text area can never extend beyond the wood frame.
                badgeRect.anchorMin = labelRect.anchorMin;
                badgeRect.anchorMax = labelRect.anchorMax;
                badgeRect.pivot = labelRect.pivot;
                badgeRect.anchoredPosition = labelRect.anchoredPosition;
                badgeRect.localRotation = labelRect.localRotation;
                badgeRect.localScale = labelRect.localScale;
                badge.transform.SetSiblingIndex(textTransform.GetSiblingIndex());
            }

            // The HUD labels use generous layout rects so their text can update without clipping.
            // Size the decorative plaque from its visible content instead of that full layout rect.
            var desiredWidth = Mathf.Clamp(text.preferredWidth + 56f, 172f, 260f);
            var sourceHeight = isBadgeChild ? badgeRect.rect.height : labelRect.rect.height;
            var desiredHeight = isBadgeChild
                ? Mathf.Max(sourceHeight, text.preferredHeight + HudBadgeVerticalInset * 2f)
                : Mathf.Max(sourceHeight + 18f, text.preferredHeight + HudBadgeVerticalInset * 2f);
            badgeRect.sizeDelta = new Vector2(desiredWidth, desiredHeight);
            badge.sprite = null;
            badge.type = Image.Type.Simple;
            badge.fillCenter = true;
            badge.preserveAspect = false;
            badge.color = HudPanelSurface;
            FarmUiThinWoodOutline.EnsureFrame(badge, outlineSprite);

            if (textTransform.parent != badge.transform)
            {
                textTransform.SetParent(badge.transform, false);
            }

            labelRect = text.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(HudBadgeHorizontalInset, HudBadgeVerticalInset);
            labelRect.offsetMax = new Vector2(-HudBadgeHorizontalInset, -HudBadgeVerticalInset);
            labelRect.localRotation = Quaternion.identity;
            labelRect.localScale = Vector3.one;
            text.alignment = TextAnchor.MiddleCenter;
            ConfigureTextToShrinkBeforeItOverflows(text);
        }

        /// <summary>
        /// Places the attacker income plaque below its gold plaque. The source labels used to have
        /// independent fixed offsets, which made the new, content-sized plaques overlap.
        /// </summary>
        private static void ArrangeHudBadgeRows(Transform subtree)
        {
            var banksHud = subtree != null ? subtree.GetComponentInParent<ResourceBanksHud>() : null;
            if (banksHud == null)
            {
                return;
            }

            var attackerGoldBadge = banksHud.transform.Find("FarmUiBadge_AttackerGoldText") as RectTransform;
            var attackerIncomeBadge = banksHud.transform.Find("FarmUiBadge_AttackerIncomeText") as RectTransform;
            if (attackerGoldBadge == null || attackerIncomeBadge == null)
            {
                return;
            }

            var incomePosition = attackerIncomeBadge.anchoredPosition;
            incomePosition.y = attackerGoldBadge.anchoredPosition.y - attackerGoldBadge.rect.height - HudBadgeRowGap;
            attackerIncomeBadge.anchoredPosition = incomePosition;
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

        private static bool IsLikelyMainMenuBackground(Image image)
        {
            if (image == null || image.gameObject.name != "Background")
            {
                return false;
            }

            for (var current = image.transform.parent; current != null; current = current.parent)
            {
                if (current.name.Contains("MainMenu"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldUsePanelSurface(Image image)
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

        private Sprite ResolveThinWoodOutlineSprite()
        {
            if (thinWoodOutlineSprite != null)
            {
                return thinWoodOutlineSprite;
            }

            // Older scene copies may not yet have the serialized field. Reuse a nested frame
            // when one exists so a saved match remains visually coherent while assets migrate.
            foreach (var outline in GetComponentsInChildren<FarmUiThinWoodOutline>(true))
            {
                if (outline != null && outline.OutlineSprite != null)
                {
                    thinWoodOutlineSprite = outline.OutlineSprite;
                    return thinWoodOutlineSprite;
                }
            }

            return null;
        }

        private static Color ResolvePanelSurface(Image image)
        {
            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("hud") ||
                   name.Contains("food") ||
                   name.Contains("bank") ||
                   name.Contains("resource") ||
                   name.Contains("day")
                ? HudPanelSurface
                : PanelSurface;
        }

        private static void DisableLegacyImageOutline(Image image)
        {
            var outline = image != null ? image.GetComponent<Outline>() : null;
            if (outline != null)
            {
                outline.enabled = false;
            }
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

        private static bool IsFoodStoreBarBackground(Image image)
        {
            return image != null &&
                   image.gameObject.name == "Background" &&
                   image.GetComponentInParent<FoodStoreHud>(true) != null;
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
