using UnityEngine;
using UnityEngine.Serialization;

namespace TAD.Defenders
{
    /// <summary>
    /// Shared defender weapon data. Projectile movement, lifetime, collision rules, and projectile prefab
    /// are supplied by the referenced ProjectileDefinition so they can be reused independently of a weapon.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Unarmed";
        [SerializeField, TextArea] private string description = "A basic unarmed farmhand attack.";

        [Header("Progression")]
        [SerializeField]
        [Tooltip("The exact level-five weapon required before this level-eight final evolution can be selected. Leave empty for base and earlier-tier weapons.")]
        private WeaponDefinition requiredPreviousWeapon;
        [SerializeField]
        [Tooltip("Whether this level-five weapon opens its final two-way evolution once the farmhand reaches level 8.")]
        private bool unlocksFinalEvolution;

        [Header("Cost")]
        [SerializeField, Min(0)] private int cost;

        [Header("Attack Range")]
        [SerializeField, Min(0f)] private float range = 1f;

        [Header("Attack Speed")]
        [FormerlySerializedAs("fireInterval")]
        [SerializeField, Min(0.01f)] private float attackSpeed = 1f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float damage = 1f;

        [Header("Reload")]
        [SerializeField, Min(1)]
        [Tooltip("Shots fired before this weapon reloads. Reloading is disabled while Reload Duration is zero.")]
        private int shotsPerMagazine = 1;
        [SerializeField, Min(0f)]
        [Tooltip("Seconds required to reload after the magazine is empty. Zero keeps the weapon continuously available.")]
        private float reloadDuration;

        [Header("Projectile")]
        [SerializeField] private ProjectileDefinition projectileDefinition;

        public int Cost => cost;
        public float Range => range;
        public float AttackSpeed => attackSpeed;
        public float FireInterval => 1f / Mathf.Max(0.01f, attackSpeed);
        public float Damage => damage;
        public ProjectileDefinition ProjectileDefinition => projectileDefinition;
        public string DisplayName => displayName;
        public string Description => description;
        /// <summary>Exact level-five parent required to select this final evolution, if any.</summary>
        public WeaponDefinition RequiredPreviousWeapon => requiredPreviousWeapon;
        /// <summary>Whether this currently equipped level-five weapon has an authored level-eight evolution branch.</summary>
        public bool UnlocksFinalEvolution => unlocksFinalEvolution;
        public int ShotsPerMagazine => Mathf.Max(1, shotsPerMagazine);
        public float ReloadDuration => Mathf.Max(0f, reloadDuration);
        public bool UsesReload => ReloadDuration > 0f;
        /// <summary>
        /// Long-run rate of fire including reload time. Burst weapons can still fire at
        /// <see cref="AttackSpeed"/> between shots, while AI balance uses this value to avoid
        /// overvaluing a weapon that spends substantial time reloading.
        /// </summary>
        public float SustainedAttackSpeed
        {
            get
            {
                if (!UsesReload)
                {
                    return AttackSpeed;
                }

                var shots = ShotsPerMagazine;
                var cycleDuration = ((shots - 1) * FireInterval) + Mathf.Max(FireInterval, ReloadDuration);
                return shots / Mathf.Max(0.01f, cycleDuration);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            cost = Mathf.Max(0, cost);
            range = Mathf.Max(0f, range);
            attackSpeed = Mathf.Max(0.01f, attackSpeed);
            damage = Mathf.Max(0f, damage);
            shotsPerMagazine = Mathf.Max(1, shotsPerMagazine);
            reloadDuration = Mathf.Max(0f, reloadDuration);
        }
#endif
    }
}
