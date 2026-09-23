using TAD.Economy;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Connects the two level banks to a single shared HUD instance.</summary>
    [DisallowMultipleComponent]
    public sealed class ResourceBanksHudBootstrap : MonoBehaviour
    {
        [SerializeField] private ResourceBank attackerBank;
        [SerializeField] private ResourceBank defenderBank;
        [SerializeField] private ResourceBanksHud hudPrefab;

        private ResourceBanksHud spawnedHud;
        private bool ownsSpawnedHud;

        private void Awake()
        {
            ResolveBanks();
        }

        private void Start()
        {
            if (attackerBank == null || defenderBank == null)
            {
                return;
            }

            spawnedHud = Object.FindAnyObjectByType<ResourceBanksHud>(FindObjectsInactive.Include);
            if (spawnedHud == null && hudPrefab != null)
            {
                spawnedHud = Instantiate(hudPrefab);
                ownsSpawnedHud = true;
            }

            spawnedHud?.Bind(attackerBank, defenderBank);
        }

        private void OnDestroy()
        {
            if (ownsSpawnedHud && spawnedHud != null)
            {
                Destroy(spawnedHud.gameObject);
            }
        }

        private void ResolveBanks()
        {
            if (attackerBank != null && defenderBank != null)
            {
                return;
            }

            foreach (var bank in GetComponentsInChildren<ResourceBank>(true))
            {
                if (bank.BankSide == BankSide.Attacker)
                {
                    attackerBank ??= bank;
                }
                else
                {
                    defenderBank ??= bank;
                }
            }
        }
    }
}
