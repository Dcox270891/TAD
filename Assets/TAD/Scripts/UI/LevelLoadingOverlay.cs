using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TAD.UI
{
    /// <summary>
    /// Full-screen, non-pausing transition UI that presents async scene-loading progress.
    /// It can be used by the Main Menu or spawned temporarily by a gameplay result overlay.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LevelLoadingOverlay : MonoBehaviour
    {
        private const float MinimumLoadingScreenDuration = 0.25f;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UnityEngine.UI.Image progressFill;
        [SerializeField] private UnityEngine.UI.Text progressLabel;

        private bool isLoading;

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.99f;
        public bool IsLoading => isLoading;

        private void Awake()
        {
            ResolveReferences();
            Hide();
        }

        /// <summary>Displays the loading screen at zero percent and blocks lower UI from receiving input.</summary>
        public void Show()
        {
            ResolveReferences();
            SetVisible(true);
            SetProgress(0f);
        }

        /// <summary>Hides the transition UI and releases input back to the scene.</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>
        /// Shows this overlay and asynchronously loads a Build Settings scene. The active scene stays
        /// visible behind the overlay until loading progress reaches 100 percent.
        /// </summary>
        public bool TryLoadScene(int sceneBuildIndex, string sceneName, Action onLoadFailed = null)
        {
            if (isLoading || sceneBuildIndex < 0 || sceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return false;
            }

            isLoading = true;
            StartCoroutine(LoadSceneAsync(sceneBuildIndex, sceneName, onLoadFailed));
            return true;
        }

        /// <summary>Updates the visible loading completion value in the normalized 0-1 range.</summary>
        public void SetProgress(float normalizedProgress)
        {
            var progress = Mathf.Clamp01(normalizedProgress);
            if (progressFill != null)
            {
                progressFill.fillAmount = progress;
            }

            if (progressLabel != null)
            {
                progressLabel.text = $"Loading... {Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        private IEnumerator LoadSceneAsync(int sceneBuildIndex, string sceneName, Action onLoadFailed)
        {
            Show();
            var loadingScreenShownAt = Time.realtimeSinceStartup;

            // Give the Canvas one frame to render before beginning the asynchronous scene work.
            yield return null;

            var loadOperation = SceneManager.LoadSceneAsync(sceneBuildIndex);
            if (loadOperation == null)
            {
                Debug.LogError($"Could not start loading scene '{sceneName}'.", this);
                Hide();
                isLoading = false;
                onLoadFailed?.Invoke();
                yield break;
            }

            // Unity reports 0-0.9 while content is prepared. Hold activation briefly so 100% is visible.
            loadOperation.allowSceneActivation = false;
            while (loadOperation.progress < 0.9f)
            {
                SetProgress(loadOperation.progress / 0.9f);
                yield return null;
            }

            SetProgress(1f);
            var remainingVisibleTime = MinimumLoadingScreenDuration -
                                       (Time.realtimeSinceStartup - loadingScreenShownAt);
            if (remainingVisibleTime > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingVisibleTime);
            }
            else
            {
                yield return null;
            }

            loadOperation.allowSceneActivation = true;
        }

        private void SetVisible(bool visible)
        {
            ResolveReferences();
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = visible;
        }

        private void ResolveReferences()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (progressFill == null)
            {
                var fillTransform = transform.Find("LoadingPanel/ProgressBar/Fill");
                if (fillTransform != null)
                {
                    progressFill = fillTransform.GetComponent<UnityEngine.UI.Image>();
                }
            }

            if (progressLabel == null)
            {
                var labelTransform = transform.Find("LoadingPanel/ProgressLabel");
                if (labelTransform != null)
                {
                    progressLabel = labelTransform.GetComponent<UnityEngine.UI.Text>();
                }
            }
        }
    }
}