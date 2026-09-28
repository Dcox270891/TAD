using TAD.Economy;
using TAD.Match;
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
        private const string InsufficientFundsMessage = "you do not you have the required amount to place this";
        private const string OccupiedPlacementMessage = "Another farmhand is already stationed there.";
        private const string RelocationFundsMessage = "You do not have the required amount to move this farmhand.";
        private const float PlacementWarningDurationSeconds = 3f;
        private const int PlacementOverlapCapacity = 16;

        private enum PlacementMode
        {
            None,
            Purchase,
            Relocation
        }

        [Header("Prefab References")]
        [SerializeField] private FarmHand farmHandPrefab;
        [SerializeField] private FarmHandPlacementUi placementUiPrefab;
        [SerializeField] private ModalOverlayMessage insufficientFundsOverlayPrefab;

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
        [Tooltip("Optional map reference used only without a local Spawn Pool. A local Spawn Pool always owns the route map.")]
        private FarmRouteMapper routeMapper;

        private SpawnPool spawnPool;
        private FarmHandPlacementUi spawnedUi;
        private ModalOverlayMessage spawnedInsufficientFundsOverlay;
        private ResourceBank defenderBank;
        private FarmHand selectedFarmHand;
        private GameObject previewObject;
        private Vector2 previewPosition;
        private bool isPlacing;
        private PlacementMode placementMode;
        private FarmHand relocatingFarmHand;
        private bool ownsSpawnedUi;
        private bool ownsInsufficientFundsOverlay;
        private bool ignoreMouseUntilReleased;
        private bool isMousePlacementLocked;
        private bool isPlacementInputEnabled = true;
        private readonly Collider2D[] placementOverlapResults = new Collider2D[PlacementOverlapCapacity];
        private ContactFilter2D defenderPlacementFilter;

        /// <summary>True while a translucent farmhand is awaiting confirmation or cancellation.</summary>
        public bool IsPlacementActive => isPlacing;

        /// <summary>The currently selected defender whose range circle is visible, if any.</summary>
        public FarmHand TargetedFarmHand => selectedFarmHand;

        /// <summary>Whether the human player can use the Buy, confirm, and cancel placement controls.</summary>
        public bool IsPlacementInputEnabled => isPlacementInputEnabled;

        private void Awake()
        {
            isPlacementInputEnabled = MatchRoleSelection.IsPlayerDefender;
            spawnPool = GetComponent<SpawnPool>();
            routeMapper = spawnPool != null
                ? spawnPool.RouteMapper
                : routeMapper ?? Object.FindAnyObjectByType<FarmRouteMapper>();
            ConfigureDefenderPlacementFilter();

            if (!isPlacementInputEnabled)
            {
                HidePlacementUi();
            }
        }

        private void OnEnable()
        {
            // Enter Play Mode options can retain scene objects between sessions. Re-establish the
            // UI connection whenever this controller becomes active so the Buy button is never left
            // pointing at an unbound placement view.
            EnsureEventSystem();
            if (isPlacementInputEnabled)
            {
                EnsurePlacementUi();
            }
            else
            {
                HidePlacementUi();
            }
        }

        private void Start()
        {
            EnsureEventSystem();
            if (isPlacementInputEnabled)
            {
                EnsurePlacementUi();
            }
            else
            {
                HidePlacementUi();
            }
        }

        private void Update()
        {
            if (ModalOverlay.IsGameplayInputBlocked)
            {
                return;
            }

            // Defender information is a shared panel, so an Attacker player may still target a
            // farmhand. Only the placement controls are removed from their input scope.
            if (!isPlacementInputEnabled)
            {
                if (isPlacing)
                {
                    CancelPlacement();
                }

                UpdateDefenderSelection();
                return;
            }

            if (!isPlacing)
            {
                UpdateDefenderSelection();
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
            ClearTargetedFarmHand();
        }

        private void OnDestroy()
        {
            DestroyPreview();
            ClearTargetedFarmHand();
            if (ownsSpawnedUi && spawnedUi != null)
            {
                Destroy(spawnedUi.gameObject);
            }

            if (ownsInsufficientFundsOverlay && spawnedInsufficientFundsOverlay != null)
            {
                Destroy(spawnedInsufficientFundsOverlay.gameObject);
            }
        }

        /// <summary>
        /// Enables or hides the player-facing placement controls without disabling shared defender inspection.
        /// </summary>
        public void SetPlacementInputEnabled(bool enabled)
        {
            if (!enabled)
            {
                isPlacementInputEnabled = false;
                CancelPlacement();
                HidePlacementUi();
                return;
            }

            isPlacementInputEnabled = true;
            EnsurePlacementUi();
        }

        /// <summary>
        /// Places one paid farmhand at a visible map position without opening player placement UI.
        /// The opposing defender AI uses the exact same pool and bank validation as a human player.
        /// </summary>
        public bool TryPlaceFarmHandAt(Vector2 requestedPosition)
        {
            if (spawnPool == null || ModalOverlay.IsGameplayInputBlocked)
            {
                return false;
            }

            routeMapper ??= spawnPool.RouteMapper;
            if (routeMapper == null || !routeMapper.TryClampToVisibleMap(requestedPosition, out var constrainedPosition))
            {
                return false;
            }

            if (isPlacing)
            {
                CancelPlacement();
            }

            return TrySpawnPaidFarmHand(constrainedPosition, showInsufficientFundsMessage: false);
        }

        /// <summary>
        /// Pays for and starts a validated defender relocation. It is public for deterministic gameplay tests,
        /// while player-facing calls must still enter through <see cref="BeginRelocation"/> to use the preview.
        /// </summary>
        public bool TryRelocateFarmHandAt(FarmHand farmHand, Vector2 requestedPosition, bool showMessage = false)
        {
            if (!isPlacementInputEnabled)
            {
                return false;
            }

            return TryRelocateFarmHandInternal(farmHand, requestedPosition, showMessage);
        }

        /// <summary>
        /// Lets the defender AI use the same paid relocation transaction as the player without
        /// enabling human placement controls for an attacker-controlled match.
        /// </summary>
        public bool TryRelocateFarmHandForAi(FarmHand farmHand, Vector2 requestedPosition)
        {
            return TryRelocateFarmHandInternal(farmHand, requestedPosition, showMessage: false);
        }

        /// <summary>
        /// Checks whether a farmhand can occupy a requested visible-map position without charging gold
        /// or starting movement. The AI uses this before route-coverage scoring.
        /// </summary>
        public bool CanRelocateFarmHandTo(
            FarmHand farmHand,
            Vector2 requestedPosition,
            out Vector2 constrainedPosition)
        {
            constrainedPosition = default;
            if (spawnPool == null || farmHand == null || !farmHand.isActiveAndEnabled ||
                !farmHand.gameObject.activeInHierarchy || farmHand.IsRelocating)
            {
                return false;
            }

            routeMapper ??= spawnPool.RouteMapper;
            return routeMapper != null && routeMapper.TryClampToVisibleMap(requestedPosition, out constrainedPosition) &&
                   IsFarmHandPlacementAvailable(constrainedPosition, farmHand, farmHand);
        }

        private bool TryRelocateFarmHandInternal(FarmHand farmHand, Vector2 requestedPosition, bool showMessage)
        {
            if (ModalOverlay.IsGameplayInputBlocked ||
                !CanRelocateFarmHandTo(farmHand, requestedPosition, out var constrainedPosition))
            {
                if (showMessage)
                {
                    ShowTransientPlacementMessage(OccupiedPlacementMessage);
                }

                return false;
            }

            var relocationCost = farmHand.RelocationCost;
            var bank = ResolveDefenderBank();
            if (bank == null || !bank.CanAfford(relocationCost) || !bank.TrySpendGold(relocationCost))
            {
                if (showMessage)
                {
                    ShowPlacementMessage(RelocationFundsMessage);
                }

                return false;
            }

            if (farmHand.TryBeginRelocation(constrainedPosition))
            {
                return true;
            }

            bank.AddGold(relocationCost);
            return false;
        }

        /// <summary>Begins a placement preview at the centre of the visible map.</summary>
        public void BeginPlacement()
        {
            if (!isPlacementInputEnabled)
            {
                return;
            }

            EnsurePlacementUi();

            if (isPlacing || ModalOverlay.IsGameplayInputBlocked || !TryGetInitialPreviewPosition(out var initialPosition))
            {
                return;
            }

            if (!CreatePreview(farmHandPrefab))
            {
                return;
            }

            ClearTargetedFarmHand();
            isPlacing = true;
            placementMode = PlacementMode.Purchase;
            ignoreMouseUntilReleased = Mouse.current != null && Mouse.current.leftButton.isPressed;
            isMousePlacementLocked = false;
            SetPreviewPosition(initialPosition);
            spawnedUi?.SetPlacementActive(true);
        }

        /// <summary>
        /// Uses the same translucent preview and confirm/cancel controls to move an existing defender.
        /// Only the Defender player can enter this mode; the final charge is taken at confirmation.
        /// </summary>
        public bool BeginRelocation(FarmHand farmHand)
        {
            if (!isPlacementInputEnabled || farmHand == null || !farmHand.isActiveAndEnabled ||
                !farmHand.gameObject.activeInHierarchy || farmHand.IsRelocating || isPlacing ||
                ModalOverlay.IsGameplayInputBlocked || !TryGetInitialPreviewPosition(out _))
            {
                return false;
            }

            EnsurePlacementUi();
            if (!CreatePreview(farmHand))
            {
                return false;
            }

            relocatingFarmHand = farmHand;
            placementMode = PlacementMode.Relocation;
            isPlacing = true;
            ignoreMouseUntilReleased = Mouse.current != null && Mouse.current.leftButton.isPressed;
            isMousePlacementLocked = false;
            SetPreviewPosition((Vector2)farmHand.transform.position);
            ClearTargetedFarmHand();
            spawnedUi?.SetPlacementActive(true);
            return true;
        }

        /// <summary>Confirms either a paid new defender or a paid relocation, then closes the preview.</summary>
        public void ConfirmPlacement()
        {
            if (!isPlacementInputEnabled || !isPlacing || ModalOverlay.IsGameplayInputBlocked || spawnPool == null)
            {
                return;
            }

            var completed = placementMode == PlacementMode.Relocation
                ? TryRelocateFarmHandAt(relocatingFarmHand, previewPosition, showMessage: true)
                : TrySpawnPaidFarmHand(previewPosition, showInsufficientFundsMessage: true);
            if (completed)
            {
                EndPlacement();
            }
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

        /// <summary>
        /// Targets a defender for selection-based presentation. Future controller or combat targeting
        /// systems can call this directly to use the same range circle as a mouse or touch selection.
        /// </summary>
        public void TargetFarmHand(FarmHand farmHand)
        {
            if (selectedFarmHand == farmHand)
            {
                selectedFarmHand?.SetRangeIndicatorVisible(true);
                spawnedUi?.SetFarmHandSelected(selectedFarmHand != null);
                return;
            }

            if (selectedFarmHand != null)
            {
                selectedFarmHand.SetRangeIndicatorVisible(false);
            }

            selectedFarmHand = farmHand;
            if (selectedFarmHand != null)
            {
                selectedFarmHand.SetRangeIndicatorVisible(true);
            }

            spawnedUi?.SetFarmHandSelected(selectedFarmHand != null);
        }

        /// <summary>Clears the current farmhand target and hides its range circle.</summary>
        public void ClearTargetedFarmHand()
        {
            TargetFarmHand(null);
        }

        private void UpdateDefenderSelection()
        {
            if (selectedFarmHand != null &&
                (!selectedFarmHand.isActiveAndEnabled || !selectedFarmHand.gameObject.activeInHierarchy))
            {
                ClearTargetedFarmHand();
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                var touchId = touchscreen.primaryTouch.touchId.ReadValue();
                if (!IsPointerOverUserInterface(touchId))
                {
                    TargetFarmHandFromScreenPosition(touchscreen.primaryTouch.position.ReadValue());
                }

                // A touch device can also synthesize a mouse event. Handle the touch once.
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUserInterface())
            {
                TargetFarmHandFromScreenPosition(mouse.position.ReadValue());
            }
        }

        private void TargetFarmHandFromScreenPosition(Vector2 screenPosition)
        {
            if (!TryGetMapPositionFromScreenPosition(screenPosition, out var worldPosition))
            {
                return;
            }

            TargetFarmHand(FindFarmHandAt(worldPosition));
        }

        private static FarmHand FindFarmHandAt(Vector2 worldPosition)
        {
            FarmHand closestFarmHand = null;
            var closestDistanceSquared = float.PositiveInfinity;
            var highestSortingOrder = int.MinValue;

            foreach (var farmHand in Object.FindObjectsByType<FarmHand>(FindObjectsInactive.Exclude))
            {
                var handVisual = farmHand.Visual;
                if (handVisual == null)
                {
                    continue;
                }

                var bounds = handVisual.bounds;
                var pointAtVisualDepth = new Vector3(worldPosition.x, worldPosition.y, bounds.center.z);
                if (!bounds.Contains(pointAtVisualDepth))
                {
                    continue;
                }

                var distanceSquared = ((Vector2)farmHand.transform.position - worldPosition).sqrMagnitude;
                if (handVisual.sortingOrder > highestSortingOrder ||
                    (handVisual.sortingOrder == highestSortingOrder && distanceSquared < closestDistanceSquared))
                {
                    closestFarmHand = farmHand;
                    closestDistanceSquared = distanceSquared;
                    highestSortingOrder = handVisual.sortingOrder;
                }
            }

            return closestFarmHand;
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

            if (IsPointerOverUserInterface())
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // A world click chooses a candidate position without confirming it. Subsequent world
                // clicks can revise that candidate while the cursor remains free to reach the tick/cross.
                if (TrySetPreviewFromScreenPosition(mouse.position.ReadValue()))
                {
                    isMousePlacementLocked = true;
                }

                return;
            }

            if (!isMousePlacementLocked)
            {
                TrySetPreviewFromScreenPosition(mouse.position.ReadValue());
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

        private bool CreatePreview(FarmHand sourceFarmHand)
        {
            if (sourceFarmHand == null || sourceFarmHand.Visual == null || sourceFarmHand.Visual.sprite == null)
            {
                Debug.LogError("Farmhand placement needs a FarmHand prefab with a Sprite Renderer.", this);
                return false;
            }

            DestroyPreview();

            previewObject = new GameObject("FarmHand Placement Preview");
            var defenderLayer = LayerMask.NameToLayer("Defender");
            previewObject.layer = defenderLayer >= 0 ? defenderLayer : gameObject.layer;
            previewObject.transform.localScale = sourceFarmHand.transform.localScale;

            var sourceVisual = sourceFarmHand.Visual;
            var previewRenderer = previewObject.AddComponent<SpriteRenderer>();
            var previewBodySortingOrder = Mathf.Max(previewSortingOrder, sourceVisual.sortingOrder + 1);
            var visualController = sourceFarmHand.GetComponent<FarmHandVisualController>();
            if (visualController == null ||
                !visualController.ConfigurePlacementPreview(
                    previewRenderer,
                    previewObject.transform,
                    previewOpacity,
                    previewObject.layer,
                    previewBodySortingOrder))
            {
                // Retain compatibility with any legacy defender prefab that has not yet opted into layers.
                previewRenderer.sprite = sourceVisual.sprite;
                previewRenderer.color = new Color(
                    sourceVisual.color.r,
                    sourceVisual.color.g,
                    sourceVisual.color.b,
                    sourceVisual.color.a * previewOpacity);
                previewRenderer.flipX = sourceVisual.flipX;
                previewRenderer.flipY = sourceVisual.flipY;
                previewRenderer.sortingLayerID = sourceVisual.sortingLayerID;
                previewRenderer.sortingOrder = previewBodySortingOrder;
            }

            var rangePreviewObject = new GameObject(
                "Range Preview",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(DefenderTargetableRangeVisual));
            rangePreviewObject.layer = previewObject.layer;
            rangePreviewObject.transform.SetParent(previewObject.transform, worldPositionStays: false);

            var projectile = sourceFarmHand.WeaponDefinition != null
                ? sourceFarmHand.WeaponDefinition.ProjectileDefinition
                : null;
            var projectileType = projectile != null ? projectile.ProjectileType : ProjectileType.Melee;
            var rangePreview = rangePreviewObject.GetComponent<DefenderTargetableRangeVisual>();
            rangePreview.ConfigurePreview(
                sourceFarmHand.Range,
                projectileType,
                sourceVisual.sortingLayerID,
                previewRenderer.sortingOrder - 1);
            return true;
        }

        private int GetFarmHandCost()
        {
            return farmHandPrefab != null && farmHandPrefab.WeaponDefinition != null
                ? farmHandPrefab.WeaponDefinition.Cost
                : 0;
        }

        private ResourceBank ResolveDefenderBank()
        {
            if (defenderBank != null && defenderBank.BankSide == BankSide.Defender)
            {
                return defenderBank;
            }

            defenderBank = null;
            foreach (var bank in Object.FindObjectsByType<ResourceBank>(FindObjectsInactive.Include))
            {
                if (bank.BankSide == BankSide.Defender)
                {
                    defenderBank = bank;
                    break;
                }
            }

            return defenderBank;
        }

        private void ConfigureDefenderPlacementFilter()
        {
            defenderPlacementFilter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = LayerMask.GetMask("Defender"),
                useTriggers = true
            };
        }

        private bool IsFarmHandPlacementAvailable(Vector2 position, FarmHand footprintSource, FarmHand ignoredFarmHand = null)
        {
            if (footprintSource == null)
            {
                return false;
            }

            var footprintSize = footprintSource.PlacementFootprintSize;
            if (footprintSize.x <= Mathf.Epsilon || footprintSize.y <= Mathf.Epsilon)
            {
                return false;
            }

            var overlapCount = Physics2D.OverlapBox(
                position + footprintSource.PlacementFootprintCenterOffset,
                footprintSize,
                0f,
                defenderPlacementFilter,
                placementOverlapResults);
            if (overlapCount >= placementOverlapResults.Length)
            {
                // A saturated query could conceal another placement footprint, so reject the position safely.
                return false;
            }

            for (var index = 0; index < overlapCount; index++)
            {
                var collider = placementOverlapResults[index];
                if (collider == null)
                {
                    continue;
                }

                var farmHand = collider.GetComponentInParent<FarmHand>();
                if (farmHand != null && farmHand != ignoredFarmHand && farmHand.isActiveAndEnabled &&
                    farmHand.gameObject.activeInHierarchy && farmHand.PlacementCollider == collider)
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowInsufficientFundsMessage()
        {
            ShowPlacementMessage(InsufficientFundsMessage);
        }

        private void ShowPlacementMessage(string message)
        {
            EnsureInsufficientFundsOverlay();
            if (spawnedInsufficientFundsOverlay != null)
            {
                spawnedInsufficientFundsOverlay.ShowMessage(message);
                return;
            }

            Debug.LogWarning(message, this);
        }

        private void ShowTransientPlacementMessage(string message)
        {
            EnsureInsufficientFundsOverlay();
            if (spawnedInsufficientFundsOverlay != null)
            {
                spawnedInsufficientFundsOverlay.ShowTransientMessage(message, PlacementWarningDurationSeconds);
                return;
            }

            Debug.LogWarning(message, this);
        }

        private bool TrySpawnPaidFarmHand(Vector2 position, bool showInsufficientFundsMessage)
        {
            if (!IsFarmHandPlacementAvailable(position, farmHandPrefab))
            {
                if (showInsufficientFundsMessage)
                {
                    ShowTransientPlacementMessage(OccupiedPlacementMessage);
                }

                return false;
            }

            var farmHandCost = GetFarmHandCost();
            var bank = ResolveDefenderBank();
            if (bank == null || !bank.CanAfford(farmHandCost))
            {
                if (showInsufficientFundsMessage)
                {
                    ShowInsufficientFundsMessage();
                }

                return false;
            }

            var spawnedFarmHand = spawnPool.Spawn(PooledObjectType.Defender, position, Quaternion.identity);
            if (spawnedFarmHand == null)
            {
                return false;
            }

            // The affordability check above keeps this failure path exceptional, but returning the
            // object protects either player or AI from a charge if the bank changes this frame.
            if (bank.TrySpendGold(farmHandCost))
            {
                return true;
            }

            var pooledInstance = spawnedFarmHand.GetComponent<PooledInstance>();
            if (pooledInstance != null)
            {
                pooledInstance.ReturnToPool();
            }
            else
            {
                spawnedFarmHand.SetActive(false);
            }

            if (showInsufficientFundsMessage)
            {
                ShowInsufficientFundsMessage();
            }

            return false;
        }

        private void EndPlacement()
        {
            isPlacing = false;
            placementMode = PlacementMode.None;
            relocatingFarmHand = null;
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
            if (!isPlacementInputEnabled)
            {
                return;
            }

            if (spawnedUi == null)
            {
                spawnedUi = Object.FindAnyObjectByType<FarmHandPlacementUi>(FindObjectsInactive.Include);
                if (spawnedUi == null && placementUiPrefab != null)
                {
                    spawnedUi = Instantiate(placementUiPrefab);
                    ownsSpawnedUi = true;
                }
            }

            if (spawnedUi != null)
            {
                if (!spawnedUi.gameObject.activeSelf)
                {
                    spawnedUi.gameObject.SetActive(true);
                }

                spawnedUi.Bind(this);
            }
        }

        private void HidePlacementUi()
        {
            spawnedUi ??= Object.FindAnyObjectByType<FarmHandPlacementUi>(FindObjectsInactive.Include);
            if (spawnedUi == null)
            {
                return;
            }

            spawnedUi.SetPlacementActive(false);
            spawnedUi.gameObject.SetActive(false);
        }

        private void EnsureInsufficientFundsOverlay()
        {
            if (spawnedInsufficientFundsOverlay == null)
            {
                spawnedInsufficientFundsOverlay = Object.FindAnyObjectByType<ModalOverlayMessage>(FindObjectsInactive.Include);
                if (spawnedInsufficientFundsOverlay == null && insufficientFundsOverlayPrefab != null)
                {
                    spawnedInsufficientFundsOverlay = Instantiate(insufficientFundsOverlayPrefab);
                    ownsInsufficientFundsOverlay = true;
                }
            }
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
