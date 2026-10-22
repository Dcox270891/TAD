using System;
using TAD.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// A lightweight, runtime-created role choice that sits above a paused or terminal match overlay.
    /// It keeps restart decisions local to the active match without requiring scene-specific UI wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RestartRoleChoiceOverlay : MonoBehaviour
    {
        private const string OverlayName = "RestartRoleChoiceOverlay";
        private const float RoleChoiceButtonHeight = 88f;

        private Action<PlayerRole> onRoleSelected;
        private Action onCancelled;
        private GameObject selectionAfterClose;
        private Button attackerButton;

        /// <summary>
        /// Displays an Attack-or-Defend choice above an existing overlay.
        /// The supplied button is cloned so the choice follows the active menu visual style.
        /// </summary>
        public static RestartRoleChoiceOverlay Show(
            Transform canvasRoot,
            Button buttonTemplate,
            GameObject returnSelectionTarget,
            Action<PlayerRole> roleSelected,
            Action cancelled)
        {
            if (canvasRoot == null || roleSelected == null)
            {
                return null;
            }

            Transform existingTransform = canvasRoot.Find(OverlayName);
            RestartRoleChoiceOverlay existing =
                existingTransform != null ? existingTransform.GetComponent<RestartRoleChoiceOverlay>() : null;
            if (existing != null)
            {
                existing.Configure(returnSelectionTarget, roleSelected, cancelled);
                existing.gameObject.SetActive(true);
                existing.transform.SetAsLastSibling();
                FarmUiSkin.ApplyFromCanvas(canvasRoot, existing.transform);
                return existing;
            }

            var root = new GameObject(
                OverlayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RestartRoleChoiceOverlay));
            root.layer = canvasRoot.gameObject.layer;

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(canvasRoot, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var backdrop = root.GetComponent<Image>();
            backdrop.color = new Color(0.015f, 0.035f, 0.06f, 0.94f);
            backdrop.raycastTarget = true;

            var overlay = root.GetComponent<RestartRoleChoiceOverlay>();
            overlay.Build(buttonTemplate, ResolveFont(canvasRoot, buttonTemplate));
            overlay.Configure(returnSelectionTarget, roleSelected, cancelled);
            FarmUiSkin.ApplyFromCanvas(canvasRoot, root.transform);
            root.transform.SetAsLastSibling();
            return overlay;
        }

        private void OnDestroy()
        {
            ClearCallbacks();
        }

        private void Build(Button buttonTemplate, Font font)
        {
            var panel = new GameObject(
                "RestartRoleChoicePanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(VerticalLayoutGroup));
            panel.layer = gameObject.layer;

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 640f);

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.075f, 0.16f, 0.21f, 0.99f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(46, 46, 40, 40);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            // Every child supplies a deliberate LayoutElement height. Let the group honour those
            // values so the cloned ATTACK/DEFEND buttons never collapse to the template's size.
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText(
                panel.transform,
                "TitleText",
                "CHOOSE YOUR SIDE",
                font,
                34,
                FontStyle.Bold,
                76f);
            CreateText(
                panel.transform,
                "DescriptionText",
                "Restart this level as the attacking animals or the defending farmhands.",
                font,
                20,
                FontStyle.Normal,
                84f);

            attackerButton = CreateButton(panel.transform, buttonTemplate, "AttackRoleButton", "ATTACK", font);
            Button defenderButton = CreateButton(panel.transform, buttonTemplate, "DefendRoleButton", "DEFEND", font);
            Button cancelButton = CreateButton(panel.transform, buttonTemplate, "CancelButton", "CANCEL", font);

            if (attackerButton != null)
            {
                attackerButton.onClick.AddListener(() => ChooseRole(PlayerRole.Attacker));
            }

            if (defenderButton != null)
            {
                defenderButton.onClick.AddListener(() => ChooseRole(PlayerRole.Defender));
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(Cancel);
            }
        }

        private void Configure(
            GameObject returnSelectionTarget,
            Action<PlayerRole> roleSelected,
            Action cancelled)
        {
            selectionAfterClose = returnSelectionTarget;
            onRoleSelected = roleSelected;
            onCancelled = cancelled;
            Select(attackerButton != null ? attackerButton.gameObject : null);
        }

        private void ChooseRole(PlayerRole role)
        {
            Action<PlayerRole> callback = onRoleSelected;
            ClearCallbacks();
            Destroy(gameObject);
            callback?.Invoke(role);
        }

        private void Cancel()
        {
            Action callback = onCancelled;
            GameObject returnTarget = selectionAfterClose;
            ClearCallbacks();
            Destroy(gameObject);
            callback?.Invoke();
            Select(returnTarget);
        }

        private void ClearCallbacks()
        {
            onRoleSelected = null;
            onCancelled = null;
            selectionAfterClose = null;
        }

        private static Text CreateText(
            Transform parent,
            string objectName,
            string value,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            float preferredHeight)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text),
                typeof(LayoutElement));
            textObject.layer = parent.gameObject.layer;
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            var layout = textObject.GetComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            return text;
        }

        private static Button CreateButton(
            Transform parent,
            Button template,
            string objectName,
            string label,
            Font fallbackFont)
        {
            if (template != null)
            {
                GameObject buttonObject = Instantiate(template.gameObject, parent);
                buttonObject.name = objectName;
                buttonObject.layer = parent.gameObject.layer;

                var button = buttonObject.GetComponent<Button>();
                if (button == null)
                {
                    Destroy(buttonObject);
                    return null;
                }

                button.onClick.RemoveAllListeners();
                Text buttonText = button.GetComponentInChildren<Text>(true);
                if (buttonText != null)
                {
                    buttonText.text = label;
                }

                LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
                if (layout == null)
                {
                    layout = buttonObject.AddComponent<LayoutElement>();
                }

                layout.minHeight = RoleChoiceButtonHeight;
                layout.preferredHeight = RoleChoiceButtonHeight;
                FarmUiSkin.EnsureReadableWoodButtonLayout(button, RoleChoiceButtonHeight);
                return button;
            }

            var buttonObjectFallback = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObjectFallback.layer = parent.gameObject.layer;
            buttonObjectFallback.transform.SetParent(parent, false);

            var image = buttonObjectFallback.GetComponent<Image>();
            image.color = new Color(0.2f, 0.45f, 0.26f, 1f);

            var fallbackButton = buttonObjectFallback.GetComponent<Button>();
            fallbackButton.targetGraphic = image;
            var fallbackLayout = buttonObjectFallback.GetComponent<LayoutElement>();
            fallbackLayout.minHeight = RoleChoiceButtonHeight;
            fallbackLayout.preferredHeight = RoleChoiceButtonHeight;

            Text text = CreateText(
                buttonObjectFallback.transform,
                "Label",
                label,
                fallbackFont,
                24,
                FontStyle.Bold,
                0f);
            var labelRect = text.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Destroy(text.GetComponent<LayoutElement>());

            FarmUiSkin.EnsureReadableWoodButtonLayout(fallbackButton, RoleChoiceButtonHeight);

            return fallbackButton;
        }

        private static Font ResolveFont(Transform canvasRoot, Button buttonTemplate)
        {
            Text templateText = buttonTemplate != null ? buttonTemplate.GetComponentInChildren<Text>(true) : null;
            if (templateText != null && templateText.font != null)
            {
                return templateText.font;
            }

            Text canvasText = canvasRoot.GetComponentInChildren<Text>(true);
            if (canvasText != null && canvasText.font != null)
            {
                return canvasText.font;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
