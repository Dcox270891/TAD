using System;
using System.Collections.Generic;
using TAD.Defenders;
using TAD.Match;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;

namespace TAD.UI
{
    /// <summary>
    /// Owns the defender's three-stage weapon tree. Human defenders open the modal from a farmhand's
    /// details card after its green world indicator appears; AI defenders resolve their own choice
    /// from the same route-aware option data. Level-eight choices are selected from the exact
    /// level-five weapon that the farmhand currently holds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefenderLevelUpgradeBootstrap : MonoBehaviour
    {
        private const int UpgradeOptionCount = 2;

        [SerializeField] private DefenderLevelUpgradeOverlay overlayPrefab;
        [FormerlySerializedAs("weaponUpgradeOptions")]
        [FormerlySerializedAs("levelThreeUpgradeOptions")]
        [SerializeField] private WeaponDefinition[] levelTwoUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] meleeLevelFiveUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] rangedLevelFiveUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] poleLevelEightUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] rollingLevelEightUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] airRifleLevelEightUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];
        [SerializeField] private WeaponDefinition[] trapperLevelEightUpgradeOptions = new WeaponDefinition[UpgradeOptionCount];

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

            if (!MatchRoleSelection.IsPlayerDefender)
            {
                QueueExistingPendingFarmHands();
                TryPresentNext();
            }
        }

        private void Update()
        {
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                TryPresentNext();
                return;
            }

            // Human choices deliberately stay non-blocking. The selected farmhand's details card
            // calls TryShowUpgradeSelection when the player is ready to choose.
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

        /// <summary>
        /// Assigns the overlay and the two options for the free level-two and level-five branches.
        /// The serialized level-eight branches remain in place for existing fixtures and prefabs.
        /// </summary>
        public void Configure(
            DefenderLevelUpgradeOverlay prefab,
            WeaponDefinition[] levelTwoOptions,
            WeaponDefinition[] meleeLevelFiveOptions,
            WeaponDefinition[] rangedLevelFiveOptions)
        {
            Configure(
                prefab,
                levelTwoOptions,
                meleeLevelFiveOptions,
                rangedLevelFiveOptions,
                poleLevelEightUpgradeOptions,
                rollingLevelEightUpgradeOptions,
                airRifleLevelEightUpgradeOptions,
                trapperLevelEightUpgradeOptions);
        }

        /// <summary>
        /// Assigns the overlay and both options for every weapon-tree branch, including the exact
        /// level-eight evolutions for Pole, Rolling, Air Rifle, and Trapper respectively.
        /// </summary>
        public void Configure(
            DefenderLevelUpgradeOverlay prefab,
            WeaponDefinition[] levelTwoOptions,
            WeaponDefinition[] meleeLevelFiveOptions,
            WeaponDefinition[] rangedLevelFiveOptions,
            WeaponDefinition[] poleLevelEightOptions,
            WeaponDefinition[] rollingLevelEightOptions,
            WeaponDefinition[] airRifleLevelEightOptions,
            WeaponDefinition[] trapperLevelEightOptions)
        {
            UnsubscribeOverlay();
            overlayPrefab = prefab;
            levelTwoUpgradeOptions = CopyOptions(levelTwoOptions);
            meleeLevelFiveUpgradeOptions = CopyOptions(meleeLevelFiveOptions);
            rangedLevelFiveUpgradeOptions = CopyOptions(rangedLevelFiveOptions);
            poleLevelEightUpgradeOptions = CopyOptions(poleLevelEightOptions);
            rollingLevelEightUpgradeOptions = CopyOptions(rollingLevelEightOptions);
            airRifleLevelEightUpgradeOptions = CopyOptions(airRifleLevelEightOptions);
            trapperLevelEightUpgradeOptions = CopyOptions(trapperLevelEightOptions);
            hasReportedConfigurationError = false;

            if (Application.isPlaying && MatchRoleSelection.IsPlayerDefender)
            {
                EnsureOverlay();
            }
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                TryPresentNext();
            }
        }

        /// <summary>
        /// Compatibility helper for older test fixtures that only supplied one option list. The first
        /// two entries become the free level-two branch; level-five entries still need explicit setup.
        /// </summary>
        [Obsolete("Configure the level-two, melee level-five, and ranged level-five branches explicitly.")]
        public void Configure(DefenderLevelUpgradeOverlay prefab, WeaponDefinition[] upgradeOptions)
        {
            Configure(prefab, upgradeOptions, meleeLevelFiveUpgradeOptions, rangedLevelFiveUpgradeOptions);
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            FarmHand.WeaponUpgradeChoiceRequested += HandleWeaponUpgradeRequested;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            FarmHand.WeaponUpgradeChoiceRequested -= HandleWeaponUpgradeRequested;
            UnsubscribeOverlay();
            isSubscribed = false;
        }

        private void HandleWeaponUpgradeRequested(FarmHand farmHand)
        {
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                QueueFarmHand(farmHand);
                TryPresentNext();
            }
        }

        /// <summary>
        /// Opens the configured choice modal only after the defender player explicitly asks for it.
        /// This is normally called by the selected farmhand's details card, so upgrade availability
        /// never interrupts active combat or placement input on its own.
        /// </summary>
        public bool TryShowUpgradeSelection(FarmHand farmHand)
        {
            if (!MatchRoleSelection.IsPlayerDefender || presentedFarmHand != null ||
                ModalOverlay.IsGameplayInputBlocked || !IsPendingFarmHand(farmHand) ||
                !TryGetUpgradeOptions(farmHand, out var options))
            {
                return false;
            }

            EnsureEventSystem();
            EnsureOverlay();
            if (spawnedOverlay == null || spawnedOverlay.IsVisible ||
                !HasCompleteConfiguration(farmHand, options))
            {
                return false;
            }

            if (!spawnedOverlay.Show(farmHand, options))
            {
                return false;
            }

            presentedFarmHand = farmHand;
            return true;
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
            if (farmHand == null || !TryGetUpgradeOptions(farmHand, out var options))
            {
                return false;
            }

            matchRoleController ??= UnityEngine.Object.FindAnyObjectByType<MatchRoleController>();
            if (matchRoleController != null && matchRoleController.IsPlayerAttacker)
            {
                // Route-aware policy owns the choice and calls FarmHand's paid upgrade API itself.
                // Returning false deliberately leaves the request queued until the attacker commits
                // a future group or the defender bank can afford a useful counter.
                return matchRoleController.TryChooseDefenderAiUpgrade(farmHand, options);
            }

            // Retain the original safe fallback for lightweight test scenes that do not use a
            // MatchRoleController at all.
            foreach (var option in options)
            {
                if (option != null &&
                    farmHand.CanAffordWeaponUpgrade(option) &&
                    farmHand.TryChooseWeaponUpgrade(option))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleChoiceResolved()
        {
            presentedFarmHand = null;
            if (!MatchRoleSelection.IsPlayerDefender)
            {
                TryPresentNext();
            }
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

        private bool TryGetUpgradeOptions(FarmHand farmHand, out WeaponDefinition[] options)
        {
            options = null;
            if (farmHand == null)
            {
                return false;
            }

            options = farmHand.PendingWeaponUpgradeLevel switch
            {
                2 => levelTwoUpgradeOptions,
                5 => GetLevelFiveOptions(farmHand),
                8 => GetLevelEightOptions(farmHand),
                _ => null
            };

            return HasCompleteOptions(options);
        }

        private WeaponDefinition[] GetLevelFiveOptions(FarmHand farmHand)
        {
            var projectile = farmHand != null && farmHand.WeaponDefinition != null
                ? farmHand.WeaponDefinition.ProjectileDefinition
                : null;
            return projectile != null && projectile.ProjectileType == ProjectileType.Ranged
                ? rangedLevelFiveUpgradeOptions
                : meleeLevelFiveUpgradeOptions;
        }

        private WeaponDefinition[] GetLevelEightOptions(FarmHand farmHand)
        {
            var previousWeapon = farmHand != null ? farmHand.WeaponDefinition : null;
            if (previousWeapon == null)
            {
                ReportConfigurationError();
                return null;
            }

            WeaponDefinition[] matchingOptions = null;
            var matchingBranchCount = 0;
            TrySelectExactLevelEightBranch(poleLevelEightUpgradeOptions, previousWeapon, ref matchingOptions, ref matchingBranchCount);
            TrySelectExactLevelEightBranch(rollingLevelEightUpgradeOptions, previousWeapon, ref matchingOptions, ref matchingBranchCount);
            TrySelectExactLevelEightBranch(airRifleLevelEightUpgradeOptions, previousWeapon, ref matchingOptions, ref matchingBranchCount);
            TrySelectExactLevelEightBranch(trapperLevelEightUpgradeOptions, previousWeapon, ref matchingOptions, ref matchingBranchCount);

            if (matchingBranchCount == 1)
            {
                return matchingOptions;
            }

            // Treat incomplete, mixed-parent, missing, and duplicate branch definitions as an
            // authoring error. Selecting the wrong final evolution would be much worse than
            // leaving the farmhand ready for its correctly configured choice.
            ReportConfigurationError();
            return null;
        }

        private static void TrySelectExactLevelEightBranch(
            WeaponDefinition[] candidateOptions,
            WeaponDefinition previousWeapon,
            ref WeaponDefinition[] matchingOptions,
            ref int matchingBranchCount)
        {
            if (!HasCompleteOptions(candidateOptions) || candidateOptions[0].RequiredPreviousWeapon != previousWeapon ||
                candidateOptions[1].RequiredPreviousWeapon != previousWeapon)
            {
                return;
            }

            matchingOptions = candidateOptions;
            matchingBranchCount++;
        }

        private bool HasCompleteConfiguration(FarmHand farmHand, IReadOnlyList<WeaponDefinition> options)
        {
            if (overlayPrefab == null || farmHand == null || !HasCompleteOptions(options))
            {
                ReportConfigurationError();
                return false;
            }

            return true;
        }

        private static bool HasCompleteOptions(IReadOnlyList<WeaponDefinition> options)
        {
            if (options == null || options.Count != UpgradeOptionCount)
            {
                return false;
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                if (options[index] == null)
                {
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
            Debug.LogError(
                "Defender Level Upgrade needs an overlay prefab plus two configured options for level 2, level-5 melee, level-5 ranged, and each exact-parent level-8 branch (Pole, Rolling, Air Rifle, and Trapper).",
                this);
        }

        private static bool IsPendingFarmHand(FarmHand farmHand)
        {
            return farmHand != null &&
                   farmHand.isActiveAndEnabled &&
                   farmHand.gameObject.activeInHierarchy &&
                   farmHand.IsAwaitingWeaponUpgrade;
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
