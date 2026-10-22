using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace TAD.UI
{
    /// <summary>
    /// Keeps the game's independent uGUI canvases usable on a portrait mobile display while
    /// preserving their authored landscape presentation on desktop and landscape phones.
    /// Attach this to each root Canvas rather than relying on one scene controller: gameplay UI
    /// is spawned from several independent prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class MobileUiLayoutController : MonoBehaviour
    {
        private static readonly Vector2 LandscapeReferenceResolution = new(1920f, 1080f);
        private static readonly Vector2 PortraitReferenceResolution = new(1080f, 1920f);

        [Header("Preview")]
        [SerializeField]
        [Tooltip("Editor-only preview switch. Builds still use the real mobile safe-area orientation.")]
        private bool previewPortraitInEditor;

        [SerializeField]
        [Tooltip("Lets automated UI tests exercise the portrait profile on a desktop runner.")]
        private bool forcePortraitForTests;

        [Header("Portrait layout")]
        [SerializeField, Min(0f)] private float safeInset = 16f;
        [SerializeField, Min(0f)] private float topHudHeight = 192f;

        private CanvasScaler scaler;
        private RectTransform canvasRect;
        private readonly Dictionary<RectTransform, RectTransformState> landscapeLayouts = new();
        private bool hasAppliedLayout;
        private bool lastPortrait;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private int lastRectTransformCount;
        private int pendingPostBuildRefreshes;
        private SafeAreaInsets portraitSafeInsets;
        private bool hasCapturedMainMenuLayout;
        private VerticalLayoutGroup mainMenuLayoutGroup;
        private VerticalLayoutGroupState mainMenuLandscapeLayout;
        private readonly Dictionary<LayoutElement, LayoutElementState> mainMenuLandscapeButtons = new();

        /// <summary>True only when the actual mobile safe area is taller than it is wide.</summary>
        public bool IsPortraitLayout => ResolvePortraitLayout();

        /// <summary>Allows a focused UI test to exercise portrait without changing device state.</summary>
        public void SetPortraitTestOverride(bool enabled)
        {
            forcePortraitForTests = enabled;
            ApplyLayout(force: true);
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            // Run after the other components' Awake/OnEnable methods so a fixed landscape helper
            // cannot overwrite this canvas during its initial construction.
            ApplyLayout(force: true);
        }

        private void OnEnable()
        {
            hasAppliedLayout = false;
        }

        private void LateUpdate()
        {
            var portrait = ResolvePortraitLayout();
            var safeArea = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            var currentRectTransformCount = GetComponentsInChildren<RectTransform>(true).Length;
            if (!hasAppliedLayout || portrait != lastPortrait || safeArea != lastSafeArea || size != lastScreenSize ||
                currentRectTransformCount != lastRectTransformCount)
            {
                ApplyLayout(force: true);
                return;
            }

            // Several HUD sources create their decorative frames or dynamic card fields during
            // their own first frame. Reapply only for the first few portrait frames so those new
            // children inherit the profile without continuously fighting runtime layout code.
            if (portrait && pendingPostBuildRefreshes > 0)
            {
                pendingPostBuildRefreshes--;
                ApplyLayout(force: true);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }
#endif

        /// <summary>
        /// Resolves the profile for a UI element. This deliberately reads the nearest root canvas
        /// controller instead of global screen state so an editor/test preview remains deterministic.
        /// </summary>
        public static bool UsesPortraitLayout(Transform element)
        {
            if (element == null)
            {
                return false;
            }

            var controller = element.GetComponent<MobileUiLayoutController>() ??
                             element.GetComponentInParent<MobileUiLayoutController>(true);
            return controller != null && controller.IsPortraitLayout;
        }

        /// <summary>Explicit refresh point for presenters that build their card contents at runtime.</summary>
        public void RefreshLayout()
        {
            ApplyLayout(force: true);
        }

        /// <summary>
        /// Returns the unobstructed content rectangle in canvas coordinates. Runtime menus use
        /// this instead of assuming that a fixed margin is enough for a notch or camera cutout.
        /// </summary>
        public Rect GetSafeContentRect(float additionalInset = 0f)
        {
            ResolveReferences();
            if (canvasRect == null)
            {
                return Rect.zero;
            }

            var insets = ResolvePortraitLayout() ? ResolvePortraitSafeInsets() : default;
            var rect = canvasRect.rect;
            return Rect.MinMaxRect(
                rect.xMin + insets.Left + additionalInset,
                rect.yMin + insets.Bottom + additionalInset,
                rect.xMax - insets.Right - additionalInset,
                rect.yMax - insets.Top - additionalInset);
        }

        private void ResolveReferences()
        {
            scaler ??= GetComponent<CanvasScaler>();
            canvasRect ??= transform as RectTransform;
        }

        private bool ResolvePortraitLayout()
        {
            if (forcePortraitForTests)
            {
                return true;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && previewPortraitInEditor)
            {
                return true;
            }
#endif

            if (!Application.isMobilePlatform)
            {
                return false;
            }

            var safeArea = Screen.safeArea;
            return safeArea.height > safeArea.width;
        }

        private void ApplyLayout(bool force)
        {
            ResolveReferences();
            var portrait = ResolvePortraitLayout();
            var safeArea = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            var profileChanged = !hasAppliedLayout || portrait != lastPortrait ||
                                 safeArea != lastSafeArea || size != lastScreenSize;
            if (!force && hasAppliedLayout && portrait == lastPortrait && safeArea == lastSafeArea && size == lastScreenSize)
            {
                return;
            }

            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = portrait ? PortraitReferenceResolution : LandscapeReferenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = portrait ? 0f : 0.5f;
            }

            if (portrait)
            {
                CaptureLandscapeLayouts();
                ApplyPortraitLayout();
                if (profileChanged)
                {
                    pendingPostBuildRefreshes = 3;
                }
            }
            else
            {
                RestoreLandscapeLayouts();
                RestoreLandscapeUpgradeChoices();
                RestoreMainMenuLayout();
                LayoutPlayerEnergyMenu(portrait: false);
                pendingPostBuildRefreshes = 0;
            }

            ApplyResponsiveTypography(portrait);

            lastPortrait = portrait;
            lastSafeArea = safeArea;
            lastScreenSize = size;
            lastRectTransformCount = GetComponentsInChildren<RectTransform>(true).Length;
            hasAppliedLayout = true;
        }

        private void ApplyPortraitLayout()
        {
            // The statements below are intentionally semantic/name based. The existing UI is
            // prefab-driven and several canvases are created at runtime, so this can improve every
            // level without replacing bindings or creating a duplicate set of controls.
            portraitSafeInsets = ResolvePortraitSafeInsets();
            LayoutMainMenu();
            LayoutPlayerEnergyMenu(portrait: true);
            LayoutFoodStoreHud();
            LayoutResourceBanks();
            LayoutTopControls();
            LayoutAttackerSelection();
            LayoutAttackerQueue();
            LayoutDetailsCards();
            LayoutFarmHandPlacementControls();
            LayoutGenericOverlayCard();
        }

        private void ApplyResponsiveTypography(bool portrait)
        {
            var skin = GetComponent<FarmUiSkin>() ?? GetComponentInParent<FarmUiSkin>();
            skin?.ApplyResponsiveTypography(portrait);
        }

        private void LayoutMainMenu()
        {
            var panel = FindRect("MainMenuPanel");
            if (panel == null || canvasRect == null)
            {
                return;
            }

            var safe = portraitSafeInsets;
            var availableWidth = Mathf.Max(0f, canvasRect.rect.width - safe.Left - safe.Right - 48f);
            var availableHeight = Mathf.Max(0f, canvasRect.rect.height - safe.Top - safe.Bottom - 72f);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(
                (safe.Left - safe.Right) * 0.5f,
                (safe.Bottom - safe.Top) * 0.5f);
            panel.sizeDelta = new Vector2(
                Mathf.Min(880f, availableWidth),
                // Keep enough breathing room for large phone-sized targets while preserving
                // more of the illustrated farm scene around the focused menu card.
                Mathf.Min(1080f, availableHeight));

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                CaptureMainMenuLayout(layout);
                layout.padding = new RectOffset(54, 54, 82, 66);
                layout.spacing = 16f;
            }

            foreach (var button in panel.GetComponentsInChildren<Button>(false))
            {
                var element = button.GetComponent<LayoutElement>();
                if (element == null)
                {
                    continue;
                }

                CaptureMainMenuButton(element);
                // The bold farm display font needs more vertical timber around it than the old
                // compact profile provided. This keeps menu labels clear of the plank edges on
                // narrow portrait phones as well as meeting a comfortable touch target size.
                element.minHeight = Mathf.Max(element.minHeight, 92f);
                element.preferredHeight = Mathf.Max(element.preferredHeight, 92f);
                FarmUiSkin.EnsureReadableWoodButtonLayout(button, 92f);
            }
        }

        private void LayoutPlayerEnergyMenu(bool portrait)
        {
            var panel = FindRect("PlayerEnergyMenuPanel");
            if (panel == null)
            {
                return;
            }

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            var timer = panel.Find("NextEnergyText")?.GetComponent<LayoutElement>();
            var informationButton = panel.Find("EnergyInfoButton") as RectTransform;

            if (!portrait)
            {
                if (layout != null)
                {
                    layout.padding = new RectOffset(22, 62, 13, 10);
                }

                if (timer != null)
                {
                    timer.preferredHeight = 30f;
                }

                if (informationButton != null)
                {
                    informationButton.anchorMin = informationButton.anchorMax = new Vector2(1f, 0.5f);
                    informationButton.pivot = new Vector2(1f, 0.5f);
                    informationButton.anchoredPosition = new Vector2(-13f, 0f);
                    informationButton.sizeDelta = new Vector2(44f, 44f);
                }

                return;
            }

            var safeContent = GetSafeContentRect(20f);
            SetTopCenter(
                panel,
                0f,
                portraitSafeInsets.Top + 20f,
                Mathf.Min(460f, safeContent.width),
                120f);

            if (layout != null)
            {
                // Leave a clear label column next to the deliberately large phone touch target.
                layout.padding = new RectOffset(22, 104, 14, 10);
            }

            if (timer != null)
            {
                timer.preferredHeight = 40f;
            }

            if (informationButton != null)
            {
                informationButton.anchorMin = informationButton.anchorMax = new Vector2(1f, 0.5f);
                informationButton.pivot = new Vector2(1f, 0.5f);
                informationButton.anchoredPosition = new Vector2(-18f, 0f);
                informationButton.sizeDelta = new Vector2(72f, 72f);
            }
        }

        private void LayoutFoodStoreHud()
        {
            var foodBar = FindRect("TopCentreFoodBar");
            if (foodBar == null)
            {
                return;
            }

            var safe = portraitSafeInsets;
            SetTopStretch(foodBar, safe.Top + 76f, 72f, Mathf.Max(204f, Mathf.Max(safe.Left, safe.Right) + 170f));
        }

        private void LayoutResourceBanks()
        {
            // FarmUiSkin may wrap these texts in content-sized plaque objects after this controller
            // wakes. Target that wrapper when it exists and otherwise target the original text.
            var safe = portraitSafeInsets;
            SetTopLeft(FindHudRect("AttackerGoldText"), safe.Left, safe.Top + 10f, 196f, 56f);
            SetTopLeft(FindHudRect("AttackerIncomeText"), safe.Left, safe.Top + 76f, 196f, 54f);
            SetTopRight(FindHudRect("DefenderGoldText"), safe.Right, safe.Top + 10f, 196f, 56f);
        }

        private void LayoutTopControls()
        {
            var safe = portraitSafeInsets;
            SetTopCenter(FindRect("PauseButton"), -56f, safe.Top + 10f, 64f, 58f);
            SetTopCenter(FindRect("PlayButton"), 56f, safe.Top + 10f, 64f, 58f);

            var dayHud = FindRect("DayCycleHud");
            if (dayHud != null)
            {
                SetTopCenter(dayHud, 0f, safe.Top + 178f, 420f, 92f);
            }
        }

        private void LayoutAttackerSelection()
        {
            if (GetComponentInChildren<AttackerSelectionPanel>(true) == null)
            {
                return;
            }

            var scrollView = FindRect("ScrollView");
            if (scrollView == null)
            {
                return;
            }

            // Keep the card tray as a horizontal, thumb-reachable strip. It becomes taller than
            // landscape so card artwork and tier badges remain legible on narrow screens.
            var safe = portraitSafeInsets;
            scrollView.anchorMin = new Vector2(0f, 0f);
            scrollView.anchorMax = new Vector2(1f, 0f);
            scrollView.pivot = new Vector2(0.5f, 0f);
            scrollView.offsetMin = new Vector2(safe.Left, safe.Bottom);
            scrollView.offsetMax = new Vector2(-safe.Right, 330f);
        }

        private void LayoutAttackerQueue()
        {
            if (GetComponentInChildren<AttackerQueuePanel>(true) == null)
            {
                return;
            }

            var scrollView = FindRect("ScrollView");
            var heading = FindRect("Heading");
            var safe = portraitSafeInsets;
            var hudHeight = Mathf.Max(topHudHeight, 238f);
            if (scrollView != null)
            {
                scrollView.anchorMin = new Vector2(0f, 1f);
                scrollView.anchorMax = new Vector2(0f, 1f);
                scrollView.pivot = new Vector2(0f, 1f);
                scrollView.anchoredPosition = new Vector2(safe.Left, -(hudHeight + safe.Top));
                scrollView.sizeDelta = new Vector2(172f, 900f);
            }

            if (heading != null)
            {
                heading.anchorMin = new Vector2(0f, 1f);
                heading.anchorMax = new Vector2(0f, 1f);
                heading.pivot = new Vector2(0f, 1f);
                heading.anchoredPosition = new Vector2(safe.Left, -(hudHeight - 28f + safe.Top));
            }
        }

        private void LayoutDetailsCards()
        {
            var safe = portraitSafeInsets;
            var hudHeight = Mathf.Max(topHudHeight, 238f);
            var availableWidth = canvasRect != null
                ? Mathf.Max(420f, canvasRect.rect.width - safe.Left - safe.Right - 210f)
                : 520f;
            var cardWidth = Mathf.Min(520f, availableWidth);
            var attackerPanel = GetComponentInChildren<AttackerDetailsPanel>(true);
            if (attackerPanel != null)
            {
                var card = FindRect("Card");
                if (card != null)
                {
                    SetTopRight(card, safe.Right, hudHeight + safe.Top, cardWidth, card.rect.height);
                }
            }

            var defenderPanel = GetComponentInChildren<DefenderDetailsPanel>(true);
            if (defenderPanel != null)
            {
                var card = FindRect("Card");
                if (card != null)
                {
                    SetTopRight(card, safe.Right, hudHeight + safe.Top, cardWidth, card.rect.height);
                }
            }
        }

        private void LayoutFarmHandPlacementControls()
        {
            if (FindRect("BuyButton") == null && FindRect("ConfirmButton") == null && FindRect("CancelButton") == null)
            {
                return;
            }

            var safe = portraitSafeInsets;
            SetBottomCenter(FindRect("BuyButton"), -230f, safe.Bottom + 22f, 276f, 82f);
            SetBottomCenter(FindRect("ConfirmButton"), 118f, safe.Bottom + 22f, 138f, 82f);
            SetBottomCenter(FindRect("CancelButton"), 286f, safe.Bottom + 22f, 138f, 82f);
        }

        private void LayoutGenericOverlayCard()
        {
            // Keep regular modal cards centred and within a safe portrait content column. Overlay
            // builders remain responsible for their own text and scroll-content sizing.
            // Settings and the upgrade book own more detailed, scroll-aware portrait profiles.
            // Do not let the generic modal pass override them after they have positioned themselves.
            var settingsPanel = FindRect("SettingsPanel");
            var upgradesPanel = FindRect("PlayerUpgradesPanel");
            if ((settingsPanel != null && settingsPanel.gameObject.activeInHierarchy) ||
                (upgradesPanel != null && upgradesPanel.gameObject.activeInHierarchy))
            {
                return;
            }

            if (GetComponentInChildren<ModalOverlay>(true) == null &&
                GetComponentInChildren<PauseOverlay>(true) == null &&
                GetComponentInChildren<AttackerWinOverlay>(true) == null &&
                GetComponentInChildren<DefenderWinOverlay>(true) == null &&
                FindRect("UpgradeChoicePanel") == null &&
                FindRect("PlayerEnergyPanel") == null &&
                FindRect("EnergyPanel") == null &&
                FindRect("RestartRoleChoicePanel") == null)
            {
                return;
            }

            var card = FindFirstRect(
                "SettingsPanel",
                "PlayerUpgradesPanel",
                "UpgradeChoicePanel",
                "PlayerEnergyPanel",
                "EnergyPanel",
                "RestartRoleChoicePanel",
                "Panel",
                "Card");
            if (card == null)
            {
                return;
            }

            var safeContent = GetSafeContentRect(20f);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = safeContent.center;
            var currentHeight = Mathf.Min(card.rect.height, Mathf.Min(1520f, safeContent.height));
            var isUpgradeChoice = card.name == "UpgradeChoicePanel";
            if (isUpgradeChoice)
            {
                currentHeight = Mathf.Min(
                    Mathf.Max(currentHeight, 1260f),
                    Mathf.Min(1520f, safeContent.height));
                ConfigurePortraitUpgradeChoices(card);
            }

            card.sizeDelta = new Vector2(
                Mathf.Min(Mathf.Max(card.rect.width, 520f), Mathf.Min(920f, safeContent.width)),
                currentHeight);
        }

        private static void ConfigurePortraitUpgradeChoices(RectTransform card)
        {
            var cards = card.Find("Cards") as RectTransform;
            if (cards == null)
            {
                return;
            }

            var horizontal = cards.GetComponent<HorizontalLayoutGroup>();
            if (horizontal != null)
            {
                horizontal.enabled = false;
            }

            var element = cards.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.preferredHeight = 880f;
                element.flexibleHeight = 1f;
            }

            // Desktop cards rely on a horizontal row to supply their height. Portrait stacks
            // them, so each direct choice needs its own touch-friendly space for wrapped copy.
            var top = 4f;
            foreach (Transform choice in cards)
            {
                var choiceElement = choice.GetComponent<LayoutElement>();
                var choiceRect = choice as RectTransform;
                if (choiceRect == null)
                {
                    continue;
                }

                choiceRect.anchorMin = new Vector2(0f, 1f);
                choiceRect.anchorMax = new Vector2(1f, 1f);
                choiceRect.pivot = new Vector2(0.5f, 1f);
                choiceRect.offsetMin = new Vector2(8f, -top - 280f);
                choiceRect.offsetMax = new Vector2(-8f, -top);
                top += 294f;

                if (choiceElement != null)
                {
                    choiceElement.ignoreLayout = true;
                    choiceElement.minHeight = 280f;
                    choiceElement.preferredHeight = 280f;
                    choiceElement.flexibleHeight = 0f;
                }
            }
        }

        private RectTransform FindRect(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var rect in GetComponentsInChildren<RectTransform>(true))
            {
                if (rect != canvasRect && rect.name == name)
                {
                    return rect;
                }
            }

            return null;
        }

        private RectTransform FindFirstRect(params string[] names)
        {
            foreach (var name in names)
            {
                var result = FindRect(name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private SafeAreaInsets ResolvePortraitSafeInsets()
        {
            var screenWidth = Mathf.Max(1, Screen.width);
            var screenHeight = Mathf.Max(1, Screen.height);
            var safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                safeArea = new Rect(0f, 0f, screenWidth, screenHeight);
            }

            var canvasWidth = canvasRect != null && canvasRect.rect.width > 0f
                ? canvasRect.rect.width
                : PortraitReferenceResolution.x;
            var canvasHeight = canvasRect != null && canvasRect.rect.height > 0f
                ? canvasRect.rect.height
                : PortraitReferenceResolution.y;
            var horizontalScale = canvasWidth / screenWidth;
            var verticalScale = canvasHeight / screenHeight;
            return new SafeAreaInsets(
                safeArea.xMin * horizontalScale + safeInset,
                (screenWidth - safeArea.xMax) * horizontalScale + safeInset,
                (screenHeight - safeArea.yMax) * verticalScale + safeInset,
                safeArea.yMin * verticalScale + safeInset);
        }

        private void CaptureMainMenuLayout(VerticalLayoutGroup layout)
        {
            if (hasCapturedMainMenuLayout || layout == null)
            {
                return;
            }

            hasCapturedMainMenuLayout = true;
            mainMenuLayoutGroup = layout;
            mainMenuLandscapeLayout = VerticalLayoutGroupState.Capture(layout);
        }

        private void CaptureMainMenuButton(LayoutElement element)
        {
            if (element != null && !mainMenuLandscapeButtons.ContainsKey(element))
            {
                mainMenuLandscapeButtons.Add(element, LayoutElementState.Capture(element));
            }
        }

        private void RestoreMainMenuLayout()
        {
            if (hasCapturedMainMenuLayout && mainMenuLayoutGroup != null)
            {
                mainMenuLandscapeLayout.Apply(mainMenuLayoutGroup);
            }

            foreach (var pair in mainMenuLandscapeButtons)
            {
                if (pair.Key != null)
                {
                    pair.Value.Apply(pair.Key);
                }
            }
        }

        private void CaptureLandscapeLayouts()
        {
            foreach (var rect in GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null || rect == canvasRect || landscapeLayouts.ContainsKey(rect))
                {
                    continue;
                }

                landscapeLayouts.Add(rect, RectTransformState.Capture(rect));
            }
        }

        private void RestoreLandscapeLayouts()
        {
            var stale = new List<RectTransform>();
            foreach (var pair in landscapeLayouts)
            {
                if (pair.Key == null)
                {
                    stale.Add(pair.Key);
                    continue;
                }

                pair.Value.Apply(pair.Key);
            }

            foreach (var rect in stale)
            {
                landscapeLayouts.Remove(rect);
            }
        }

        private void RestoreLandscapeUpgradeChoices()
        {
            var card = FindRect("UpgradeChoicePanel");
            var cards = card != null ? card.Find("Cards") as RectTransform : null;
            if (cards == null)
            {
                return;
            }

            var horizontal = cards.GetComponent<HorizontalLayoutGroup>();
            if (horizontal != null)
            {
                horizontal.enabled = true;
            }

            var vertical = cards.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
            {
                vertical.enabled = false;
            }

            var element = cards.GetComponent<LayoutElement>();
            if (element != null)
            {
                element.preferredHeight = 290f;
            }

            foreach (Transform choice in cards)
            {
                var choiceElement = choice.GetComponent<LayoutElement>();
                if (choiceElement == null)
                {
                    continue;
                }

                choiceElement.minHeight = -1f;
                choiceElement.preferredHeight = -1f;
                choiceElement.flexibleHeight = -1f;
                choiceElement.ignoreLayout = false;
            }
        }

        private RectTransform FindHudRect(string textName)
        {
            var wrapped = FindRect("FarmUiBadge_" + textName);
            if (wrapped != null)
            {
                return wrapped;
            }

            var text = FindRect(textName);
            if (text == null)
            {
                return null;
            }

            var parent = text.parent as RectTransform;
            return parent != null && parent.name.StartsWith("FarmUiBadge_") ? parent : text;
        }

        private static void SetTopStretch(RectTransform target, float top, float height, float horizontalInset)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.offsetMin = new Vector2(horizontalInset, -top - height);
            target.offsetMax = new Vector2(-horizontalInset, -top);
        }

        private static void SetTopLeft(RectTransform target, float left, float top, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(left, -top);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopRight(RectTransform target, float right, float top, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(1f, 1f);
            target.anchoredPosition = new Vector2(-right, -top);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopCenter(RectTransform target, float x, float top, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.anchoredPosition = new Vector2(x, -top);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void SetBottomCenter(RectTransform target, float x, float bottom, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 0f);
            target.pivot = new Vector2(0.5f, 0f);
            target.anchoredPosition = new Vector2(x, bottom);
            target.sizeDelta = new Vector2(width, height);
        }

        private readonly struct RectTransformState
        {
            private readonly Vector2 anchorMin;
            private readonly Vector2 anchorMax;
            private readonly Vector2 pivot;
            private readonly Vector2 anchoredPosition;
            private readonly Vector2 sizeDelta;
            private readonly Vector2 offsetMin;
            private readonly Vector2 offsetMax;

            private RectTransformState(RectTransform target)
            {
                anchorMin = target.anchorMin;
                anchorMax = target.anchorMax;
                pivot = target.pivot;
                anchoredPosition = target.anchoredPosition;
                sizeDelta = target.sizeDelta;
                offsetMin = target.offsetMin;
                offsetMax = target.offsetMax;
            }

            public static RectTransformState Capture(RectTransform target)
            {
                return new RectTransformState(target);
            }

            public void Apply(RectTransform target)
            {
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.pivot = pivot;
                target.anchoredPosition = anchoredPosition;
                target.sizeDelta = sizeDelta;
                target.offsetMin = offsetMin;
                target.offsetMax = offsetMax;
            }
        }

        private readonly struct SafeAreaInsets
        {
            public SafeAreaInsets(float left, float right, float top, float bottom)
            {
                Left = Mathf.Max(0f, left);
                Right = Mathf.Max(0f, right);
                Top = Mathf.Max(0f, top);
                Bottom = Mathf.Max(0f, bottom);
            }

            public float Left { get; }
            public float Right { get; }
            public float Top { get; }
            public float Bottom { get; }
        }

        private readonly struct VerticalLayoutGroupState
        {
            private readonly RectOffset padding;
            private readonly float spacing;

            private VerticalLayoutGroupState(VerticalLayoutGroup layout)
            {
                padding = new RectOffset(
                    layout.padding.left,
                    layout.padding.right,
                    layout.padding.top,
                    layout.padding.bottom);
                spacing = layout.spacing;
            }

            public static VerticalLayoutGroupState Capture(VerticalLayoutGroup layout)
            {
                return new VerticalLayoutGroupState(layout);
            }

            public void Apply(VerticalLayoutGroup layout)
            {
                layout.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
                layout.spacing = spacing;
            }
        }

        private readonly struct LayoutElementState
        {
            private readonly float minHeight;
            private readonly float preferredHeight;

            private LayoutElementState(LayoutElement element)
            {
                minHeight = element.minHeight;
                preferredHeight = element.preferredHeight;
            }

            public static LayoutElementState Capture(LayoutElement element)
            {
                return new LayoutElementState(element);
            }

            public void Apply(LayoutElement element)
            {
                element.minHeight = minHeight;
                element.preferredHeight = preferredHeight;
            }
        }
    }
}
