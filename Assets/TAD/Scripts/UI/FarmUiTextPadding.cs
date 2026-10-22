using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Keeps one direct text child inside the readable interior of its visual owner. It is used
    /// by the shared farm UI skin for labels which have bespoke positions (card headings, costs,
    /// and descriptions) rather than a layout group. The component deliberately owns neither
    /// the text nor the owner hierarchy; it only restores safe bounds after a responsive layout
    /// has changed their dimensions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class FarmUiTextPadding : MonoBehaviour
    {
        [SerializeField] private RectTransform contentOwner;
        [SerializeField, Min(0f)] private float horizontalPadding = 16f;
        [SerializeField, Min(0f)] private float verticalPadding = 12f;
        [SerializeField] private bool keepSingleLine;
        // Regular mobile button labels retain a readable floor. If a direct button is too narrow,
        // the decorated surface grows within its parent before this floor is compromised.
        [SerializeField, Min(1)] private int minimumSingleLineFontSize = 14;
        [SerializeField, HideInInspector] private int maximumSingleLineFontSize;

        private RectTransform textRect;
        private Text text;
        private int pendingLayoutFrames;

        /// <summary>Horizontal breathing room reserved inside the decorated surface.</summary>
        public float HorizontalPadding => horizontalPadding;

        /// <summary>Vertical breathing room reserved inside the decorated surface.</summary>
        public float VerticalPadding => verticalPadding;

        /// <summary>The immediate card or button surface that contains this label.</summary>
        public RectTransform ContentOwner => contentOwner;

        /// <summary>True when the owning button requires this label to remain on one line.</summary>
        public bool KeepsSingleLine => keepSingleLine;

        /// <summary>
        /// Configures this label's safe drawing area.  Only immediate children are supported so
        /// a nested layout group remains the sole owner of its own content placement.
        /// </summary>
        public void Configure(
            RectTransform owner,
            float horizontalInset,
            float verticalInset,
            bool preventWrapping = false)
        {
            ResolveReferences();
            contentOwner = owner;
            horizontalPadding = Mathf.Max(0f, horizontalInset);
            verticalPadding = Mathf.Max(0f, verticalInset);
            keepSingleLine = preventWrapping;
            if (keepSingleLine && text != null)
            {
                // Existing serialized labels predate this field; preserve a usable mobile floor
                // when Unity deserializes those components before the editor art pass rewrites them.
                minimumSingleLineFontSize = Mathf.Max(14, minimumSingleLineFontSize);
                // FarmUiSkin resets its typography before asking this component to fit the label,
                // so this stores the intended display size rather than a previously shrunken size.
                maximumSingleLineFontSize = Mathf.Max(1, Mathf.Max(text.fontSize, text.resizeTextMaxSize));
            }

            RequestLayout();

            if (!Application.isPlaying)
            {
                ApplyNow();
            }
        }

        /// <summary>Queues one or two layout passes for a responsive canvas size change.</summary>
        public void RequestLayout()
        {
            pendingLayoutFrames = Mathf.Max(pendingLayoutFrames, 2);
        }

        /// <summary>Applies the safe content bounds immediately when the owner has a usable size.</summary>
        public void ApplyNow()
        {
            ResolveReferences();
            if (textRect == null || text == null || contentOwner == null || textRect.parent != contentOwner)
            {
                return;
            }

            var ownerRect = contentOwner.rect;
            if (ownerRect.width <= 0.01f || ownerRect.height <= 0.01f)
            {
                return;
            }

            if (keepSingleLine)
            {
                ExpandDirectButtonToReadableWidth();
                ownerRect = contentOwner.rect;
            }

            var availableWidth = Mathf.Max(1f, ownerRect.width - horizontalPadding * 2f);
            var availableHeight = Mathf.Max(1f, ownerRect.height - verticalPadding * 2f);
            if (textRect.rect.width > availableWidth + 0.01f)
            {
                textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
            }

            if (textRect.rect.height > availableHeight + 0.01f)
            {
                textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, availableHeight);
            }

            var contentBounds = Rect.MinMaxRect(
                ownerRect.xMin + horizontalPadding,
                ownerRect.yMin + verticalPadding,
                ownerRect.xMax - horizontalPadding,
                ownerRect.yMax - verticalPadding);
            var textBounds = GetBoundsInOwnerSpace(textRect, contentOwner);
            var correction = Vector2.zero;
            if (textBounds.xMin < contentBounds.xMin)
            {
                correction.x += contentBounds.xMin - textBounds.xMin;
            }
            else if (textBounds.xMax > contentBounds.xMax)
            {
                correction.x += contentBounds.xMax - textBounds.xMax;
            }

            if (textBounds.yMin < contentBounds.yMin)
            {
                correction.y += contentBounds.yMin - textBounds.yMin;
            }
            else if (textBounds.yMax > contentBounds.yMax)
            {
                correction.y += contentBounds.yMax - textBounds.yMax;
            }

            if (correction.sqrMagnitude > 0.0001f)
            {
                textRect.anchoredPosition += correction;
            }

            ApplyTextOverflowAndFit(availableWidth);
        }

        /// <summary>Gets or adds the shared containment component without altering hierarchy ownership.</summary>
        public static FarmUiTextPadding Ensure(
            Text target,
            RectTransform owner,
            float horizontalInset,
            float verticalInset,
            bool preventWrapping = false)
        {
            if (target == null || owner == null || target.rectTransform.parent != owner)
            {
                return null;
            }

            var padding = target.GetComponent<FarmUiTextPadding>();
            if (padding == null)
            {
                padding = target.gameObject.AddComponent<FarmUiTextPadding>();
            }

            padding.Configure(owner, horizontalInset, verticalInset, preventWrapping);
            return padding;
        }

        private void ApplyTextOverflowAndFit(float availableWidth)
        {
            if (!keepSingleLine)
            {
                // Information-card copy may still use multiple lines; it is kept inside its
                // owner by the same four-sided bounds correction above.
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                return;
            }

            // TAD currently ships the legacy uGUI Text component, so apply the same no-wrap
            // contract expected of a TMP button label without adding a second text package.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;

            if (text.font == null || string.IsNullOrEmpty(text.text))
            {
                return;
            }

            var textWidth = Mathf.Max(1f, Mathf.Min(availableWidth, textRect.rect.width));
            var maximumSize = Mathf.Max(1, maximumSingleLineFontSize > 0
                ? maximumSingleLineFontSize
                : Mathf.Max(text.fontSize, text.resizeTextMaxSize));
            var minimumSize = Mathf.Clamp(minimumSingleLineFontSize, 1, maximumSize);
            text.fontSize = FindLargestFittingSingleLineFontSize(textWidth, minimumSize, maximumSize);
        }

        /// <summary>
        /// Lets a direct, absolute-positioned button make room for a readable label. Layout-group
        /// buttons keep their parent-owned sizing, so this never changes an existing content flow.
        /// </summary>
        private void ExpandDirectButtonToReadableWidth()
        {
            if (text == null || text.font == null || contentOwner == null ||
                contentOwner.GetComponent<Button>() == null ||
                contentOwner.parent is not RectTransform parent ||
                parent.GetComponent<LayoutGroup>() != null ||
                !Mathf.Approximately(contentOwner.anchorMin.x, contentOwner.anchorMax.x))
            {
                return;
            }

            var requiredWidth = GetPreferredSingleLineWidth(minimumSingleLineFontSize) + horizontalPadding * 2f;
            if (requiredWidth <= contentOwner.rect.width + 0.1f)
            {
                return;
            }

            // Preserve a modest gutter against the surrounding card or screen. The button can
            // grow only within the area its existing direct parent makes available.
            var outerGutter = Mathf.Max(12f, horizontalPadding * 0.5f);
            var maximumWidth = parent.rect.width - outerGutter * 2f;
            if (maximumWidth <= contentOwner.rect.width + 0.1f)
            {
                return;
            }

            contentOwner.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Min(requiredWidth, maximumWidth));
        }

        private int FindLargestFittingSingleLineFontSize(float availableWidth, int minimumSize, int maximumSize)
        {
            if (!FitsSingleLine(maximumSize, availableWidth))
            {
                var lower = minimumSize;
                var upper = maximumSize;
                var best = minimumSize;
                while (lower <= upper)
                {
                    var candidate = lower + (upper - lower) / 2;
                    if (FitsSingleLine(candidate, availableWidth))
                    {
                        best = candidate;
                        lower = candidate + 1;
                    }
                    else
                    {
                        upper = candidate - 1;
                    }
                }

                return best;
            }

            return maximumSize;
        }

        private bool FitsSingleLine(int fontSize, float availableWidth)
        {
            return GetPreferredSingleLineWidth(fontSize) <= availableWidth + 0.1f;
        }

        private float GetPreferredSingleLineWidth(int fontSize)
        {
            var settings = text.GetGenerationSettings(new Vector2(100000f, Mathf.Max(1f, textRect.rect.height)));
            settings.fontSize = fontSize;
            settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Truncate;
            var pixelsPerUnit = Mathf.Max(0.0001f, text.pixelsPerUnit);
            return text.cachedTextGeneratorForLayout.GetPreferredWidth(text.text, settings) / pixelsPerUnit;
        }

        private void OnEnable()
        {
            RequestLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            RequestLayout();
        }

        private void LateUpdate()
        {
            if (pendingLayoutFrames <= 0)
            {
                return;
            }

            pendingLayoutFrames--;
            ApplyNow();
        }

        private void ResolveReferences()
        {
            textRect ??= transform as RectTransform;
            text ??= GetComponent<Text>();
        }

        private static Rect GetBoundsInOwnerSpace(RectTransform target, RectTransform owner)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var minimum = owner.InverseTransformPoint(corners[0]);
            var maximum = minimum;
            for (var index = 1; index < corners.Length; index++)
            {
                var local = owner.InverseTransformPoint(corners[index]);
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }

            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }
    }
}
