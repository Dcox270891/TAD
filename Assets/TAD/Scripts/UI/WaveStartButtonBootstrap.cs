using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Suppresses automatic waves and creates the Play UI plus its Input System EventSystem.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    public sealed class WaveStartButtonBootstrap : MonoBehaviour
    {
        [SerializeField] private WaveStartButton playButtonPrefab;

        private WaveController waveController;
        private WaveStartButton spawnedPlayButton;
        private bool ownsSpawnedPlayButton;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            waveController.RequireManualStart();
        }

        private void Start()
        {
            if (waveController == null)
            {
                return;
            }

            var eventSystem = EnsureEventSystem();
            spawnedPlayButton = Object.FindFirstObjectByType<WaveStartButton>(FindObjectsInactive.Include);
            if (spawnedPlayButton == null)
            {
                if (playButtonPrefab == null)
                {
                    return;
                }

                spawnedPlayButton = Instantiate(playButtonPrefab);
                ownsSpawnedPlayButton = true;
            }

            spawnedPlayButton.Bind(waveController);

            if (eventSystem != null && spawnedPlayButton.SelectableGameObject.activeInHierarchy)
            {
                eventSystem.SetSelectedGameObject(spawnedPlayButton.SelectableGameObject);
            }
        }

        private void OnDestroy()
        {
            if (ownsSpawnedPlayButton && spawnedPlayButton != null)
            {
                Destroy(spawnedPlayButton.gameObject);
            }
        }

        private static EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return EventSystem.current;
            }

            var eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystemObject.layer = LayerMask.NameToLayer("UI");

            var inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
            return eventSystemObject.GetComponent<EventSystem>();
        }
    }
}
