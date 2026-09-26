using TAD.Economy;
using TAD.Pooling;
using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>Creates and connects the attacker ordering, queued-group, and non-modal details UI for this level.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    [RequireComponent(typeof(AttackerWaveQueue))]
    public sealed class AttackerSelectionBootstrap : MonoBehaviour
    {
        [SerializeField] private AttackerSelectionPanel selectionPanelPrefab;
        [SerializeField] private AttackerQueuePanel queuePanelPrefab;
        [SerializeField] private AttackerDetailsPanel detailsPanelPrefab;
        [SerializeField] private SpawnPool spawnPool;
        [SerializeField] private ResourceBank attackerBank;

        private WaveController waveController;
        private AttackerWaveQueue attackerWaveQueue;
        private AttackerSelectionPanel spawnedSelectionPanel;
        private AttackerQueuePanel spawnedQueuePanel;
        private AttackerDetailsPanel spawnedDetailsPanel;
        private bool ownsSpawnedSelectionPanel;
        private bool ownsSpawnedQueuePanel;
        private bool ownsSpawnedDetailsPanel;

        private void Awake()
        {
            waveController = GetComponent<WaveController>();
            attackerWaveQueue = GetComponent<AttackerWaveQueue>();
            ResolveDependencies();
        }

        private void Start()
        {
            if (spawnPool == null || attackerBank == null || attackerWaveQueue == null || waveController == null)
            {
                Debug.LogError("Attacker UI needs a Spawn Pool, attacker bank, Wave Controller, and attacker wave queue.", this);
                return;
            }

            EnsureEventSystem();
            if (!EnsurePresenters())
            {
                return;
            }

            spawnedSelectionPanel.Bind(spawnPool, attackerBank, attackerWaveQueue);
            spawnedQueuePanel.Bind(
                attackerWaveQueue,
                spawnPool,
                waveController,
                spawnedSelectionPanel,
                spawnedDetailsPanel);
        }

        private void OnDestroy()
        {
            if (ownsSpawnedQueuePanel && spawnedQueuePanel != null)
            {
                Destroy(spawnedQueuePanel.gameObject);
            }

            if (ownsSpawnedDetailsPanel && spawnedDetailsPanel != null)
            {
                Destroy(spawnedDetailsPanel.gameObject);
            }

            if (ownsSpawnedSelectionPanel && spawnedSelectionPanel != null)
            {
                Destroy(spawnedSelectionPanel.gameObject);
            }
        }

        private bool EnsurePresenters()
        {
            spawnedSelectionPanel = Object.FindFirstObjectByType<AttackerSelectionPanel>(FindObjectsInactive.Include);
            if (spawnedSelectionPanel == null)
            {
                if (selectionPanelPrefab == null)
                {
                    Debug.LogError("Attacker UI needs an Attacker Selection Panel prefab.", this);
                    return false;
                }

                spawnedSelectionPanel = Instantiate(selectionPanelPrefab);
                ownsSpawnedSelectionPanel = true;
            }

            spawnedDetailsPanel = Object.FindFirstObjectByType<AttackerDetailsPanel>(FindObjectsInactive.Include);
            if (spawnedDetailsPanel == null)
            {
                if (detailsPanelPrefab == null)
                {
                    Debug.LogError("Attacker UI needs an Attacker Details Panel prefab.", this);
                    return false;
                }

                spawnedDetailsPanel = Instantiate(detailsPanelPrefab);
                ownsSpawnedDetailsPanel = true;
            }

            spawnedQueuePanel = Object.FindFirstObjectByType<AttackerQueuePanel>(FindObjectsInactive.Include);
            if (spawnedQueuePanel == null)
            {
                if (queuePanelPrefab == null)
                {
                    Debug.LogError("Attacker UI needs an Attacker Queue Panel prefab.", this);
                    return false;
                }

                spawnedQueuePanel = Instantiate(queuePanelPrefab);
                ownsSpawnedQueuePanel = true;
            }

            return true;
        }

        private void ResolveDependencies()
        {
            spawnPool ??= Object.FindAnyObjectByType<SpawnPool>();
            if (attackerBank != null)
            {
                return;
            }

            foreach (var bank in GetComponentsInChildren<ResourceBank>(true))
            {
                if (bank != null && bank.BankSide == BankSide.Attacker)
                {
                    attackerBank = bank;
                    return;
                }
            }
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