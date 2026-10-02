using TAD.Economy;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Presents the current attacker and defender gold without owning either bank's state.</summary>
    [DisallowMultipleComponent]
    public sealed class ResourceBanksHud : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text attackerGoldText;
        [SerializeField] private UnityEngine.UI.Text attackerIncomeText;
        [SerializeField] private UnityEngine.UI.Text defenderGoldText;

        private ResourceBank attackerBank;
        private ResourceBank defenderBank;
        private bool isSubscribed;

        /// <summary>Binds the top-corner labels to the two level banks.</summary>
        public void Bind(ResourceBank attackerSource, ResourceBank defenderSource)
        {
            if (attackerBank == attackerSource && defenderBank == defenderSource)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            attackerBank = attackerSource;
            defenderBank = defenderSource;
            Subscribe();
            Refresh();
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

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            if (attackerBank != null)
            {
                attackerBank.GoldChanged += HandleAttackerGoldChanged;
                attackerBank.IncomeChanged += HandleAttackerIncomeChanged;
            }

            if (defenderBank != null)
            {
                defenderBank.GoldChanged += HandleDefenderGoldChanged;
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (attackerBank != null)
            {
                attackerBank.GoldChanged -= HandleAttackerGoldChanged;
                attackerBank.IncomeChanged -= HandleAttackerIncomeChanged;
            }

            if (defenderBank != null)
            {
                defenderBank.GoldChanged -= HandleDefenderGoldChanged;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
            if (attackerBank != null)
            {
                HandleAttackerGoldChanged(attackerBank.CurrentGold);
                HandleAttackerIncomeChanged(attackerBank.CurrentIncome);
            }

            if (defenderBank != null)
            {
                HandleDefenderGoldChanged(defenderBank.CurrentGold);
            }
        }

        private void HandleAttackerGoldChanged(int gold)
        {
            if (attackerGoldText != null)
            {
                attackerGoldText.text = $"Attacker Gold: {gold}";
                RefreshTextSkin(attackerGoldText);
            }
        }

        private void HandleAttackerIncomeChanged(int income)
        {
            if (attackerIncomeText != null)
            {
                attackerIncomeText.text = $"Income: +{income}";
                RefreshTextSkin(attackerIncomeText);
            }
        }

        private void HandleDefenderGoldChanged(int gold)
        {
            if (defenderGoldText != null)
            {
                defenderGoldText.text = $"Defender Gold: {gold}";
                RefreshTextSkin(defenderGoldText);
            }
        }

        private void RefreshTextSkin(UnityEngine.UI.Text text)
        {
            if (text == null)
            {
                return;
            }

            var skin = GetComponent<FarmUiSkin>() ?? GetComponentInParent<FarmUiSkin>();
            skin?.ApplyThemeTo(text.transform);
        }
    }
}
