using System;
using System.Collections.Generic;
using TAD.Levels;
using TAD.Match;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TAD.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class LevelSelectionOverlay : MonoBehaviour
    {
        private const float LandscapeSelectionButtonHeight = 80f;
        private const float PortraitSelectionButtonHeight = 92f;

        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private Transform levelButtonContainer;
        [SerializeField] private UnityEngine.UI.Button levelButtonTemplate;
        [SerializeField] private UnityEngine.UI.Button closeButton;
        [SerializeField] private UnityEngine.UI.Text emptyMessageText;
        [SerializeField] private UnityEngine.UI.Text titleText;
        [SerializeField] private UnityEngine.UI.Text descriptionText;

        private readonly List<UnityEngine.UI.Button> generatedButtons = new List<UnityEngine.UI.Button>();
        private Action<LevelDefinition> onLevelSelected;
        private Action<PlayerRole> onRoleSelected;
        private GameObject selectionAfterClose;
        private bool isSubscribed;
        private bool portraitLayoutApplied;
        private string defaultTitle;
        private string defaultDescription;
        private string defaultEmptyMessage;

        private void Awake()
        {
            ResolveReferences();
            CaptureDefaultText();
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
            ClearCallbacks();
            portraitLayoutApplied = false;
        }

        private void LateUpdate()
        {
            if (modalOverlay == null || !modalOverlay.IsVisible)
            {
                portraitLayoutApplied = false;
                return;
            }

            var layoutController = GetComponentInParent<MobileUiLayoutController>(true);
            if (layoutController == null || !layoutController.IsPortraitLayout)
            {
                portraitLayoutApplied = false;
                return;
            }

            // The overlay can be opened in the same frame as its canvas finishes its own
            // orientation refresh. Retry once in LateUpdate to guarantee the full-width list
            // is applied after all generated buttons exist.
            if (!portraitLayoutApplied)
            {
                ApplyMobilePortraitLayout();
            }
        }

        /// <summary>Shows the available level list.</summary>
        public bool Show(LevelCatalog catalog, Action<LevelDefinition> levelSelectedCallback, GameObject returnSelectionTarget)
        {
            ResolveReferences();
            CaptureDefaultText();
            if (modalOverlay == null || catalog == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            ConfigureLevelListText();
            RebuildLevelButtons(catalog);
            onLevelSelected = levelSelectedCallback;
            onRoleSelected = null;
            selectionAfterClose = returnSelectionTarget;
            portraitLayoutApplied = false;
            modalOverlay.Show();
            ApplyMobilePortraitLayout();

            SelectFirstGeneratedButtonOrClose();
            return true;
        }

        /// <summary>
        /// Reuses the selection overlay for a clear Attack-or-Defend decision after a level is chosen.
        /// It intentionally does not require a second scene object or a Main Menu scene rewrite.
        /// </summary>
        public bool ShowRoleChoice(
            LevelDefinition level,
            Action<PlayerRole> roleSelectedCallback,
            GameObject returnSelectionTarget)
        {
            ResolveReferences();
            CaptureDefaultText();
            if (modalOverlay == null || level == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            ClearGeneratedButtons();
            if (levelButtonTemplate != null)
            {
                levelButtonTemplate.gameObject.SetActive(false);
            }

            if (titleText != null)
            {
                titleText.text = "CHOOSE YOUR SIDE";
            }

            SetRoleDescription(
                $"{level.DisplayName}{System.Environment.NewLine}Attack sends animal waves. Defend places farmhands.");
            CreateRoleButton(PlayerRole.Attacker, "ATTACK");
            CreateRoleButton(PlayerRole.Defender, "DEFEND");

            onLevelSelected = null;
            onRoleSelected = roleSelectedCallback;
            selectionAfterClose = returnSelectionTarget;
            portraitLayoutApplied = false;
            modalOverlay.Show();
            ApplyMobilePortraitLayout();

            SelectFirstGeneratedButtonOrClose();
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
            titleText = null;
            descriptionText = null;
            defaultTitle = null;
            defaultDescription = null;
            defaultEmptyMessage = null;

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

        private void ChooseRole(PlayerRole role)
        {
            Action<PlayerRole> callback = onRoleSelected;
            Dismiss(false);
            callback?.Invoke(role);
        }

        private void CreateRoleButton(PlayerRole role, string label)
        {
            if (levelButtonTemplate == null || levelButtonContainer == null)
            {
                return;
            }

            UnityEngine.UI.Button roleButton = Instantiate(levelButtonTemplate, levelButtonContainer);
            roleButton.name = role + "RoleButton";
            roleButton.gameObject.SetActive(true);

            UnityEngine.UI.Text buttonLabel = roleButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (buttonLabel != null)
            {
                buttonLabel.text = label;
            }

            ConfigureSelectionButtonLayout(roleButton, LandscapeSelectionButtonHeight);
            roleButton.onClick.RemoveAllListeners();
            roleButton.onClick.AddListener(() => ChooseRole(role));
            generatedButtons.Add(roleButton);
        }

        private void Dismiss(bool restoreSelection)
        {
            if (modalOverlay == null || !modalOverlay.IsVisible)
            {
                return;
            }

            GameObject returnTarget = selectionAfterClose;
            ClearCallbacks();
            modalOverlay.Hide();

            if (restoreSelection)
            {
                Select(returnTarget);
            }
        }

        private void ClearCallbacks()
        {
            selectionAfterClose = null;
            onLevelSelected = null;
            onRoleSelected = null;
        }

        private void ConfigureLevelListText()
        {
            if (titleText != null)
            {
                titleText.text = string.IsNullOrEmpty(defaultTitle) ? "SELECT LEVEL" : defaultTitle;
            }

            if (descriptionText != null)
            {
                descriptionText.text = defaultDescription ?? string.Empty;
                descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(descriptionText.text));
            }

            if (emptyMessageText != null)
            {
                emptyMessageText.text = defaultEmptyMessage ?? string.Empty;
                emptyMessageText.gameObject.SetActive(false);
            }
        }

        private void SetRoleDescription(string message)
        {
            if (descriptionText != null)
            {
                descriptionText.text = message;
                descriptionText.gameObject.SetActive(true);
                if (emptyMessageText != null)
                {
                    emptyMessageText.gameObject.SetActive(false);
                }

                return;
            }

            if (emptyMessageText != null)
            {
                emptyMessageText.text = message;
                emptyMessageText.gameObject.SetActive(true);
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

                    ConfigureSelectionButtonLayout(levelButton, LandscapeSelectionButtonHeight);
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

        /// <summary>
        /// Keeps the authored landscape card untouched while converting this compact modal into a
        /// comfortable portrait phone layout. The level list uses wide, consistently-sized rows
        /// so the button surface is always as visible and tappable as its bold label.
        /// </summary>
        private void ApplyMobilePortraitLayout()
        {
            var layoutController = GetComponentInParent<MobileUiLayoutController>(true);
            if (layoutController == null || !layoutController.IsPortraitLayout)
            {
                return;
            }

            // Let the canvas capture its authored positions before this modal applies its
            // portrait presentation. This preserves a clean landscape restore on rotation.
            layoutController.RefreshLayout();

            var panel = transform.Find("LevelSelectionPanel") as RectTransform;
            var content = levelButtonContainer as RectTransform;
            if (panel == null || content == null)
            {
                return;
            }

            var safeContent = layoutController.GetSafeContentRect(32f);
            var panelWidth = Mathf.Min(900f, safeContent.width);
            var panelHeight = Mathf.Min(880f, safeContent.height);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = safeContent.center;
            panel.sizeDelta = new Vector2(panelWidth, panelHeight);

            var title = titleText != null ? titleText.rectTransform : null;
            if (title != null)
            {
                SetTopCenter(title, 64f, panelWidth - 220f, 70f);
            }

            var description = descriptionText != null ? descriptionText.rectTransform : null;
            var descriptionIsVisible = descriptionText != null && descriptionText.gameObject.activeSelf;
            if (description != null && descriptionIsVisible)
            {
                SetTopCenter(description, 136f, panelWidth - 104f, 88f);
            }

            var closeRect = closeButton != null ? closeButton.transform as RectTransform : null;
            if (closeRect != null)
            {
                SetTopRight(closeRect, 24f, 24f, 190f, 72f);
                var closeLabel = closeButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (closeLabel != null && string.IsNullOrWhiteSpace(closeLabel.text))
                {
                    closeLabel.text = "CLOSE";
                }
            }

            var contentTop = descriptionIsVisible ? 252f : 166f;
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = new Vector2(0f, -contentTop);
            content.sizeDelta = new Vector2(panelWidth - 96f, panelHeight - contentTop - 54f);

            var vertical = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vertical != null)
            {
                vertical.padding = new RectOffset(0, 0, 0, 0);
                vertical.spacing = 18f;
                vertical.childAlignment = TextAnchor.UpperCenter;
                vertical.childControlWidth = true;
                vertical.childControlHeight = true;
                vertical.childForceExpandWidth = true;
                vertical.childForceExpandHeight = false;
            }

            foreach (var button in content.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var element = button.GetComponent<UnityEngine.UI.LayoutElement>();
                if (element == null)
                {
                    element = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                }

                element.minHeight = Mathf.Max(element.minHeight, PortraitSelectionButtonHeight);
                element.preferredHeight = Mathf.Max(element.preferredHeight, PortraitSelectionButtonHeight);
                FarmUiSkin.EnsureReadableWoodButtonLayout(button, PortraitSelectionButtonHeight);
            }

            UnityEngine.Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            portraitLayoutApplied = true;
        }

        private static void ConfigureSelectionButtonLayout(UnityEngine.UI.Button button, float minimumHeight)
        {
            FarmUiSkin.EnsureReadableWoodButtonLayout(button, minimumHeight);
        }

        private static void SetTopCenter(RectTransform target, float top, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.anchoredPosition = new Vector2(0f, -top);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopRight(RectTransform target, float right, float top, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(1f, 1f);
            target.anchoredPosition = new Vector2(-right, -top);
            target.sizeDelta = new Vector2(width, height);
        }

        private void SelectFirstGeneratedButtonOrClose()
        {
            if (generatedButtons.Count > 0)
            {
                Select(generatedButtons[0].gameObject);
            }
            else if (closeButton != null)
            {
                Select(closeButton.gameObject);
            }
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
                levelButtonContainer = transform.Find("LevelSelectionPanel/LevelsContent");
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

            if (titleText == null)
            {
                Transform title = transform.Find("LevelSelectionPanel/TitleText");
                if (title != null)
                {
                    titleText = title.GetComponent<UnityEngine.UI.Text>();
                }
            }

            if (descriptionText == null)
            {
                Transform description = transform.Find("LevelSelectionPanel/DescriptionText");
                if (description != null)
                {
                    descriptionText = description.GetComponent<UnityEngine.UI.Text>();
                }
            }
        }

        private void CaptureDefaultText()
        {
            if (defaultTitle == null && titleText != null)
            {
                defaultTitle = titleText.text;
            }

            if (defaultDescription == null && descriptionText != null)
            {
                defaultDescription = descriptionText.text;
            }

            if (defaultEmptyMessage == null && emptyMessageText != null)
            {
                defaultEmptyMessage = emptyMessageText.text;
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
