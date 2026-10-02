using TAD.Attackers;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Compact, colour-coded attacker archetype marker shared by selection cards, queued-wave
    /// controls, and the attacker information panel. The badge is presentation only; the
    /// attacker definition remains the single source of truth for its archetype.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackerArchetypeBadge : MonoBehaviour
    {
        private const string BadgeObjectName = "ArchetypeBadge";
        private const string LabelObjectName = "Label";
        private const float MinimumHeight = 15f;
        private const float MaximumHeight = 24f;

        private UnityEngine.UI.Image background;
        private UnityEngine.UI.Text label;
        private UnityEngine.UI.Outline rim;

        /// <summary>Current player-facing archetype label, exposed for lightweight UI verification.</summary>
        public string Label => label != null ? label.text : string.Empty;

        /// <summary>
        /// Finds or creates the one archetype badge on an attacker portrait and updates it from
        /// the shared definition. The top-left location deliberately leaves the combat-rating
        /// star free in the portrait's bottom-right corner.
        /// </summary>
        public static AttackerArchetypeBadge ShowFor(
            UnityEngine.UI.Image attackerImage,
            AttackerDefinition definition)
        {
            if (attackerImage == null)
            {
                return null;
            }

            var badgeTransform = attackerImage.transform.Find(BadgeObjectName);
            var badge = badgeTransform != null
                ? badgeTransform.GetComponent<AttackerArchetypeBadge>()
                : null;
            if (badge == null)
            {
                var badgeObject = new GameObject(
                    BadgeObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(UnityEngine.UI.Image),
                    typeof(UnityEngine.UI.Outline),
                    typeof(AttackerArchetypeBadge));
                badgeObject.layer = attackerImage.gameObject.layer;
                badgeObject.transform.SetParent(attackerImage.transform, worldPositionStays: false);
                badge = badgeObject.GetComponent<AttackerArchetypeBadge>();
            }

            badge.Configure(definition, attackerImage.rectTransform);
            return badge;
        }

        private void Awake()
        {
            EnsureChildren();
        }

        /// <summary>Refreshes this marker from the authoritative attacker definition.</summary>
        public void Configure(AttackerDefinition definition, RectTransform attackerImageRect)
        {
            EnsureChildren();
            gameObject.SetActive(definition != null);
            if (definition == null)
            {
                return;
            }

            label.text = GetDisplayLabel(definition.Archetype);
            background.color = GetBackgroundColor(definition.Archetype);
            ConfigureLayout(attackerImageRect);
        }

        /// <summary>Returns the concise label used consistently throughout the attacker UI.</summary>
        public static string GetDisplayLabel(AttackerArchetype archetype)
        {
            return archetype switch
            {
                AttackerArchetype.Fast => "FAST",
                AttackerArchetype.Strong => "STRONG",
                AttackerArchetype.Flying => "FLYING",
                _ => "BASIC"
            };
        }

        private void EnsureChildren()
        {
            background ??= GetComponent<UnityEngine.UI.Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<UnityEngine.UI.Image>();
            }

            background.sprite = null;
            background.type = UnityEngine.UI.Image.Type.Simple;
            background.raycastTarget = false;

            rim ??= GetComponent<UnityEngine.UI.Outline>();
            if (rim == null)
            {
                rim = gameObject.AddComponent<UnityEngine.UI.Outline>();
            }

            rim.effectColor = new Color(0.24f, 0.105f, 0.03f, 0.92f);
            rim.effectDistance = new Vector2(0.75f, -0.75f);
            rim.useGraphicAlpha = true;

            var labelTransform = transform.Find(LabelObjectName);
            label = labelTransform != null
                ? labelTransform.GetComponent<UnityEngine.UI.Text>()
                : null;
            if (label == null)
            {
                var labelObject = new GameObject(
                    LabelObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(UnityEngine.UI.Text));
                labelObject.layer = gameObject.layer;
                labelObject.transform.SetParent(transform, worldPositionStays: false);
                label = labelObject.GetComponent<UnityEngine.UI.Text>();
            }

            label.font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color = new Color(0.24f, 0.105f, 0.03f, 1f);
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            label.raycastTarget = false;
        }

        private void ConfigureLayout(RectTransform attackerImageRect)
        {
            var availableSize = attackerImageRect != null
                ? Mathf.Min(attackerImageRect.rect.width, attackerImageRect.rect.height)
                : 80f;
            var height = Mathf.Clamp(availableSize * 0.23f, MinimumHeight, MaximumHeight);
            var width = Mathf.Clamp(height * 3.45f, 44f, 78f);
            var padding = Mathf.Max(2f, height * 0.13f);

            var badgeRect = transform as RectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(width, height);
            badgeRect.anchoredPosition = new Vector2(padding, -padding);
            badgeRect.localScale = Vector3.one;

            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(2f, 0f);
            labelRect.offsetMax = new Vector2(-2f, 0f);
            labelRect.localScale = Vector3.one;
            label.fontSize = Mathf.Clamp(Mathf.RoundToInt(height * 0.56f), 8, 14);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(7, Mathf.RoundToInt(label.fontSize * 0.72f));
            label.resizeTextMaxSize = label.fontSize;
        }

        private static Color GetBackgroundColor(AttackerArchetype archetype)
        {
            return archetype switch
            {
                AttackerArchetype.Fast => new Color(0.44f, 0.82f, 0.74f, 0.96f),
                AttackerArchetype.Strong => new Color(0.88f, 0.49f, 0.34f, 0.96f),
                AttackerArchetype.Flying => new Color(0.52f, 0.76f, 0.96f, 0.96f),
                _ => new Color(0.95f, 0.78f, 0.33f, 0.96f)
            };
        }
    }
}
