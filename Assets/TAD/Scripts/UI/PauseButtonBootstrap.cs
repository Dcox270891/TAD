using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Creates the active-wave Pause control and its pause menu alongside the existing manual wave controls.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    public sealed class PauseButtonBootstrap : MonoBehaviour
    {
        [SerializeField] private PauseButton pauseButtonPrefab;
        [SerializeField] private PauseOverlay pauseOverlayPrefab;

        private WaveController waveController;
        private PauseButton spawnedPauseButton;
        private PauseOverlay spawnedPauseOverlay;
        private bool ownsSpawnedPauseButton;
        private bool ownsSpawnedPauseOverlay;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
        }

        private void Start()
        {
            if (waveController == null)
            {
                return;
            }

            EnsureEventSystem();
            spawnedPauseOverlay = Object.FindAnyObjectByType<PauseOverlay>(FindObjectsInactive.Include);
            if (spawnedPauseOverlay == null && pauseOverlayPrefab != null)
            {
                spawnedPauseOverlay = Instantiate(pauseOverlayPrefab);
                ownsSpawnedPauseOverlay = true;
            }

            spawnedPauseButton = Object.FindAnyObjectByType<PauseButton>(FindObjectsInactive.Include);
            if (spawnedPauseButton == null && pauseButtonPrefab != null)
            {
                spawnedPauseButton = Instantiate(pauseButtonPrefab);
                ownsSpawnedPauseButton = true;
            }

            if (spawnedPauseButton != null)
            {
                spawnedPauseButton.Bind(waveController, spawnedPauseOverlay);
            }
        }

        private void OnDestroy()
        {
            if (ownsSpawnedPauseButton && spawnedPauseButton != null)
            {
                Destroy(spawnedPauseButton.gameObject);
            }

            if (ownsSpawnedPauseOverlay && spawnedPauseOverlay != null)
            {
                Destroy(spawnedPauseOverlay.gameObject);
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
