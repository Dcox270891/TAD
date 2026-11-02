using System;
using System.IO;
using TAD.Levels;
using TAD.Match;
using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string FullGameButtonName = "FullGameButton";
        private const string SupportDeveloperButtonName = "SupportDeveloperButton";

        [Header("Menu Buttons")]
        [SerializeField] private UnityEngine.UI.Button selectLevelButton;
        [SerializeField] private UnityEngine.UI.Button playButton;
        [SerializeField] private UnityEngine.UI.Button settingsButton;
        [SerializeField] private UnityEngine.UI.Button playerUpgradesButton;
        [SerializeField] private UnityEngine.UI.Button exitGameButton;

        [Header("Menu Content")]
        [SerializeField] private LevelCatalog levelCatalog;
        [SerializeField] private LevelSelectionOverlay levelSelectionOverlay;
        [SerializeField] private NotImplementedOverlay notImplementedOverlay;
        [SerializeField] private LevelLoadingOverlay levelLoadingOverlay;

        private PlayerProfileData playerProfile;
        private UnityEngine.UI.Button fullGameButton;
        private UnityEngine.UI.Button supportDeveloperButton;
        private bool isSubscribed;
        private bool isLoadingLevel;

        private void Awake()
        {
            ResolveReferences();
            playerProfile = PlayerSettingsService.Instance.Profile;
            EnsurePlayerEnergyPanel();
            EnsureMenuUtilityButtons();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            Select(selectLevelButton != null ? selectLevelButton.gameObject : playButton != null ? playButton.gameObject : null);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                SaveProfile();
            }
        }

        private void OnApplicationQuit()
        {
            SaveProfile();
        }

        /// <summary>Opens the level list. Choosing a level then asks the player which side to control.</summary>
        public void SelectLevel()
        {
            if (levelCatalog == null)
            {
                Debug.LogError("Main Menu is missing its Level Catalog.", this);
                return;
            }

            if (levelSelectionOverlay == null ||
                !levelSelectionOverlay.Show(levelCatalog, ChooseRoleForLevel,
                    selectLevelButton != null ? selectLevelButton.gameObject : null))
            {
                Debug.LogError("Main Menu could not show the Select Level overlay.", this);
            }
        }

        /// <summary>Starts a random available level and randomly assigns the player to Attack or Defend.</summary>
        public void Play()
        {
            if (levelCatalog == null || !levelCatalog.TryGetRandomLevel(out LevelDefinition level))
            {
                Debug.LogError("Main Menu cannot choose a playable level because the Level Catalog is empty.", this);
                return;
            }

            var randomRole = UnityEngine.Random.value < 0.5f
                ? PlayerRole.Attacker
                : PlayerRole.Defender;
            LoadLevel(level, randomRole, playButton);
        }

        public void OpenSettings()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null ||
                PlayerSettingsOverlay.Show(
                    canvas.transform,
                    settingsButton,
                    settingsButton != null ? settingsButton.gameObject : null) == null)
            {
                Debug.LogError("Main Menu could not show the Player Settings overlay.", this);
            }
        }

        public void OpenPlayerUpgrades()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null ||
                PlayerUpgradesOverlay.Show(
                    canvas.transform,
                    playerUpgradesButton,
                    playerUpgradesButton != null ? playerUpgradesButton.gameObject : null) == null)
            {
                Debug.LogError("Main Menu could not show the Player Upgrades overlay.", this);
            }
        }

        public void ExitGame()
        {
            SaveProfile();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Configure(
            UnityEngine.UI.Button selectLevel,
            UnityEngine.UI.Button play,
            UnityEngine.UI.Button settings,
            UnityEngine.UI.Button upgrades,
            UnityEngine.UI.Button exit,
            LevelCatalog catalog,
            LevelSelectionOverlay selectionOverlay,
            NotImplementedOverlay unavailableOverlay)
        {
            Unsubscribe();
            selectLevelButton = selectLevel;
            playButton = play;
            settingsButton = settings;
            playerUpgradesButton = upgrades;
            exitGameButton = exit;
            levelCatalog = catalog;
            levelSelectionOverlay = selectionOverlay;
            notImplementedOverlay = unavailableOverlay;

            EnsurePlayerEnergyPanel();
            EnsureMenuUtilityButtons();

            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void ChooseRoleForLevel(LevelDefinition level)
        {
            if (levelSelectionOverlay == null ||
                !levelSelectionOverlay.ShowRoleChoice(
                level,
                    selectedRole => LoadLevel(level, selectedRole, selectLevelButton),
                    selectLevelButton != null ? selectLevelButton.gameObject : null))
            {
                Debug.LogError("Main Menu could not show the Attack or Defend choice.", this);
            }
        }

        private void LoadLevel(
            LevelDefinition level,
            PlayerRole playerRole,
            UnityEngine.UI.Button energySourceButton)
        {
            if (isLoadingLevel)
            {
                return;
            }

            if (level == null || string.IsNullOrWhiteSpace(level.SceneName))
            {
                Debug.LogError("The selected level does not have a scene assigned.", this);
                return;
            }

            if (!TryGetBuildSceneIndex(level.SceneName, out var sceneBuildIndex))
            {
                Debug.LogError($"The selected level scene '{level.SceneName}' is not included in Build Settings.", this);
                return;
            }

            ResolveReferences();
            if (levelLoadingOverlay == null)
            {
                Debug.LogError("Main Menu is missing its Level Loading Overlay.", this);
                return;
            }

            if (!MatchStartEnergyGate.TryPayForNewMatch(
                    transform,
                    energySourceButton,
                    energySourceButton != null ? energySourceButton.gameObject : null,
                    out MatchStartEnergyCharge energyCharge))
            {
                return;
            }

            playerProfile = PlayerSettingsService.Instance.Profile;

            playerProfile.lastPlayedLevelId = level.Id;
            SaveProfile();
            MatchRoleSelection.Select(playerRole);
            Time.timeScale = 1f;
            isLoadingLevel = levelLoadingOverlay.TryLoadScene(
                sceneBuildIndex,
                level.SceneName,
                () => HandleLevelLoadFailed(energyCharge));
            if (!isLoadingLevel)
            {
                energyCharge.Refund();
                Debug.LogError($"Main Menu could not begin loading level scene '{level.SceneName}'.", this);
            }
        }

        private void HandleLevelLoadFailed(MatchStartEnergyCharge energyCharge)
        {
            isLoadingLevel = false;
            energyCharge?.Refund();
        }

        private void ShowNotImplemented(UnityEngine.UI.Button sourceButton, string title)
        {
            notImplementedOverlay ??= UnityEngine.Object.FindAnyObjectByType<NotImplementedOverlay>(FindObjectsInactive.Include);
            if (notImplementedOverlay == null || !notImplementedOverlay.Show(sourceButton != null ? sourceButton.gameObject : null, title))
            {
                Debug.LogError("Main Menu could not show the not-implemented overlay.", this);
            }
        }

        private void SaveProfile()
        {
            var settings = PlayerSettingsService.Instance;
            playerProfile = settings.Profile;
            settings.Save();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            if (selectLevelButton != null)
            {
                selectLevelButton.onClick.AddListener(SelectLevel);
            }

            if (playButton != null)
            {
                playButton.onClick.AddListener(Play);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.AddListener(OpenSettings);
            }

            if (playerUpgradesButton != null)
            {
                playerUpgradesButton.onClick.AddListener(OpenPlayerUpgrades);
            }

            if (exitGameButton != null)
            {
                exitGameButton.onClick.AddListener(ExitGame);
            }

            if (fullGameButton != null)
            {
                fullGameButton.onClick.AddListener(OpenFullGameInformation);
            }

            if (supportDeveloperButton != null)
            {
                supportDeveloperButton.onClick.AddListener(OpenSupportDeveloperInformation);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (selectLevelButton != null)
            {
                selectLevelButton.onClick.RemoveListener(SelectLevel);
            }

            if (playButton != null)
            {
                playButton.onClick.RemoveListener(Play);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OpenSettings);
            }

            if (playerUpgradesButton != null)
            {
                playerUpgradesButton.onClick.RemoveListener(OpenPlayerUpgrades);
            }

            if (exitGameButton != null)
            {
                exitGameButton.onClick.RemoveListener(ExitGame);
            }

            if (fullGameButton != null)
            {
                fullGameButton.onClick.RemoveListener(OpenFullGameInformation);
            }

            if (supportDeveloperButton != null)
            {
                supportDeveloperButton.onClick.RemoveListener(OpenSupportDeveloperInformation);
            }

            isSubscribed = false;
        }

        private void ResolveReferences()
        {
            if (selectLevelButton == null)
            {
                selectLevelButton = FindButton("SelectLevelButton");
            }

            if (playButton == null)
            {
                playButton = FindButton("PlayButton");
            }

            if (settingsButton == null)
            {
                settingsButton = FindButton("SettingsButton");
            }

            if (playerUpgradesButton == null)
            {
                playerUpgradesButton = FindButton("PlayerUpgradesButton");
            }

            if (exitGameButton == null)
            {
                exitGameButton = FindButton("ExitGameButton");
            }

            if (levelLoadingOverlay == null)
            {
                levelLoadingOverlay = UnityEngine.Object.FindAnyObjectByType<LevelLoadingOverlay>(FindObjectsInactive.Include);
            }
        }

        private void EnsurePlayerEnergyPanel()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                PlayerEnergyMenuPanel.Ensure(canvas.transform, playButton);
            }
        }

        /// <summary>
        /// Adds the two clearly-labelled utility planks shown around the main-menu vista. They
        /// deliberately use the standard planned-feature overlay: this pre-release build does not
        /// offer a transaction or donation flow yet, but the visual menu remains ready for it.
        /// </summary>
        private void EnsureMenuUtilityButtons()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null || playButton == null)
            {
                return;
            }

            fullGameButton = EnsureUtilityButton(canvas.transform, FullGameButtonName, "FULL GAME");
            supportDeveloperButton = EnsureUtilityButton(canvas.transform, SupportDeveloperButtonName, "SUPPORT DEV");
            GetComponent<MobileUiLayoutController>()?.RefreshLayout();
        }

        private UnityEngine.UI.Button EnsureUtilityButton(Transform canvasRoot, string buttonName, string label)
        {
            var existing = canvasRoot.Find(buttonName);
            var button = existing != null ? existing.GetComponent<UnityEngine.UI.Button>() : null;
            if (button == null)
            {
                var clone = Instantiate(playButton.gameObject, canvasRoot, false);
                clone.name = buttonName;
                button = clone.GetComponent<UnityEngine.UI.Button>();
                if (button == null)
                {
                    Destroy(clone);
                    return null;
                }

                // A clone made during Awake has no runtime listener from the original menu
                // button, but clearing listeners also makes the presentation robust if Unity
                // recreates this controller while retaining the scene hierarchy.
                button.onClick.RemoveAllListeners();
            }

            var text = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (text != null)
            {
                text.text = label;
            }

            button.gameObject.SetActive(true);
            FarmUiSkin.ApplyFromCanvas(canvasRoot, button.transform);
            return button;
        }

        private void OpenFullGameInformation()
        {
            ShowNotImplemented(fullGameButton, "FULL GAME");
        }

        private void OpenSupportDeveloperInformation()
        {
            ShowNotImplemented(supportDeveloperButton, "SUPPORT THE DEVELOPER");
        }

        private UnityEngine.UI.Button FindButton(string buttonName)
        {
            Transform buttonTransform = transform.Find($"MainMenuPanel/{buttonName}");
            return buttonTransform != null ? buttonTransform.GetComponent<UnityEngine.UI.Button>() : null;
        }

        private static bool TryGetBuildSceneIndex(string sceneName, out int sceneBuildIndex)
        {
            for (var buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                var scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                var sceneFileName = Path.GetFileName(scenePath);
                var sceneShortName = Path.GetFileNameWithoutExtension(scenePath);
                if (string.Equals(sceneName, scenePath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sceneName, sceneFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sceneName, sceneShortName, StringComparison.OrdinalIgnoreCase))
                {
                    sceneBuildIndex = buildIndex;
                    return true;
                }
            }

            sceneBuildIndex = -1;
            return false;
        }

        private static void Select(GameObject target)
        {
            if (target != null && target.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }
    }
}
