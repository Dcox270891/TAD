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

        /// <summary>Provides the Wave Controller this button should start.</summary>
        public void Bind(WaveController controller)
        {
            waveController = controller;
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
        }

        private void HandlePlayPressed()
        {
            if (ModalOverlay.IsGameplayInputBlocked || waveController == null)
            {
                return;
            }

            if (waveController.StartWaves())
            {
                // The first wave is underway; hide the one-shot control to prevent a duplicate sequence.
                gameObject.SetActive(false);
                return;
            }

            RefreshInteractableState();
        }

        private void RefreshInteractableState()
        {
            if (playButton == null)
            {
                return;
            }

            playButton.interactable = waveController != null &&
                                      !waveController.IsRunning &&
                                      !waveController.HasCompletedAllWaves &&
                                      waveController.TotalWaveCount > 0 &&
                                      !ModalOverlay.IsGameplayInputBlocked;
        }
    }
}
