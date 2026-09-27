using TAD.Defenders;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>
    /// Connects the existing Farmhand click-selection flow to one reusable non-modal details card.
    /// Polling the selection owner keeps this independent from placement and avoids duplicate input handling.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FarmHandPlacementController))]
    public sealed class DefenderDetailsBootstrap : MonoBehaviour
    {
        [SerializeField] private DefenderDetailsPanel detailsPanelPrefab;

        private FarmHandPlacementController placementController;
        private DefenderDetailsPanel spawnedPanel;
        private FarmHand lastTarget;
        private bool ownsSpawnedPanel;

        private void Awake()
        {
            placementController = GetComponent<FarmHandPlacementController>();
        }

        private void Start()
        {
            EnsurePanel();
            RefreshSelection(force: true);
        }

        private void Update()
        {
            RefreshSelection(force: false);
        }

        private void OnDisable()
        {
            lastTarget = null;
            spawnedPanel?.Hide();
        }

        private void OnDestroy()
        {
            if (ownsSpawnedPanel && spawnedPanel != null)
            {
                Destroy(spawnedPanel.gameObject);
            }
        }

        private void RefreshSelection(bool force)
        {
            var selectedFarmHand = placementController != null
                ? placementController.TargetedFarmHand
                : null;
            if (!force && selectedFarmHand == lastTarget)
            {
                return;
            }

            lastTarget = selectedFarmHand;
            EnsurePanel();
            if (spawnedPanel == null)
            {
                return;
            }

            if (selectedFarmHand != null && selectedFarmHand.isActiveAndEnabled &&
                selectedFarmHand.gameObject.activeInHierarchy)
            {
                spawnedPanel.Show(selectedFarmHand);
                return;
            }

            spawnedPanel.Hide();
        }

        private void EnsurePanel()
        {
            if (spawnedPanel != null)
            {
                spawnedPanel.BindPlacementController(placementController);
                return;
            }

            spawnedPanel = Object.FindFirstObjectByType<DefenderDetailsPanel>(FindObjectsInactive.Include);
            if (spawnedPanel != null)
            {
                spawnedPanel.BindPlacementController(placementController);
                return;
            }

            if (detailsPanelPrefab == null)
            {
                Debug.LogError("Defender details need a Defender Details Panel prefab.", this);
                return;
            }

            spawnedPanel = Instantiate(detailsPanelPrefab);
            ownsSpawnedPanel = true;
            spawnedPanel.BindPlacementController(placementController);
        }
    }
}
