using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    /// <summary>
    /// Owns the minimal Main Menu's Play and Quit actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Button playButton;
        [SerializeField] private UnityEngine.UI.Button quitButton;
        [SerializeField] private string gameplaySceneName = "SampleScene";

        private bool isSubscribed;

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

            Subscribe();
        }

        private void Start()
        {
            if (EventSystem.current != null && playButton != null)
            {
                EventSystem.current.SetSelectedGameObject(playButton.gameObject);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>Starts the configured gameplay scene.</summary>
        public void Play()
        {
            if (string.IsNullOrWhiteSpace(gameplaySceneName) ||
                !Application.CanStreamedLevelBeLoaded(gameplaySceneName))
            {
                Debug.LogError($"Main Menu cannot load gameplay scene '{gameplaySceneName}'.", this);
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(gameplaySceneName);
        }

        /// <summary>Closes the standalone application. In the Editor this stops Play Mode.</summary>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Assigns prefab or scene-owned controls without adding listeners in the Editor.</summary>
        public void Configure(
            UnityEngine.UI.Button play,
            UnityEngine.UI.Button quit)
        {
            Unsubscribe();
            playButton = play;
            quitButton = quit;

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            ResolveReferences();

            if (playButton != null)
            {
                playButton.onClick.AddListener(Play);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(Quit);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (playButton != null)
            {
                playButton.onClick.RemoveListener(Play);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(Quit);
            }

            isSubscribed = false;
        }

        private void ResolveReferences()
        {
            if (playButton != null && quitButton != null)
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
                    case "QuitButton":
                        quitButton ??= button;
                        break;
                }
            }
        }
    }
}