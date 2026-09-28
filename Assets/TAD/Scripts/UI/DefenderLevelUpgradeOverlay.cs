using System;
using System.Collections.Generic;
using TAD.Defenders;
using TAD.Economy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace TAD.UI
{
    /// <summary>
    /// Displays the four permanent weapon choices offered to one farmhand when it reaches level 2.
    /// The modal blocks lower input while each option reflects the live Defender resource balance.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModalOverlay))]
    public sealed class DefenderLevelUpgradeOverlay : MonoBehaviour
    {
        private const int UpgradeOptionCount = 4;

        [SerializeField] private ModalOverlay modalOverlay;
        [SerializeField] private UnityEngine.UI.Text titleText;
        [SerializeField] private UnityEngine.UI.Text subtitleText;
        [SerializeField] private UnityEngine.UI.Button[] optionButtons = new UnityEngine.UI.Button[UpgradeOptionCount];
        [SerializeField] private UnityEngine.UI.Text[] optionTexts = new UnityEngine.UI.Text[UpgradeOptionCount];

        private readonly UnityAction[] optionActions = new UnityAction[UpgradeOptionCount];
        private readonly WeaponDefinition[] options = new WeaponDefinition[UpgradeOptionCount];
        private FarmHand targetFarmHand;
        private ResourceBank defenderBank;
        private bool isSubscribed;
        private bool isDefenderBankSubscribed;
        private string selectionError;

        /// <summary>True while this choice is visible and owns gameplay input.</summary>
        public bool IsVisible => modalOverlay != null && modalOverlay.IsVisible;

        /// <summary>Raised after a choice is applied or the pending farmhand becomes unavailable.</summary>
        public event Action ChoiceResolved;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnbindDefenderBank();
        }

        private void Update()
        {
            if (!IsVisible)
            {
                return;
            }

            if (!IsEligibleTarget(targetFarmHand))
            {
                DismissUnavailableTarget();
                return;
            }

            if (defenderBank == null)
            {
                BindDefenderBank(targetFarmHand.DefenderBank);
                RefreshVisuals();
            }
        }

        /// <summary>Shows a blocking level-3 weapon choice for the supplied farmhand.</summary>
        public bool Show(FarmHand farmHand, IReadOnlyList<WeaponDefinition> upgradeOptions)
        {
            ResolveReferences();
            if (modalOverlay == null || modalOverlay.IsVisible || ModalOverlay.IsGameplayInputBlocked ||
                !IsEligibleTarget(farmHand) || !TryCopyOptions(upgradeOptions) || !HasCompleteBindings())
            {
                return false;
            }

            targetFarmHand = farmHand;
            selectionError = null;
            BindDefenderBank(farmHand.DefenderBank);
            RefreshVisuals();
            modalOverlay.Show();
            SelectFirstOption();
            return true;
        }

        /// <summary>Assigns prefab-owned controls without registering runtime listeners in the Editor.</summary>
        public void Configure(
            ModalOverlay overlay,
            UnityEngine.UI.Text title,
            UnityEngine.UI.Text subtitle,
            UnityEngine.UI.Button[] buttons,
            UnityEngine.UI.Text[] labels)
        {
            Unsubscribe();
            modalOverlay = overlay;
            titleText = title;
            subtitleText = subtitle;
            optionButtons = CopyReferences(buttons);
            optionTexts = CopyReferences(labels);

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            ResolveReferences();
            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                var optionIndex = index;
                var button = GetOptionButton(index);
                if (button == null)
                {
                    continue;
                }

                optionActions[index] = () => Choose(optionIndex);
                button.onClick.AddListener(optionActions[index]);
            }

            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                var button = GetOptionButton(index);
                if (button != null && optionActions[index] != null)
                {
                    button.onClick.RemoveListener(optionActions[index]);
                }

                optionActions[index] = null;
            }

            isSubscribed = false;
        }

        private void Choose(int optionIndex)
        {
            if (!IsVisible || optionIndex < 0 || optionIndex >= UpgradeOptionCount ||
                !IsEligibleTarget(targetFarmHand))
            {
                return;
            }

            var selectedWeapon = options[optionIndex];
            if (selectedWeapon == null)
            {
                return;
            }

            if (!targetFarmHand.TryChooseWeaponUpgrade(selectedWeapon))
            {
                selectionError = targetFarmHand.CanAffordWeaponUpgrade(selectedWeapon)
                    ? $"Unable to purchase {selectedWeapon.DisplayName}."
                    : $"Not enough Defender Gold for {selectedWeapon.DisplayName}.";
                RefreshVisuals();
                SelectFirstOption();
                return;
            }

            ClearPresentation();
            modalOverlay.CompleteChoice();
            ChoiceResolved?.Invoke();
        }

        private void DismissUnavailableTarget()
        {
            ClearPresentation();
            modalOverlay?.Hide();
            ChoiceResolved?.Invoke();
        }

        private bool TryCopyOptions(IReadOnlyList<WeaponDefinition> upgradeOptions)
        {
            if (upgradeOptions == null || upgradeOptions.Count != UpgradeOptionCount)
            {
                return false;
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                if (upgradeOptions[index] == null)
                {
                    return false;
                }
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                options[index] = upgradeOptions[index];
            }

            return true;
        }

        private void RefreshVisuals()
        {
            var hasDefenderBank = defenderBank != null;
            var defenderGold = hasDefenderBank ? defenderBank.CurrentGold : 0;

            if (titleText != null)
            {
                titleText.text = "DEFENDER LEVEL 2";
            }

            if (subtitleText != null)
            {
                subtitleText.text = hasDefenderBank
                    ? $"Defender Gold: {defenderGold} - Choose one weapon upgrade{FormatSelectionError()}"
                    : $"Defender Gold unavailable - weapon choice cannot be purchased.{FormatSelectionError()}";
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                var definition = options[index];
                var canAfford = definition != null && hasDefenderBank && targetFarmHand != null &&
                                targetFarmHand.CanAffordWeaponUpgrade(definition);
                var button = GetOptionButton(index);
                var label = GetOptionText(index);
                if (button != null)
                {
                    button.interactable = canAfford;
                }

                if (label != null)
                {
                    label.text = BuildOptionText(definition, defenderGold, hasDefenderBank);
                }
            }
        }

        private string FormatSelectionError()
        {
            return string.IsNullOrWhiteSpace(selectionError) ? string.Empty : $"\n{selectionError}";
        }

        private static string BuildOptionText(WeaponDefinition definition, int defenderGold, bool hasDefenderBank)
        {
            if (definition == null)
            {
                return "Unavailable";
            }

            var costText = hasDefenderBank
                ? definition.Cost <= defenderGold
                    ? $"Cost: {definition.Cost} Gold"
                    : $"Cost: {definition.Cost} Gold\nNeed {definition.Cost - defenderGold} more Gold"
                : "Cost: Defender Gold unavailable";

            return $"{definition.DisplayName}\n{definition.Description}\n\n{costText}\nDamage {definition.Damage:0.#}   Range {definition.Range:0.#}   Speed {definition.AttackSpeed:0.#}";
        }

        private void SelectFirstOption()
        {
            if (EventSystem.current == null)
            {
                return;
            }

            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                var button = GetOptionButton(index);
                if (button != null && button.interactable)
                {
                    EventSystem.current.SetSelectedGameObject(button.gameObject);
                    return;
                }
            }
        }

        private bool HasCompleteBindings()
        {
            for (var index = 0; index < UpgradeOptionCount; index++)
            {
                if (GetOptionButton(index) == null || GetOptionText(index) == null)
                {
                    Debug.LogError("Defender Level Upgrade Overlay needs four buttons and four labels.", this);
                    return false;
                }
            }

            return true;
        }

        private void BindDefenderBank(ResourceBank bank)
        {
            if (defenderBank == bank)
            {
                return;
            }

            UnbindDefenderBank();
            defenderBank = bank;
            if (defenderBank != null)
            {
                defenderBank.GoldChanged += HandleDefenderGoldChanged;
                isDefenderBankSubscribed = true;
            }
        }

        private void UnbindDefenderBank()
        {
            if (isDefenderBankSubscribed && defenderBank != null)
            {
                defenderBank.GoldChanged -= HandleDefenderGoldChanged;
            }

            isDefenderBankSubscribed = false;
            defenderBank = null;
        }

        private void HandleDefenderGoldChanged(int gold)
        {
            selectionError = null;
            if (IsVisible)
            {
                RefreshVisuals();
            }
        }

        private static bool IsEligibleTarget(FarmHand farmHand)
        {
            return farmHand != null &&
                   farmHand.isActiveAndEnabled &&
                   farmHand.gameObject.activeInHierarchy &&
                   farmHand.IsAwaitingWeaponUpgrade;
        }

        private void ClearPresentation()
        {
            targetFarmHand = null;
            selectionError = null;
            UnbindDefenderBank();
            Array.Clear(options, 0, options.Length);
        }

        private UnityEngine.UI.Button GetOptionButton(int index)
        {
            return optionButtons != null && index >= 0 && index < optionButtons.Length
                ? optionButtons[index]
                : null;
        }

        private UnityEngine.UI.Text GetOptionText(int index)
        {
            return optionTexts != null && index >= 0 && index < optionTexts.Length
                ? optionTexts[index]
                : null;
        }

        private void ResolveReferences()
        {
            modalOverlay ??= GetComponent<ModalOverlay>();
        }

        private static T[] CopyReferences<T>(T[] source) where T : UnityEngine.Object
        {
            var copy = new T[UpgradeOptionCount];
            if (source != null)
            {
                Array.Copy(source, copy, Mathf.Min(source.Length, copy.Length));
            }

            return copy;
        }
    }
}
