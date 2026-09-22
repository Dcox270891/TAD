using TAD.Environment;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Creates the Food Store HUD for its owning Food Store without requiring a scene-specific reference.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FoodStore))]
    public sealed class FoodStoreHudBootstrap : MonoBehaviour
    {
        [SerializeField] private FoodStoreHud hudPrefab;

        private FoodStoreHud spawnedHud;

        private void Start()
        {
            if (hudPrefab == null)
            {
                return;
            }

            var foodStore = GetComponent<FoodStore>();
            if (foodStore == null)
            {
                return;
            }

            spawnedHud = Instantiate(hudPrefab);
            spawnedHud.Bind(foodStore);
        }

        private void OnDestroy()
        {
            if (spawnedHud != null)
            {
                Destroy(spawnedHud.gameObject);
            }
        }
    }
}
