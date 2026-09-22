using TAD.Navigation;
using TAD.Pooling;
using TAD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TAD.Defenders
{
    /// <summary>
    /// Lets a player preview and confirm a farmhand position before asking the reusable defender pool
    /// for the final instance. Mouse, touch, and gamepad input all drive the same world-space preview.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpawnPool))]
    public sealed class FarmHandPlacementController : MonoBehaviour
    {
        [Header("Prefab References")]
        [SerializeField] private FarmHand farmHandPrefab;
        [SerializeField] private FarmHandPlacementUi placementUiPrefab;

        [Header("Placement")]
        [SerializeField, Min(0.01f)]
        [Tooltip("World units per second moved by a controller left thumbstick.")]
        private float controllerPlacementSpeed = 5f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Opacity used by the farmhand preview before it is confirmed.")]
        private float previewOpacity = 0.5f;

        [SerializeField]
        [Tooltip("Sorting order for the translucent preview. It is drawn above the placed farmhand.")]
        private int previewSortingOrder = 3;

        [SerializeField]
        [Tooltip("Optional map reference. If blank, the first Farm Route Mapper in the scene is used.")]
        private FarmRouteMapper routeMapper;

        private SpawnPool spawnPool;
        private FarmHandPlacementUi spawnedUi;
        private GameObject previewObject;
        private Vector2 previewPosition;
        private bool isPlacing;
        private bool ownsSpawnedUi;
        private bool ignoreMouseUntilReleased;
        private bool isMousePlacementLocked;

        /// <summary>True while a translucent farmhand is awaiting confirmation or cancellation.</summary>
        public bool IsPlacementActive => isPlacing;

        private void Awake()
        {
            spawnPool = GetComponent<SpawnPool>();
            routeMapper ??= Object.FindAnyObjectByType<FarmRouteMapper>();
        }

        private void OnEnable()
        {
            // Enter Play Mode options can retain scene objects between sessions. Re-establish the
            // UI connection whenever this controller becomes active so the Buy button is never left
            // pointing at an unbound placement view.
            EnsureEventSystem();
            EnsurePlacementUi();
        }

        private void Start()
        {
            EnsureEventSystem();
            EnsurePlacementUi();
        }

        private void Update()
        {
            if (!isPlacing || ModalOverlay.IsGameplayInputBlocked)
            {
                return;
            }

            UpdateTouchPreview();
            UpdateMousePreview();
            UpdateGamepadPreview();

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelPlacement();
            }
        }

        private void OnDisable()
        {
            CancelPlacement();
        }

        private void OnDestroy()
        {
            DestroyPreview();
            if (ownsSpawnedUi && spawnedUi != null)
            {
                Destroy(spawnedUi.gameObject);
            }
        }

        /// <summary>Begins a placement preview at the centre of the visible map.</summary>
        public void BeginPlacement()
        {
            EnsurePlacementUi();

            if (isPlacing || ModalOverlay.IsGameplayInputBlocked || !TryGetInitialPreviewPosition(out var initialPosition))
            {
                return;
            }

            if (!CreatePreview())
            {
                return;
            }

            isPlacing = true;
            ignoreMouseUntilReleased = Mouse.current != null && Mouse.current.leftButton.isPressed;
            isMousePlacementLocked = false;
            SetPreviewPosition(initialPosition);
            spawnedUi?.SetPlacementActive(true);
        }

        /// <summary>Spawns a pooled defender at the preview position and ends placement mode.</summary>
        public void ConfirmPlacement()
        {
            if (!isPlacing || ModalOverlay.IsGameplayInputBlocked || spawnPool == null)
            {
                return;
            }

            var spawnedFarmHand = spawnPool.Spawn(PooledObjectType.Defender, previewPosition, Quaternion.identity);
            if (spawnedFarmHand == null)
            {
                return;
            }

            EndPlacement();
        }

        /// <summary>Removes the preview without consuming a pooled farmhand.</summary>
        public void CancelPlacement()
        {
            if (!isPlacing && previewObject == null)
            {
                return;
            }

            EndPlacement();
        }

        private void UpdateTouchPreview()
        {
            if (isMousePlacementLocked)
            {
                return;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen == null || !touchscreen.primaryTouch.press.isPressed)
            {
                return;
            }

            var touchId = touchscreen.primaryTouch.touchId.ReadValue();
            if (IsPointerOverUserInterface(touchId))
            {
                return;
            }

            TrySetPreviewFromScreenPosition(touchscreen.primaryTouch.position.ReadValue());
        }

        private void UpdateMousePreview()
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (ignoreMouseUntilReleased)
            {
                if (!mouse.leftButton.isPressed)
                {
                    ignoreMouseUntilReleased = false;
                }

                return;
            }

            if (isMousePlacementLocked || IsPointerOverUserInterface())
            {
                return;
            }

            var didMovePreview = TrySetPreviewFromScreenPosition(mouse.position.ReadValue());
            if (didMovePreview && mouse.leftButton.wasPressedThisFrame)
            {
                // The first world click chooses the final candidate position. The player can then
                // move the cursor to the tick or cross without the preview following it.
                isMousePlacementLocked = true;
            }
        }

        private void UpdateGamepadPreview()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return;
            }

            var movement = gamepad.leftStick.ReadValue();
            if (!isMousePlacementLocked && movement.sqrMagnitude > 0.0001f)
            {
                SetPreviewPosition(previewPosition + (movement * controllerPlacementSpeed * Time.unscaledDeltaTime));
            }

            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                ConfirmPlacement();
            }
            else if (gamepad.buttonEast.wasPressedThisFrame)
            {
                CancelPlacement();
            }
        }

        private bool TryGetInitialPreviewPosition(out Vector2 position)
        {
            return TryGetMapPositionFromScreenPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out position);
        }

        private bool TrySetPreviewFromScreenPosition(Vector2 screenPosition)
        {
            if (!TryGetMapPositionFromScreenPosition(screenPosition, out var position))
            {
                return false;
            }

            SetPreviewPosition(position);
            return true;
        }

        private bool TryGetMapPositionFromScreenPosition(Vector2 screenPosition, out Vector2 position)
        {
            position = default;
            routeMapper ??= Object.FindAnyObjectByType<FarmRouteMapper>();
            var mainCamera = Camera.main;
            if (routeMapper == null || mainCamera == null)
            {
                return false;
            }

            var mapPlane = new Plane(Vector3.forward, routeMapper.transform.position);
            var ray = mainCamera.ScreenPointToRay(screenPosition);
            if (!mapPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            var worldPoint = ray.GetPoint(distance);
            return routeMapper.TryClampToVisibleMap(worldPoint, out position);
        }

        private void SetPreviewPosition(Vector2 requestedPosition)
        {
            if (previewObject == null || routeMapper == null ||
                !routeMapper.TryClampToVisibleMap(requestedPosition, out var constrainedPosition))
            {
                return;
            }

            previewPosition = constrainedPosition;
            previewObject.transform.position = new Vector3(
                previewPosition.x,
                previewPosition.y,
                routeMapper.transform.position.z);
        }

        private bool CreatePreview()
        {
            if (farmHandPrefab == null || farmHandPrefab.Visual == null || farmHandPrefab.Visual.sprite == null)
            {
                Debug.LogError("Farmhand placement needs a FarmHand prefab with a Sprite Renderer.", this);
                return false;
            }

            DestroyPreview();

            previewObject = new GameObject("FarmHand Placement Preview");
            var defenderLayer = LayerMask.NameToLayer("Defender");
            previewObject.layer = defenderLayer >= 0 ? defenderLayer : gameObject.layer;
            previewObject.transform.localScale = farmHandPrefab.transform.localScale;

            var sourceVisual = farmHandPrefab.Visual;
            var previewRenderer = previewObject.AddComponent<SpriteRenderer>();
            previewRenderer.sprite = sourceVisual.sprite;
            previewRenderer.color = new Color(
                sourceVisual.color.r,
                sourceVisual.color.g,
                sourceVisual.color.b,
                sourceVisual.color.a * previewOpacity);
            previewRenderer.flipX = sourceVisual.flipX;
            previewRenderer.flipY = sourceVisual.flipY;
            previewRenderer.sortingLayerID = sourceVisual.sortingLayerID;
            previewRenderer.sortingOrder = Mathf.Max(previewSortingOrder, sourceVisual.sortingOrder + 1);
            return true;
        }

        private void EndPlacement()
        {
            isPlacing = false;
            ignoreMouseUntilReleased = false;
            isMousePlacementLocked = false;
            DestroyPreview();
            spawnedUi?.SetPlacementActive(false);
        }

        private void DestroyPreview()
        {
            if (previewObject != null)
            {
                Destroy(previewObject);
                previewObject = null;
            }
        }

        private void EnsurePlacementUi()
        {
            if (spawnedUi == null)
            {
                spawnedUi = Object.FindAnyObjectByType<FarmHandPlacementUi>(FindObjectsInactive.Include);
                if (spawnedUi == null && placementUiPrefab != null)
                {
                    spawnedUi = Instantiate(placementUiPrefab);
                    ownsSpawnedUi = true;
                }
            }

            spawnedUi?.Bind(this);
        }

        private static bool IsPointerOverUserInterface(int pointerId = -1)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            return pointerId < 0
                ? eventSystem.IsPointerOverGameObject()
                : eventSystem.IsPointerOverGameObject(pointerId);
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
            eventSystemObject.layer = LayerMask.NameToLayer("UI");

            var inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
            return eventSystemObject.GetComponent<EventSystem>();
        }
    }
}
