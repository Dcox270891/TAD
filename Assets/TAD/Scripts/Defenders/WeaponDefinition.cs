using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Authoring data shared by every farmhand using the same defender weapon.
    /// Runtime combat code reads this asset but must not modify it.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "Unarmed";
        [SerializeField, TextArea(2, 5)] private string description = "A farmhand's fists. No projectile is used.";

        [Header("Cost")]
        [SerializeField, Min(0)] private int cost;

        [Header("Projectile")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField, Min(0f)] private float damage = 1f;
        [SerializeField, Min(0f)] private float projectileLifetime;

        public int Cost => cost;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float Damage => damage;
        public float ProjectileLifetime => projectileLifetime;
        public string DisplayName => displayName;
        public string Description => description;

#if UNITY_EDITOR
        private void OnValidate()
        {
            cost = Mathf.Max(0, cost);
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            damage = Mathf.Max(0f, damage);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);
        }
#endif
    }
}
