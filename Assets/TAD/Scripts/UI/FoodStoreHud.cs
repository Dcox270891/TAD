using TAD.Environment;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Presents a Food Store's remaining supply as a screen-space HUD bar.
    /// The Food Store remains the sole owner of its food state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoodStoreHud : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Image fillImage;
        [SerializeField] private UnityEngine.UI.Text amountText;
        [SerializeField] private Color depletedColor = new(0.88f, 0.18f, 0.16f, 1f);
        [SerializeField] private Color fullColor = new(0.18f, 0.78f, 0.28f, 1f);

        private FoodStore foodStore;
        private bool isSubscribed;

        /// <summary>Binds this display to the supplied Food Store and immediately presents its current state.</summary>
        public void Bind(FoodStore source)
        {
            if (foodStore == source)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            foodStore = source;
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

        private void Refresh()
        {
            if (foodStore != null)
            {
                HandleFoodChanged(foodStore.CurrentFood, foodStore.StartingFood);
            }
        }

        private void HandleFoodChanged(float currentFood, float maximumFood)
        {
            var normalizedFood = maximumFood > 0f
                ? Mathf.Clamp01(currentFood / maximumFood)
                : 0f;

            if (fillImage != null)
            {
                fillImage.fillAmount = normalizedFood;
                fillImage.color = Color.Lerp(depletedColor, fullColor, normalizedFood);
            }

            if (amountText != null)
            {
                amountText.text = $"Food store: {currentFood:0.#} / {maximumFood:0.#}";
            }
        }
    }
}
