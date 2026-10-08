using TAD.Levels;
using TAD.Match;
using TAD.Progression;
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
        [SerializeField] private PlayerMatchExperiencePresentation experiencePresentation;
        [SerializeField] private UnityEngine.UI.Button restartButton;
        [SerializeField] private UnityEngine.UI.Button nextLevelButton;
        [SerializeField] private UnityEngine.UI.Button mainMenuButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private LevelLoadingOverlay levelLoadingOverlayPrefab;

        private DayCycleController dayCycle;
        private bool buttonsSubscribed;
        private bool dayCycleSubscribed;
        private bool matchExperiencePresented;
        private bool isRestarting;
        private bool isChoosingRestartRole;
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
            ResolveDayCycle();
            SubscribeToDayCycle();
        }

        private void OnDisable()
        {
            UnsubscribeButtons();
            UnsubscribeFromDayCycle();
            experiencePresentation?.Dismiss();
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
            matchExperiencePresented = false;
            SubscribeToDayCycle();
            ShowIfDefendersHaveWon();
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
            Debug.LogError("Defender Win Overlay could not show the restart side selection.", this);
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
                Debug.LogError("Defender Win Overlay is missing its Level Loading Overlay prefab.", this);
                SelectRestartButton();
                return;
            }

            if (!MatchStartEnergyGate.TryPayForNewMatch(
                    transform,
                    restartButton,
                    restartButton != null ? restartButton.gameObject : null,
                    out MatchStartEnergyCharge energyCharge))
            {
                return;
            }

            MatchRoleSelection.Select(role);
            HideMatchExperiencePresentation();
            modalOverlay?.Hide();
            Time.timeScale = 1f;

            var loadingOverlay = Instantiate(levelLoadingOverlayPrefab);
            isRestarting = loadingOverlay.TryLoadScene(
                activeScene.buildIndex,
                activeScene.name,
                () => HandleRestartLoadFailed(loadingOverlay, energyCharge));
            if (!isRestarting)
            {
                HandleRestartLoadFailed(loadingOverlay, energyCharge);
            }
        }

        /// <summary>
        /// Loads the immediate next unlocked catalog level without changing the selected player side.
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
                Debug.LogError("Defender Win Overlay is missing its Level Loading Overlay prefab.", this);
                RefreshNextLevelButton();
                SelectNextLevelOrRestartButton();
                return;
            }

            if (!MatchStartEnergyGate.TryPayForNewMatch(
                    transform,
                    nextLevelButton,
                    nextLevelButton != null ? nextLevelButton.gameObject : null,
                    out MatchStartEnergyCharge energyCharge))
            {
                return;
            }

            HideMatchExperiencePresentation();
            modalOverlay?.Hide();
            Time.timeScale = 1f;

            var loadingOverlay = Instantiate(levelLoadingOverlayPrefab);
            isLoadingNextLevel = loadingOverlay.TryLoadScene(
                sceneBuildIndex,
                nextLevel.SceneName,
                () => HandleNextLevelLoadFailed(loadingOverlay, energyCharge));
            if (!isLoadingNextLevel)
            {
                HandleNextLevelLoadFailed(loadingOverlay, energyCharge);
            }
        }

        private void CancelRestartRoleChoice()
        {
            isChoosingRestartRole = false;
            SelectRestartButton();
        }

        private void HandleNextLevelLoadFailed(
            LevelLoadingOverlay loadingOverlay,
            MatchStartEnergyCharge energyCharge)
        {
            isLoadingNextLevel = false;
            energyCharge?.Refund();
            if (loadingOverlay != null)
            {
                Destroy(loadingOverlay.gameObject);
            }

            modalOverlay?.Show();
            ShowMatchExperienceIfAvailable();
            RefreshNextLevelButton();
            SelectNextLevelOrRestartButton();
        }

        private void HandleRestartLoadFailed(
            LevelLoadingOverlay loadingOverlay,
            MatchStartEnergyCharge energyCharge)
        {
            isRestarting = false;
            energyCharge?.Refund();
            if (loadingOverlay != null)
            {
                Destroy(loadingOverlay.gameObject);
            }

            modalOverlay?.Show();
            ShowMatchExperienceIfAvailable();
            SelectRestartButton();
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

            HideMatchExperiencePresentation();
            modalOverlay?.Hide();
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }

        /// <summary>Closes the standalone application. In the Editor this stops Play Mode.</summary>
        public void Quit()
        {
            HideMatchExperiencePresentation();
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

        private void SubscribeToDayCycle()
        {
            if (!Application.isPlaying || dayCycleSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.DefendersWon += HandleDefendersWon;
            dayCycle.PlayerExperienceAwarded += HandlePlayerExperienceAwarded;
            dayCycleSubscribed = true;
        }

        private void UnsubscribeFromDayCycle()
        {
            if (!dayCycleSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.DefendersWon -= HandleDefendersWon;
            dayCycle.PlayerExperienceAwarded -= HandlePlayerExperienceAwarded;
            dayCycleSubscribed = false;
        }

        private void HandleDefendersWon()
        {
            RefreshNextLevelButton();
            modalOverlay?.Show();
            ShowMatchExperienceIfAvailable();
            SelectRestartButton();
        }

        private void ShowIfDefendersHaveWon()
        {
            if (dayCycle != null && dayCycle.IsComplete && dayCycle.CurrentDay >= 10)
            {
                HandleDefendersWon();
            }
        }

        private void HandlePlayerExperienceAwarded(PlayerMatchExperienceReward reward)
        {
            if (modalOverlay != null && modalOverlay.IsVisible)
            {
                ShowMatchExperience(reward);
            }
        }

        private void ShowMatchExperienceIfAvailable()
        {
            if (dayCycle != null && dayCycle.HasAwardedPlayerExperience)
            {
                ShowMatchExperience(dayCycle.LastPlayerExperienceReward);
            }
        }

        private void ShowMatchExperience(PlayerMatchExperienceReward reward)
        {
            if (matchExperiencePresented)
            {
                return;
            }

            ResolveReferences();
            if (experiencePresentation != null && experiencePresentation.Present(reward, restartButton))
            {
                matchExperiencePresented = true;
            }
        }

        private void HideMatchExperiencePresentation()
        {
            matchExperiencePresented = false;
            experiencePresentation?.Dismiss();
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
            experiencePresentation ??= GetComponent<PlayerMatchExperiencePresentation>();
            if (experiencePresentation == null && Application.isPlaying && GetComponent<Canvas>() != null)
            {
                experiencePresentation = gameObject.AddComponent<PlayerMatchExperiencePresentation>();
            }
            if (restartButton == null ||
                nextLevelButton == null ||
                mainMenuButton == null ||
                quitButton == null)
            {
                foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    switch (button.gameObject.name)
                    {
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

            EnsureNextLevelButton();
        }

        private void ResolveDayCycle()
        {
            dayCycle ??= FindAnyObjectByType<DayCycleController>(FindObjectsInactive.Include);
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
                    ? Mathf.Max(panelSizeWithoutNextLevel.y + 68f, 500f)
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
