using TAD.Attackers;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Reusable overlay for attacker artwork. It draws a compact star-shaped Combat Rating label
    /// and the card's send size beside it without giving each attacker UI a separate rating state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackerCombatRatingBadge : MonoBehaviour
    {
        private const string BadgeObjectName = "CombatRatingBadge";
        private const string RatingLabelName = "Rating";
        private const string SendSizeLabelName = "SendSize";
        private const float DefaultBadgeSize = 36f;
        private const float MinimumBadgeSize = 24f;
        private const float MaximumBadgeSize = 42f;

        private CombatRatingStarGraphic starGraphic;
        private UnityEngine.UI.Text ratingText;
        private UnityEngine.UI.Text sendSizeText;

        /// <summary>Current rating label, exposed for lightweight UI verification.</summary>
        public string RatingLabel => ratingText != null ? ratingText.text : string.Empty;

        /// <summary>Current group-size label, exposed for lightweight UI verification.</summary>
        public string SendSizeLabel => sendSizeText != null ? sendSizeText.text : string.Empty;

        /// <summary>
        /// Finds or creates the one badge beneath an attacker image and updates it from the shared
        /// data definition. Selection cards, queue cues, and detail cards all call this same entry point.
        /// </summary>
        public static AttackerCombatRatingBadge ShowFor(
            UnityEngine.UI.Image attackerImage,
            AttackerDefinition definition)
        {
            if (attackerImage == null)
            {
                return null;
            }

            var badgeTransform = attackerImage.transform.Find(BadgeObjectName);
            var badge = badgeTransform != null
                ? badgeTransform.GetComponent<AttackerCombatRatingBadge>()
                : null;
            if (badge == null)
            {
                var badgeObject = new GameObject(
                    BadgeObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(CombatRatingStarGraphic),
                    typeof(AttackerCombatRatingBadge));
                badgeObject.transform.SetParent(attackerImage.transform, worldPositionStays: false);
                badge = badgeObject.GetComponent<AttackerCombatRatingBadge>();
            }

            badge.Configure(definition, attackerImage.rectTransform);
            FarmUiSkin.ApplyFromCanvas(attackerImage.transform, badge.transform);
            return badge;
        }

        private void Awake()
        {
            EnsureChildren();
        }

        /// <summary>Refreshes the presentation from its authoritative attacker definition.</summary>
        public void Configure(AttackerDefinition definition, RectTransform attackerImageRect)
        {
            EnsureChildren();
            ConfigureLayout(attackerImageRect);
            gameObject.SetActive(definition != null);
            if (definition == null)
            {
                return;
            }

            ratingText.text = "CR:" + definition.CombatRating.ToString("0.#");
            sendSizeText.text = "x" + definition.SendSize;
        }

        private void EnsureChildren()
        {
            starGraphic ??= GetComponent<CombatRatingStarGraphic>();
            if (starGraphic == null)
            {
                starGraphic = gameObject.AddComponent<CombatRatingStarGraphic>();
            }

            starGraphic.color = new Color(1f, 0.72f, 0.12f, 0.96f);
            starGraphic.raycastTarget = false;

            ratingText ??= FindOrCreateText(RatingLabelName);
            sendSizeText ??= FindOrCreateText(SendSizeLabelName);
            ConfigureTextDefaults(ratingText, Color.black);
            ConfigureTextDefaults(sendSizeText, Color.white);

            var sendSizeOutline = sendSizeText.GetComponent<UnityEngine.UI.Outline>();
            if (sendSizeOutline == null)
            {
                sendSizeOutline = sendSizeText.gameObject.AddComponent<UnityEngine.UI.Outline>();
            }

            sendSizeOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sendSizeOutline.effectDistance = new Vector2(1f, -1f);
        }

        private void ConfigureLayout(RectTransform attackerImageRect)
        {
            var badgeSize = DefaultBadgeSize;
            if (attackerImageRect != null)
            {
                var availableSize = Mathf.Min(attackerImageRect.rect.width, attackerImageRect.rect.height);
                if (availableSize > 0.01f)
                {
                    badgeSize = Mathf.Clamp(availableSize * 0.42f, MinimumBadgeSize, MaximumBadgeSize);
                }
            }

            var badgeRect = transform as RectTransform;
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.sizeDelta = Vector2.one * badgeSize;
            var padding = Mathf.Max(2f, badgeSize * 0.08f);
            badgeRect.anchoredPosition = new Vector2(-padding, padding);

            var ratingRect = ratingText.rectTransform;
            ratingRect.anchorMin = Vector2.zero;
            ratingRect.anchorMax = Vector2.one;
            ratingRect.offsetMin = new Vector2(3f, 4f);
            ratingRect.offsetMax = new Vector2(-3f, -3f);
            ratingText.fontSize = Mathf.Clamp(Mathf.RoundToInt(badgeSize * 0.24f), 9, 12);

            // The group count sits just to the left of the star, keeping it visibly outside the
            // shape while remaining within even compact circular queue buttons.
            var sendSizeRect = sendSizeText.rectTransform;
            sendSizeRect.anchorMin = new Vector2(0f, 0.5f);
            sendSizeRect.anchorMax = new Vector2(0f, 0.5f);
            sendSizeRect.pivot = new Vector2(1f, 0.5f);
            sendSizeRect.sizeDelta = new Vector2(badgeSize * 0.75f, badgeSize * 0.55f);
            sendSizeRect.anchoredPosition = new Vector2(-2f, 0f);
            sendSizeText.fontSize = Mathf.Clamp(Mathf.RoundToInt(badgeSize * 0.30f), 10, 15);
        }

        private UnityEngine.UI.Text FindOrCreateText(string childName)
        {
            var existing = transform.Find(childName);
            if (existing != null && existing.TryGetComponent<UnityEngine.UI.Text>(out var text))
            {
                return text;
            }

            var labelObject = new GameObject(
                childName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text));
            labelObject.transform.SetParent(transform, worldPositionStays: false);
            return labelObject.GetComponent<UnityEngine.UI.Text>();
        }

        private static void ConfigureTextDefaults(UnityEngine.UI.Text text, Color color)
        {
            if (text == null)
            {
                return;
            }

            text.font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
        }
    }

    /// <summary>Minimal uGUI graphic that renders a filled five-point star without a texture asset.</summary>
    [DisallowMultipleComponent]
    internal sealed class CombatRatingStarGraphic : UnityEngine.UI.MaskableGraphic
    {
        private const int PointCount = 10;
        private const float InnerRadiusMultiplier = 0.45f;

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = rectTransform.rect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0.001f)
            {
                return;
            }

            var center = rect.center;
            vertexHelper.AddVert(center, color, Vector2.zero);
            for (var index = 0; index < PointCount; index++)
            {
                var angle = (90f - (index * 36f)) * Mathf.Deg2Rad;
                var pointRadius = index % 2 == 0 ? radius : radius * InnerRadiusMultiplier;
                var position = center + new Vector2(
                    Mathf.Cos(angle) * pointRadius,
                    Mathf.Sin(angle) * pointRadius);
                vertexHelper.AddVert(position, color, Vector2.zero);
            }

            for (var index = 0; index < PointCount; index++)
            {
                var nextIndex = (index + 1) % PointCount;
                vertexHelper.AddTriangle(0, index + 1, nextIndex + 1);
            }
        }
    }
}
