using System;
using TAD.Progression;
using UnityEngine;

namespace TAD.Environment
{
    /// <summary>
    /// Contract an attacker uses when it reaches a <see cref="FoodStore"/>.
    /// Implement this on the root component of each pooled animal prefab.
    /// </summary>
    public interface IFoodStoreRaider
    {
        bool HasReachedFoodStore { get; }
        float RemainingHealth { get; }

        void OnFoodStoreReached(FoodStore foodStore, float foodConsumed);
    }

    /// <summary>
    /// Stores the farm's food and accepts attacking animals that enter its trigger.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(Rigidbody2D))]
    public sealed class FoodStore : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("Food available when a wave begins or the store is reset.")]
        private float startingFood = 200f;

        [NonSerialized] private float currentFood;

        public float StartingFood => startingFood;
        public float CurrentFood => currentFood;
        public bool IsDepleted => currentFood <= 0f;

        /// <summary>Supplies the current food and starting-food values for HUDs and game rules.</summary>
        public event Action<float, float> FoodChanged;

        /// <summary>Raised once whenever food reaches zero.</summary>
        public event Action FoodDepleted;

        /// <summary>
        /// Raised when a valid attacker reaches the barn, before the food is consumed. This lets
        /// wave economy settle the arrival reward even when this specific attacker depletes the
        /// final food and immediately ends the match.
        /// </summary>
        public event Action<IFoodStoreRaider> RaiderReached;

        private void Awake()
        {
            ResetFood();
        }

        /// <summary>Refills the store to its configured amount, ready for a new wave.</summary>
        public void ResetFood()
        {
            currentFood = startingFood;
            FoodChanged?.Invoke(currentFood, startingFood);
        }

        /// <summary>
        /// Removes food, returning the amount that could actually be taken.
        /// </summary>
        public float ConsumeFood(float requestedAmount)
        {
            if (requestedAmount <= 0f || currentFood <= 0f)
            {
                return 0f;
            }

            var foodConsumed = Mathf.Min(requestedAmount, currentFood);
            currentFood -= foodConsumed;

            FoodChanged?.Invoke(currentFood, startingFood);

            if (currentFood <= 0f)
            {
                FoodDepleted?.Invoke();
            }

            return foodConsumed;
        }

        /// <summary>
        /// Resolves one attacker's arrival at the barn. Normally this is called by the food-store
        /// trigger. Route maps whose visible grass stops at the barn's edge use the same method
        /// once the animal reaches the last valid grass cell, so its body never has to leave the
        /// playable ground just to touch an off-edge barn collider.
        /// </summary>
        public bool TryAcceptRaider(IFoodStoreRaider raider)
        {
            if (IsDepleted)
            {
                return false;
            }

            if (raider == null || raider.HasReachedFoodStore)
            {
                return false;
            }

            RaiderReached?.Invoke(raider);
            var foodConsumed = ConsumeFood(raider.RemainingHealth);
            raider.OnFoodStoreReached(this, foodConsumed);
            TadHapticFeedback.PulseFoodStoreReached();
            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryAcceptRaider(FindRaider(other));
        }

        private static IFoodStoreRaider FindRaider(Collider2D collider)
        {
            var behaviours = collider.GetComponentsInParent<MonoBehaviour>();
            foreach (var behaviour in behaviours)
            {
                if (behaviour is IFoodStoreRaider raider)
                {
                    return raider;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            startingFood = Mathf.Max(0f, startingFood);
        }
#endif
    }
}
