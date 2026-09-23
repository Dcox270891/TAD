using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    /// <summary>
    /// Shows the pause control while a manually started wave is active and opens the shared-style pause menu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button pauseButton;

        private WaveController waveController;
        private PauseOverlay pauseOverlay;
        private bool isSubscribed;

        /// <summary>Gets the selectable visual element used to return controller focus after resuming.</summary>
        public GameObject SelectableGameObject => pauseButton != null ? pauseButton.gameObject : gameObject;

        private void Awake()
        {
            pauseButton ??= GetComponentInChildren<UnityEngine.UI.Button>(true);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(HandlePausePressed);
            }

            SubscribeToWaveController();
            RefreshVisibility();
        }

        private void Start()
        {
            RefreshVisibility();
        }

        private void OnDisable()
        {
            if (pauseButton != null)
            {
                pauseButton.onClick.RemoveListener(HandlePausePressed);
            }

            UnsubscribeFromWaveController();
        }

        /// <summary>Provides the active Wave Controller and pause menu that this control should use.</summary>
        public void Bind(WaveController controller, PauseOverlay overlay)
        {
            if (waveController == controller && pauseOverlay == overlay)
            {
                RefreshVisibility();
                return;
            }

            UnsubscribeFromWaveController();
            waveController = controller;
            pauseOverlay = overlay;
            SubscribeToWaveController();
            RefreshVisibility();
        }

        private void HandlePausePressed()
        {
            if (waveController == null || !waveController.IsRunning || ModalOverlay.IsGameplayInputBlocked)
            {
                return;
            }

            var overlay = ResolvePauseOverlay();
            overlay?.ShowPause(SelectableGameObject);
        }

        private PauseOverlay ResolvePauseOverlay()
        {
            pauseOverlay ??= Object.FindFirstObjectByType<PauseOverlay>(FindObjectsInactive.Include);
            return pauseOverlay;
        }

        private void SubscribeToWaveController()
        {
            if (isSubscribed || waveController == null)
            {
                return;
            }

            waveController.WaveStarted += HandleWaveStarted;
            waveController.WaveRunFinished += HandleWaveRunFinished;
            waveController.AllWavesCompleted += HandleAllWavesCompleted;
            isSubscribed = true;
        }

        private void UnsubscribeFromWaveController()
        {
            if (!isSubscribed || waveController == null)
            {
                return;
            }

            waveController.WaveStarted -= HandleWaveStarted;
            waveController.WaveRunFinished -= HandleWaveRunFinished;
            waveController.AllWavesCompleted -= HandleAllWavesCompleted;
            isSubscribed = false;
        }

        private void HandleWaveStarted(int waveNumber, int attackerCount)
        {
            RefreshVisibility();

            if (EventSystem.current != null && pauseButton != null && pauseButton.gameObject.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(pauseButton.gameObject);
            }
        }

        private void HandleWaveRunFinished(bool completed)
        {
            RefreshVisibility();
        }

        private void HandleAllWavesCompleted()
        {
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (pauseButton == null)
            {
                return;
            }

            var isWaveActive = waveController != null && waveController.IsRunning;
            if (pauseButton.gameObject != gameObject)
            {
                pauseButton.gameObject.SetActive(isWaveActive);
            }

            pauseButton.interactable = isWaveActive;
        }
    }
}