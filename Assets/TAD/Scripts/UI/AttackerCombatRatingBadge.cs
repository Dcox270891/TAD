using TAD.Attackers;
using TAD.Match;
using TAD.Progression;
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
        private const string RatingSafeAreaObjectName = "RatingSafeArea";
        private const string RatingLabelName = "Rating";
        private const string SendSizeLabelName = "SendSize";
        private const float DefaultBadgeSize = 44f;
        private const float MinimumBadgeSize = 30f;
        private const float MaximumBadgeSize = 56f;
        private const float SafeAreaHorizontalInsetFraction = 0.16f;
        private const float SafeAreaVerticalInsetFraction = 0.18f;

        private CombatRatingStarGraphic starGraphic;
        private RectTransform ratingSafeArea;
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

            var profile = MatchRoleSelection.IsPlayerAttacker
                ? PlayerSettingsService.Instance.Profile
                : null;
            ratingText.text = "CR:" + AttackerCombatRating.Calculate(definition, profile).ToString("0.#");
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

            ratingSafeArea ??= FindOrCreateSafeArea();
            ratingText ??= FindOrCreateText(RatingLabelName);
            sendSizeText ??= FindOrCreateText(SendSizeLabelName);
            var inkBrown = new Color(0.24f, 0.105f, 0.03f, 1f);
            ConfigureTextDefaults(ratingText, inkBrown);
            ConfigureTextDefaults(sendSizeText, inkBrown);
        }

        private void ConfigureLayout(RectTransform attackerImageRect)
        {
            var badgeSize = DefaultBadgeSize;
            if (attackerImageRect != null)
            {
                var availableSize = Mathf.Min(attackerImageRect.rect.width, attackerImageRect.rect.height);
                if (availableSize > 0.01f)
                {
                    badgeSize = Mathf.Clamp(availableSize * 0.5f, MinimumBadgeSize, MaximumBadgeSize);
                }
            }

            var badgeRect = transform as RectTransform;
            badgeRect.anchorMin = new Vector2(1f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 0f);
            badgeRect.pivot = new Vector2(1f, 0f);
            badgeRect.sizeDelta = Vector2.one * badgeSize;
            var padding = Mathf.Max(2f, badgeSize * 0.08f);
            badgeRect.anchoredPosition = new Vector2(-padding, padding);

            ConfigureSafeArea(badgeSize);

            // Keep the rating and send count in a single clipped interior region.  In the older
            // presentation the group count was positioned outside the star, which let it cross
            // the card edge on compact mobile layouts.  Both values now remain readable without
            // ever drawing beyond their star parent.
            var ratingRect = ratingText.rectTransform;
            ratingRect.anchorMin = new Vector2(0f, 0.38f);
            ratingRect.anchorMax = Vector2.one;
            ratingRect.pivot = new Vector2(0.5f, 0.5f);
            ratingRect.offsetMin = Vector2.zero;
            ratingRect.offsetMax = Vector2.zero;
            ratingText.fontSize = Mathf.Clamp(Mathf.RoundToInt(badgeSize * 0.25f), 8, 14);

            var sendSizeRect = sendSizeText.rectTransform;
            sendSizeRect.anchorMin = Vector2.zero;
            sendSizeRect.anchorMax = new Vector2(1f, 0.38f);
            sendSizeRect.pivot = new Vector2(0.5f, 0.5f);
            sendSizeRect.offsetMin = Vector2.zero;
            sendSizeRect.offsetMax = Vector2.zero;
            sendSizeText.fontSize = Mathf.Clamp(Mathf.RoundToInt(badgeSize * 0.21f), 7, 12);
        }

        private UnityEngine.UI.Text FindOrCreateText(string childName)
        {
            var existing = ratingSafeArea != null ? ratingSafeArea.Find(childName) : null;
            if (existing == null)
            {
                // Migrate badges created by the previous layout without introducing duplicate
                // labels in a saved scene or a pooled runtime card.
                existing = transform.Find(childName);
            }

            if (existing != null && existing.TryGetComponent<UnityEngine.UI.Text>(out var text))
            {
                if (ratingSafeArea != null && text.transform.parent != ratingSafeArea)
                {
                    text.transform.SetParent(ratingSafeArea, worldPositionStays: false);
                }

                return text;
            }

            var labelObject = new GameObject(
                childName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Text));
            labelObject.transform.SetParent(ratingSafeArea != null ? ratingSafeArea : transform, worldPositionStays: false);
            return labelObject.GetComponent<UnityEngine.UI.Text>();
        }

        private RectTransform FindOrCreateSafeArea()
        {
            var existing = transform.Find(RatingSafeAreaObjectName) as RectTransform;
            if (existing != null)
            {
                if (existing.GetComponent<UnityEngine.UI.RectMask2D>() == null)
                {
                    existing.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                }

                return existing;
            }

            var safeAreaObject = new GameObject(
                RatingSafeAreaObjectName,
                typeof(RectTransform),
                typeof(UnityEngine.UI.RectMask2D));
            safeAreaObject.transform.SetParent(transform, worldPositionStays: false);
            return safeAreaObject.GetComponent<RectTransform>();
        }

        private void ConfigureSafeArea(float badgeSize)
        {
            if (ratingSafeArea == null)
            {
                return;
            }

            var horizontalInset = badgeSize * SafeAreaHorizontalInsetFraction;
            var verticalInset = badgeSize * SafeAreaVerticalInsetFraction;
            ratingSafeArea.anchorMin = Vector2.zero;
            ratingSafeArea.anchorMax = Vector2.one;
            ratingSafeArea.pivot = new Vector2(0.5f, 0.5f);
            ratingSafeArea.offsetMin = new Vector2(horizontalInset, verticalInset);
            ratingSafeArea.offsetMax = new Vector2(-horizontalInset, -verticalInset);
            ratingSafeArea.localScale = Vector3.one;
            ratingSafeArea.SetAsLastSibling();
        }

        private static void ConfigureTextDefaults(UnityEngine.UI.Text text, Color color)
        {
            if (text == null)
            {
                return;
            }

            text.font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = color;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 7;
            text.resizeTextMaxSize = 14;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
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
