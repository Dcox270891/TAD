using TAD.Economy;
using TAD.Defenders;
using TAD.Match;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Non-modal defender information card. It mirrors the attacker selection card style while
    /// presenting the currently selected farmhand's live combat and progression values.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefenderDetailsPanel : MonoBehaviour
    {
        private const float TopPadding = 7f;
        private const float SidePadding = 11f;
        private const float HeadingMinimumHeight = 28f;
        private const float HeadingToImageSpacing = 4f;
        private const float DefenderImageWidth = 90f;
        private const float DefenderImageHeight = 76f;
        private const float ImageToWeaponSpacing = 7f;
        private const float WeaponMinimumHeight = 34f;
        private const float WeaponToStatsSpacing = 7f;
        private const float StatsMinimumHeight = 126f;
        private const float StatsToTargetingSpacing = 8f;
        private const float TargetingControlHeight = 34f;
        private const float TargetingToBarSpacing = 8f;
        private const float ExperienceBarHeight = 12f;
        private const float BarToExperienceSpacing = 4f;
        private const float ExperienceMinimumHeight = 22f;
        private const float ExperienceToMoveSpacing = 8f;
        private const float MoveButtonWidth = 220f;
        private const float MoveButtonHeight = 36f;
        private const float BottomPadding = 12f;
        private const float MinimumCardHeight = 360f;

        [SerializeField] private UnityEngine.UI.Text headingText;
        [SerializeField] private UnityEngine.UI.Image defenderImage;
        [SerializeField] private UnityEngine.UI.Text weaponText;
        [SerializeField] private UnityEngine.UI.Text statsText;
        [SerializeField] private UnityEngine.UI.Text experienceText;
        [SerializeField] private UnityEngine.UI.Image experienceFill;
        [SerializeField] private UnityEngine.UI.Button moveButton;
        [SerializeField] private UnityEngine.UI.Text moveButtonText;

        private UnityEngine.UI.Dropdown targetingDropdown;
        private UnityEngine.UI.Text targetingReadOnlyText;
        private RectTransform cardRect;
        private RectTransform headingRect;
        private RectTransform defenderImageRect;
        private RectTransform weaponRect;
        private RectTransform statsRect;
        private RectTransform experienceRect;
        private RectTransform experienceBarRect;
        private RectTransform moveButtonRect;
        private RectTransform targetingDropdownRect;
        private RectTransform targetingReadOnlyRect;
        private FarmHand target;
        private FarmHandPlacementController placementController;
        private ResourceBank observedBank;
        private bool isSubscribed;
        private bool isBankSubscribed;
        private bool isTargetingDropdownSubscribed;

        /// <summary>The farmhand currently represented by this card, if any.</summary>
        public FarmHand Target => target;

        /// <summary>Provides the defender-only relocation action without coupling this reusable card to a scene object.</summary>
        public void BindPlacementController(FarmHandPlacementController controller)
        {
            placementController = controller;
            Refresh();
        }

        private void Awake()
        {
            ResolveReferences();
            if (target == null)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (isTargetingDropdownSubscribed && targetingDropdown != null)
            {
                targetingDropdown.onValueChanged.RemoveListener(HandleTargetingModeSelected);
                isTargetingDropdownSubscribed = false;
            }
        }

        /// <summary>Opens the card and binds it to a selected farmhand.</summary>
        public void Show(FarmHand farmHand)
        {
            ResolveReferences();
            if (target != farmHand)
            {
                Unsubscribe();
                target = farmHand;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            Subscribe();
            Refresh();
        }

        /// <summary>Closes the card and stops observing its previous farmhand.</summary>
        public void Hide()
        {
            Unsubscribe();
            target = null;
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        private void ResolveReferences()
        {
            cardRect ??= transform.Find("Card") as RectTransform;
            headingText ??= FindText("Card/Heading");
            defenderImage ??= transform.Find("Card/DefenderImage")?.GetComponent<UnityEngine.UI.Image>();
            weaponText ??= FindText("Card/Weapon");
            statsText ??= FindText("Card/Stats");
            experienceText ??= FindText("Card/Experience");
            experienceFill ??= transform.Find("Card/ExperienceBar/Fill")?.GetComponent<UnityEngine.UI.Image>();
            moveButton ??= transform.Find("Card/MoveButton")?.GetComponent<UnityEngine.UI.Button>();
            moveButtonText ??= FindText("Card/MoveButton/Label");
            targetingDropdown ??= transform.Find("Card/TargetingDropdown")?.GetComponent<UnityEngine.UI.Dropdown>();
            targetingReadOnlyText ??= FindText("Card/TargetingReadOnly");

            EnsureMoveButton();
            EnsureTargetingControls();

            headingRect ??= headingText != null ? headingText.rectTransform : null;
            defenderImageRect ??= defenderImage != null ? defenderImage.rectTransform : null;
            weaponRect ??= weaponText != null ? weaponText.rectTransform : null;
            statsRect ??= statsText != null ? statsText.rectTransform : null;
            experienceRect ??= experienceText != null ? experienceText.rectTransform : null;
            experienceBarRect ??= experienceFill != null && experienceFill.transform.parent != null
                ? experienceFill.transform.parent as RectTransform
                : null;
            moveButtonRect ??= moveButton != null ? moveButton.GetComponent<RectTransform>() : null;
            targetingDropdownRect ??= targetingDropdown != null ? targetingDropdown.GetComponent<RectTransform>() : null;
            targetingReadOnlyRect ??= targetingReadOnlyText != null ? targetingReadOnlyText.rectTransform : null;

            ConfigureTextWrapping(headingText);
            ConfigureTextWrapping(weaponText);
            ConfigureTextWrapping(statsText);
            ConfigureTextWrapping(experienceText);
            ConfigureTextWrapping(targetingReadOnlyText);
        }

        private void EnsureMoveButton()
        {
            if (moveButton != null || cardRect == null)
            {
                return;
            }

            var buttonObject = new GameObject(
                "MoveButton",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.Button));
            buttonObject.layer = cardRect.gameObject.layer;
            buttonObject.transform.SetParent(cardRect, worldPositionStays: false);

            var buttonImage = buttonObject.GetComponent<UnityEngine.UI.Image>();
            buttonImage.color = new Color(0.18f, 0.42f, 0.22f, 0.96f);
            moveButton = buttonObject.GetComponent<UnityEngine.UI.Button>();
            moveButton.targetGraphic = buttonImage;
            moveButton.onClick.AddListener(HandleMovePressed);
            moveButtonRect = buttonObject.GetComponent<RectTransform>();

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Text));
            labelObject.layer = buttonObject.layer;
            labelObject.transform.SetParent(buttonObject.transform, worldPositionStays: false);
            moveButtonText = labelObject.GetComponent<UnityEngine.UI.Text>();
            moveButtonText.font = headingText != null ? headingText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            moveButtonText.fontSize = 15;
            moveButtonText.alignment = TextAnchor.MiddleCenter;
            moveButtonText.color = Color.white;
            moveButtonText.horizontalOverflow = HorizontalWrapMode.Overflow;
            moveButtonText.verticalOverflow = VerticalWrapMode.Truncate;
            var labelRect = moveButtonText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Creates a complete legacy uGUI dropdown at runtime so older details-panel prefabs remain compatible.
        /// The attacker view uses a parallel non-interactive label instead of exposing a selectable control.
        /// </summary>
        private void EnsureTargetingControls()
        {
            if (cardRect == null)
            {
                return;
            }

            if (targetingDropdown == null)
            {
                var dropdownObject = UnityEngine.UI.DefaultControls.CreateDropdown(new UnityEngine.UI.DefaultControls.Resources());
                dropdownObject.name = "TargetingDropdown";
                dropdownObject.transform.SetParent(cardRect, worldPositionStays: false);
                SetLayerRecursively(dropdownObject, cardRect.gameObject.layer);

                targetingDropdown = dropdownObject.GetComponent<UnityEngine.UI.Dropdown>();
                targetingDropdown.options.Clear();
                targetingDropdown.options.Add(new UnityEngine.UI.Dropdown.OptionData(
                    $"Target Enemy: {FarmHand.GetTargetingModeDisplayName(DefenderTargetingMode.FirstEnemyInRange)}"));
                targetingDropdown.SetValueWithoutNotify(0);

                var dropdownBackground = dropdownObject.GetComponent<UnityEngine.UI.Image>();
                if (dropdownBackground != null)
                {
                    dropdownBackground.type = UnityEngine.UI.Image.Type.Simple;
                    dropdownBackground.color = new Color(0.18f, 0.42f, 0.22f, 0.96f);
                }

                ConfigureDropdownText(targetingDropdown.captionText, Color.white, TextAnchor.MiddleLeft);
                ConfigureDropdownText(targetingDropdown.itemText, new Color(0.12f, 0.12f, 0.12f, 1f), TextAnchor.MiddleLeft);

                var arrow = dropdownObject.transform.Find("Arrow");
                if (arrow != null)
                {
                    var arrowLabel = arrow.Find("Label");
                    if (arrowLabel == null)
                    {
                        var arrowLabelObject = new GameObject(
                            "Label",
                            typeof(RectTransform),
                            typeof(CanvasRenderer),
                            typeof(UnityEngine.UI.Text));
                        arrowLabelObject.layer = arrow.gameObject.layer;
                        arrowLabelObject.transform.SetParent(arrow, worldPositionStays: false);
                        var arrowLabelRect = arrowLabelObject.GetComponent<RectTransform>();
                        arrowLabelRect.anchorMin = Vector2.zero;
                        arrowLabelRect.anchorMax = Vector2.one;
                        arrowLabelRect.offsetMin = Vector2.zero;
                        arrowLabelRect.offsetMax = Vector2.zero;
                        arrowLabel = arrowLabelObject.transform;
                    }

                    var arrowText = arrowLabel.GetComponent<UnityEngine.UI.Text>();
                    arrowText.text = "▼";
                    arrowText.font = headingText != null ? headingText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    arrowText.fontSize = 15;
                    arrowText.alignment = TextAnchor.MiddleCenter;
                    arrowText.color = Color.white;
                    arrowText.raycastTarget = false;
                }

                if (targetingDropdown.template != null)
                {
                    var templateBackground = targetingDropdown.template.GetComponent<UnityEngine.UI.Image>();
                    if (templateBackground != null)
                    {
                        templateBackground.type = UnityEngine.UI.Image.Type.Simple;
                        templateBackground.color = new Color(0.95f, 0.95f, 0.9f, 1f);
                    }
                }

                targetingDropdown.RefreshShownValue();
            }

            if (!isTargetingDropdownSubscribed && targetingDropdown != null)
            {
                targetingDropdown.onValueChanged.AddListener(HandleTargetingModeSelected);
                isTargetingDropdownSubscribed = true;
            }

            if (targetingReadOnlyText != null)
            {
                return;
            }

            var readOnlyObject = new GameObject(
                "TargetingReadOnly",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text));
            readOnlyObject.layer = cardRect.gameObject.layer;
            readOnlyObject.transform.SetParent(cardRect, worldPositionStays: false);
            targetingReadOnlyText = readOnlyObject.GetComponent<UnityEngine.UI.Text>();
            targetingReadOnlyText.font = headingText != null ? headingText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            targetingReadOnlyText.fontSize = 14;
            targetingReadOnlyText.alignment = TextAnchor.MiddleLeft;
            targetingReadOnlyText.color = Color.white;
            targetingReadOnlyText.raycastTarget = false;
            ConfigureTextWrapping(targetingReadOnlyText);
        }

        private void ConfigureDropdownText(UnityEngine.UI.Text text, Color color, TextAnchor alignment)
        {
            if (text == null)
            {
                return;
            }

            text.font = headingText != null ? headingText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }

        private static void SetLayerRecursively(GameObject gameObject, int layer)
        {
            if (gameObject == null)
            {
                return;
            }

            gameObject.layer = layer;
            foreach (Transform child in gameObject.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private UnityEngine.UI.Text FindText(string path)
        {
            var child = transform.Find(path);
            return child != null ? child.GetComponent<UnityEngine.UI.Text>() : null;
        }

        private static void ConfigureTextWrapping(UnityEngine.UI.Text text)
        {
            if (text == null)
            {
                return;
            }

            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void Subscribe()
        {
            if (isSubscribed || target == null)
            {
                return;
            }

            target.ProgressionChanged += Refresh;
            target.TargetingModeChanged += HandleTargetingModeChanged;
            isSubscribed = true;
            SubscribeToBank();
        }

        private void Unsubscribe()
        {
            if (!isSubscribed && !isBankSubscribed)
            {
                return;
            }

            if (isSubscribed && target != null)
            {
                target.ProgressionChanged -= Refresh;
                target.TargetingModeChanged -= HandleTargetingModeChanged;
            }

            UnsubscribeFromBank();
            isSubscribed = false;
        }

        private void SubscribeToBank()
        {
            var desiredBank = target != null ? target.DefenderBank : null;
            if (observedBank == desiredBank && isBankSubscribed)
            {
                return;
            }

            UnsubscribeFromBank();
            observedBank = desiredBank;
            if (observedBank != null)
            {
                observedBank.GoldChanged += HandleBankGoldChanged;
                isBankSubscribed = true;
            }
        }

        private void UnsubscribeFromBank()
        {
            if (isBankSubscribed && observedBank != null)
            {
                observedBank.GoldChanged -= HandleBankGoldChanged;
            }

            observedBank = null;
            isBankSubscribed = false;
        }

        private void HandleBankGoldChanged(int _)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (target == null || !target.isActiveAndEnabled || !target.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

            SubscribeToBank();

            var weapon = target.WeaponDefinition;
            var projectile = weapon != null ? weapon.ProjectileDefinition : null;
            if (headingText != null)
            {
                headingText.text = $"Farmhand  •  Level {target.CurrentLevel}/{target.MaxLevel}";
            }

            if (defenderImage != null)
            {
                var spriteRenderer = target.Visual;
                defenderImage.sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
                defenderImage.enabled = defenderImage.sprite != null;
            }

            if (weaponText != null)
            {
                var weaponName = weapon != null ? weapon.DisplayName : "Unarmed";
                var description = weapon != null ? weapon.Description : "No weapon assigned.";
                weaponText.text = $"{weaponName}\n{description}";
            }

            if (statsText != null)
            {
                var projectileType = projectile != null ? projectile.ProjectileType.ToString() : "Melee";
                var projectileBehavior = projectile != null ? projectile.ProjectileBehavior.ToString() : "Direct";
                var projectileSpeed = projectile != null ? projectile.ProjectileSpeed.ToString("0.##") : "—";
                var projectileLifetime = projectile == null
                    ? "—"
                    : projectile.UsesTimedLifetime
                        ? $"{projectile.ProjectileLifetime:0.##} sec"
                        : "Until activated";
                var attackSpeed = weapon != null ? weapon.AttackSpeed.ToString("0.##") : "0";
                var weaponCost = weapon != null ? weapon.Cost.ToString() : "0";

                statsText.text =
                    $"Damage: {target.Damage:0.##}    Range: {target.Range:0.##}\n" +
                    $"Attack Speed: {attackSpeed}/sec\n" +
                    $"Attack: {projectileType}  •  {projectileBehavior}\n" +
                    $"Projectile Speed: {projectileSpeed}\n" +
                    $"Lifetime: {projectileLifetime}\n" +
                    $"Weapon Cost: {weaponCost}";
            }

            if (experienceText != null)
            {
                var levelStatus = target.IsAtMaxLevel ? "MAX LEVEL" : "EXP";
                experienceText.text =
                    $"{levelStatus}: {target.CurrentExp:0.#}/{target.ExpMax}   Total: {target.TotalExperience:0.#}";
            }

            if (experienceFill != null)
            {
                experienceFill.fillAmount = target.IsAtMaxLevel
                    ? 1f
                    : Mathf.Clamp01(target.CurrentExp / Mathf.Max(1, target.ExpMax));
            }

            RefreshTargetingControls();
            UpdateMoveButton();
            RefreshLayout();
        }

        private void RefreshTargetingControls()
        {
            if (target == null)
            {
                return;
            }

            var playerCanChooseTargeting = MatchRoleSelection.IsPlayerDefender;
            if (targetingDropdown != null)
            {
                targetingDropdown.gameObject.SetActive(playerCanChooseTargeting);
                if (playerCanChooseTargeting)
                {
                    targetingDropdown.SetValueWithoutNotify(TargetingModeToDropdownValue(target.TargetingMode));
                    targetingDropdown.RefreshShownValue();
                }
            }

            if (targetingReadOnlyText != null)
            {
                targetingReadOnlyText.gameObject.SetActive(!playerCanChooseTargeting);
                if (!playerCanChooseTargeting)
                {
                    targetingReadOnlyText.text = $"Target Enemy: {target.TargetingModeDisplayName}";
                }
            }
        }

        private void HandleTargetingModeSelected(int optionIndex)
        {
            if (target == null || !MatchRoleSelection.IsPlayerDefender)
            {
                return;
            }

            target.SetTargetingMode(TargetingModeFromDropdownValue(optionIndex));
            Refresh();
        }

        private void HandleTargetingModeChanged(DefenderTargetingMode _)
        {
            Refresh();
        }

        private static int TargetingModeToDropdownValue(DefenderTargetingMode mode)
        {
            return mode == DefenderTargetingMode.FirstEnemyInRange ? 0 : 0;
        }

        private static DefenderTargetingMode TargetingModeFromDropdownValue(int _)
        {
            return DefenderTargetingMode.FirstEnemyInRange;
        }

        private void UpdateMoveButton()
        {
            if (moveButton == null)
            {
                return;
            }

            var canShowMove = MatchRoleSelection.IsPlayerDefender && target != null &&
                target.isActiveAndEnabled && target.gameObject.activeInHierarchy && !target.IsRelocating &&
                placementController != null && !placementController.IsPlacementActive;
            moveButton.gameObject.SetActive(canShowMove);
            if (!canShowMove)
            {
                return;
            }

            if (moveButtonText != null)
            {
                moveButtonText.text = $"MOVE  •  {target.RelocationCost} GOLD";
            }

            var bank = target.DefenderBank;
            moveButton.interactable = bank != null && bank.CanAfford(target.RelocationCost);
        }

        private void HandleMovePressed()
        {
            if (target == null || placementController == null || !MatchRoleSelection.IsPlayerDefender ||
                target.IsRelocating)
            {
                return;
            }

            placementController.BeginRelocation(target);
        }

        /// <summary>
        /// Keeps every text field within the card's width and grows the bottom-anchored card upward
        /// when a weapon description or any stat needs more vertical space.
        /// </summary>
        private void RefreshLayout()
        {
            if (cardRect == null || headingRect == null || defenderImageRect == null ||
                weaponRect == null || statsRect == null || experienceRect == null ||
                experienceBarRect == null)
            {
                return;
            }

            SetTopStretch(headingRect, TopPadding, HeadingMinimumHeight, SidePadding);
            Canvas.ForceUpdateCanvases();
            var headingHeight = GetPreferredHeight(headingText, HeadingMinimumHeight);
            SetTopStretch(headingRect, TopPadding, headingHeight, SidePadding);

            var imageTop = TopPadding + headingHeight + HeadingToImageSpacing;
            SetTopCentered(defenderImageRect, imageTop, DefenderImageWidth, DefenderImageHeight);

            var weaponTop = imageTop + DefenderImageHeight + ImageToWeaponSpacing;
            SetTopStretch(weaponRect, weaponTop, WeaponMinimumHeight, SidePadding);
            Canvas.ForceUpdateCanvases();
            var weaponHeight = GetPreferredHeight(weaponText, WeaponMinimumHeight);
            SetTopStretch(weaponRect, weaponTop, weaponHeight, SidePadding);

            var statsTop = weaponTop + weaponHeight + WeaponToStatsSpacing;
            SetTopStretch(statsRect, statsTop, StatsMinimumHeight, SidePadding + 4f);
            Canvas.ForceUpdateCanvases();
            var statsHeight = GetPreferredHeight(statsText, StatsMinimumHeight);
            SetTopStretch(statsRect, statsTop, statsHeight, SidePadding + 4f);

            var afterStatsTop = statsTop + statsHeight;
            var targetingRect = GetVisibleTargetingControl();
            var barTop = afterStatsTop + TargetingToBarSpacing;
            if (targetingRect != null)
            {
                var targetingTop = afterStatsTop + StatsToTargetingSpacing;
                SetTopStretch(targetingRect, targetingTop, TargetingControlHeight, SidePadding);
                barTop = targetingTop + TargetingControlHeight + TargetingToBarSpacing;
            }

            SetTopStretch(experienceBarRect, barTop, ExperienceBarHeight, SidePadding + 2f);

            var experienceTop = barTop + ExperienceBarHeight + BarToExperienceSpacing;
            SetTopStretch(experienceRect, experienceTop, ExperienceMinimumHeight, SidePadding);
            Canvas.ForceUpdateCanvases();
            var experienceHeight = GetPreferredHeight(experienceText, ExperienceMinimumHeight);
            SetTopStretch(experienceRect, experienceTop, experienceHeight, SidePadding);

            var preferredCardHeight = experienceTop + experienceHeight + BottomPadding;
            if (moveButtonRect != null && moveButton != null && moveButton.gameObject.activeSelf)
            {
                var moveTop = experienceTop + experienceHeight + ExperienceToMoveSpacing;
                SetTopCentered(moveButtonRect, moveTop, MoveButtonWidth, MoveButtonHeight);
                preferredCardHeight = moveTop + MoveButtonHeight + BottomPadding;
            }

            cardRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(MinimumCardHeight, preferredCardHeight));
        }

        private RectTransform GetVisibleTargetingControl()
        {
            if (targetingDropdownRect != null && targetingDropdownRect.gameObject.activeSelf)
            {
                return targetingDropdownRect;
            }

            return targetingReadOnlyRect != null && targetingReadOnlyRect.gameObject.activeSelf
                ? targetingReadOnlyRect
                : null;
        }

        private static float GetPreferredHeight(UnityEngine.UI.Text text, float minimumHeight)
        {
            return text != null ? Mathf.Max(minimumHeight, text.preferredHeight) : minimumHeight;
        }

        private static void SetTopStretch(RectTransform rectTransform, float top, float height, float horizontalPadding)
        {
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(-(horizontalPadding * 2f), height);
            rectTransform.anchoredPosition = new Vector2(0f, -top);
        }

        private static void SetTopCentered(RectTransform rectTransform, float top, float width, float height)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.sizeDelta = new Vector2(width, height);
            rectTransform.anchoredPosition = new Vector2(0f, -top);
        }
    }
}
