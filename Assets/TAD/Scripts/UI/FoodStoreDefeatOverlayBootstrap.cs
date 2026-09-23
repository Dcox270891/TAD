using TAD.Environment;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Creates one attacker-win overlay for its Food Store and binds it to that store's depletion event.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FoodStore))]
    public sealed class FoodStoreDefeatOverlayBootstrap : MonoBehaviour
    {
        [SerializeField] private AttackerWinOverlay overlayPrefab;

        private AttackerWinOverlay spawnedOverlay;
        private bool ownsSpawnedOverlay;

        private void Start()
        {
            var foodStore = GetComponent<FoodStore>();
            if (foodStore == null)
            {
                return;
            }

            EnsureEventSystem();

            spawnedOverlay = Object.FindFirstObjectByType<AttackerWinOverlay>(FindObjectsInactive.Include);
            if (spawnedOverlay == null)
            {
                if (overlayPrefab == null)
                {
                    Debug.LogError("Food Store needs an Attacker Win Overlay prefab.", this);
                    return;
                }

                spawnedOverlay = Instantiate(overlayPrefab);
                ownsSpawnedOverlay = true;
            }

            spawnedOverlay.Bind(foodStore);
        }

        private void OnDestroy()
        {
            if (ownsSpawnedOverlay && spawnedOverlay != null)
            {
                Destroy(spawnedOverlay.gameObject);
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                eventSystemObject.layer = uiLayer;
            }

            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
