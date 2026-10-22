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
        // Unity's async scene operation stops at 0.9 while it waits for activation. Presenting
        // that as 100% made the loading screen look finished while the old scene was still
        // visible. Keep a small, explicit gap until the new scene has actually activated.
        private const float UnityReadyForActivationProgress = 0.9f;
        public const float PreActivationProgressLimit = 0.96f;

        // The early fill is intentionally long enough to be readable on a fast local load,
        // while the final 100% state is only a brief confirmation once the destination scene is
        // already active behind this persistent overlay.
        private const float MinimumPreActivationDuration = 0.8f;
        private const float PreActivationProgressUnitsPerSecond = 1.15f;
        private const float CompletionVisibleDuration = 0.16f;
        private const float BarHorizontalInset = 6f;
        private const float BarVerticalInset = 6f;
        private const float LabelHorizontalInset = 18f;
        private const float LabelVerticalInset = 6f;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UnityEngine.UI.Image progressFill;
        [SerializeField] private UnityEngine.UI.Text progressLabel;

        private UnityEngine.UI.Image progressBarFrame;
        private bool isLoading;
        private float displayedProgress;

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.99f;
        public bool IsLoading => isLoading;
        public float DisplayedProgress => displayedProgress;

        private void Awake()
        {
            ResolveReferences();
            ConfigureFarmStatusBar();
            Hide();
        }

        /// <summary>Displays the loading screen at zero percent and blocks lower UI from receiving input.</summary>
        public void Show()
        {
            ResolveReferences();
            ConfigureFarmStatusBar();
            SetVisible(true);
            SetProgress(0f);
        }

        /// <summary>Hides the transition UI and releases input back to the scene.</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>
        /// Shows this overlay and asynchronously loads a Build Settings scene. The transition
        /// remains in front until the destination scene is active and its brief 100% cue ends.
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
            displayedProgress = progress;
            if (progressFill != null)
            {
                progressFill.fillAmount = progress;
            }

            if (progressLabel != null)
            {
                progressLabel.text = $"Loading... {Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        /// <summary>
        /// Converts Unity's 0-0.9 scene preparation signal into the deliberately incomplete
        /// visual range shown before a scene has activated. This is public so automated checks
        /// can keep the "never show 100% early" contract deterministic without faking an async
        /// scene operation.
        /// </summary>
        public static float GetPreActivationProgress(float unityAsyncOperationProgress)
        {
            var normalizedPreparation = Mathf.Clamp01(
                unityAsyncOperationProgress / UnityReadyForActivationProgress);
            return normalizedPreparation * PreActivationProgressLimit;
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

            // Keep the one transition surface alive across the single-scene load. That lets the
            // target scene be genuinely active before the player sees 100%, instead of showing a
            // false completion state while Unity is still holding activation at 0.9.
            DontDestroyOnLoad(gameObject);
            loadOperation.allowSceneActivation = false;
            while (loadOperation.progress < UnityReadyForActivationProgress ||
                   displayedProgress < PreActivationProgressLimit ||
                   Time.realtimeSinceStartup - loadingScreenShownAt < MinimumPreActivationDuration)
            {
                var targetProgress = GetPreActivationProgress(loadOperation.progress);
                var nextProgress = Mathf.MoveTowards(
                    displayedProgress,
                    targetProgress,
                    PreActivationProgressUnitsPerSecond * Time.unscaledDeltaTime);
                SetProgress(nextProgress);
                yield return null;
            }

            // Hold just below complete until activation has succeeded. The target scene then
            // exists behind this overlay, so the brief final state is an honest completion cue.
            SetProgress(PreActivationProgressLimit);
            loadOperation.allowSceneActivation = true;
            while (!loadOperation.isDone)
            {
                yield return null;
            }

            SetProgress(1f);
            yield return new WaitForSecondsRealtime(CompletionVisibleDuration);

            isLoading = false;
            Hide();
            Destroy(gameObject);
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

            if (progressBarFrame == null)
            {
                var frameTransform = transform.Find("LoadingPanel/ProgressBar");
                if (frameTransform != null)
                {
                    progressBarFrame = frameTransform.GetComponent<UnityEngine.UI.Image>();
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

        /// <summary>
        /// Reuses the shared recessed wooden status-bar frame and fill used by food, health, and
        /// experience. The loading label lives inside the frame too, so the complete value stays
        /// readable without becoming a separate piece of UI underneath the bar.
        /// </summary>
        private void ConfigureFarmStatusBar()
        {
            ResolveReferences();

            var skin = GetComponent<FarmUiSkin>();
            var frameSprite = skin != null ? skin.StatusBarFrameSprite : null;
            var fillSprite = skin != null ? skin.StatusBarFillSprite : null;

            if (progressBarFrame != null)
            {
                progressBarFrame.sprite = frameSprite;
                progressBarFrame.type = frameSprite != null
                    ? UnityEngine.UI.Image.Type.Sliced
                    : UnityEngine.UI.Image.Type.Simple;
                progressBarFrame.fillCenter = true;
                progressBarFrame.preserveAspect = false;
                progressBarFrame.color = frameSprite != null ? Color.white : Color.black;
                progressBarFrame.raycastTarget = false;
            }

            if (progressFill != null)
            {
                progressFill.sprite = fillSprite != null ? fillSprite : progressFill.sprite;
                progressFill.type = UnityEngine.UI.Image.Type.Filled;
                progressFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                progressFill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
                progressFill.fillCenter = true;
                progressFill.preserveAspect = false;
                progressFill.color = new Color(0.18f, 0.78f, 0.28f, 1f);
                progressFill.raycastTarget = false;

                var fillRect = progressFill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.pivot = new Vector2(0.5f, 0.5f);
                fillRect.offsetMin = new Vector2(BarHorizontalInset, BarVerticalInset);
                fillRect.offsetMax = new Vector2(-BarHorizontalInset, -BarVerticalInset);
                fillRect.localScale = Vector3.one;
            }

            if (progressLabel == null || progressBarFrame == null)
            {
                return;
            }

            var labelTransform = progressLabel.rectTransform;
            if (labelTransform.parent != progressBarFrame.transform)
            {
                labelTransform.SetParent(progressBarFrame.transform, false);
            }

            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.pivot = new Vector2(0.5f, 0.5f);
            labelTransform.offsetMin = new Vector2(LabelHorizontalInset, LabelVerticalInset);
            labelTransform.offsetMax = new Vector2(-LabelHorizontalInset, -LabelVerticalInset);
            labelTransform.localScale = Vector3.one;
            labelTransform.SetAsLastSibling();

            progressLabel.alignment = TextAnchor.MiddleCenter;
            progressLabel.resizeTextForBestFit = true;
            progressLabel.resizeTextMinSize = 12;
            progressLabel.resizeTextMaxSize = Mathf.Max(12, progressLabel.fontSize);
            progressLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            progressLabel.verticalOverflow = VerticalWrapMode.Truncate;
            progressLabel.raycastTarget = false;
        }
    }
}
