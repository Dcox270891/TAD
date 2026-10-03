using TAD.Waves;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>Presents the current day and its active countdown without owning game state.</summary>
    [DisallowMultipleComponent]
    public sealed class DayCycleHud : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text dayText;
        [SerializeField] private UnityEngine.UI.Text preparationText;

        private DayCycleController dayCycle;
        private bool isSubscribed;

        /// <summary>Connects this reusable HUD to the current level day cycle.</summary>
        public void Bind(DayCycleController source)
        {
            if (dayCycle == source)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            dayCycle = source;
            Subscribe();
            Refresh();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged += Refresh;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || dayCycle == null)
            {
                return;
            }

            dayCycle.CycleChanged -= Refresh;
            isSubscribed = false;
        }

        private void Refresh()
        {
            if (dayCycle == null)
            {
                return;
            }

            if (dayText != null)
            {
                dayText.text = $"Day {dayCycle.CurrentDay}";
            }

            var showCountdown = !dayCycle.IsComplete &&
                                (dayCycle.IsPreparing || dayCycle.IsWaitingForEmptyDay);
            if (preparationText != null)
            {
                preparationText.gameObject.SetActive(showCountdown);
                if (dayCycle.IsTutorialHeld)
                {
                    preparationText.text = "Preparation: Tutorial";
                }
                else if (dayCycle.IsPreparing)
                {
                    preparationText.text = $"Preparation: {Mathf.CeilToInt(dayCycle.PreparationRemaining)}";
                }
                else if (dayCycle.IsWaitingForEmptyDay)
                {
                    preparationText.text = $"No attackers - next day: {Mathf.CeilToInt(dayCycle.EmptyDayRemaining)}";
                }
            }
        }

        private void ResolveReferences()
        {
            dayText ??= FindText("DayLabel");
            preparationText ??= FindText("PreparationLabel");
        }

        private UnityEngine.UI.Text FindText(string childName)
        {
            foreach (var text in GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (text.gameObject.name == childName)
                {
                    return text;
                }
            }

            return null;
        }
    }
}
