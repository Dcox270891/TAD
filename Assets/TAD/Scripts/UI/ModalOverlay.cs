using System;
using System;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Makes a full-screen Overlay canvas modal. While visible, its raycast blocker prevents clicks from
    /// reaching lower UI and gameplay is paused until <see cref="CompleteChoice"/> or <see cref="Hide"/> is called.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ModalOverlay : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Pause the current gameplay clock while this overlay requires a player choice.")]
        private bool pauseGameplay = true;

        [SerializeField]
        [Tooltip("Show this modal immediately when the scene starts. Leave off for overlays opened by game events.")]
        private bool showOnAwake;

        private static int activeBlockerCount;
        private static int activePauseCount;
        private static float timeScaleBeforeFirstPause = 1f;

        private CanvasGroup canvasGroup;
        private bool isVisible;
        private bool isRegisteredAsBlocker;
        private bool hasPausedGameplay;
        private bool hasAwoken;

        /// <summary>True while one or more visible overlays are preventing gameplay input.</summary>
        public static bool IsGameplayInputBlocked => activeBlockerCount > 0;

        public bool IsVisible => isVisible;

        /// <summary>Raised after this overlay is dismissed by a completed player choice.</summary>
        public event Action ChoiceCompleted;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            hasAwoken = true;
            SetVisible(showOnAwake);
        }

        private void OnEnable()
        {
            if (hasAwoken)
            {
                SynchronizeBlockingState();
            }
        }

        private void OnDisable()
        {
            UnregisterBlocker();
            ReleaseGameplayPause();
        }

        private void OnDestroy()
        {
            UnregisterBlocker();
            ReleaseGameplayPause();
        }

        /// <summary>Show this overlay and require the player to resolve it before gameplay resumes.</summary>
        public void Show()
        {
            SetVisible(true);
        }

        /// <summary>Dismiss this overlay without invoking the choice-completed event.</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>Dismiss this overlay after a player confirms a warning, level-up, or pause-menu choice.</summary>
        public void CompleteChoice()
        {
            if (!isVisible)
            {
                return;
            }

            Hide();
            ChoiceCompleted?.Invoke();
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }

            if (hasAwoken && isActiveAndEnabled)
            {
                SynchronizeBlockingState();
            }
        }

        private void SynchronizeBlockingState()
        {
            if (isVisible)
            {
                RegisterBlocker();
                if (pauseGameplay)
                {
                    AcquireGameplayPause();
                }

                return;
            }

            UnregisterBlocker();
            ReleaseGameplayPause();
        }

        private void RegisterBlocker()
        {
            if (isRegisteredAsBlocker)
            {
                return;
            }

            activeBlockerCount++;
            isRegisteredAsBlocker = true;
        }

        private void UnregisterBlocker()
        {
            if (!isRegisteredAsBlocker)
            {
                return;
            }

            activeBlockerCount = Mathf.Max(0, activeBlockerCount - 1);
            isRegisteredAsBlocker = false;
        }

        private void AcquireGameplayPause()
        {
            if (hasPausedGameplay)
            {
                return;
            }

            if (activePauseCount == 0)
            {
                timeScaleBeforeFirstPause = Time.timeScale;
            }

            activePauseCount++;
            Time.timeScale = 0f;
            hasPausedGameplay = true;
        }

        private void ReleaseGameplayPause()
        {
            if (!hasPausedGameplay)
            {
                return;
            }

            activePauseCount = Mathf.Max(0, activePauseCount - 1);
            if (activePauseCount == 0)
            {
                Time.timeScale = timeScaleBeforeFirstPause;
            }

            hasPausedGameplay = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            activeBlockerCount = 0;
            activePauseCount = 0;
            timeScaleBeforeFirstPause = 1f;
        }
    }
}
