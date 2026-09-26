using System;
using System.Collections.Generic;
using TAD.Defenders;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Owns level-3 defender upgrade requests for one level. It queues farmhands so each player choice
    /// is resolved through one modal at a time, even if multiple farmhands reach level 3 together.
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

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            EnsureEventSystem();
            EnsureOverlay();
            QueueExistingPendingFarmHands();
            TryPresentNext();
        }

        private void Update()
        {
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

            if (Application.isPlaying)
            {
                EnsureOverlay();
                TryPresentNext();
            }
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