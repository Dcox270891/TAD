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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Tests.PlayMode
{
    internal static class GameBasicsTestSupport
    {
        private const string TestPrefix = "TAD Test ";
        private const string IsolatedScenePrefix = "TAD Test Sandbox ";
        private static ResourceBank latestTestDefenderBank;

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

            if (side == BankSide.Defender)
            {
                // Tests deliberately leave the editor's current scene loaded while building a
                // compact fixture. Bind its newly-created Defender bank explicitly so it cannot
                // be confused with a bank belonging to that background scene.
                latestTestDefenderBank = bank;
                foreach (var placementController in Object.FindObjectsByType<FarmHandPlacementController>(FindObjectsInactive.Include))
                {
                    if (placementController != null &&
                        placementController.gameObject.name.StartsWith(TestPrefix, System.StringComparison.Ordinal))
                    {
                        placementController.BindDefenderBank(bank);
                    }
                }
            }

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
            hand.BindDefenderBank(latestTestDefenderBank);
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
            cameraObject.AddComponent<AudioListener>();
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
            latestTestDefenderBank = null;
            var objectsToDestroy = new HashSet<GameObject>();

            // Spawned pool objects detach from their pool while active. Mark them before their owning
            // test pool is removed so one test cannot leave an active farmhand or projectile behind for
            // the next test's physics queries.
            foreach (var pooledInstance in Object.FindObjectsByType<PooledInstance>(FindObjectsInactive.Include))
            {
                if (pooledInstance != null && pooledInstance.Owner != null &&
                    pooledInstance.Owner.name.StartsWith(TestPrefix, System.StringComparison.Ordinal))
                {
                    objectsToDestroy.Add(pooledInstance.gameObject);
                }
            }

            foreach (var gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                if (gameObject != null && gameObject.name.StartsWith(TestPrefix, System.StringComparison.Ordinal))
                {
                    objectsToDestroy.Add(gameObject);
                }
            }

            foreach (var gameObject in objectsToDestroy)
            {
                Object.Destroy(gameObject);
            }

            yield return null;
        }

        /// <summary>
        /// Removes any authored game scene leaked by PlayMode coverage while retaining Unity's
        /// initial test-runner scene. Some coverage deliberately loads a shipped level with
        /// <see cref="LoadSceneMode.Single"/>; only deleting objects named for a test leaves
        /// that level's walls, terrain, camera, and global lookups alive for the next fixture.
        /// We must not unload Unity's temporary initial scene, because that also owns the
        /// PlayMode runner. A runtime-only sandbox is created only when a known authored scene
        /// needs to be unloaded.
        /// </summary>
        internal static IEnumerator ResetToIsolatedTestScene()
        {
            yield return DestroyAllTestObjects();

            var scenesToUnload = new List<Scene>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (IsAuthoredGameplayScene(scene))
                {
                    scenesToUnload.Add(scene);
                }
            }

            if (scenesToUnload.Count == 0)
            {
                yield return null;
                yield break;
            }

            var sandbox = FindOrCreateSandboxScene();
            if (!SceneManager.SetActiveScene(sandbox))
            {
                throw new System.InvalidOperationException("Could not activate the isolated PlayMode test scene.");
            }

            foreach (var scene in scenesToUnload)
            {
                if (!scene.isLoaded)
                {
                    continue;
                }

                var unloadOperation = SceneManager.UnloadSceneAsync(scene);
                if (unloadOperation != null)
                {
                    yield return unloadOperation;
                }
            }

            yield return null;
        }

        private static Scene FindOrCreateSandboxScene()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded &&
                    scene.name.StartsWith(IsolatedScenePrefix, System.StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return SceneManager.CreateScene(IsolatedScenePrefix + System.Guid.NewGuid().ToString("N"));
        }

        private static bool IsAuthoredGameplayScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            return scene.path == "Assets/Scenes/MainMenu.unity" ||
                   scene.path.StartsWith("Assets/Scenes/Levels/", System.StringComparison.Ordinal);
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
