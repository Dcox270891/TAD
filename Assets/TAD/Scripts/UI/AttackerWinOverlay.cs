using TAD.Environment;
using TAD.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    /// <summary>
    /// Presents the terminal attacker-victory choice when the farm's Food Store is depleted.
    /// The Food Store remains authoritative; this view only observes its depletion event.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class AttackerWinOverlay : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Button restartButton;
        [SerializeField] private UnityEngine.UI.Button mainMenuButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private LevelLoadingOverlay levelLoadingOverlayPrefab;

        private FoodStore foodStore;
        private bool buttonsSubscribed;
        private bool foodStoreSubscribed;
        private bool isRestarting;
        private bool isChoosingRestartRole;

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
            SubscribeToFoodStore();
        }

        private void OnDisable()
        {
            UnsubscribeButtons();
            UnsubscribeFromFoodStore();
        }

        /// <summary>Assigns the Food Store whose depletion should show this terminal overlay.</summary>
        public void Bind(FoodStore source)
        {
            if (foodStore == source)
            {
                ShowIfFoodStoreIsDepleted();
                return;
            }

            UnsubscribeFromFoodStore();
            foodStore = source;
            SubscribeToFoodStore();
            ShowIfFoodStoreIsDepleted();
        }

        /// <summary>Prompts for the player side, then reloads the active scene through the loading transition.</summary>
        public void Restart()
        {
            ResolveReferences();
            if (isRestarting || isChoosingRestartRole)
            {
                return;
            }

            isChoosingRestartRole = true;
            RestartRoleChoiceOverlay roleChoice = RestartRoleChoiceOverlay.Show(
                transform,
                restartButton,
                restartButton != null ? restartButton.gameObject : null,
                RestartWithRole,
                CancelRestartRoleChoice);

            if (roleChoice != null)
            {
                return;
            }

            isChoosingRestartRole = false;
            Debug.LogError("Attacker Win Overlay could not show the restart side selection.", this);
            SelectRestartButton();
        }

        private void RestartWithRole(PlayerRole role)
        {
            isChoosingRestartRole = false;
            if (isRestarting)
            {
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.buildIndex < 0)
            {
                SelectRestartButton();
                return;
            }

            if (levelLoadingOverlayPrefab == null)
            {
                Debug.LogError("Attacker Win Overlay is missing its Level Loading Overlay prefab.", this);
                SelectRestartButton();
                return;
            }

            MatchRoleSelection.Select(role);
            modalOverlay?.Hide();
            Time.timeScale = 1f;

            var loadingOverlay = Instantiate(levelLoadingOverlayPrefab);
            isRestarting = loadingOverlay.TryLoadScene(
                activeScene.buildIndex,
                activeScene.name,
                () => HandleRestartLoadFailed(loadingOverlay));
            if (!isRestarting)
            {
                HandleRestartLoadFailed(loadingOverlay);
            }
        }

        private void CancelRestartRoleChoice()
        {
            isChoosingRestartRole = false;
            SelectRestartButton();
        }

        private void HandleRestartLoadFailed(LevelLoadingOverlay loadingOverlay)
        {
            isRestarting = false;
            if (loadingOverlay != null)
            {
                Destroy(loadingOverlay.gameObject);
            }

            modalOverlay?.Show();
            SelectRestartButton();
        }

        /// <summary>Leaves the match and loads the project's dedicated Main Menu scene.</summary>
        public void GoToMainMenu()
        {
            if (string.IsNullOrWhiteSpace(mainMenuSceneName) ||
                !Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError($"Attacker Win Overlay cannot load Main Menu scene '{mainMenuSceneName}'.", this);
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

        private void SubscribeToFoodStore()
        {
            if (!Application.isPlaying || foodStoreSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodDepleted += HandleFoodDepleted;
            foodStoreSubscribed = true;
        }

        private void UnsubscribeFromFoodStore()
        {
            if (!foodStoreSubscribed || foodStore == null)
            {
                return;
            }

            foodStore.FoodDepleted -= HandleFoodDepleted;
            foodStoreSubscribed = false;
        }

        private void HandleFoodDepleted()
        {
            modalOverlay?.Show();
            SelectRestartButton();
        }

        private void ShowIfFoodStoreIsDepleted()
        {
            if (foodStore != null && foodStore.IsDepleted)
            {
                HandleFoodDepleted();
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
