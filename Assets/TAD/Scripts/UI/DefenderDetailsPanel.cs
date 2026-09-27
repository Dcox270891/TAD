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
        private const float StatsToBarSpacing = 8f;
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

        private RectTransform cardRect;
        private RectTransform headingRect;
        private RectTransform defenderImageRect;
        private RectTransform weaponRect;
        private RectTransform statsRect;
        private RectTransform experienceRect;
        private RectTransform experienceBarRect;
        private RectTransform moveButtonRect;
        private FarmHand target;
        private FarmHandPlacementController placementController;
        private ResourceBank observedBank;
        private bool isSubscribed;
        private bool isBankSubscribed;

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

            headingRect ??= headingText != null ? headingText.rectTransform : null;
            defenderImageRect ??= defenderImage != null ? defenderImage.rectTransform : null;
            weaponRect ??= weaponText != null ? weaponText.rectTransform : null;
            statsRect ??= statsText != null ? statsText.rectTransform : null;
            experienceRect ??= experienceText != null ? experienceText.rectTransform : null;
            experienceBarRect ??= experienceFill != null && experienceFill.transform.parent != null
                ? experienceFill.transform.parent as RectTransform
                : null;
            moveButtonRect ??= moveButton != null ? moveButton.GetComponent<RectTransform>() : null;

            EnsureMoveButton();

            ConfigureTextWrapping(headingText);
            ConfigureTextWrapping(weaponText);
            ConfigureTextWrapping(statsText);
            ConfigureTextWrapping(experienceText);
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

            UpdateMoveButton();
            RefreshLayout();
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

            var barTop = statsTop + statsHeight + StatsToBarSpacing;
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
