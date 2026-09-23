using System;
using System.Collections.Generic;
using TAD.Levels;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class LevelSelectionOverlay : MonoBehaviour
    {
        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private Transform levelButtonContainer;
        [SerializeField] private UnityEngine.UI.Button levelButtonTemplate;
        [SerializeField] private UnityEngine.UI.Button closeButton;
        [SerializeField] private UnityEngine.UI.Text emptyMessageText;

        private readonly List<UnityEngine.UI.Button> generatedButtons = new List<UnityEngine.UI.Button>();
        private Action<LevelDefinition> onLevelSelected;
        private GameObject selectionAfterClose;
        private bool isSubscribed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            onLevelSelected = null;
            selectionAfterClose = null;
        }

        public bool Show(LevelCatalog catalog, Action<LevelDefinition> levelSelectedCallback, GameObject returnSelectionTarget)
        {
            ResolveReferences();
            if (modalOverlay == null || catalog == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            RebuildLevelButtons(catalog);
            onLevelSelected = levelSelectedCallback;
            selectionAfterClose = returnSelectionTarget;
            modalOverlay.Show();

            if (generatedButtons.Count > 0)
            {
                Select(generatedButtons[0].gameObject);
            }
            else if (closeButton != null)
            {
                Select(closeButton.gameObject);
            }

            return true;
        }

        public void Close()
        {
            Dismiss(true);
        }

        public void Configure(
            ModalOverlay overlay,
            Transform buttonContainer,
            UnityEngine.UI.Button buttonTemplate,
            UnityEngine.UI.Button close,
            UnityEngine.UI.Text emptyMessage)
        {
            Unsubscribe();
            modalOverlay = overlay;
            levelButtonContainer = buttonContainer;
            levelButtonTemplate = buttonTemplate;
            closeButton = close;
            emptyMessageText = emptyMessage;

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void ChooseLevel(LevelDefinition definition)
        {
            Action<LevelDefinition> callback = onLevelSelected;
            Dismiss(false);
            callback?.Invoke(definition);
        }

        private void Dismiss(bool restoreSelection)
        {
            if (modalOverlay == null || !modalOverlay.IsVisible)
            {
                return;
            }

            GameObject returnTarget = selectionAfterClose;
            selectionAfterClose = null;
            onLevelSelected = null;
            modalOverlay.Hide();

            if (restoreSelection)
            {
                Select(returnTarget);
            }
        }

        private void RebuildLevelButtons(LevelCatalog catalog)
        {
            ClearGeneratedButtons();

            if (levelButtonTemplate != null)
            {
                levelButtonTemplate.gameObject.SetActive(false);
            }

            IReadOnlyList<LevelDefinition> levels = catalog.Levels;
            if (levels != null && levelButtonTemplate != null && levelButtonContainer != null)
            {
                foreach (LevelDefinition definition in levels)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.SceneName))
                    {
                        continue;
                    }

                    LevelDefinition selectedDefinition = definition;
                    UnityEngine.UI.Button levelButton = Instantiate(levelButtonTemplate, levelButtonContainer);
                    levelButton.name = $"LevelButton_{selectedDefinition.Id}";
                    levelButton.gameObject.SetActive(true);

                    UnityEngine.UI.Text label = levelButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (label != null)
                    {
                        label.text = selectedDefinition.DisplayName;
                    }

                    levelButton.onClick.RemoveAllListeners();
                    levelButton.onClick.AddListener(() => ChooseLevel(selectedDefinition));
                    generatedButtons.Add(levelButton);
                }
            }

            if (emptyMessageText != null)
            {
                emptyMessageText.gameObject.SetActive(generatedButtons.Count == 0);
            }
        }

        private void ClearGeneratedButtons()
        {
            foreach (UnityEngine.UI.Button button in generatedButtons)
            {
                if (button == null)
                {
                    continue;
                }

                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }

            generatedButtons.Clear();
        }

        private void Subscribe()
        {
            if (isSubscribed || closeButton == null)
            {
                return;
            }

            closeButton.onClick.AddListener(Close);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || closeButton == null)
            {
                isSubscribed = false;
                return;
            }

            closeButton.onClick.RemoveListener(Close);
            isSubscribed = false;
        }

        private void ResolveReferences()
        {
            if (modalOverlay == null)
            {
                modalOverlay = GetComponent<ModalOverlay>();
            }

            if (levelButtonContainer == null)
            {
                Transform container = transform.Find("LevelSelectionPanel/LevelsContent");
                levelButtonContainer = container;
            }

            if (levelButtonTemplate == null && levelButtonContainer != null)
            {
                Transform template = levelButtonContainer.Find("LevelButtonTemplate");
                if (template != null)
                {
                    levelButtonTemplate = template.GetComponent<UnityEngine.UI.Button>();
                }
            }

            if (closeButton == null)
            {
                Transform close = transform.Find("LevelSelectionPanel/CloseButton");
                if (close != null)
                {
                    closeButton = close.GetComponent<UnityEngine.UI.Button>();
                }
            }

            if (emptyMessageText == null)
            {
                Transform emptyMessage = transform.Find("LevelSelectionPanel/EmptyMessageText");
                if (emptyMessage != null)
                {
                    emptyMessageText = emptyMessage.GetComponent<UnityEngine.UI.Text>();
                }
            }
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