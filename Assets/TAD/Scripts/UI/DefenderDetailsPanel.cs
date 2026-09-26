using TAD.Defenders;
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
        private const float BottomPadding = 12f;
        private const float MinimumCardHeight = 360f;

        [SerializeField] private UnityEngine.UI.Text headingText;
        [SerializeField] private UnityEngine.UI.Image defenderImage;
        [SerializeField] private UnityEngine.UI.Text weaponText;
        [SerializeField] private UnityEngine.UI.Text statsText;
        [SerializeField] private UnityEngine.UI.Text experienceText;
        [SerializeField] private UnityEngine.UI.Image experienceFill;

        private RectTransform cardRect;
        private RectTransform headingRect;
        private RectTransform defenderImageRect;
        private RectTransform weaponRect;
        private RectTransform statsRect;
        private RectTransform experienceRect;
        private RectTransform experienceBarRect;
        private FarmHand target;
        private bool isSubscribed;

        /// <summary>The farmhand currently represented by this card, if any.</summary>
        public FarmHand Target => target;

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

            headingRect ??= headingText != null ? headingText.rectTransform : null;
            defenderImageRect ??= defenderImage != null ? defenderImage.rectTransform : null;
            weaponRect ??= weaponText != null ? weaponText.rectTransform : null;
            statsRect ??= statsText != null ? statsText.rectTransform : null;
            experienceRect ??= experienceText != null ? experienceText.rectTransform : null;
            experienceBarRect ??= experienceFill != null && experienceFill.transform.parent != null
                ? experienceFill.transform.parent as RectTransform
                : null;

            ConfigureTextWrapping(headingText);
            ConfigureTextWrapping(weaponText);
            ConfigureTextWrapping(statsText);
            ConfigureTextWrapping(experienceText);
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
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (target != null)
            {
                target.ProgressionChanged -= Refresh;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
            if (target == null || !target.isActiveAndEnabled || !target.gameObject.activeInHierarchy)
            {
                Hide();
                return;
            }

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

            RefreshLayout();
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
