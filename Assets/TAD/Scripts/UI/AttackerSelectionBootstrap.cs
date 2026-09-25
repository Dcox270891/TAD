using TAD.Economy;
using TAD.Pooling;
using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>Creates and connects the shared attacker selector to this level's Spawn Pool and attacker bank.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    [RequireComponent(typeof(AttackerWaveQueue))]
    public sealed class AttackerSelectionBootstrap : MonoBehaviour
    {
        [SerializeField] private AttackerSelectionPanel selectionPanelPrefab;
        [SerializeField] private SpawnPool spawnPool;
        [SerializeField] private ResourceBank attackerBank;

        private AttackerWaveQueue attackerWaveQueue;
        private AttackerSelectionPanel spawnedPanel;
        private bool ownsSpawnedPanel;

        private void Awake()
        {
            attackerWaveQueue = GetComponent<AttackerWaveQueue>();
            ResolveDependencies();
        }

        private void Start()
        {
            if (spawnPool == null || attackerBank == null || attackerWaveQueue == null)
            {
                Debug.LogError("Attacker Selection needs a Spawn Pool, attacker bank, and attacker wave queue.", this);
                return;
            }

            EnsureEventSystem();
            spawnedPanel = Object.FindFirstObjectByType<AttackerSelectionPanel>(FindObjectsInactive.Include);
            if (spawnedPanel == null)
            {
                if (selectionPanelPrefab == null)
                {
                    Debug.LogError("Attacker Selection needs an Attacker Selection Panel prefab.", this);
                    return;
                }

                spawnedPanel = Instantiate(selectionPanelPrefab);
                ownsSpawnedPanel = true;
            }

            spawnedPanel.Bind(spawnPool, attackerBank, attackerWaveQueue);
        }

        private void OnDestroy()
        {
            if (ownsSpawnedPanel && spawnedPanel != null)
            {
                Destroy(spawnedPanel.gameObject);
            }
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
