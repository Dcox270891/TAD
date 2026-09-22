using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Connects the player-facing Play button to a manual Wave Controller start.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaveStartButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button playButton;

        private WaveController waveController;
        private bool isSubscribed;

        /// <summary>Provides the Wave Controller this button should start.</summary>
        public void Bind(WaveController controller)
        {
            if (waveController == controller)
            {
                RefreshInteractableState();
                return;
            }

            UnsubscribeFromWaveController();
            waveController = controller;
            SubscribeToWaveController();
            RefreshInteractableState();
        }

        /// <summary>Gets the selectable visual element used for controller focus.</summary>
        public GameObject SelectableGameObject => playButton != null ? playButton.gameObject : gameObject;

        private void Awake()
        {
            playButton ??= GetComponentInChildren<UnityEngine.UI.Button>(true);
        }

        private void OnEnable()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(HandlePlayPressed);
            }

            SubscribeToWaveController();
            RefreshInteractableState();
        }

        private void Start()
        {
            RefreshInteractableState();
        }

        private void OnDisable()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(HandlePlayPressed);
            }

            UnsubscribeFromWaveController();
        }

        private void HandlePlayPressed()
        {
            if (ModalOverlay.IsGameplayInputBlocked || waveController == null)
            {
                return;
            }

            // StartNextWave assigns its coroutine before returning, so this locks immediately,
            // including a configured pre-wave delay before the first attacker appears.
            waveController.StartNextWave();
            RefreshInteractableState();
        }

        private void SubscribeToWaveController()
        {
            if (isSubscribed || waveController == null)
            {
                return;
            }

            waveController.WaveStarted += HandleWaveStateChanged;
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

            waveController.WaveStarted -= HandleWaveStateChanged;
            waveController.WaveRunFinished -= HandleWaveRunFinished;
            waveController.AllWavesCompleted -= HandleAllWavesCompleted;
            isSubscribed = false;
        }

        private void HandleWaveStateChanged(int waveNumber, int attackerCount)
        {
            RefreshInteractableState();
        }

        private void HandleWaveRunFinished(bool completed)
        {
            RefreshInteractableState();
        }

        private void HandleAllWavesCompleted()
        {
            RefreshInteractableState();
        }

        private void RefreshInteractableState()
        {
            if (playButton == null)
            {
                return;
            }

            var isReadyToPlay = waveController != null &&
                                waveController.CanStartNextWave &&
                                !ModalOverlay.IsGameplayInputBlocked;

            // Keep this presenter enabled so it can receive the completion event that makes the
            // button visible again. The nested button owns the visual and interaction state.
            if (playButton.gameObject != gameObject)
            {
                playButton.gameObject.SetActive(isReadyToPlay);
            }

            playButton.interactable = isReadyToPlay;
        }
    }
}
