using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    /// <summary>
    /// Presents the player-initiated pause menu. Its Modal Overlay owns the paused clock and input block.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class PauseOverlay : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Button playButton;
        [SerializeField] private UnityEngine.UI.Button mainMenuButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private GameObject resumeSelectionTarget;
        private bool buttonsSubscribed;

        public bool IsVisible => modalOverlay != null && modalOverlay.IsVisible;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SubscribeButtons();
        }

        private void OnDisable()
        {
            UnsubscribeButtons();
            resumeSelectionTarget = null;
        }

        /// <summary>
        /// Opens this pause menu when no other modal has already claimed gameplay input.
        /// </summary>
        public bool ShowPause(GameObject selectionAfterResume)
        {
            ResolveReferences();
            if (modalOverlay == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            resumeSelectionTarget = selectionAfterResume;
            modalOverlay.Show();
            SelectPlayButton();
            return true;
        }

        /// <summary>Closes the pause menu and lets Modal Overlay restore the previously active game clock.</summary>
        public void Play()
        {
            if (modalOverlay == null || !modalOverlay.IsVisible)
            {
                return;
            }

            var selectionTarget = resumeSelectionTarget;
            resumeSelectionTarget = null;
            modalOverlay.Hide();

            if (EventSystem.current != null && selectionTarget != null && selectionTarget.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(selectionTarget);
            }
        }

        /// <summary>Leaves the paused match and loads the project's Main Menu scene.</summary>
        public void GoToMainMenu()
        {
            if (string.IsNullOrWhiteSpace(mainMenuSceneName) ||
                !Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError($"Pause Overlay cannot load Main Menu scene '{mainMenuSceneName}'.", this);
                return;
            }

            modalOverlay?.Hide();
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        /// <summary>Closes the standalone application. In the Editor this stops Play Mode.</summary>
        public void Quit()
        {
            modalOverlay?.Hide();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Assigns prefab-owned controls without registering runtime listeners in the Editor.</summary>
        public void Configure(
            ModalOverlay overlay,
            UnityEngine.UI.Button play,
            UnityEngine.UI.Button mainMenu,
            UnityEngine.UI.Button quit)
        {
            UnsubscribeButtons();
            modalOverlay = overlay;
            playButton = play;
            mainMenuButton = mainMenu;
            quitButton = quit;

            if (Application.isPlaying && isActiveAndEnabled)
            {
                SubscribeButtons();
            }
        }

        private void SubscribeButtons()
        {
            if (buttonsSubscribed)
            {
                return;
            }

            ResolveReferences();

            if (playButton != null)
            {
                playButton.onClick.AddListener(Play);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.AddListener(GoToMainMenu);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(Quit);
            }

            buttonsSubscribed = true;
        }

        private void UnsubscribeButtons()
        {
            if (!buttonsSubscribed)
            {
                return;
            }

            if (playButton != null)
            {
                playButton.onClick.RemoveListener(Play);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(GoToMainMenu);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(Quit);
            }

            buttonsSubscribed = false;
        }

        private void SelectPlayButton()
        {
            if (EventSystem.current != null && playButton != null)
            {
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            }
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();

            if (playButton != null && mainMenuButton != null && quitButton != null)
            {
                return;
            }

            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                switch (button.gameObject.name)
                {
                    case "PlayButton":
                        playButton ??= button;
                        break;
                    case "MainMenuButton":
                        mainMenuButton ??= button;
                        break;
                    case "QuitButton":
                        quitButton ??= button;
                        break;
                }
            }
        }
    }
}