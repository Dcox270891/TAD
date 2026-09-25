using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    /// <summary>Shows the compact pause control throughout an active level and opens the shared pause menu.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button pauseButton;

        private WaveController waveController;
        private DayCycleController dayCycle;
        private PauseOverlay pauseOverlay;
        private bool isSubscribedToWaveController;
        private bool isSubscribedToDayCycle;

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
            SubscribeToDayCycle();
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
            UnsubscribeFromDayCycle();
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
            UnsubscribeFromDayCycle();
            waveController = controller;
            dayCycle = waveController != null ? waveController.GetComponent<DayCycleController>() : null;
            pauseOverlay = overlay;
            SubscribeToWaveController();
            SubscribeToDayCycle();
            RefreshVisibility();
        }

        private void HandlePausePressed()
        {
            var canPause = dayCycle != null ? dayCycle.CanPause : waveController != null && waveController.IsRunning;
            if (!canPause || ModalOverlay.IsGameplayInputBlocked)
            {
                return;
            }

            var overlay = ResolvePauseOverlay();
            overlay?.ShowPause(SelectableGameObject);
        }

        private PauseOverlay ResolvePauseOverlay()
        {
            pauseOverlay ??= Object.FindAnyObjectByType<PauseOverlay>(FindObjectsInactive.Include);
            return pauseOverlay;
        }

        private void SubscribeToWaveController()
        {
            if (isSubscribedToWaveController || waveController == null)
            {
                return;
            }

            waveController.WaveStarted += HandleWaveStarted;
            waveController.WaveRunFinished += HandleWaveRunFinished;
            waveController.AllWavesCompleted += HandleAllWavesCompleted;
            isSubscribedToWaveController = true;
        }

        private void UnsubscribeFromWaveController()
        {
            if (!isSubscribedToWaveController || waveController == null)
            {
                return;
            }

            waveController.WaveStarted -= HandleWaveStarted;
            waveController.WaveRunFinished -= HandleWaveRunFinished;
            waveController.AllWavesCompleted -= HandleAllWavesCompleted;
            isSubscribedToWaveController = false;
        }

        private void SubscribeToDayCycle()
        {
            if (isSubscribedToDayCycle || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged += RefreshVisibility;
            isSubscribedToDayCycle = true;
        }

        private void UnsubscribeFromDayCycle()
        {
            if (!isSubscribedToDayCycle || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged -= RefreshVisibility;
            isSubscribedToDayCycle = false;
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

            var isPauseAvailable = dayCycle != null ? dayCycle.CanPause : waveController != null && waveController.IsRunning;
            if (pauseButton.gameObject != gameObject)
            {
                pauseButton.gameObject.SetActive(isPauseAvailable);
            }

            pauseButton.interactable = isPauseAvailable;
        }
    }
}
