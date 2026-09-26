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
        [SerializeField] private UnityEngine.UI.Text headingText;
        [SerializeField] private UnityEngine.UI.Image defenderImage;
        [SerializeField] private UnityEngine.UI.Text weaponText;
        [SerializeField] private UnityEngine.UI.Text statsText;
        [SerializeField] private UnityEngine.UI.Text experienceText;
        [SerializeField] private UnityEngine.UI.Image experienceFill;

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
            headingText ??= FindText("Card/Heading");
            defenderImage ??= transform.Find("Card/DefenderImage")?.GetComponent<UnityEngine.UI.Image>();
            weaponText ??= FindText("Card/Weapon");
            statsText ??= FindText("Card/Stats");
            experienceText ??= FindText("Card/Experience");
            experienceFill ??= transform.Find("Card/ExperienceBar/Fill")?.GetComponent<UnityEngine.UI.Image>();
        }

        private UnityEngine.UI.Text FindText(string path)
        {
            var child = transform.Find(path);
            return child != null ? child.GetComponent<UnityEngine.UI.Text>() : null;
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
        }
    }
}