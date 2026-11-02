using TAD.Economy;
using TAD.Match;
using TAD.Pooling;
using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TAD.UI
{
    /// <summary>
    /// Creates and connects the shared attacker queue/details presentation for every level.
    /// The send-wave card panel is created only when the human player selected the Attacker role.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaveController))]
    [RequireComponent(typeof(AttackerWaveQueue))]
    public sealed class AttackerSelectionBootstrap : MonoBehaviour
    {
        private const int MinimumMobileTapDragThreshold = 18;
        private const int MaximumMobileTapDragThreshold = 24;
        private const float MobileTapDragThresholdInches = 0.075f;

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

            ConfigureMobileTapSlop(EnsureEventSystem());
            if (!EnsurePresenters())
            {
                return;
            }

            // The attacker card panel is the player-only send-wave control. The queue cue and
            // attacker detail card remain shared, so a Defender player can inspect AI-planned waves.
            if (spawnedSelectionPanel != null)
            {
                spawnedSelectionPanel.Bind(spawnPool, attackerBank, attackerWaveQueue);
            }

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
            if (MatchRoleSelection.IsPlayerAttacker)
            {
                spawnedSelectionPanel = Object.FindAnyObjectByType<AttackerSelectionPanel>(FindObjectsInactive.Include);
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

                spawnedSelectionPanel.gameObject.SetActive(true);
            }
            else
            {
                // A defender must not see or use the attacking AI's send-wave controls. This also
                // clears a panel retained by the editor/play-mode lifecycle or a prior scene load.
                var existingSelectionPanel =
                    Object.FindAnyObjectByType<AttackerSelectionPanel>(FindObjectsInactive.Include);
                if (existingSelectionPanel != null)
                {
                    existingSelectionPanel.gameObject.SetActive(false);
                }

                // Keeping this reference null makes AttackerQueuePanel treat the send panel as absent,
                // while the shared queue cue and attacker-details panel continue to work.
                spawnedSelectionPanel = null;
            }

            spawnedDetailsPanel = Object.FindAnyObjectByType<AttackerDetailsPanel>(FindObjectsInactive.Include);
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

            spawnedQueuePanel = Object.FindAnyObjectByType<AttackerQueuePanel>(FindObjectsInactive.Include);
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

        private static EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return EventSystem.current;
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
            return eventSystemObject.GetComponent<EventSystem>();
        }

        private static void ConfigureMobileTapSlop(EventSystem eventSystem)
        {
            // Attacker cards live in a horizontal ScrollRect. Unity cancels a Button click once
            // a touch moves past pixelDragThreshold, and the desktop default of ten pixels is
            // too tight for a natural mobile tap. Preserve mouse behaviour while allowing a
            // small, DPI-aware finger drift; deliberate swipes remain comfortably above it.
            if (eventSystem == null || (!Application.isMobilePlatform && Touchscreen.current == null))
            {
                return;
            }

            var dpiScaledThreshold = Screen.dpi > 0f
                ? Mathf.RoundToInt(Screen.dpi * MobileTapDragThresholdInches)
                : 20;
            var mobileThreshold = Mathf.Clamp(
                dpiScaledThreshold,
                MinimumMobileTapDragThreshold,
                MaximumMobileTapDragThreshold);
            eventSystem.pixelDragThreshold = Mathf.Max(eventSystem.pixelDragThreshold, mobileThreshold);
        }
    }
}
