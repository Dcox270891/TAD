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
        /// <summary>
        /// Bumps whenever the editor art pass needs to revisit existing UI assets for a shared
        /// layout correction. Keeping this alongside the visual skin avoids one-off prefab edits.
        /// </summary>
        public const int CurrentFarmUiLayoutRevision = 5;

        private const string ButtonSurfaceObjectName = "FarmUiButtonSurface";
        private const float ButtonSurfaceInset = 7f;
        private const float OrdinaryButtonHorizontalPadding = 28f;
        private const float OrdinaryButtonVerticalPadding = 14f;
        private const float CardButtonHorizontalPadding = 18f;
        // Card choice buttons can deliberately stack more than one label. Eight pixels leaves
        // meaningful breathing room without pulling their cost and income rows into each other.
        private const float CardButtonVerticalPadding = 8f;
        private const float CardSurfaceHorizontalPadding = 24f;
        private const float CardSurfaceVerticalPadding = 20f;
        private const float HudBadgeHorizontalInset = 16f;
        private const float HudBadgeVerticalInset = 7f;
        private const float HudBadgeRowGap = 12f;
        private const int LandscapeBodyFontSize = 22;
        private const int LandscapeButtonFontSize = 28;
        private const int LandscapeHudFontSize = 26;
        private const int LandscapeBadgeFontSize = 22;
        private const int LandscapeHeadingFontSize = 36;
        private const int LandscapeAlertFontSize = 28;
        private const int PortraitBodyFontSize = 26;
        private const int PortraitButtonFontSize = 34;
        private const int PortraitHudFontSize = 30;
        private const int PortraitBadgeFontSize = 26;
        private const int PortraitHeadingFontSize = 44;
        private const int PortraitAlertFontSize = 34;

        [Header("Farm UI Artwork")]
        [SerializeField] private Sprite menuGrassSprite;
        [SerializeField] private Sprite thinWoodOutlineSprite;
        [SerializeField] private Sprite woodPlankButtonSprite;
        [SerializeField] private Sprite parchmentCardSprite;
        [SerializeField] private Sprite statusBarFrameSprite;
        [SerializeField] private Sprite statusBarFillSprite;
        [SerializeField] private Font boldFarmFont;
        [SerializeField, HideInInspector] private int appliedFarmUiLayoutRevision;

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
            var font = ResolveBoldFarmFont();
            var portraitTypography = MobileUiLayoutController.UsesPortraitLayout(subtree);
            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StyleImage(image, outlineSprite, parchmentCardSprite);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, outlineSprite, woodPlankButtonSprite);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text, font, portraitTypography);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                EnsureHudBadge(text, outlineSprite, woodPlankButtonSprite);
            }

            // Some early authoring passes created the decorative child before its layout marker.
            // Normalise those frames once the canvas is running, outside Awake/OnValidate.
            foreach (var outline in subtree.GetComponentsInChildren<FarmUiThinWoodOutline>(true))
            {
                FarmUiThinWoodOutline.EnsureLayoutNeutral(outline.transform);
            }

            ArrangeHudBadgeRows(subtree);
        }

        /// <summary>
        /// Used by the editor asset builder to apply the same visual rules to every shipped UI
        /// prefab. It deliberately avoids gameplay bindings and layout components.
        /// </summary>
        public static void ApplyThinWoodOutlineTheme(Transform subtree, Sprite outlineSprite, Font font = null)
        {
            if (subtree == null || outlineSprite == null)
            {
                return;
            }

            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StylePanelImage(image, outlineSprite, null);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, outlineSprite, null);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text, font, MobileUiLayoutController.UsesPortraitLayout(text.transform));
            }

            foreach (var outline in subtree.GetComponentsInChildren<FarmUiThinWoodOutline>(true))
            {
                FarmUiThinWoodOutline.EnsureLayoutNeutral(outline.transform);
            }
        }

        /// <summary>
        /// Applies the current carved-wood, parchment and status-bar family to editor-authored
        /// or runtime-built uGUI content. Gameplay code remains independent of these visuals.
        /// </summary>
        public static void ApplyFarmArtDirectionTheme(
            Transform subtree,
            Sprite outlineSprite,
            Sprite buttonSprite,
            Sprite cardSprite,
            Font font = null)
        {
            if (subtree == null)
            {
                return;
            }

            foreach (var image in subtree.GetComponentsInChildren<Image>(true))
            {
                StylePanelImage(image, outlineSprite, cardSprite);
            }

            foreach (var button in subtree.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, outlineSprite, buttonSprite);
            }

            foreach (var text in subtree.GetComponentsInChildren<Text>(true))
            {
                StyleText(text, font, MobileUiLayoutController.UsesPortraitLayout(text.transform));
            }

            foreach (var outline in subtree.GetComponentsInChildren<FarmUiThinWoodOutline>(true))
            {
                FarmUiThinWoodOutline.EnsureLayoutNeutral(outline.transform);
            }
        }

        /// <summary>Assigns the shared thin-outline artwork for an editor-authored UI owner.</summary>
        public void ConfigureThinWoodOutline(Sprite sprite)
        {
            thinWoodOutlineSprite = sprite;
        }

        /// <summary>Assigns the shared visual family used by menus, cards, and status displays.</summary>
        public void ConfigureFarmArtDirection(
            Sprite buttonSprite,
            Sprite cardSprite,
            Sprite barFrameSprite,
            Sprite barFillSprite)
        {
            woodPlankButtonSprite = buttonSprite;
            parchmentCardSprite = cardSprite;
            statusBarFrameSprite = barFrameSprite;
            statusBarFillSprite = barFillSprite;
            appliedFarmUiLayoutRevision = CurrentFarmUiLayoutRevision;
        }

        /// <summary>Assigns the bundled rounded display font used by menus and runtime overlays.</summary>
        public void ConfigureBoldFarmFont(Font font)
        {
            boldFarmFont = font;
        }

        /// <summary>Assigns the illustrated farm backdrop used by the main menu only.</summary>
        public void ConfigureMenuBackdrop(Sprite sprite)
        {
            menuGrassSprite = sprite;
        }

        public Sprite ThinWoodOutlineSprite => thinWoodOutlineSprite;

        public Sprite WoodPlankButtonSprite => woodPlankButtonSprite;

        public Sprite ParchmentCardSprite => parchmentCardSprite;

        public Sprite StatusBarFrameSprite => statusBarFrameSprite;

        public Sprite StatusBarFillSprite => statusBarFillSprite;

        public Font BoldFarmFont => boldFarmFont;

        /// <summary>Returns the serialized revision consumed by the editor art pass.</summary>
        public int AppliedFarmUiLayoutRevision => appliedFarmUiLayoutRevision;

        /// <summary>
        /// Reserves a clear interior for a simple, one-label wood button. Complex selection
        /// cards, masked queue controls, and icon-only controls deliberately retain their own
        /// layout because their children have specialised arrangements.
        /// </summary>
        public static void EnsureReadableWoodButtonLayout(Button button, float minimumHeight = 0f)
        {
            if (!TryGetOrdinarySingleLabel(button, out var label))
            {
                return;
            }

            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(OrdinaryButtonHorizontalPadding, OrdinaryButtonVerticalPadding);
            labelRect.offsetMax = new Vector2(-OrdinaryButtonHorizontalPadding, -OrdinaryButtonVerticalPadding);
            labelRect.localScale = Vector3.one;
            label.alignment = TextAnchor.MiddleCenter;
            FarmUiTextPadding.Ensure(
                label,
                button.transform as RectTransform,
                OrdinaryButtonHorizontalPadding,
                OrdinaryButtonVerticalPadding,
                preventWrapping: true);

            if (minimumHeight <= 0f)
            {
                return;
            }

            var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(layout.minHeight, minimumHeight);
            layout.preferredHeight = Mathf.Max(layout.preferredHeight, minimumHeight);
        }

        /// <summary>
        /// Lets the responsive canvas refresh only typography after an orientation change without
        /// rebuilding panel frames, badges, or gameplay bindings.
        /// </summary>
        public void ApplyResponsiveTypography(bool portrait)
        {
            var font = ResolveBoldFarmFont();
            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                StyleText(text, font, portrait);
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

        private void StyleImage(Image image, Sprite outlineSprite, Sprite cardSprite)
        {
            if (IsMainMenuBackground(image))
            {
                // The refreshed menu uses a composed farm vista, not a repeating grass tile.
                // Preserve its focal barn and clear button area by stretching it once to the canvas.
                ApplySprite(image, menuGrassSprite, Image.Type.Simple);
                image.color = Color.white;
                return;
            }

            StylePanelImage(image, outlineSprite, cardSprite);
        }

        private static void StylePanelImage(Image image, Sprite outlineSprite, Sprite cardSprite)
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

            if (cardSprite != null && ShouldUseParchmentCard(image))
            {
                RemoveFrameDecorations(image.transform);
                var directCardOutline = image.GetComponent<FarmUiThinWoodOutline>();
                if (directCardOutline != null)
                {
                    RemoveDirectThinWoodOutline(directCardOutline);
                }

                image.sprite = cardSprite;
                image.type = Image.Type.Sliced;
                image.fillCenter = true;
                image.preserveAspect = false;
                image.color = Color.white;
                DisableLegacyImageOutline(image);
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

        private static void StyleButton(Button button, Sprite outlineSprite, Sprite buttonSprite)
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
                else if (buttonSprite != null)
                {
                    RemoveFrameDecorations(targetImage.transform);
                    var oldOutline = targetImage.GetComponent<FarmUiThinWoodOutline>();
                    if (oldOutline != null)
                    {
                        RemoveDirectThinWoodOutline(oldOutline);
                    }

                    DisableButtonSurface(targetImage);
                    targetImage.sprite = buttonSprite;
                    targetImage.type = Image.Type.Sliced;
                    targetImage.fillCenter = true;
                    targetImage.preserveAspect = false;
                    targetImage.color = Color.white;
                    DisableLegacyImageOutline(targetImage);
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

            EnsureAllButtonLabelPadding(button);
            EnsureReadableWoodButtonLayout(button);
        }

        /// <summary>
        /// Gives every text label owned by a button a four-sided safe area without re-parenting
        /// its existing visual hierarchy. Direct labels are constrained against the button;
        /// nested labels retain their local container while still receiving an inset there.
        /// </summary>
        private static void EnsureAllButtonLabelPadding(Button button)
        {
            if (button == null)
            {
                return;
            }

            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                EnsureButtonLabelPadding(label, button);
            }
        }

        private static void EnsureButtonLabelPadding(Text text, Button button)
        {
            if (text == null || button == null ||
                text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null ||
                text.GetComponentInParent<AttackerArchetypeBadge>(true) != null)
            {
                return;
            }

            var owner = text.transform.parent as RectTransform;
            if (owner == null || owner.GetComponent<LayoutGroup>() != null ||
                ((owner.GetComponent<Mask>() != null || owner.GetComponent<RectMask2D>() != null) &&
                 owner != button.transform))
            {
                return;
            }

            var isOrdinaryLabel = owner == button.transform && text.gameObject.name == "Label";
            FarmUiTextPadding.Ensure(
                text,
                owner,
                isOrdinaryLabel ? OrdinaryButtonHorizontalPadding : CardButtonHorizontalPadding,
                isOrdinaryLabel ? OrdinaryButtonVerticalPadding : CardButtonVerticalPadding,
                preventWrapping: true);
        }

        private static bool TryGetOrdinarySingleLabel(Button button, out Text label)
        {
            label = null;
            if (button == null)
            {
                return false;
            }

            var target = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (target != null && target.GetComponent<Mask>() != null)
            {
                return false;
            }

            var labels = button.GetComponentsInChildren<Text>(true);
            if (labels.Length != 1)
            {
                return false;
            }

            label = labels[0];
            // The shared plank buttons and menu controls have one direct "Label" child. This
            // keeps explanatory card copy and compact glyph controls out of the generic padding
            // pass even when they happen to have a single Text component.
            if (label.transform.parent != button.transform || label.gameObject.name != "Label")
            {
                label = null;
                return false;
            }

            var glyph = label.text != null ? label.text.Trim() : string.Empty;
            if (glyph == ">" || glyph == "<" || glyph == "||" || glyph == "X")
            {
                label = null;
                return false;
            }

            return true;
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

        private static void DisableButtonSurface(Image targetImage)
        {
            if (targetImage == null)
            {
                return;
            }

            var surface = targetImage.transform.Find(ButtonSurfaceObjectName);
            if (surface != null)
            {
                surface.gameObject.SetActive(false);
            }
        }

        private static void RemoveFrameDecorations(Transform target)
        {
            if (target == null)
            {
                return;
            }

            var frame = target.Find(FarmUiThinWoodOutline.FrameObjectName);
            if (frame == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(frame.gameObject);
                return;
            }
#endif

            Destroy(frame.gameObject);
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

        private static void StyleText(Text text, Font preferredFont, bool portrait)
        {
            if (text == null)
            {
                return;
            }

            RemoveTextEffects(text);
            text.alignByGeometry = true;
            text.font = ResolveTextFont(text, preferredFont);
            var treatment = ResolveTextTreatment(text);
            switch (treatment)
            {
                case TextTreatment.Button:
                    text.color = ButtonText;
                    text.fontStyle = FontStyle.Bold;
                    // Preserve deliberate top/bottom placement (such as queue count badges),
                    // while always centring labels horizontally inside their own button region.
                    text.alignment = CenterHorizontally(text.alignment);
                    break;

                case TextTreatment.Hud:
                    text.color = HudText;
                    text.fontStyle = FontStyle.Bold;
                    text.alignment = TextAnchor.MiddleCenter;
                    break;

                case TextTreatment.Heading:
                    text.color = HeadingText;
                    text.fontStyle = FontStyle.Bold;
                    break;

                case TextTreatment.Alert:
                    text.color = AlertText;
                    text.fontStyle = FontStyle.Bold;
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

            ConfigureReadableTextMetrics(text, treatment, portrait);
            EnsureTextContentPadding(text);
        }

        /// <summary>
        /// Adds safe interior bounds to labels which sit directly on a visual card or button
        /// surface.  Layout-group children retain their group-owned placement; this only protects
        /// the legacy absolute-positioned labels that previously crossed a wood or parchment edge
        /// on compact displays.
        /// </summary>
        private static void EnsureTextContentPadding(Text text)
        {
            if (text == null || text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null)
            {
                return;
            }

            var button = text.GetComponentInParent<Button>(true);
            if (button != null)
            {
                EnsureButtonLabelPadding(text, button);
                return;
            }

            var owner = text.transform.parent as RectTransform;
            if (owner == null || owner.GetComponent<LayoutGroup>() != null ||
                owner.GetComponent<Mask>() != null || owner.GetComponent<RectMask2D>() != null)
            {
                return;
            }

            var image = owner.GetComponent<Image>();
            if (image != null && IsTextContentSurface(image))
            {
                FarmUiTextPadding.Ensure(text, owner, CardSurfaceHorizontalPadding, CardSurfaceVerticalPadding);
            }
        }

        private Font ResolveBoldFarmFont()
        {
            return boldFarmFont != null
                ? boldFarmFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Font ResolveTextFont(Text text, Font preferredFont)
        {
            if (preferredFont != null)
            {
                return preferredFont;
            }

            if (text != null && text.font != null)
            {
                return text.font;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        private static void ConfigureReadableTextMetrics(Text text, TextTreatment treatment, bool portrait)
        {
            if (text == null)
            {
                return;
            }

            if (text.GetComponentInParent<AttackerCombatRatingBadge>(true) != null ||
                text.GetComponentInParent<AttackerArchetypeBadge>(true) != null)
            {
                ConfigureCombatRatingBadgeTextMetrics(text);
                return;
            }

            var preferredSize = GetPreferredFontSize(treatment, portrait);
            var minimumSize = GetMinimumFontSize(treatment, portrait);
            text.fontSize = Mathf.Max(text.fontSize, preferredSize);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Min(minimumSize, Mathf.Max(1, text.fontSize));
            text.resizeTextMaxSize = Mathf.Max(preferredSize, text.fontSize);

            if (text.GetComponentInParent<Button>(true) != null)
            {
                // Button labels should remain a single bold line. FarmUiTextPadding performs a
                // bounded font-size fit once the button's actual padded width is known.
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }

            if (treatment == TextTreatment.Hud || treatment == TextTreatment.Badge)
            {
                // Numeric HUD values stay on one centred line, shrinking only when a value
                // genuinely needs the room inside its plaque.
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }

            // A common font and preferred size must never be allowed to escape its card. Let
            // uGUI wrap and use best-fit first; if the available rectangle is still too small,
            // clip the final line rather than painting into a neighbouring button or panel.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>
        /// The combat-rating star is intentionally much smaller than a regular HUD plaque.
        /// Its two labels have a dedicated clipped safe area, so they need a compact best-fit
        /// range instead of inheriting the ordinary badge minimum that made them spill outside
        /// the star on narrow attacker cards.
        /// </summary>
        private static void ConfigureCombatRatingBadgeTextMetrics(Text text)
        {
            text.fontSize = Mathf.Clamp(text.fontSize, 7, 14);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Min(7, text.fontSize);
            text.resizeTextMaxSize = Mathf.Max(7, text.fontSize);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
        }

        private static int GetPreferredFontSize(TextTreatment treatment, bool portrait)
        {
            return treatment switch
            {
                TextTreatment.Button => portrait ? PortraitButtonFontSize : LandscapeButtonFontSize,
                TextTreatment.Hud => portrait ? PortraitHudFontSize : LandscapeHudFontSize,
                TextTreatment.Badge => portrait ? PortraitBadgeFontSize : LandscapeBadgeFontSize,
                TextTreatment.Heading => portrait ? PortraitHeadingFontSize : LandscapeHeadingFontSize,
                TextTreatment.Alert => portrait ? PortraitAlertFontSize : LandscapeAlertFontSize,
                _ => portrait ? PortraitBodyFontSize : LandscapeBodyFontSize
            };
        }

        private static int GetMinimumFontSize(TextTreatment treatment, bool portrait)
        {
            if (portrait)
            {
                return treatment switch
                {
                    TextTreatment.Heading => 30,
                    TextTreatment.Button => 24,
                    TextTreatment.Hud => 22,
                    TextTreatment.Alert => 24,
                    TextTreatment.Badge => 18,
                    _ => 19
                };
            }

            return treatment switch
            {
                TextTreatment.Heading => 24,
                TextTreatment.Button => 18,
                TextTreatment.Hud => 18,
                TextTreatment.Alert => 20,
                TextTreatment.Badge => 16,
                _ => 16
            };
        }

        private static bool ShouldWrapText(Text text, TextTreatment treatment)
        {
            var name = text.gameObject.name.ToLowerInvariant();
            if (name == "pageheading" || name == "cardtext")
            {
                return true;
            }

            if (treatment == TextTreatment.Alert)
            {
                return true;
            }

            if (treatment != TextTreatment.Body)
            {
                return false;
            }

            return name.Contains("description") ||
                   name.Contains("details") ||
                   name.Contains("instruction") ||
                   name.Contains("guide") ||
                   name.Contains("body") ||
                   name.Contains("copy");
        }

        private static TextTreatment ResolveTextTreatment(Text text)
        {
            // The three pending-upgrade choices are buttons, but their card copy is explanatory
            // text rather than a one-line button label. Treat it as body copy so it can wrap
            // inside the larger portrait card instead of overflowing or becoming unreadably small.
            if (text.gameObject.name == "CardText")
            {
                return TextTreatment.Body;
            }

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

        private void EnsureHudBadge(Text text, Sprite outlineSprite, Sprite buttonSprite)
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
            badge.sprite = buttonSprite;
            badge.type = buttonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            badge.fillCenter = true;
            badge.preserveAspect = false;
            badge.color = buttonSprite != null ? Color.white : HudPanelSurface;
            if (buttonSprite != null)
            {
                RemoveFrameDecorations(badge.transform);
                var badgeOutline = badge.GetComponent<FarmUiThinWoodOutline>();
                if (badgeOutline != null)
                {
                    RemoveDirectThinWoodOutline(badgeOutline);
                }
            }
            else
            {
                FarmUiThinWoodOutline.EnsureFrame(badge, outlineSprite);
            }

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
            ConfigureReadableTextMetrics(text, TextTreatment.Hud, MobileUiLayoutController.UsesPortraitLayout(text.transform));
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

        /// <summary>
        /// Cards are the readable, expandable surfaces in the new farm UI. Compact HUD and
        /// world-art images deliberately keep their specialised presentation instead.
        /// </summary>
        private static bool ShouldUseParchmentCard(Image image)
        {
            if (image == null || !ShouldUsePanelSurface(image))
            {
                return false;
            }

            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("panel") ||
                   name.Contains("card") ||
                   name.Contains("details") ||
                   name.Contains("window") ||
                   name.Contains("selection") ||
                   name.Contains("queue") ||
                    name.Contains("scrollview");
        }

        /// <summary>
        /// Identifies the readable inner surfaces that own absolute-positioned labels.  Their
        /// text receives a modest inset through <see cref="FarmUiTextPadding"/>, while artwork,
        /// health bars, and scroll viewports keep their specialised bounds unchanged.
        /// </summary>
        private static bool IsTextContentSurface(Image image)
        {
            if (image == null || image.GetComponent<Button>() != null || image.GetComponent<Mask>() != null)
            {
                return false;
            }

            var name = image.gameObject.name.ToLowerInvariant();
            return name.Contains("panel") ||
                   name.Contains("card") ||
                   name.Contains("details") ||
                   name.Contains("window") ||
                   name.Contains("selection") ||
                   name.Contains("upgrade") ||
                   name.Contains("message") ||
                   name.Contains("pause") ||
                   name.Contains("win") ||
                   name.Contains("energy");
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
                    name.Contains("energy") ||
                    name.Contains("food") ||
                    name.Contains("experience") ||
                    name.Contains("exp") ||
                    name.Contains("level") ||
                    name.Contains("timer") ||
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
                    name.Contains("section") ||
                    name.Contains("category") ||
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
