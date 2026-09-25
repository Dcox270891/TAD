using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Connects the player-facing Play button to either a queued attacker wave or the next configured wave.</summary>
    [DisallowMultipleComponent]
    public sealed class WaveStartButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button playButton;
        [SerializeField] private UnityEngine.UI.Text playLabel;

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private bool isSubscribedToWaveController;
        private bool isSubscribedToQueue;

        /// <summary>Provides the Wave Controller this button should start.</summary>
        public void Bind(WaveController controller)
        {
            UnsubscribeFromWaveController();
            UnsubscribeFromQueue();
            waveController = controller;
            attackerWaveQueue = waveController != null ? waveController.GetComponent<AttackerWaveQueue>() : null;
            SubscribeToWaveController();
            SubscribeToQueue();
            RefreshInteractableState();
        }

        /// <summary>Gets the selectable visual element used for controller focus.</summary>
        public GameObject SelectableGameObject => playButton != null ? playButton.gameObject : gameObject;

        private void Awake()
        {
            playButton ??= GetComponentInChildren<UnityEngine.UI.Button>(true);
            playLabel ??= playButton != null ? playButton.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
        }

        private void OnEnable()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(HandlePlayPressed);
            }

            SubscribeToWaveController();
            SubscribeToQueue();
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
            UnsubscribeFromQueue();
        }

        private void HandlePlayPressed()
        {
            if (ModalOverlay.IsGameplayInputBlocked || waveController == null)
            {
                return;
            }

            if (attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers)
            {
                attackerWaveQueue.TrySendQueuedWave();
            }
            else
            {
                waveController.StartNextWave();
            }

            RefreshInteractableState();
        }

        private void SubscribeToWaveController()
        {
            if (isSubscribedToWaveController || waveController == null)
            {
                return;
            }

            waveController.WaveStarted += HandleWaveStateChanged;
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

            waveController.WaveStarted -= HandleWaveStateChanged;
            waveController.WaveRunFinished -= HandleWaveRunFinished;
            waveController.AllWavesCompleted -= HandleAllWavesCompleted;
            isSubscribedToWaveController = false;
        }

        private void SubscribeToQueue()
        {
            if (isSubscribedToQueue || attackerWaveQueue == null)
            {
                return;
            }

            attackerWaveQueue.QueueChanged += HandleQueueChanged;
            isSubscribedToQueue = true;
        }

        private void UnsubscribeFromQueue()
        {
            if (!isSubscribedToQueue || attackerWaveQueue == null)
            {
                return;
            }

            attackerWaveQueue.QueueChanged -= HandleQueueChanged;
            isSubscribedToQueue = false;
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

        private void HandleQueueChanged()
        {
            RefreshInteractableState();
        }

        private void RefreshInteractableState()
        {
            if (playButton == null)
            {
                return;
            }

            var hasQueuedAttackers = attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers;
            var isReadyToPlay = waveController != null &&
                                !ModalOverlay.IsGameplayInputBlocked &&
                                (hasQueuedAttackers
                                    ? attackerWaveQueue.CanSendQueuedWave
                                    : waveController.CanStartNextWave);

            if (playLabel != null)
            {
                playLabel.text = hasQueuedAttackers ? "Send Wave" : "Play";
            }

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
