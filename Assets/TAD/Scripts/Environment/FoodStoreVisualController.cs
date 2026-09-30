using System;
using UnityEngine;

namespace TAD.Environment
{
    /// <summary>
    /// Presents the Food Store as a barn and progressively removes its exterior bag stacks as food is raided.
    /// Food state remains owned by <see cref="FoodStore"/>; this component only observes its event.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FoodStore))]
    public sealed class FoodStoreVisualController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The barn art displayed for this Food Store.")]
        private SpriteRenderer barnRenderer;

        [SerializeField]
        [Tooltip("Exterior food bag stacks, ordered from the first to disappear to the last.")]
        private SpriteRenderer[] foodBagRenderers = Array.Empty<SpriteRenderer>();

        private FoodStore foodStore;
        private bool isSubscribed;

        private void Awake()
        {
            foodStore = GetComponent<FoodStore>();
        }

        private void OnEnable()
        {
            Subscribe();
            RefreshVisuals();
        }

        private void Start()
        {
            // FoodStore resets during Awake, before this component's first visible refresh.
            RefreshVisuals();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>Calculates how many exterior bag stacks should still be visible for the supplied food ratio.</summary>
        public static int GetVisibleBagPileCount(float currentFood, float startingFood, int bagPileCount)
        {
            if (bagPileCount <= 0 || startingFood <= 0f || currentFood <= 0f)
            {
                return 0;
            }

            var normalizedFood = Mathf.Clamp01(currentFood / startingFood);
            return Mathf.Clamp(Mathf.CeilToInt(normalizedFood * bagPileCount), 0, bagPileCount);
        }

        private void Subscribe()
        {
            if (isSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodChanged += HandleFoodChanged;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodChanged -= HandleFoodChanged;
            isSubscribed = false;
        }

        private void HandleFoodChanged(float currentFood, float startingFood)
        {
            SetVisibleBagPiles(GetVisibleBagPileCount(currentFood, startingFood, foodBagRenderers.Length));
        }

        private void RefreshVisuals()
        {
            if (foodStore == null)
            {
                foodStore = GetComponent<FoodStore>();
            }

            if (barnRenderer != null)
            {
                barnRenderer.enabled = true;
            }

            if (foodStore != null)
            {
                SetVisibleBagPiles(GetVisibleBagPileCount(
                    foodStore.CurrentFood,
                    foodStore.StartingFood,
                    foodBagRenderers.Length));
            }
        }

        private void SetVisibleBagPiles(int visibleBagPiles)
        {
            for (var index = 0; index < foodBagRenderers.Length; index++)
            {
                if (foodBagRenderers[index] != null)
                {
                    foodBagRenderers[index].enabled = index < visibleBagPiles;
                }
            }
        }
    }
}
