using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    /// <summary>
    /// Presents an already-persisted player match reward without owning, recalculating, or
    /// mutating progression. It lives under a terminal overlay's canvas so the player can watch
    /// their account bar fill while the match remains paused.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMatchExperiencePresentation : MonoBehaviour
    {
        private const float AnimationExperiencePerSecond = 110f;
        private const float ProgressEpsilon = 0.001f;
        private const float BarHorizontalInset = 58f;
        private const float BarBottomInset = 34f;
        private const float BarHeight = 232f;
        private const float StarBadgeWidth = 188f;
        private const float BarHorizontalFrameInset = 26f;
        private const float BarVerticalFrameInset = 18f;

        private static readonly Color InkBrown = new(0.24f, 0.105f, 0.03f, 1f);
        private static readonly Color Parchment = new(0.94f, 0.86f, 0.66f, 0.98f);

        private static Sprite fallbackFillSprite;

        private GameObject resultsRoot;
        private GameObject levelUpModalRoot;
        private UnityEngine.UI.Image progressFill;
        private UnityEngine.UI.Text earnedText;
        private UnityEngine.UI.Text startText;
        private UnityEngine.UI.Text currentText;
        private UnityEngine.UI.Text progressText;
        private UnityEngine.UI.Text starLevelText;
        private UnityEngine.UI.Text levelUpTitleText;
        private UnityEngine.UI.Text levelUpDescriptionText;
        private UnityEngine.UI.Text levelUpFutureChoicesText;
        private UnityEngine.UI.Button skipButton;
        private UnityEngine.UI.Button levelUpContinueButton;
        private UnityEngine.UI.Button levelUpButtonTemplate;
        private PlayerUpgradeChoiceOverlay upgradeChoiceOverlay;
        private CombatRatingStarGraphic playerLevelStarGraphic;
        private Font font;
        private Sprite outlineSprite;
        private Sprite buttonSprite;
        private Sprite cardSprite;
        private Sprite statusBarFrameSprite;
        private Sprite statusBarFillSprite;

        private PlayerMatchExperienceReward reward;
        private int displayedLevel;
        private int pendingLevel;
        private float displayedExperience;
        private float remainingAwardedExperience;
        private bool isAnimating;
        private bool isWaitingForLevelUp;
        private bool skipRequested;
        private int paletteRepairFrames;
        private Transform terminalPanel;
        private bool terminalPanelExpandedForMatchExperience;

        [SerializeField]
        [Tooltip("Reusable card surface reserved for future player upgrade choices.")]
        private GameObject levelUpCardPrefab;

        /// <summary>True while experience is still travelling through the account level bar.</summary>
        public bool IsAnimating => isAnimating;

        /// <summary>True while a level-up acknowledgement pauses the bar animation.</summary>
        public bool IsWaitingForLevelUp => isWaitingForLevelUp;

        /// <summary>The account level currently shown in the star badge.</summary>
        public int DisplayedLevel => displayedLevel;

        /// <summary>The current level-local experience amount shown in the bar.</summary>
        public float DisplayedExperience => displayedExperience;

        /// <summary>
        /// Lets the UI asset builder assign the shared card surface that future three-choice
        /// player upgrades will reuse. Runtime-created fallback cards keep older scenes safe.
        /// </summary>
        public void ConfigureLevelUpCardTemplate(GameObject template)
        {
            levelUpCardPrefab = template;
        }

        /// <summary>
        /// Opens the lower-screen results presentation from an immutable, already-saved match
        /// reward. Returns false if this component is not attached to a terminal overlay canvas.
        /// </summary>
        public bool Present(PlayerMatchExperienceReward matchReward, UnityEngine.UI.Button buttonTemplate)
        {
            if (!EnsureBuilt(buttonTemplate))
            {
                return false;
            }

            reward = matchReward;
            displayedLevel = Mathf.Max(PlayerLevelProgression.StartingLevel, reward.StartingLevel);
            displayedExperience = Mathf.Clamp(
                Mathf.Max(0f, reward.StartingExperience),
                0f,
                PlayerLevelProgression.GetExperienceRequiredForNextLevel(displayedLevel));
            remainingAwardedExperience = Mathf.Max(0f, reward.AwardedExperience);
            pendingLevel = 0;
            skipRequested = false;
            isWaitingForLevelUp = false;
            isAnimating = remainingAwardedExperience > ProgressEpsilon || displayedLevel < reward.ResultingLevel;
            paletteRepairFrames = 3;

            resultsRoot.SetActive(true);
            resultsRoot.transform.SetAsLastSibling();
            if (levelUpModalRoot != null)
            {
                levelUpModalRoot.SetActive(false);
            }

            if (upgradeChoiceOverlay != null)
            {
                upgradeChoiceOverlay.gameObject.SetActive(false);
            }

            earnedText.text = BuildEarnedText(reward);
            startText.text = $"START: LV {reward.StartingLevel}  {reward.StartingExperience:0.#} / " +
                             $"{Mathf.Max(0f, reward.StartingExperienceRequired):0.#} XP";
            RenderProgress();
            SetSkipButtonVisible(isAnimating);

            if (!isAnimating)
            {
                CompleteAnimation();
            }

            return true;
        }

        /// <summary>Completes bar movement instantly, while preserving any level-up acknowledgements.</summary>
        public void SkipAnimation()
        {
            if (!isAnimating || isWaitingForLevelUp)
            {
                return;
            }

            skipRequested = true;
            SetSkipButtonVisible(false);
            AdvanceAnimation(float.MaxValue);
        }

        /// <summary>Hides the presentation when its owner is dismissed or changes scene.</summary>
        public void Dismiss()
        {
            isAnimating = false;
            isWaitingForLevelUp = false;
            skipRequested = false;
            pendingLevel = 0;
            paletteRepairFrames = 0;
            if (resultsRoot != null)
            {
                resultsRoot.SetActive(false);
            }

            if (levelUpModalRoot != null)
            {
                levelUpModalRoot.SetActive(false);
            }

            if (upgradeChoiceOverlay != null)
            {
                upgradeChoiceOverlay.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (!isAnimating || isWaitingForLevelUp || skipRequested)
            {
                return;
            }

            AdvanceAnimation(AnimationExperiencePerSecond * Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            // FarmUiSkin performs one delayed startup pass over its canvas. Repair our explicit
            // result colours for the next few frames so this dynamic results view remains
            // readable even if it was opened during that pass.
            if (paletteRepairFrames <= 0 || resultsRoot == null || !resultsRoot.activeInHierarchy)
            {
                return;
            }

            ApplyPresentationPalette();
            paletteRepairFrames--;
        }

        private bool EnsureBuilt(UnityEngine.UI.Button buttonTemplate)
        {
            if (resultsRoot != null)
            {
                return true;
            }

            if (transform is not RectTransform || GetComponent<Canvas>() == null)
            {
                return false;
            }

            font = ResolveFont(buttonTemplate);
            var skin = GetComponent<FarmUiSkin>() ?? GetComponentInParent<FarmUiSkin>();
            outlineSprite = skin?.ThinWoodOutlineSprite;
            buttonSprite = skin?.WoodPlankButtonSprite;
            cardSprite = skin?.ParchmentCardSprite;
            statusBarFrameSprite = skin?.StatusBarFrameSprite;
            statusBarFillSprite = skin?.StatusBarFillSprite;
            levelUpButtonTemplate = buttonTemplate;
            BuildResultsBar(buttonTemplate);
            BuildLevelUpModal(buttonTemplate);
            Dismiss();
            return resultsRoot != null && progressFill != null && skipButton != null;
        }

        private void BuildResultsBar(UnityEngine.UI.Button buttonTemplate)
        {
            resultsRoot = CreateObject("PlayerExperienceResults", transform, typeof(RectTransform));
            var rootRect = resultsRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.offsetMin = new Vector2(BarHorizontalInset, BarBottomInset);
            rootRect.offsetMax = new Vector2(-BarHorizontalInset, BarBottomInset + BarHeight);

            var summaryParent = ResolveTerminalPanel() ?? resultsRoot.transform;
            var summaryObject = CreateObject(
                "PlayerExperienceSummaryPanel",
                summaryParent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.LayoutElement));
            var summaryImage = summaryObject.GetComponent<UnityEngine.UI.Image>();
            summaryImage.sprite = cardSprite;
            summaryImage.type = cardSprite != null
                ? UnityEngine.UI.Image.Type.Sliced
                : UnityEngine.UI.Image.Type.Simple;
            summaryImage.fillCenter = true;
            summaryImage.color = cardSprite != null ? Color.white : Parchment;
            summaryImage.raycastTarget = false;
            if (cardSprite == null)
            {
                FarmUiThinWoodOutline.EnsureFrame(summaryImage, outlineSprite);
            }
            ConfigureSummaryLayout(summaryObject.GetComponent<RectTransform>(), summaryParent);

            earnedText = CreateText(
                "EarnedText",
                summaryObject.transform,
                38,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                InkBrown);
            SetStretch(earnedText.rectTransform, new Vector2(0f, 0.66f), Vector2.one,
                new Vector2(18f, 0f), new Vector2(-18f, -2f));

            startText = CreateText(
                "StartingExperienceText",
                summaryObject.transform,
                28,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                InkBrown);
            SetStretch(startText.rectTransform, new Vector2(0f, 0.33f), new Vector2(1f, 0.68f),
                new Vector2(18f, 0f), new Vector2(-18f, 0f));

            currentText = CreateText(
                "CurrentExperienceText",
                summaryObject.transform,
                28,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                InkBrown);
            SetStretch(currentText.rectTransform, Vector2.zero, new Vector2(1f, 0.35f),
                new Vector2(18f, 2f), new Vector2(-18f, 0f));

            var barObject = CreateObject(
                "PlayerExperienceBarBackground",
                resultsRoot.transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
            var barRect = barObject.GetComponent<RectTransform>();
            SetStretch(barRect, Vector2.zero, Vector2.one,
                new Vector2(0f, 0f), new Vector2(-(StarBadgeWidth + 22f), 0f));
            var barImage = barObject.GetComponent<UnityEngine.UI.Image>();
            barImage.sprite = statusBarFrameSprite;
            barImage.type = statusBarFrameSprite != null
                ? UnityEngine.UI.Image.Type.Sliced
                : UnityEngine.UI.Image.Type.Simple;
            barImage.fillCenter = true;
            barImage.color = statusBarFrameSprite != null ? Color.white : Color.black;
            barImage.raycastTarget = false;
            if (statusBarFrameSprite == null)
            {
                FarmUiThinWoodOutline.EnsureFrame(barImage, outlineSprite);
            }

            var fillObject = CreateObject(
                "PlayerExperienceFill",
                barObject.transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
            progressFill = fillObject.GetComponent<UnityEngine.UI.Image>();
            progressFill.sprite = statusBarFillSprite != null ? statusBarFillSprite : GetFallbackFillSprite();
            progressFill.type = UnityEngine.UI.Image.Type.Filled;
            progressFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            progressFill.fillCenter = true;
            progressFill.preserveAspect = false;
            progressFill.color = new Color(0.94f, 0.73f, 0.16f, 1f);
            progressFill.raycastTarget = false;
            SetStretch(progressFill.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(BarHorizontalFrameInset, BarVerticalFrameInset),
                new Vector2(-BarHorizontalFrameInset, -BarVerticalFrameInset));

            progressText = CreateText(
                "PlayerExperienceProgressText",
                barObject.transform,
                40,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                InkBrown);
            SetStretch(progressText.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(16f, 4f), new Vector2(-16f, -4f));

            var starObject = CreateObject(
                "PlayerLevelStarBadge",
                resultsRoot.transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(CombatRatingStarGraphic));
            var starRect = starObject.GetComponent<RectTransform>();
            starRect.anchorMin = new Vector2(1f, 0.5f);
            starRect.anchorMax = new Vector2(1f, 0.5f);
            starRect.pivot = new Vector2(1f, 0.5f);
            starRect.anchoredPosition = Vector2.zero;
            starRect.sizeDelta = new Vector2(StarBadgeWidth, 172f);
            playerLevelStarGraphic = starObject.GetComponent<CombatRatingStarGraphic>();
            playerLevelStarGraphic.color = new Color(1f, 0.72f, 0.12f, 0.96f);
            playerLevelStarGraphic.raycastTarget = false;

            starLevelText = CreateText(
                "StarLevelText",
                starObject.transform,
                48,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                InkBrown);
            SetStretch(starLevelText.rectTransform, new Vector2(0.18f, 0.28f), new Vector2(0.82f, 0.68f),
                Vector2.zero, Vector2.zero);

            skipButton = CreateButton(
                "SkipExperienceAnimationButton",
                resultsRoot.transform,
                buttonTemplate,
                "SKIP",
                new Vector2(174f, 54f));
            var skipRect = skipButton.GetComponent<RectTransform>();
            skipRect.anchorMin = new Vector2(1f, 1f);
            skipRect.anchorMax = new Vector2(1f, 1f);
            skipRect.pivot = new Vector2(1f, 1f);
            skipRect.anchoredPosition = new Vector2(0f, 0f);
            skipButton.onClick.AddListener(SkipAnimation);
        }

        private Transform ResolveTerminalPanel()
        {
            if (terminalPanel != null)
            {
                return terminalPanel;
            }

            terminalPanel = transform.Find("AttackerWinPanel") ?? transform.Find("DefenderWinPanel");
            return terminalPanel;
        }

        private void ConfigureSummaryLayout(RectTransform summaryRect, Transform summaryParent)
        {
            if (summaryRect == null)
            {
                return;
            }

            var layoutElement = summaryRect.GetComponent<UnityEngine.UI.LayoutElement>();
            if (summaryParent == resultsRoot.transform)
            {
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = true;
                }

                SetStretch(summaryRect, new Vector2(0f, 0.62f), Vector2.one, Vector2.zero, Vector2.zero);
                return;
            }

            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = false;
                layoutElement.minHeight = 132f;
                layoutElement.preferredHeight = 132f;
                layoutElement.flexibleHeight = 0f;
            }

            // Place the summary immediately after the terminal title and give the shared card
            // enough room for its three rows without crushing the restart/menu/quit controls.
            summaryRect.SetSiblingIndex(Mathf.Min(1, summaryParent.childCount - 1));
            if (summaryParent is RectTransform panelRect)
            {
                if (!terminalPanelExpandedForMatchExperience)
                {
                    panelRect.sizeDelta = new Vector2(
                        Mathf.Max(620f, panelRect.sizeDelta.x),
                        Mathf.Max(570f, panelRect.sizeDelta.y));
                    panelRect.anchoredPosition += Vector2.up * 90f;
                    terminalPanelExpandedForMatchExperience = true;
                }

                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }
        }

        private void BuildLevelUpModal(UnityEngine.UI.Button buttonTemplate)
        {
            levelUpModalRoot = CreateObject(
                "PlayerLevelUpModal",
                transform,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
            var rootRect = levelUpModalRoot.GetComponent<RectTransform>();
            SetStretch(rootRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var blocker = levelUpModalRoot.GetComponent<UnityEngine.UI.Image>();
            blocker.color = new Color(0.015f, 0.035f, 0.06f, 0.72f);
            blocker.raycastTarget = true;

            var cardObject = CreateLevelUpCard(levelUpModalRoot.transform);
            var cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(680f, 380f);
            var cardImage = cardObject.GetComponent<UnityEngine.UI.Image>();
            cardImage.sprite = cardSprite;
            cardImage.type = cardSprite != null
                ? UnityEngine.UI.Image.Type.Sliced
                : UnityEngine.UI.Image.Type.Simple;
            cardImage.fillCenter = true;
            cardImage.color = cardSprite != null ? Color.white : new Color(0.94f, 0.86f, 0.66f, 1f);
            cardImage.raycastTarget = true;
            if (cardSprite == null)
            {
                FarmUiThinWoodOutline.EnsureFrame(cardImage, outlineSprite);
            }

            levelUpTitleText = CreateText(
                "TitleText",
                cardObject.transform,
                52,
                FontStyle.BoldAndItalic,
                TextAnchor.MiddleCenter,
                InkBrown);
            SetStretch(levelUpTitleText.rectTransform, new Vector2(0f, 0.72f), new Vector2(1f, 0.95f),
                new Vector2(34f, 0f), new Vector2(-34f, 0f));

            levelUpDescriptionText = CreateText(
                "DescriptionText",
                cardObject.transform,
                34,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                InkBrown);
            levelUpDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetStretch(levelUpDescriptionText.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.72f),
                new Vector2(42f, 0f), new Vector2(-42f, 0f));

            levelUpFutureChoicesText = CreateText(
                "FutureUpgradesText",
                cardObject.transform,
                26,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                InkBrown);
            levelUpFutureChoicesText.text = "Choose an Attacker, Defender, and Random reward for this level.";
            levelUpFutureChoicesText.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetStretch(levelUpFutureChoicesText.rectTransform, new Vector2(0f, 0.28f), new Vector2(1f, 0.48f),
                new Vector2(48f, 0f), new Vector2(-48f, 0f));

            levelUpContinueButton = CreateButton(
                "ContinueLevelUpButton",
                cardObject.transform,
                buttonTemplate,
                "CHOOSE UPGRADES",
                new Vector2(240f, 58f));
            var continueRect = levelUpContinueButton.GetComponent<RectTransform>();
            continueRect.anchorMin = new Vector2(0.5f, 0f);
            continueRect.anchorMax = new Vector2(0.5f, 0f);
            continueRect.pivot = new Vector2(0.5f, 0f);
            continueRect.anchoredPosition = new Vector2(0f, 30f);
            levelUpContinueButton.onClick.AddListener(OpenPlayerUpgradeChoices);
            levelUpModalRoot.SetActive(false);
        }

        private void AdvanceAnimation(float allowance)
        {
            var safety = 0;
            while (!isWaitingForLevelUp && isAnimating && safety++ < 32)
            {
                var requirement = PlayerLevelProgression.GetExperienceRequiredForNextLevel(displayedLevel);
                var distanceToLevelUp = Mathf.Max(0f, requirement - displayedExperience);
                if (distanceToLevelUp <= ProgressEpsilon && displayedLevel < reward.ResultingLevel)
                {
                    BeginLevelUpAcknowledgement();
                    return;
                }

                if (remainingAwardedExperience <= ProgressEpsilon)
                {
                    CompleteAnimation();
                    return;
                }

                var availableThisStep = skipRequested
                    ? remainingAwardedExperience
                    : Mathf.Max(0f, allowance);
                var movedExperience = Mathf.Min(remainingAwardedExperience, distanceToLevelUp, availableThisStep);
                if (movedExperience <= ProgressEpsilon)
                {
                    return;
                }

                displayedExperience += movedExperience;
                remainingAwardedExperience = Mathf.Max(0f, remainingAwardedExperience - movedExperience);
                allowance = Mathf.Max(0f, allowance - movedExperience);
                RenderProgress();

                if (displayedExperience + ProgressEpsilon >= requirement && displayedLevel < reward.ResultingLevel)
                {
                    displayedExperience = requirement;
                    RenderProgress();
                    BeginLevelUpAcknowledgement();
                    return;
                }

                if (!skipRequested || allowance <= ProgressEpsilon)
                {
                    return;
                }
            }
        }

        private void BeginLevelUpAcknowledgement()
        {
            if (displayedLevel >= reward.ResultingLevel)
            {
                CompleteAnimation();
                return;
            }

            pendingLevel = displayedLevel + 1;
            isWaitingForLevelUp = true;
            paletteRepairFrames = 3;
            SetSkipButtonVisible(false);
            levelUpTitleText.text = "LEVEL UP!";
            levelUpDescriptionText.text = $"You reached Player Level {pendingLevel}.";
            levelUpFutureChoicesText.text = PlayerSettingsService.Instance.PendingPlayerUpgradeChoiceCount > 0
                ? "Your permanent account rewards are ready."
                : "All available rewards for this level have already been collected.";
            SetLevelUpContinueLabel(
                PlayerSettingsService.Instance.PendingPlayerUpgradeChoiceCount > 0
                    ? "CHOOSE UPGRADES"
                    : "CONTINUE");
            levelUpModalRoot.SetActive(true);
            levelUpModalRoot.transform.SetAsLastSibling();
            Select(levelUpContinueButton != null ? levelUpContinueButton.gameObject : null);
        }

        private void ContinueAfterLevelUp()
        {
            if (!isWaitingForLevelUp)
            {
                return;
            }

            isWaitingForLevelUp = false;
            if (levelUpModalRoot != null)
            {
                levelUpModalRoot.SetActive(false);
            }

            displayedLevel = Mathf.Clamp(
                pendingLevel,
                PlayerLevelProgression.StartingLevel,
                Mathf.Max(PlayerLevelProgression.StartingLevel, reward.ResultingLevel));
            displayedExperience = 0f;
            pendingLevel = 0;
            paletteRepairFrames = 3;
            RenderProgress();
            SetSkipButtonVisible(isAnimating && !skipRequested);

            if (skipRequested)
            {
                AdvanceAnimation(float.MaxValue);
            }
            else
            {
                AdvanceAnimation(0f);
            }
        }

        private void OpenPlayerUpgradeChoices()
        {
            if (!isWaitingForLevelUp)
            {
                return;
            }

            var pendingChoices = new System.Collections.Generic.List<PendingPlayerUpgradeChoiceData>();
            PlayerSettingsService.Instance.CopyPendingPlayerUpgradeChoices(pendingLevel, pendingChoices);
            if (pendingChoices.Count == 0)
            {
                ContinueAfterLevelUp();
                return;
            }

            upgradeChoiceOverlay = PlayerUpgradeChoiceOverlay.Show(
                transform,
                levelUpButtonTemplate,
                levelUpContinueButton != null ? levelUpContinueButton.gameObject : null,
                pendingLevel,
                ContinueAfterLevelUp);
        }

        private void SetLevelUpContinueLabel(string label)
        {
            var text = levelUpContinueButton != null
                ? levelUpContinueButton.GetComponentInChildren<UnityEngine.UI.Text>(true)
                : null;
            if (text != null)
            {
                text.text = label;
            }
        }

        private void CompleteAnimation()
        {
            displayedLevel = Mathf.Max(PlayerLevelProgression.StartingLevel, reward.ResultingLevel);
            var requirement = PlayerLevelProgression.GetExperienceRequiredForNextLevel(displayedLevel);
            displayedExperience = Mathf.Clamp(reward.CurrentExperience, 0f, requirement);
            remainingAwardedExperience = 0f;
            isAnimating = false;
            isWaitingForLevelUp = false;
            RenderProgress();
            SetSkipButtonVisible(false);
        }

        private void RenderProgress()
        {
            if (progressFill == null || progressText == null || starLevelText == null)
            {
                return;
            }

            var requirement = PlayerLevelProgression.GetExperienceRequiredForNextLevel(displayedLevel);
            var normalizedProgress = requirement > ProgressEpsilon
                ? Mathf.Clamp01(displayedExperience / requirement)
                : 1f;
            progressFill.fillAmount = normalizedProgress;
            progressText.text = requirement > ProgressEpsilon
                ? $"{displayedExperience:0.#} / {requirement:0.#} XP"
                : "MAX LEVEL";
            starLevelText.text = $"LV\n{displayedLevel}";
            if (currentText != null)
            {
                currentText.text = requirement > ProgressEpsilon
                    ? $"CURRENT: LV {displayedLevel}  {displayedExperience:0.#} / {requirement:0.#} XP"
                    : $"CURRENT: LV {displayedLevel}  MAX LEVEL";
            }

            ApplyPresentationPalette();
        }

        private void SetSkipButtonVisible(bool visible)
        {
            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(visible);
            }
        }

        private void ApplyPresentationPalette()
        {
            if (earnedText != null)
            {
                earnedText.color = InkBrown;
            }

            if (startText != null)
            {
                startText.color = InkBrown;
            }

            if (currentText != null)
            {
                currentText.color = InkBrown;
            }

            if (progressText != null)
            {
                progressText.color = progressFill != null && progressFill.fillAmount <= 0.35f
                    ? new Color(1f, 0.94f, 0.78f, 1f)
                    : InkBrown;
            }

            if (starLevelText != null)
            {
                starLevelText.color = InkBrown;
            }

            if (playerLevelStarGraphic != null)
            {
                playerLevelStarGraphic.color = new Color(1f, 0.72f, 0.12f, 0.96f);
            }

            if (levelUpTitleText != null)
            {
                levelUpTitleText.color = InkBrown;
            }

            if (levelUpDescriptionText != null)
            {
                levelUpDescriptionText.color = InkBrown;
            }

            if (levelUpFutureChoicesText != null)
            {
                levelUpFutureChoicesText.color = InkBrown;
            }
        }

        private static string BuildEarnedText(PlayerMatchExperienceReward matchReward)
        {
            if (matchReward.RewardMultiplier >= 0.999f)
            {
                return $"MATCH XP EARNED: +{matchReward.AwardedExperience:0.#} XP";
            }

            return $"MATCH XP EARNED: +{matchReward.AwardedExperience:0.#} XP " +
                   $"({matchReward.RewardMultiplier:P0} of {matchReward.MatchExperience:0.#})";
        }

        private static GameObject CreateObject(string name, Transform parent, params System.Type[] components)
        {
            var gameObject = new GameObject(name, components);
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private UnityEngine.UI.Text CreateText(
            string name,
            Transform parent,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            var textObject = CreateObject(
                name,
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text));
            var text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
            return text;
        }

        private UnityEngine.UI.Button CreateButton(
            string name,
            Transform parent,
            UnityEngine.UI.Button template,
            string label,
            Vector2 fallbackSize)
        {
            if (template != null)
            {
                var clone = Instantiate(template.gameObject, parent);
                clone.name = name;
                clone.layer = parent.gameObject.layer;
                var button = clone.GetComponent<UnityEngine.UI.Button>();
                if (button != null)
                {
                    var cloneRect = clone.GetComponent<RectTransform>();
                    if (cloneRect != null)
                    {
                        cloneRect.sizeDelta = fallbackSize;
                        cloneRect.localScale = Vector3.one;
                    }

                    button.onClick.RemoveAllListeners();
                    var buttonText = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (buttonText != null)
                    {
                        buttonText.text = label;
                        buttonText.alignment = TextAnchor.MiddleCenter;
                        buttonText.fontSize = Mathf.Max(buttonText.fontSize, 32);
                        buttonText.resizeTextForBestFit = true;
                        buttonText.resizeTextMinSize = Mathf.Max(buttonText.resizeTextMinSize, 14);
                        buttonText.resizeTextMaxSize = Mathf.Max(buttonText.resizeTextMaxSize, 32);
                    }

                    return button;
                }

                Destroy(clone);
            }

            var fallback = CreateObject(
                name,
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            var image = fallback.GetComponent<UnityEngine.UI.Image>();
            image.sprite = buttonSprite;
            image.type = buttonSprite != null
                ? UnityEngine.UI.Image.Type.Sliced
                : UnityEngine.UI.Image.Type.Simple;
            image.fillCenter = true;
            image.color = buttonSprite != null ? Color.white : new Color(0.94f, 0.86f, 0.66f, 1f);
            var buttonFallback = fallback.GetComponent<UnityEngine.UI.Button>();
            buttonFallback.targetGraphic = image;
            fallback.GetComponent<RectTransform>().sizeDelta = fallbackSize;
            if (buttonSprite == null)
            {
                FarmUiThinWoodOutline.EnsureFrame(image, outlineSprite);
            }
            var labelText = CreateText(
                "Label",
                fallback.transform,
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(0.24f, 0.105f, 0.03f, 1f));
            labelText.text = label;
            SetStretch(labelText.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(10f, 4f), new Vector2(-10f, -4f));
            return buttonFallback;
        }

        private GameObject CreateLevelUpCard(Transform parent)
        {
            if (levelUpCardPrefab != null)
            {
                var card = Instantiate(levelUpCardPrefab, parent);
                card.name = "PlayerLevelUpCard";
                card.layer = parent.gameObject.layer;
                return card;
            }

            return CreateObject(
                "PlayerLevelUpCard",
                parent,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
        }

        private Font ResolveFont(UnityEngine.UI.Button buttonTemplate)
        {
            var templateText = buttonTemplate != null
                ? buttonTemplate.GetComponentInChildren<UnityEngine.UI.Text>(true)
                : null;
            if (templateText != null && templateText.font != null)
            {
                return templateText.font;
            }

            var canvasText = GetComponentInChildren<UnityEngine.UI.Text>(true);
            return canvasText != null && canvasText.font != null
                ? canvasText.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Sprite GetFallbackFillSprite()
        {
            if (fallbackFillSprite != null)
            {
                return fallbackFillSprite;
            }

            var texture = Texture2D.whiteTexture;
            fallbackFillSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            fallbackFillSprite.name = "Runtime Player Experience Fill";
            return fallbackFillSprite;
        }

        private static void SetStretch(
            RectTransform target,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            target.anchorMin = anchorMin;
            target.anchorMax = anchorMax;
            target.pivot = new Vector2(0.5f, 0.5f);
            target.offsetMin = offsetMin;
            target.offsetMax = offsetMax;
            target.localScale = Vector3.one;
        }

        private static void Select(GameObject target)
        {
            if (target != null && target.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }
    }
}
