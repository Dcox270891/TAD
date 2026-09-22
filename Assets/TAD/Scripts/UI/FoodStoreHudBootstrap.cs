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
        private bool ownsSpawnedHud;

        private void Start()
        {
            var foodStore = GetComponent<FoodStore>();
            if (foodStore == null)
            {
                return;
            }

            spawnedHud = Object.FindFirstObjectByType<FoodStoreHud>(FindObjectsInactive.Include);
            if (spawnedHud == null)
            {
                if (hudPrefab == null)
                {
                    return;
                }

                spawnedHud = Instantiate(hudPrefab);
                ownsSpawnedHud = true;
            }

            spawnedHud.Bind(foodStore);
        }

        private void OnDestroy()
        {
            if (ownsSpawnedHud && spawnedHud != null)
            {
                Destroy(spawnedHud.gameObject);
            }
        }
    }
}
