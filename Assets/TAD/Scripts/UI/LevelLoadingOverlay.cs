using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Full-screen, non-pausing transition UI shown while the Main Menu prepares a selected level.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LevelLoadingOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UnityEngine.UI.Image progressFill;
        [SerializeField] private UnityEngine.UI.Text progressLabel;

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.99f;

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

        /// <summary>Hides the transition UI and releases input back to the menu.</summary>
        public void Hide()
        {
            SetVisible(false);
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