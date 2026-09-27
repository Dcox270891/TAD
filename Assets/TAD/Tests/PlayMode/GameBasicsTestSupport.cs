using System.Collections;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using TAD.Economy;
using TAD.Environment;
using TAD.Pooling;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    internal static class GameBasicsTestSupport
    {
        private const string TestPrefix = "TAD Test ";

        internal sealed class QueueFixture
        {
            public QueueFixture(
                ResourceBank attackerBank,
                WaveController waveController,
                AttackerWaveQueue waveQueue,
                SpawnPool spawnPool,
                FoodStore foodStore)
            {
                AttackerBank = attackerBank;
                WaveController = waveController;
                WaveQueue = waveQueue;
                SpawnPool = spawnPool;
                FoodStore = foodStore;
            }

            public ResourceBank AttackerBank { get; }
            public WaveController WaveController { get; }
            public AttackerWaveQueue WaveQueue { get; }
            public SpawnPool SpawnPool { get; }
            public FoodStore FoodStore { get; }
        }

        internal static FoodStore CreateFoodStore(Vector2 position = default)
        {
            var storeObject = new GameObject(TestPrefix + "Food Store");
            storeObject.transform.position = position;
            storeObject.SetActive(false);
            storeObject.AddComponent<BoxCollider2D>();
            storeObject.AddComponent<Rigidbody2D>();
            var store = storeObject.AddComponent<FoodStore>();
            storeObject.SetActive(true);
            return store;
        }

        internal static ResourceBank CreateBank(BankSide side, int startingGold = 100)
        {
            var bankObject = new GameObject(TestPrefix + side + " Bank");
            bankObject.SetActive(false);
            var bank = bankObject.AddComponent<ResourceBank>();
            bank.Configure(side, startingGold);
            bankObject.SetActive(true);
            return bank;
        }

        internal static BasicAttacker CreateAttacker(AttackerDefinition definition)
        {
            var attackerObject = new GameObject(TestPrefix + "Attacker");
            attackerObject.SetActive(false);
            var attacker = attackerObject.AddComponent<BasicAttacker>();
            attacker.ConfigureDefinition(definition);
            attackerObject.SetActive(true);
            return attacker;
        }

        internal static FarmHand CreateFarmHand()
        {
            var handObject = new GameObject(TestPrefix + "FarmHand");
            handObject.SetActive(false);
            var hand = handObject.AddComponent<FarmHand>();
            handObject.SetActive(true);
            return hand;
        }

        internal static Camera CreateMainCamera()
        {
            var cameraObject = new GameObject(TestPrefix + "Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            return camera;
        }

        internal static SpawnPool CreateSpawnPool()
        {
            var prefab = Resources.Load<GameObject>("TADTests/SpawnPool");
            if (prefab == null)
            {
                return null;
            }

            var pool = Object.Instantiate(prefab);
            pool.name = TestPrefix + "Spawn Pool";
            return pool.GetComponent<SpawnPool>();
        }

        internal static QueueFixture CreateQueueFixture()
        {
            CreateMainCamera();
            var foodStore = CreateFoodStore(new Vector2(4f, 0f));
            var spawnPool = CreateSpawnPool();

            var controllerObject = new GameObject(TestPrefix + "Wave Controller");
            controllerObject.SetActive(false);
            var attackerBank = controllerObject.AddComponent<ResourceBank>();
            attackerBank.Configure(BankSide.Attacker, 100);
            var waveController = controllerObject.AddComponent<WaveController>();
            var waveQueue = controllerObject.AddComponent<AttackerWaveQueue>();
            controllerObject.SetActive(true);

            return new QueueFixture(attackerBank, waveController, waveQueue, spawnPool, foodStore);
        }

        internal static AttackerDetailsPanel CreateAttackerDetailsPanel(
            out Text heading,
            out Text status,
            out Text stats,
            out Text route)
        {
            var root = new GameObject(TestPrefix + "Attacker Details", typeof(RectTransform));
            root.SetActive(false);
            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(root.transform, false);
            heading = AddText(card.transform, "Heading");
            AddImage(card.transform, "AttackerImage");
            status = AddText(card.transform, "Status");
            stats = AddText(card.transform, "Stats");
            route = AddText(card.transform, "Route");
            AddButton(card.transform, "Close");
            AddButton(card.transform, "RemoveFromQueue");

            var panel = root.AddComponent<AttackerDetailsPanel>();
            return panel;
        }

        internal static AttackerWinOverlay CreateAttackerWinOverlay()
        {
            var overlayObject = new GameObject(TestPrefix + "Attacker Win");
            overlayObject.SetActive(false);
            overlayObject.AddComponent<CanvasGroup>();
            overlayObject.AddComponent<ModalOverlay>();
            var overlay = overlayObject.AddComponent<AttackerWinOverlay>();
            overlayObject.SetActive(true);
            return overlay;
        }

        internal static DefenderWinOverlay CreateDefenderWinOverlay()
        {
            var overlayObject = new GameObject(TestPrefix + "Defender Win");
            overlayObject.SetActive(false);
            overlayObject.AddComponent<CanvasGroup>();
            overlayObject.AddComponent<ModalOverlay>();
            var overlay = overlayObject.AddComponent<DefenderWinOverlay>();
            overlayObject.SetActive(true);
            return overlay;
        }

        internal static PauseOverlay CreatePauseOverlay()
        {
            var overlayObject = new GameObject(TestPrefix + "Pause");
            overlayObject.SetActive(false);
            overlayObject.AddComponent<CanvasGroup>();
            overlayObject.AddComponent<ModalOverlay>();
            var overlay = overlayObject.AddComponent<PauseOverlay>();
            overlayObject.SetActive(true);
            return overlay;
        }

        internal static DayCycleController CreateDayCycle()
        {
            var cycleObject = new GameObject(TestPrefix + "Day Cycle");
            cycleObject.SetActive(false);
            cycleObject.AddComponent<WaveController>();
            cycleObject.AddComponent<AttackerWaveQueue>();
            var cycle = cycleObject.AddComponent<DayCycleController>();
            cycleObject.SetActive(true);
            return cycle;
        }

        internal static IEnumerator DestroyAllTestObjects()
        {
            Time.timeScale = 1f;
            foreach (var gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                if (gameObject != null && gameObject.name.StartsWith(TestPrefix, System.StringComparison.Ordinal))
                {
                    Object.Destroy(gameObject);
                }
            }

            yield return null;
        }

        private static Text AddText(Transform parent, string name)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);
            return textObject.GetComponent<Text>();
        }

        private static Image AddImage(Transform parent, string name)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            return imageObject.GetComponent<Image>();
        }

        private static Button AddButton(Transform parent, string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }
    }
}
