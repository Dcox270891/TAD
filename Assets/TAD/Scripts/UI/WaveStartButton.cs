using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Connects the compact Play control to the current preparation period.</summary>
    [DisallowMultipleComponent]
    public sealed class WaveStartButton : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button playButton;
        [SerializeField] private UnityEngine.UI.Text playLabel;

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private DayCycleController dayCycle;
        private bool isSubscribedToWaveController;
        private bool isSubscribedToQueue;
        private bool isSubscribedToDayCycle;

        /// <summary>Provides the Wave Controller this button should start.</summary>
        public void Bind(WaveController controller)
        {
            UnsubscribeFromWaveController();
            UnsubscribeFromQueue();
            UnsubscribeFromDayCycle();
            waveController = controller;
            attackerWaveQueue = waveController != null ? waveController.GetComponent<AttackerWaveQueue>() : null;
            dayCycle = waveController != null ? waveController.GetComponent<DayCycleController>() : null;
            SubscribeToWaveController();
            SubscribeToQueue();
            SubscribeToDayCycle();
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
            SubscribeToDayCycle();
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
            UnsubscribeFromDayCycle();
        }

        private void HandlePlayPressed()
        {
            if (ModalOverlay.IsGameplayInputBlocked || waveController == null)
            {
                return;
            }

            if (dayCycle != null)
            {
                dayCycle.TryStartQueuedWavesEarly();
            }
            else if (attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers)
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

        private void SubscribeToDayCycle()
        {
            if (isSubscribedToDayCycle || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged += RefreshInteractableState;
            isSubscribedToDayCycle = true;
        }

        private void UnsubscribeFromDayCycle()
        {
            if (!isSubscribedToDayCycle || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged -= RefreshInteractableState;
            isSubscribedToDayCycle = false;
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

            var isReadyToPlay = dayCycle != null
                ? !ModalOverlay.IsGameplayInputBlocked && dayCycle.CanStartQueuedWavesEarly
                : waveController != null &&
                  !ModalOverlay.IsGameplayInputBlocked &&
                  (attackerWaveQueue != null && attackerWaveQueue.HasQueuedAttackers
                      ? attackerWaveQueue.CanSendQueuedWave
                      : waveController.CanStartNextWave);

            if (playLabel != null)
            {
                playLabel.text = ">";
            }

            // The compact play icon only appears once a queued group is ready to send, then returns
            // at the next preparation period. The presenter remains active to receive those updates.
            if (playButton.gameObject != gameObject)
            {
                playButton.gameObject.SetActive(isReadyToPlay);
            }

            playButton.interactable = isReadyToPlay;
        }
    }
}
