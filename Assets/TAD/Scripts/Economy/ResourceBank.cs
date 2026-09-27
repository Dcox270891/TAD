using System;
using UnityEngine;

namespace TAD.Economy
{
    /// <summary>
    /// Owns one side's current gold for a level. The value resets to its configured starting amount
    /// whenever the level is loaded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceBank : MonoBehaviour
    {
        [SerializeField] private BankSide bankSide;
        [SerializeField, Min(0)] private int startingGold = 100;

        private int currentGold;
        private int currentIncome;

        /// <summary>Raised whenever the current gold value changes.</summary>
        public event Action<int> GoldChanged;

        /// <summary>Raised when the stored attacker income total changes.</summary>
        public event Action<int> IncomeChanged;

        public BankSide BankSide => bankSide;
        public int StartingGold => startingGold;
        public int CurrentGold => currentGold;
        public int CurrentIncome => currentIncome;

        /// <summary>
        /// Configures this bank for a level-side owner and resets it to the supplied starting gold.
        /// This is useful for level bootstrap code that creates a bank dynamically.
        /// </summary>
        public void Configure(BankSide side, int initialGold)
        {
            bankSide = side;
            startingGold = Mathf.Max(0, initialGold);
            ResetGold();
        }

        private void Awake()
        {
            ResetGold();
        }

        /// <summary>Restores this bank to the level's configured starting gold.</summary>
        public void ResetGold()
        {
            SetGold(startingGold);
            ClearIncome();
        }

        /// <summary>Returns whether the bank can pay an amount without changing its balance.</summary>
        public bool CanAfford(int amount)
        {
            return amount >= 0 && currentGold >= amount;
        }

        /// <summary>Adds earned gold to this bank.</summary>
        public void AddGold(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            var increasedGold = (int)Math.Min(int.MaxValue, (long)currentGold + amount);
            SetGold(increasedGold);
        }

        /// <summary>Adds recurring attacker income that is paid after future resolved waves.</summary>
        public void AddIncome(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            var increasedIncome = (int)Math.Min(int.MaxValue, (long)currentIncome + amount);
            SetIncome(increasedIncome);
        }

        /// <summary>Pays all queued income into this bank and returns the amount collected.</summary>
        public int CollectIncome()
        {
            return CollectIncome(currentIncome);
        }

        /// <summary>
        /// Pays up to the requested amount of queued income into this bank. This lets an active day
        /// collect only the income for its resolved attacker groups while later orders remain pending.
        /// </summary>
        public int CollectIncome(int amount)
        {
            var collectedIncome = Mathf.Clamp(amount, 0, currentIncome);
            if (collectedIncome <= 0)
            {
                return 0;
            }

            SetIncome(currentIncome - collectedIncome);
            AddGold(collectedIncome);
            return collectedIncome;
        }

        /// <summary>
        /// Pays a snapshot of recurring income into this bank without consuming the stored income total.
        /// This lets attacker income grow across resolved waves while the HUD continues to show the full amount.
        /// </summary>
        public int PayIncome(int amount)
        {
            var paidIncome = Mathf.Clamp(amount, 0, currentIncome);
            if (paidIncome <= 0)
            {
                return 0;
            }

            AddGold(paidIncome);
            return paidIncome;
        }

        /// <summary>
        /// Removes income associated with a cancelled attacker order without touching the current gold.
        /// Returns false when there was no positive income to remove.
        /// </summary>
        public bool TryRemoveIncome(int amount)
        {
            if (amount <= 0 || currentIncome <= 0)
            {
                return false;
            }

            SetIncome(Mathf.Max(0, currentIncome - amount));
            return true;
        }

        /// <summary>Clears the stored income total without adding it to the current gold.</summary>
        public void ClearIncome()
        {
            SetIncome(0);
        }

        /// <summary>Attempts to pay gold, returning false when funds are insufficient.</summary>
        public bool TrySpendGold(int amount)
        {
            if (!CanAfford(amount))
            {
                return false;
            }

            SetGold(currentGold - amount);
            return true;
        }

        private void SetIncome(int value)
        {
            var clampedIncome = Mathf.Max(0, value);
            if (currentIncome == clampedIncome)
            {
                return;
            }

            currentIncome = clampedIncome;
            IncomeChanged?.Invoke(currentIncome);
        }

        private void SetGold(int value)
        {
            var clampedGold = Mathf.Max(0, value);
            if (currentGold == clampedGold)
            {
                return;
            }

            currentGold = clampedGold;
            GoldChanged?.Invoke(currentGold);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            startingGold = Mathf.Max(0, startingGold);
        }
#endif
    }
}
