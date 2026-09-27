using System;
using System.Collections.Generic;
using TAD.Defenders;
using TAD.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Owns level-3 defender upgrade requests for one level. Human Defenders choose through a
    /// single modal; AI-controlled Defenders choose the best affordable upgrade for the queued attacker routes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefenderLevelUpgradeBootstrap : MonoBehaviour
    {
        private const int UpgradeOptionCount = 4;

        [SerializeField] private DefenderLevelUpgradeOverlay overlayPrefab;
        [SerializeField] private WeaponDefinition[] levelThreeUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];

        private readonly Queue<FarmHand> pendingFarmHands = new();
        private readonly HashSet<FarmHand> pendingFarmHandSet = new();
        private DefenderLevelUpgradeOverlay spawnedOverlay;
        private FarmHand presentedFarmHand;
        private bool ownsSpawnedOverlay;
        private bool isSubscribed;
        private bool isOverlaySubscribed;
        private bool hasReportedConfigurationError;
        private MatchRoleController matchRoleController;

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            if (MatchRoleSelection.IsPlayerDefender)
            {
                EnsureEventSystem();
                EnsureOverlay();
            }

            QueueExistingPendingFarmHands();
            TryPresentNext();
        }

        private void Update()
        {
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                TryPresentNext();
                return;
            }

            if (presentedFarmHand == null && pendingFarmHands.Count > 0 && !ModalOverlay.IsGameplayInputBlocked)
            {
                TryPresentNext();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (ownsSpawnedOverlay && spawnedOverlay != null)
            {
                Destroy(spawnedOverlay.gameObject);
            }
        }

        /// <summary>Assigns the overlay prefab and exactly four level-3 upgrade definitions.</summary>
        public void Configure(DefenderLevelUpgradeOverlay prefab, WeaponDefinition[] upgradeOptions)
        {
            UnsubscribeOverlay();
            overlayPrefab = prefab;
            levelThreeUpgradeOptions = CopyOptions(upgradeOptions);
            hasReportedConfigurationError = false;

            if (Application.isPlaying && MatchRoleSelection.IsPlayerDefender)
            {
                EnsureOverlay();
            }

            TryPresentNext();
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            FarmHand.LevelThreeUpgradeChoiceRequested += HandleLevelThreeUpgradeRequested;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            FarmHand.LevelThreeUpgradeChoiceRequested -= HandleLevelThreeUpgradeRequested;
            UnsubscribeOverlay();
            isSubscribed = false;
        }

        private void HandleLevelThreeUpgradeRequested(FarmHand farmHand)
        {
            QueueFarmHand(farmHand);
            TryPresentNext();
        }

        private void QueueExistingPendingFarmHands()
        {
            foreach (var farmHand in UnityEngine.Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude))
            {
                QueueFarmHand(farmHand);
            }
        }

        private void QueueFarmHand(FarmHand farmHand)
        {
            if (!IsPendingFarmHand(farmHand) || farmHand == presentedFarmHand ||
                !pendingFarmHandSet.Add(farmHand))
            {
                return;
            }

            pendingFarmHands.Enqueue(farmHand);
        }

        private void TryPresentNext()
        {
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                TryResolveAiUpgrades();
                return;
            }

            if (presentedFarmHand != null || ModalOverlay.IsGameplayInputBlocked ||
                !HasCompleteConfiguration())
            {
                return;
            }

            EnsureOverlay();
            if (spawnedOverlay == null || spawnedOverlay.IsVisible)
            {
                return;
            }

            while (pendingFarmHands.Count > 0)
            {
                var candidate = pendingFarmHands.Dequeue();
                pendingFarmHandSet.Remove(candidate);
                if (!IsPendingFarmHand(candidate))
                {
                    continue;
                }

                if (spawnedOverlay.Show(candidate, levelThreeUpgradeOptions))
                {
                    presentedFarmHand = candidate;
                    return;
                }

                // Another modal can claim input between the check above and Show. Preserve this request for retry.
                QueueFarmHand(candidate);
                return;
            }
        }

        private void TryResolveAiUpgrades()
        {
            if (pendingFarmHands.Count == 0)
            {
                return;
            }

            var attemptsThisFrame = pendingFarmHands.Count;
            while (attemptsThisFrame-- > 0 && pendingFarmHands.Count > 0)
            {
                var candidate = pendingFarmHands.Dequeue();
                pendingFarmHandSet.Remove(candidate);
                if (!IsPendingFarmHand(candidate))
                {
                    continue;
                }

                if (TryChooseAffordableAiUpgrade(candidate))
                {
                    continue;
                }

                // No affordable option is available yet. Keep the request for a later resource gain.
                QueueFarmHand(candidate);
                return;
            }
        }

        private bool TryChooseAffordableAiUpgrade(FarmHand farmHand)
        {
            if (farmHand == null || levelThreeUpgradeOptions == null)
            {
                return false;
            }

            matchRoleController ??= UnityEngine.Object.FindAnyObjectByType<MatchRoleController>();
            if (matchRoleController != null && matchRoleController.IsPlayerAttacker)
            {
                // Route-aware policy owns the choice and calls FarmHand's paid upgrade API itself.
                // Returning false deliberately leaves the request queued until the attacker commits
                // a future group or the defender bank can afford a useful counter.
                return matchRoleController.TryChooseDefenderAiUpgrade(farmHand, levelThreeUpgradeOptions);
            }

            // Retain the original safe fallback for lightweight test scenes that do not use a
            // MatchRoleController at all.
            foreach (var option in levelThreeUpgradeOptions)
            {
                if (option != null &&
                    farmHand.CanAffordLevelThreeUpgrade(option) &&
                    farmHand.TryChooseLevelThreeUpgrade(option))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleChoiceResolved()
        {
            presentedFarmHand = null;
            TryPresentNext();
        }

        private void EnsureOverlay()
        {
            if (spawnedOverlay == null)
            {
                spawnedOverlay = UnityEngine.Object.FindAnyObjectByType<DefenderLevelUpgradeOverlay>(FindObjectsInactive.Include);
                if (spawnedOverlay == null && overlayPrefab != null)
                {
                    spawnedOverlay = Instantiate(overlayPrefab);
                    ownsSpawnedOverlay = true;
                }
            }

            if (spawnedOverlay != null && !isOverlaySubscribed)
            {
                spawnedOverlay.ChoiceResolved += HandleChoiceResolved;
                isOverlaySubscribed = true;
            }
        }

        private void UnsubscribeOverlay()
        {
            if (!isOverlaySubscribed || spawnedOverlay == null)
            {
                isOverlaySubscribed = false;
                return;
            }

            spawnedOverlay.ChoiceResolved -= HandleChoiceResolved;
            isOverlaySubscribed = false;
        }

        private bool HasCompleteConfiguration()
        {
            if (overlayPrefab == null || levelThreeUpgradeOptions == null ||
                levelThreeUpgradeOptions.Length != UpgradeOptionCount)
            {
                ReportConfigurationError();
                return false;
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                if (levelThreeUpgradeOptions[index] == null)
                {
                    ReportConfigurationError();
                    return false;
                }
            }

            return true;
        }

        private void ReportConfigurationError()
        {
            if (hasReportedConfigurationError)
            {
                return;
            }

            hasReportedConfigurationError = true;
            Debug.LogError("Defender Level Upgrade needs an overlay prefab and exactly four weapon definitions.", this);
        }

        private static bool IsPendingFarmHand(FarmHand farmHand)
        {
            return farmHand != null &&
                   farmHand.isActiveAndEnabled &&
                   farmHand.gameObject.activeInHierarchy &&
                   farmHand.IsAwaitingLevelThreeUpgrade;
        }

        private static WeaponDefinition[] CopyOptions(WeaponDefinition[] source)
        {
            var copy = new WeaponDefinition[UpgradeOptionCount];
            if (source != null)
            {
                Array.Copy(source, copy, Mathf.Min(source.Length, copy.Length));
            }

            return copy;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                eventSystemObject.layer = uiLayer;
            }

            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}