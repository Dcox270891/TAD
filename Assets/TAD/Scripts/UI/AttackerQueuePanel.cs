using System;
using System.Collections.Generic;
using TAD.Pooling;
using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Presents each distinct player-selected attacker group as a left-side circular control and
    /// coordinates the non-modal details card, route highlight, and local ordering-input suspension.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AttackerQueuePanel : MonoBehaviour
    {
        // The attacker bank uses two stacked plaques. Keep the queue below both and stop before
        // the bottom attacker-card tray, rather than relying on the old single-label offset.
        private const float QueueScrollTopOffset = -212f;
        private const float QueueScrollHeight = 602f;
        private const float QueueHeadingTopOffset = -189f;

        [SerializeField] private RectTransform content;
        [SerializeField] private AttackerQueueGroupButton groupButtonPrefab;

        private readonly Dictionary<long, AttackerQueueGroupButton> groupButtons = new();
        private readonly List<long> staleGroupIds = new();
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

        private void Awake()
        {
            content ??= transform.Find("ScrollView/Viewport/Content") as RectTransform;
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
            ConfigureLeftSideLayout();
        }

        private void ConfigureLeftSideLayout()
        {
            queueScrollView ??= transform.Find("ScrollView") as RectTransform;
            queueHeading ??= transform.Find("Heading") as RectTransform;
            ConfigureTopLeft(queueScrollView, new Vector2(16f, QueueScrollTopOffset));
            if (queueScrollView != null)
            {
                queueScrollView.sizeDelta = new Vector2(queueScrollView.sizeDelta.x, QueueScrollHeight);
            }

            ConfigureTopLeft(queueHeading, new Vector2(16f, QueueHeadingTopOffset));
            var headingText = queueHeading != null ? queueHeading.GetComponent<UnityEngine.UI.Text>() : null;
            if (headingText != null)
            {
                headingText.text = "UP NEXT";
            }
        }

        private static void ConfigureTopLeft(RectTransform target, Vector2 anchoredPosition)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = anchoredPosition;
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
            Unsubscribe();
            CloseSelectedGroup(false);
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

            attackerWaveQueue.QueueChanged += Refresh;
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
                attackerWaveQueue.QueueChanged -= Refresh;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
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
    }
}
