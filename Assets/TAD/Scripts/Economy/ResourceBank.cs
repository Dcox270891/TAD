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

        /// <summary>Raised whenever the current gold value changes.</summary>
        public event Action<int> GoldChanged;

        public BankSide BankSide => bankSide;
        public int StartingGold => startingGold;
        public int CurrentGold => currentGold;

        private void Awake()
        {
            ResetGold();
        }

        /// <summary>Restores this bank to the level's configured starting gold.</summary>
        public void ResetGold()
        {
            SetGold(startingGold);
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
