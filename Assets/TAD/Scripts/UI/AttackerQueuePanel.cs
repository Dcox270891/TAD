using System;
using System.Collections.Generic;
using TAD.Match;
using TAD.Pooling;
using TAD.Progression;
using TAD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.UI
{
    /// <summary>
    /// Presents each distinct player-selected attacker group in the top-wave rail and
    /// coordinates the non-modal details card, route highlight, and local ordering-input suspension.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackerQueuePanel : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private AttackerQueueGroupButton groupButtonPrefab;

        private RectTransform legacyContent;
        private RectTransform queueRailContent;
        private ScrollRect scrollRect;
        private readonly Dictionary<long, AttackerQueueGroupButton> groupButtons = new();
        private readonly Dictionary<long, AttackerRoutePreview> allQueuedRoutePreviews = new();
        private readonly List<long> staleGroupIds = new();
        private readonly List<long> staleRoutePreviewIds = new();
        private readonly List<AttackerQueuedGroupInfo> displayGroups = new();
        private AttackerWaveQueue attackerWaveQueue;
        private SpawnPool spawnPool;
        private WaveController waveController;
        private AttackerSelectionPanel selectionPanel;
        private AttackerDetailsPanel detailsPanel;
        private WaveStartButton waveStartButton;
        private AttackerRoutePreview routePreview;
        private RectTransform queueScrollView;
        private RectTransform queueHeading;
        private long selectedGroupId;
        private bool isSubscribed;
        private bool isClosingSelection;
        // A dispatch can spawn several attackers in a single rendered frame. Refreshing every
        // card and every optional route preview synchronously for each spawn blocks taps on the
        // attacker tray, so coalesce presentation work to the next frame.
        private bool isRefreshQueued;

        private void Awake()
        {
            EnsureTopRailContent();
            queueScrollView ??= transform.Find("ScrollView") as RectTransform;
            queueHeading ??= transform.Find("Heading") as RectTransform;
            ApplyResponsiveLayout();
        }

        /// <summary>
        /// Reapplies the fixed safe region used by the left-side attacker UI. This is public so
        /// the UI asset builder can persist the same layout into the prefab, while OnEnable
        /// repairs retained play-session instances after scripts are reloaded.
        /// </summary>
        public void ApplyResponsiveLayout()
        {
            ConfigureTopRailPresentation();
            queueHeading ??= transform.Find("Heading") as RectTransform;
            var headingText = queueHeading != null ? queueHeading.GetComponent<UnityEngine.UI.Text>() : null;
            if (headingText != null)
            {
                headingText.text = "NEXT WAVE QUEUE";
            }

            // Every queue prefab owns a MobileUiLayoutController. Ask its canvas-level owner to
            // apply the current landscape or portrait rail rather than keeping a second set of
            // fixed offsets here that could fight the responsive profile on rotation.
            var layoutController = GetComponent<MobileUiLayoutController>();
            if (layoutController != null)
            {
                layoutController.RefreshLayout();
                return;
            }

            ConfigureTopRailFallback();
        }

        /// <summary>
        /// Presents queued groups in one horizontal row without replacing the serialized vertical
        /// content container. Unity permits only one LayoutGroup on an object, so the row owns a
        /// sibling content transform and the original prefab content remains available for older
        /// instances that may still be loaded.
        /// </summary>
        internal void ConfigureTopRailPresentation()
        {
            EnsureTopRailContent();
            if (queueRailContent == null)
            {
                return;
            }

            var horizontal = queueRailContent.GetComponent<HorizontalLayoutGroup>();
            if (horizontal != null)
            {
                horizontal.padding = new RectOffset(10, 10, 4, 4);
                horizontal.spacing = 10f;
                horizontal.childAlignment = TextAnchor.MiddleLeft;
                horizontal.childControlWidth = false;
                horizontal.childControlHeight = false;
                horizontal.childForceExpandWidth = false;
                horizontal.childForceExpandHeight = false;
            }

            if (scrollRect != null)
            {
                scrollRect.horizontal = true;
                scrollRect.vertical = false;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }
        }

        private void ConfigureTopRailFallback()
        {
            queueScrollView ??= transform.Find("ScrollView") as RectTransform;
            queueHeading ??= transform.Find("Heading") as RectTransform;

            ConfigureTopCenter(queueScrollView, 0f, -54f, 980f, 112f);
            ConfigureTopCenter(queueHeading, 0f, -12f, 980f, 34f);
        }

        private void EnsureTopRailContent()
        {
            queueScrollView ??= transform.Find("ScrollView") as RectTransform;
            scrollRect ??= queueScrollView != null ? queueScrollView.GetComponent<ScrollRect>() : null;
            var viewport = scrollRect != null
                ? scrollRect.viewport ?? queueScrollView?.Find("Viewport") as RectTransform
                : null;
            if (viewport == null)
            {
                return;
            }

            legacyContent ??= viewport.Find("Content") as RectTransform;
            queueRailContent ??= viewport.Find("QueueRailContent") as RectTransform;
            if (queueRailContent == null)
            {
                var railObject = new GameObject(
                    "QueueRailContent",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(HorizontalLayoutGroup),
                    typeof(ContentSizeFitter));
                railObject.layer = gameObject.layer;
                railObject.transform.SetParent(viewport, false);
                queueRailContent = railObject.GetComponent<RectTransform>();
                queueRailContent.anchorMin = new Vector2(0f, 0f);
                queueRailContent.anchorMax = new Vector2(0f, 1f);
                queueRailContent.pivot = new Vector2(0f, 0.5f);
                queueRailContent.anchoredPosition = Vector2.zero;
                queueRailContent.sizeDelta = Vector2.zero;

                var fitter = railObject.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

                if (legacyContent != null)
                {
                    while (legacyContent.childCount > 0)
                    {
                        legacyContent.GetChild(0).SetParent(queueRailContent, false);
                    }
                }
            }

            if (legacyContent != null && legacyContent != queueRailContent)
            {
                var oldLayout = legacyContent.GetComponent<VerticalLayoutGroup>();
                if (oldLayout != null)
                {
                    oldLayout.enabled = false;
                }

                legacyContent.gameObject.SetActive(false);
            }

            content = queueRailContent;
            if (scrollRect != null)
            {
                scrollRect.content = queueRailContent;
            }
        }

        private static void ConfigureTopCenter(RectTransform target, float x, float y, float width, float height)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = target.anchorMax = new Vector2(0.5f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.anchoredPosition = new Vector2(x, y);
            target.sizeDelta = new Vector2(width, height);
        }

        private void OnEnable()
        {
            // With domain/scene reload disabled, an already-instantiated panel can survive a
            // script update. Reapply the anchors here as well as in Awake so its old, taller
            // queue never resumes over the attacker income plaque.
            ApplyResponsiveLayout();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            isRefreshQueued = false;
            Unsubscribe();
            CloseSelectedGroup(false);
        }

        private void LateUpdate()
        {
            if (!isRefreshQueued)
            {
                return;
            }

            isRefreshQueued = false;
            Refresh();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (detailsPanel != null)
            {
                detailsPanel.Closed -= HandleDetailsClosed;
            }

            SetAttackerOrderingBlocked(false);
            if (routePreview != null)
            {
                Destroy(routePreview.gameObject);
            }

            ClearAllQueuedRoutePreviews();

            ClearButtons();
        }

        /// <summary>Connects this visual-only panel to the authoritative queue and existing attacker controls.</summary>
        public void Bind(
            AttackerWaveQueue waveQueue,
            SpawnPool pool,
            WaveController controller,
            AttackerSelectionPanel attackerSelectionPanel,
            AttackerDetailsPanel attackerDetailsPanel)
        {
            Unsubscribe();
            if (detailsPanel != null)
            {
                detailsPanel.Closed -= HandleDetailsClosed;
            }

            attackerWaveQueue = waveQueue;
            spawnPool = pool;
            waveController = controller;
            selectionPanel = attackerSelectionPanel;
            detailsPanel = attackerDetailsPanel;
            if (detailsPanel != null)
            {
                detailsPanel.Closed += HandleDetailsClosed;
            }

            ApplyResponsiveLayout();
            EnsureRoutePreview();
            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (isSubscribed || attackerWaveQueue == null)
            {
                return;
            }

            attackerWaveQueue.QueueChanged += ScheduleRefresh;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (attackerWaveQueue != null)
            {
                attackerWaveQueue.QueueChanged -= ScheduleRefresh;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
            isRefreshQueued = false;
            if (attackerWaveQueue == null || content == null || groupButtonPrefab == null)
            {
                return;
            }

            attackerWaveQueue.CopyDisplayGroups(displayGroups);
            staleGroupIds.Clear();
            foreach (var pair in groupButtons)
            {
                staleGroupIds.Add(pair.Key);
            }

            for (var index = 0; index < displayGroups.Count; index++)
            {
                var group = displayGroups[index];
                staleGroupIds.Remove(group.Id);
                if (!groupButtons.TryGetValue(group.Id, out var groupButton) || groupButton == null)
                {
                    groupButton = Instantiate(groupButtonPrefab, content);
                    groupButton.name = "Queued " + (group.Definition != null ? group.Definition.DisplayName : "Attacker");
                    groupButtons[group.Id] = groupButton;
                }

                groupButton.transform.SetSiblingIndex(index);
                groupButton.Bind(
                    group,
                    ResolveRightFacingSprite(group),
                    HandleGroupPressed,
                    group.Id == selectedGroupId);
            }

            foreach (var staleGroupId in staleGroupIds)
            {
                if (!groupButtons.TryGetValue(staleGroupId, out var staleButton))
                {
                    continue;
                }

                if (staleButton != null)
                {
                    Destroy(staleButton.gameObject);
                }

                groupButtons.Remove(staleGroupId);
            }

            RefreshAllQueuedRoutePreviews();

            if (selectedGroupId != 0)
            {
                if (attackerWaveQueue.TryGetDisplayGroup(selectedGroupId, out var selectedGroup))
                {
                    ShowSelectedGroup(selectedGroup);
                }
                else
                {
                    CloseSelectedGroup(false);
                }
            }
        }

        private void HandleGroupPressed(long groupId)
        {
            if (attackerWaveQueue == null)
            {
                return;
            }

            if (groupId == selectedGroupId)
            {
                CloseSelectedGroup(true);
                return;
            }

            if (!attackerWaveQueue.TryGetDisplayGroup(groupId, out var group) || group.Definition == null)
            {
                return;
            }

            selectedGroupId = groupId;
            SetAttackerOrderingBlocked(true);
            ShowSelectedGroup(group);
            Refresh();
        }

        private void ShowSelectedGroup(AttackerQueuedGroupInfo group)
        {
            var rightFacingSprite = ResolveRightFacingSprite(group);
            detailsPanel?.Show(attackerWaveQueue, group, rightFacingSprite);
            EnsureRoutePreview();
            routePreview?.Show(
                spawnPool != null ? spawnPool.RouteMapper : null,
                waveController != null ? waveController.SpawnPosition : Vector3.zero,
                group.Definition);
        }

        private Sprite ResolveRightFacingSprite(AttackerQueuedGroupInfo group)
        {
            if (group.Definition == null)
            {
                return null;
            }

            GameObject attackerPrefab = null;
            spawnPool?.TryGetAttackerPrefab(group.Definition, out attackerPrefab);
            return AttackerSelectionPanel.ResolveRightFacingSprite(group.Definition, attackerPrefab);
        }

        private void CloseSelectedGroup(bool refresh)
        {
            selectedGroupId = 0;
            routePreview?.Hide();
            SetAttackerOrderingBlocked(false);
            if (detailsPanel != null && detailsPanel.IsShowing)
            {
                isClosingSelection = true;
                detailsPanel.Hide();
                isClosingSelection = false;
            }

            if (refresh)
            {
                Refresh();
            }
        }

        private void HandleDetailsClosed()
        {
            if (isClosingSelection || selectedGroupId == 0)
            {
                return;
            }

            selectedGroupId = 0;
            routePreview?.Hide();
            SetAttackerOrderingBlocked(false);
            Refresh();
        }

        private void SetAttackerOrderingBlocked(bool blocked)
        {
            selectionPanel?.SetOrderInputEnabled(!blocked);
            selectionPanel?.SetPresentationVisible(!blocked);
            SetQueuePresentationVisible(!blocked);
            waveStartButton ??= FindAnyObjectByType<WaveStartButton>(FindObjectsInactive.Include);
            waveStartButton?.SetExternalInputBlocked(blocked);
        }

        private void SetQueuePresentationVisible(bool visible)
        {
            if (queueScrollView != null && queueScrollView.gameObject.activeSelf != visible)
            {
                queueScrollView.gameObject.SetActive(visible);
            }

            if (queueHeading != null && queueHeading.gameObject.activeSelf != visible)
            {
                queueHeading.gameObject.SetActive(visible);
            }
        }

        private void EnsureRoutePreview()
        {
            if (routePreview != null)
            {
                return;
            }

            var previewObject = new GameObject("Queued Attacker Route Preview", typeof(AttackerRoutePreview));
            routePreview = previewObject.GetComponent<AttackerRoutePreview>();
        }

        /// <summary>
        /// The General Route Scouting account upgrade turns the selected-route helper into a
        /// persistent overview. These previews are presentation-only and are refreshed whenever
        /// the authoritative queue changes, so removals and dispatches cannot leave stale paths.
        /// </summary>
        private void RefreshAllQueuedRoutePreviews()
        {
            var shouldShowAllRoutes = MatchRoleSelection.IsPlayerAttacker &&
                                      PlayerSettingsService.Instance.HasQueuedRouteDisplayUpgrade;
            if (!shouldShowAllRoutes || spawnPool == null || waveController == null)
            {
                ClearAllQueuedRoutePreviews();
                return;
            }

            staleRoutePreviewIds.Clear();
            foreach (var routePreviewId in allQueuedRoutePreviews.Keys)
            {
                staleRoutePreviewIds.Add(routePreviewId);
            }

            for (var index = 0; index < displayGroups.Count; index++)
            {
                var group = displayGroups[index];
                if (group.Definition == null)
                {
                    continue;
                }

                staleRoutePreviewIds.Remove(group.Id);
                if (!allQueuedRoutePreviews.TryGetValue(group.Id, out var preview) || preview == null)
                {
                    var previewObject = new GameObject("Queued Attacker Route " + group.Id, typeof(AttackerRoutePreview));
                    preview = previewObject.GetComponent<AttackerRoutePreview>();
                    allQueuedRoutePreviews[group.Id] = preview;
                }

                preview.Show(spawnPool.RouteMapper, waveController.SpawnPosition, group.Definition);
            }

            for (var index = 0; index < staleRoutePreviewIds.Count; index++)
            {
                var id = staleRoutePreviewIds[index];
                if (allQueuedRoutePreviews.TryGetValue(id, out var stalePreview) && stalePreview != null)
                {
                    Destroy(stalePreview.gameObject);
                }

                allQueuedRoutePreviews.Remove(id);
            }

            staleRoutePreviewIds.Clear();
        }

        private void ClearAllQueuedRoutePreviews()
        {
            foreach (var preview in allQueuedRoutePreviews.Values)
            {
                if (preview != null)
                {
                    Destroy(preview.gameObject);
                }
            }

            allQueuedRoutePreviews.Clear();
            staleRoutePreviewIds.Clear();
        }

        private void ClearButtons()
        {
            foreach (var groupButton in groupButtons.Values)
            {
                if (groupButton != null)
                {
                    Destroy(groupButton.gameObject);
                }
            }

            groupButtons.Clear();
            staleGroupIds.Clear();
            displayGroups.Clear();
        }

        private void ScheduleRefresh()
        {
            isRefreshQueued = true;
        }
    }
}
