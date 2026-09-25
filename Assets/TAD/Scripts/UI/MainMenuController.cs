using System;
using System.IO;
using TAD.Levels;
using TAD.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
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
        private bool isSubscribed;
        private bool isLoadingLevel;

        private void Awake()
        {
            ResolveReferences();
            playerProfile = PlayerProfileStore.Load();
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

        public void SelectLevel()
        {
            if (levelCatalog == null)
            {
                Debug.LogError("Main Menu is missing its Level Catalog.", this);
                return;
            }

            if (levelSelectionOverlay == null || !levelSelectionOverlay.Show(levelCatalog, LoadLevel, selectLevelButton != null ? selectLevelButton.gameObject : null))
            {
                Debug.LogError("Main Menu could not show the Select Level overlay.", this);
            }
        }

        public void Play()
        {
            if (levelCatalog == null || !levelCatalog.TryGetRandomLevel(out LevelDefinition level))
            {
                Debug.LogError("Main Menu cannot choose a playable level because the Level Catalog is empty.", this);
                return;
            }

            LoadLevel(level);
        }

        public void OpenSettings()
        {
            ShowNotImplemented(settingsButton, "OPTIONS / SETTINGS");
        }

        public void OpenPlayerUpgrades()
        {
            ShowNotImplemented(playerUpgradesButton, "PLAYER UPGRADES");
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

            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void LoadLevel(LevelDefinition level)
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

            if (playerProfile == null)
            {
                playerProfile = PlayerProfileStore.Load();
            }

            playerProfile.lastPlayedLevelId = level.Id;
            SaveProfile();
            Time.timeScale = 1f;
            isLoadingLevel = levelLoadingOverlay.TryLoadScene(sceneBuildIndex, level.SceneName, HandleLevelLoadFailed);
            if (!isLoadingLevel)
            {
                Debug.LogError($"Main Menu could not begin loading level scene '{level.SceneName}'.", this);
            }
        }

        private void HandleLevelLoadFailed()
        {
            isLoadingLevel = false;
        }

        private void ShowNotImplemented(UnityEngine.UI.Button sourceButton, string title)
        {
            if (notImplementedOverlay == null || !notImplementedOverlay.Show(sourceButton != null ? sourceButton.gameObject : null, title))
            {
                Debug.LogError("Main Menu could not show the not-implemented overlay.", this);
            }
        }

        private void SaveProfile()
        {
            if (playerProfile != null)
            {
                PlayerProfileStore.Save(playerProfile);
            }
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