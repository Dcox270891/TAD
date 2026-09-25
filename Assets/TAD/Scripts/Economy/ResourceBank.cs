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

        /// <summary>Raised when queued attacker-wave income changes.</summary>
        public event Action<int> IncomeChanged;

        public BankSide BankSide => bankSide;
        public int StartingGold => startingGold;
        public int CurrentGold => currentGold;
        public int CurrentIncome => currentIncome;

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

        /// <summary>Adds income that will be paid when the queued attacker wave resolves.</summary>
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
            var collectedIncome = currentIncome;
            if (collectedIncome <= 0)
            {
                return 0;
            }

            SetIncome(0);
            AddGold(collectedIncome);
            return collectedIncome;
        }

        /// <summary>Clears queued income without adding it to the current gold.</summary>
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
