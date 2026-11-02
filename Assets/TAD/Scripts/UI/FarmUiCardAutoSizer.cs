using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Lets a parchment information card grow vertically when its wrapped copy, buttons, or
    /// runtime statistics need more room. It never takes ownership of child placement and never
    /// shrinks below the author-designed size, so fixed mobile layouts remain stable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class FarmUiCardAutoSizer : MonoBehaviour
    {
        [SerializeField] private float minimumHeight;
        [SerializeField] private float bottomPadding = 24f;
        [SerializeField, Range(0.45f, 0.92f)] private float maximumCanvasHeightFraction = 0.82f;

        private RectTransform cardRect;
        private Canvas parentCanvas;
        private ModalOverlay enclosingModalOverlay;
        private Text[] contentTexts;
        private readonly Vector3[] worldCorners = new Vector3[4];
        private int lastContentHash = int.MinValue;
        private bool layoutRequested = true;
        private bool wasHiddenByModal;

        public void Configure(float minimumCardHeight, float padding = 24f)
        {
            minimumHeight = Mathf.Max(0f, minimumCardHeight);
            bottomPadding = Mathf.Max(0f, padding);
            RequestLayout();
        }

        public void RequestLayout()
        {
            layoutRequested = true;
        }

        private void Awake()
        {
            ResolveReferences();
            RefreshContentReferences();
            if (cardRect != null)
            {
                minimumHeight = Mathf.Max(minimumHeight, cardRect.rect.height);
            }
        }

        private void OnEnable()
        {
            RefreshContentReferences();
            RequestLayout();
        }

        private void OnTransformChildrenChanged()
        {
            // Runtime cards occasionally add or remove a content row. Cache the text collection
            // at that point rather than allocating a new component array every rendered frame.
            RefreshContentReferences();
            RequestLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            RequestLayout();
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResolveReferences();
            if (enclosingModalOverlay != null && !enclosingModalOverlay.IsVisible)
            {
                // Modal overlays hide by CanvasGroup rather than deactivating their hierarchy.
                // Avoid walking every child text and calculating preferred heights while that card
                // cannot be seen or interacted with; this otherwise continues every frame during
                // active combat behind hidden details/win/pause cards.
                wasHiddenByModal = true;
                return;
            }

            if (wasHiddenByModal)
            {
                // Copy may have changed while the overlay was hidden. Rebuild once as it becomes
                // visible again, then return to the normal low-frequency content-hash path.
                wasHiddenByModal = false;
                RequestLayout();
            }

            var contentHash = CalculateContentHash();
            if (contentHash != lastContentHash)
            {
                layoutRequested = true;
            }

            if (!layoutRequested)
            {
                return;
            }

            Rebuild();
            lastContentHash = CalculateContentHash();
            layoutRequested = false;
        }

        private void ResolveReferences()
        {
            cardRect ??= transform as RectTransform;
            parentCanvas ??= GetComponentInParent<Canvas>();
            enclosingModalOverlay ??= GetComponentInParent<ModalOverlay>(true);
        }

        private int CalculateContentHash()
        {
            contentTexts ??= GetComponentsInChildren<Text>(true);
            unchecked
            {
                var hash = 17;
                foreach (var text in contentTexts)
                {
                    if (text == null || !text.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    hash = hash * 31 + (text.text != null ? text.text.GetHashCode() : 0);
                    hash = hash * 31 + Mathf.RoundToInt(text.rectTransform.rect.width * 10f);
                    hash = hash * 31 + Mathf.RoundToInt(text.preferredHeight * 10f);
                }

                return hash;
            }
        }

        private void Rebuild()
        {
            if (cardRect == null || cardRect.GetComponent<LayoutGroup>() != null)
            {
                return;
            }

            contentTexts ??= GetComponentsInChildren<Text>(true);
            Canvas.ForceUpdateCanvases();
            var top = cardRect.rect.yMax;
            var lowestContent = top - minimumHeight + bottomPadding;
            foreach (var text in contentTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy ||
                    text.transform.IsChildOf(transform) == false ||
                    IsDecoration(text.transform))
                {
                    continue;
                }

                text.rectTransform.GetWorldCorners(worldCorners);
                for (var index = 0; index < worldCorners.Length; index++)
                {
                    lowestContent = Mathf.Min(lowestContent, cardRect.InverseTransformPoint(worldCorners[index]).y);
                }
            }

            var requiredHeight = Mathf.Max(minimumHeight, top - lowestContent + bottomPadding);
            var canvasHeight = parentCanvas != null && parentCanvas.transform is RectTransform canvasRect
                ? canvasRect.rect.height
                : 0f;
            if (canvasHeight > 0f)
            {
                requiredHeight = Mathf.Min(requiredHeight, canvasHeight * maximumCanvasHeightFraction);
            }

            if (requiredHeight > cardRect.rect.height + 0.5f)
            {
                cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
            }
        }

        private void RefreshContentReferences()
        {
            contentTexts = GetComponentsInChildren<Text>(true);
        }

        private static bool IsDecoration(Transform candidate)
        {
            return candidate != null &&
                   (candidate.name == FarmUiThinWoodOutline.FrameObjectName ||
                    candidate.GetComponentInParent<FarmUiThinWoodOutline>(true) != null);
        }
    }
}
