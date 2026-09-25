using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    /// <summary>
    /// Presents the terminal defender-victory choices after the level's day cycle reaches its final day.
    /// The day cycle remains authoritative; this component only observes its completion event.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class DefenderWinOverlay : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Button restartButton;
        [SerializeField] private UnityEngine.UI.Button mainMenuButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private DayCycleController dayCycle;
        private bool buttonsSubscribed;
        private bool dayCycleSubscribed;

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
            SubscribeToDayCycle();
        }

        private void OnDisable()
        {
            UnsubscribeButtons();
            UnsubscribeFromDayCycle();
        }

        /// <summary>Assigns the cycle whose defender victory should open this overlay.</summary>
        public void Bind(DayCycleController source)
        {
            if (dayCycle == source)
            {
                ShowIfDefendersHaveWon();
                return;
            }

            UnsubscribeFromDayCycle();
            dayCycle = source;
            SubscribeToDayCycle();
            ShowIfDefendersHaveWon();
        }

        /// <summary>Reloads the current level from its initial preparation phase.</summary>
        public void Restart()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.buildIndex < 0)
            {
                return;
            }

            modalOverlay?.Hide();
            Time.timeScale = 1f;
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        /// <summary>Leaves the match and returns to the Main Menu.</summary>
        public void GoToMainMenu()
        {
            if (string.IsNullOrWhiteSpace(mainMenuSceneName) ||
                !Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError($"Defender Win Overlay cannot load Main Menu scene '{mainMenuSceneName}'.", this);
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
            UnityEngine.UI.Button restart,
            UnityEngine.UI.Button mainMenu,
            UnityEngine.UI.Button quit)
        {
            UnsubscribeButtons();
            modalOverlay = overlay;
            restartButton = restart;
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
            if (restartButton != null)
            {
                restartButton.onClick.AddListener(Restart);
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

            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(Restart);
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

        private void SubscribeToDayCycle()
        {
            if (!Application.isPlaying || dayCycleSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.DefendersWon += HandleDefendersWon;
            dayCycleSubscribed = true;
        }

        private void UnsubscribeFromDayCycle()
        {
            if (!dayCycleSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.DefendersWon -= HandleDefendersWon;
            dayCycleSubscribed = false;
        }

        private void HandleDefendersWon()
        {
            modalOverlay?.Show();
            SelectRestartButton();
        }

        private void ShowIfDefendersHaveWon()
        {
            if (dayCycle != null && dayCycle.IsComplete && dayCycle.CurrentDay >= 10)
            {
                HandleDefendersWon();
            }
        }

        private void SelectRestartButton()
        {
            if (EventSystem.current != null && restartButton != null)
            {
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            }
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();
            if (restartButton != null && mainMenuButton != null && quitButton != null)
            {
                return;
            }

            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                switch (button.gameObject.name)
                {
                    case "RestartButton":
                        restartButton ??= button;
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
