using TAD.Levels;
using TAD.Match;
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
        [SerializeField] private UnityEngine.UI.Button restartButton;
        [SerializeField] private UnityEngine.UI.Button nextLevelButton;
        [SerializeField] private UnityEngine.UI.Button mainMenuButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private LevelLoadingOverlay levelLoadingOverlayPrefab;
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private GameObject resumeSelectionTarget;
        private bool buttonsSubscribed;
        private bool isChoosingRestartRole;
        private bool isRestarting;
        private bool isLoadingNextLevel;
        private Vector2 panelSizeWithoutNextLevel;
        private bool hasPanelSizeWithoutNextLevel;

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
            RefreshNextLevelButton();
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

        /// <summary>
        /// Loads the immediate next unlocked catalog level while retaining the selected player side.
        /// </summary>
        public void NextLevel()
        {
            ResolveReferences();
            if (isRestarting || isChoosingRestartRole || isLoadingNextLevel)
            {
                return;
            }

            if (!TryGetNextLevel(out LevelDefinition nextLevel, out var sceneBuildIndex))
            {
                RefreshNextLevelButton();
                SelectNextLevelOrRestartButton();
                return;
            }

            if (levelLoadingOverlayPrefab == null)
            {
                Debug.LogError("Pause Overlay is missing its Level Loading Overlay prefab.", this);
                RefreshNextLevelButton();
                SelectNextLevelOrRestartButton();
                return;
            }

            resumeSelectionTarget = null;
            modalOverlay?.Hide();
            Time.timeScale = 1f;

            var loadingOverlay = Instantiate(levelLoadingOverlayPrefab);
            isLoadingNextLevel = loadingOverlay.TryLoadScene(
                sceneBuildIndex,
                nextLevel.SceneName,
                () => HandleNextLevelLoadFailed(loadingOverlay));
            if (!isLoadingNextLevel)
            {
                HandleNextLevelLoadFailed(loadingOverlay);
            }
        }

        /// <summary>Prompts for the player side, then restarts the active level.</summary>
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
            Debug.LogError("Pause Overlay could not show the restart side selection.", this);
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

            MatchRoleSelection.Select(role);
            isRestarting = true;
            resumeSelectionTarget = null;
            modalOverlay?.Hide();
            Time.timeScale = 1f;
            SceneManager.LoadScene(activeScene.buildIndex);
        }

        private void HandleNextLevelLoadFailed(LevelLoadingOverlay loadingOverlay)
        {
            isLoadingNextLevel = false;
            if (loadingOverlay != null)
            {
                Destroy(loadingOverlay.gameObject);
            }

            modalOverlay?.Show();
            RefreshNextLevelButton();
            SelectNextLevelOrRestartButton();
        }

        private void CancelRestartRoleChoice()
        {
            isChoosingRestartRole = false;
            SelectRestartButton();
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
            EnsureRestartButton();

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

            if (restartButton != null)
            {
                restartButton.onClick.AddListener(Restart);
            }

            if (nextLevelButton != null)
            {
                nextLevelButton.onClick.AddListener(NextLevel);
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

            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(Restart);
            }

            if (nextLevelButton != null)
            {
                nextLevelButton.onClick.RemoveListener(NextLevel);
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

        private void SelectRestartButton()
        {
            if (EventSystem.current != null && restartButton != null)
            {
                EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
            }
        }

        private void SelectNextLevelOrRestartButton()
        {
            if (EventSystem.current == null)
            {
                return;
            }

            if (nextLevelButton != null && nextLevelButton.gameObject.activeInHierarchy)
            {
                EventSystem.current.SetSelectedGameObject(nextLevelButton.gameObject);
                return;
            }

            SelectRestartButton();
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();

            if (playButton == null ||
                restartButton == null ||
                nextLevelButton == null ||
                mainMenuButton == null ||
                quitButton == null)
            {
                foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    switch (button.gameObject.name)
                    {
                        case "PlayButton":
                            playButton ??= button;
                            break;
                        case "RestartButton":
                            restartButton ??= button;
                            break;
                        case "NextLevelButton":
                            nextLevelButton ??= button;
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

            EnsureRestartButton();
            EnsureNextLevelButton();
        }

        private void EnsureRestartButton()
        {
            if (restartButton != null || playButton == null || playButton.transform.parent == null)
            {
                return;
            }

            GameObject restartObject = Instantiate(playButton.gameObject, playButton.transform.parent);
            restartObject.name = "RestartButton";
            restartObject.layer = gameObject.layer;
            restartObject.transform.SetSiblingIndex(playButton.transform.GetSiblingIndex() + 1);

            restartButton = restartObject.GetComponent<UnityEngine.UI.Button>();
            if (restartButton == null)
            {
                Destroy(restartObject);
                return;
            }

            restartButton.onClick.RemoveAllListeners();
            UnityEngine.UI.Text label = restartButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (label != null)
            {
                label.text = "Restart";
            }

            var pausePanelRect = playButton.transform.parent as RectTransform;
            if (pausePanelRect != null)
            {
                pausePanelRect.sizeDelta = new Vector2(
                    pausePanelRect.sizeDelta.x,
                    Mathf.Max(pausePanelRect.sizeDelta.y, 500f));
            }
        }

        private void EnsureNextLevelButton()
        {
            if (nextLevelButton != null || restartButton == null || restartButton.transform.parent == null)
            {
                return;
            }

            var panelRect = restartButton.transform.parent as RectTransform;
            if (panelRect != null && !hasPanelSizeWithoutNextLevel)
            {
                panelSizeWithoutNextLevel = panelRect.sizeDelta;
                hasPanelSizeWithoutNextLevel = true;
            }

            GameObject nextLevelObject = Instantiate(restartButton.gameObject, restartButton.transform.parent);
            nextLevelObject.name = "NextLevelButton";
            nextLevelObject.layer = gameObject.layer;
            nextLevelObject.transform.SetSiblingIndex(restartButton.transform.GetSiblingIndex() + 1);

            nextLevelButton = nextLevelObject.GetComponent<UnityEngine.UI.Button>();
            if (nextLevelButton == null)
            {
                Destroy(nextLevelObject);
                return;
            }

            nextLevelButton.onClick.RemoveAllListeners();
            UnityEngine.UI.Text label = nextLevelButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (label != null)
            {
                label.text = "Next Level";
            }

            nextLevelButton.gameObject.SetActive(false);
        }

        private void RefreshNextLevelButton()
        {
            EnsureNextLevelButton();
            if (nextLevelButton == null)
            {
                return;
            }

            var shouldShow = TryGetNextLevel(out _, out _);
            nextLevelButton.gameObject.SetActive(shouldShow);

            var panelRect = nextLevelButton.transform.parent as RectTransform;
            if (panelRect == null)
            {
                return;
            }

            if (!hasPanelSizeWithoutNextLevel)
            {
                panelSizeWithoutNextLevel = panelRect.sizeDelta;
                hasPanelSizeWithoutNextLevel = true;
            }

            panelRect.sizeDelta = new Vector2(
                panelRect.sizeDelta.x,
                shouldShow
                    ? Mathf.Max(panelSizeWithoutNextLevel.y + 68f, 568f)
                    : panelSizeWithoutNextLevel.y);
        }

        private bool TryGetNextLevel(out LevelDefinition nextLevel, out int sceneBuildIndex)
        {
            return LevelCatalogNavigation.TryGetNextUnlockedLevel(
                levelCatalog,
                SceneManager.GetActiveScene().name,
                out nextLevel,
                out sceneBuildIndex);
        }
    }
}
