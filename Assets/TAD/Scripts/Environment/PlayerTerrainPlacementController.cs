using TAD.Match;
using TAD.Navigation;
using TAD.Progression;
using TAD.UI;
using TAD.Waves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TAD.Environment
{
    /// <summary>
    /// Adds a small, role-appropriate terrain-placement action once the player has unlocked it:
    /// attackers place Ice to speed their route, defenders place Mud to slow the incoming route.
    /// Each effect is intentionally limited to one free patch per preparation phase so the
    /// account unlock is meaningful without allowing an unlimited route rewrite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerTerrainPlacementController : MonoBehaviour
    {
        private const float TerrainPatchDiameter = 1.05f;
        private const string ButtonName = "PlayerTerrainPlacementButton";

        private static Sprite patchSprite;

        private FarmRouteMapper routeMapper;
        private DayCycleController dayCycle;
        private Button placementButton;
        private Text buttonText;
        private TerrainSpeedEffect currentTerrainType;
        private int preparedDay = int.MinValue;
        private bool placedThisPreparation;
        private bool isPlacing;
        private bool ignorePointerUntilReleased;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForLoadedScene()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryInstallForActiveScene();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInstallForActiveScene();
        }

        private static void TryInstallForActiveScene()
        {
            var routeMapper = Object.FindAnyObjectByType<FarmRouteMapper>();
            if (routeMapper == null || routeMapper.GetComponent<PlayerTerrainPlacementController>() != null)
            {
                return;
            }

            routeMapper.gameObject.AddComponent<PlayerTerrainPlacementController>();
        }

        private void Awake()
        {
            routeMapper = GetComponent<FarmRouteMapper>() ?? Object.FindAnyObjectByType<FarmRouteMapper>();
            dayCycle = Object.FindAnyObjectByType<DayCycleController>();
        }

        private void Start()
        {
            RefreshAvailability();
        }

        private void OnDestroy()
        {
            if (placementButton != null)
            {
                placementButton.onClick.RemoveListener(BeginPlacement);
                Destroy(placementButton.gameObject);
            }
        }

        private void Update()
        {
            dayCycle ??= Object.FindAnyObjectByType<DayCycleController>();
            if (dayCycle != null && dayCycle.IsPreparing && preparedDay != dayCycle.CurrentDay)
            {
                preparedDay = dayCycle.CurrentDay;
                placedThisPreparation = false;
                isPlacing = false;
                RefreshAvailability();
            }

            if (!isPlacing)
            {
                return;
            }

            if (!CanPlaceThisPreparation())
            {
                CancelPlacement();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (ignorePointerUntilReleased)
            {
                if (!IsPrimaryPointerPressed())
                {
                    ignorePointerUntilReleased = false;
                }

                return;
            }

            if (!TryGetPrimaryPointerPress(out var screenPosition, out var pointerId))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
            {
                return;
            }

            TryPlaceTerrain(screenPosition);
        }

        private void RefreshAvailability()
        {
            if (!TryGetUnlockedTerrainType(out currentTerrainType))
            {
                if (placementButton != null)
                {
                    placementButton.gameObject.SetActive(false);
                }

                return;
            }

            EnsureButton();
            if (placementButton == null)
            {
                return;
            }

            placementButton.gameObject.SetActive(true);
            placementButton.interactable = CanPlaceThisPreparation();
            if (buttonText != null)
            {
                buttonText.text = placedThisPreparation
                    ? $"{GetTerrainLabel(currentTerrainType)} USED"
                    : isPlacing
                        ? "TAP THE FARM"
                        : $"PLACE {GetTerrainLabel(currentTerrainType)}";
            }
        }

        private bool TryGetUnlockedTerrainType(out TerrainSpeedEffect terrainType)
        {
            terrainType = TerrainSpeedEffect.Mud;
            var settings = PlayerSettingsService.Instance;
            if (MatchRoleSelection.IsPlayerAttacker && settings.HasIcePlacementUpgrade)
            {
                terrainType = TerrainSpeedEffect.Ice;
                return true;
            }

            if (MatchRoleSelection.IsPlayerDefender && settings.HasMudPlacementUpgrade)
            {
                terrainType = TerrainSpeedEffect.Mud;
                return true;
            }

            return false;
        }

        private bool CanPlaceThisPreparation()
        {
            return !placedThisPreparation && dayCycle != null && dayCycle.IsPreparing && !dayCycle.IsComplete;
        }

        private void BeginPlacement()
        {
            if (!CanPlaceThisPreparation())
            {
                RefreshAvailability();
                return;
            }

            isPlacing = true;
            ignorePointerUntilReleased = IsPrimaryPointerPressed();
            RefreshAvailability();
        }

        private void CancelPlacement()
        {
            isPlacing = false;
            ignorePointerUntilReleased = false;
            RefreshAvailability();
        }

        private void TryPlaceTerrain(Vector2 screenPosition)
        {
            if (routeMapper == null || Camera.main == null)
            {
                CancelPlacement();
                return;
            }

            var camera = Camera.main;
            var world = camera.ScreenToWorldPoint(new Vector3(
                screenPosition.x,
                screenPosition.y,
                -camera.transform.position.z));
            var requestedPosition = new Vector2(world.x, world.y);
            if (!routeMapper.TryClampToVisibleMap(requestedPosition, out var position) ||
                !routeMapper.CanOccupyGroundPosition(position, TerrainPatchDiameter))
            {
                return;
            }

            CreateTerrainPatch(position, currentTerrainType);
            routeMapper.RebuildRoutes();
            placedThisPreparation = true;
            isPlacing = false;
            RefreshAvailability();
        }

        private static void CreateTerrainPatch(Vector2 position, TerrainSpeedEffect terrainType)
        {
            var patch = new GameObject(
                terrainType == TerrainSpeedEffect.Ice ? "Player Ice Patch" : "Player Mud Patch",
                typeof(SpriteRenderer),
                typeof(PolygonCollider2D),
                typeof(TerrainSpeedModifier));
            var environmentLayer = LayerMask.NameToLayer("Environment");
            if (environmentLayer >= 0)
            {
                patch.layer = environmentLayer;
            }

            patch.transform.position = new Vector3(position.x, position.y, 0.04f);
            patch.transform.localScale = Vector3.one * TerrainPatchDiameter;
            var renderer = patch.GetComponent<SpriteRenderer>();
            renderer.sprite = GetPatchSprite();
            renderer.color = terrainType == TerrainSpeedEffect.Ice
                ? new Color(0.45f, 0.86f, 1f, 0.72f)
                : new Color(0.31f, 0.16f, 0.06f, 0.76f);
            renderer.sortingOrder = -2;

            var collider = patch.GetComponent<PolygonCollider2D>();
            collider.isTrigger = true;
            collider.pathCount = 1;
            collider.SetPath(0, new[]
            {
                new Vector2(-0.5f, -0.5f),
                new Vector2(-0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, -0.5f)
            });
            patch.GetComponent<TerrainSpeedModifier>().Configure(terrainType);
        }

        private void EnsureButton()
        {
            if (placementButton != null)
            {
                return;
            }

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var root = new GameObject(
                ButtonName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            root.layer = canvas.gameObject.layer;
            root.transform.SetParent(canvas.transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(210f, 58f);
            rect.anchoredPosition = new Vector2(-22f, 22f);
            var image = root.GetComponent<Image>();
            image.color = new Color(0.93f, 0.77f, 0.34f, 0.98f);
            placementButton = root.GetComponent<Button>();
            placementButton.targetGraphic = image;
            placementButton.onClick.AddListener(BeginPlacement);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.layer = root.layer;
            labelObject.transform.SetParent(root.transform, false);
            buttonText = labelObject.GetComponent<Text>();
            buttonText.font = ResolveFont(canvas.transform);
            buttonText.fontSize = 21;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = new Color(0.24f, 0.105f, 0.03f, 1f);
            buttonText.raycastTarget = false;
            var textRect = buttonText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);
            FarmUiSkin.ApplyFromCanvas(canvas.transform, root.transform);
        }

        private static bool TryGetPrimaryPointerPress(out Vector2 screenPosition, out int pointerId)
        {
            screenPosition = default;
            pointerId = -1;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                pointerId = Touchscreen.current.primaryTouch.touchId.ReadValue();
                return true;
            }

            return false;
        }

        private static bool IsPrimaryPointerPressed()
        {
            return Mouse.current != null && Mouse.current.leftButton.isPressed ||
                   Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed;
        }

        private static Sprite GetPatchSprite()
        {
            if (patchSprite != null)
            {
                return patchSprite;
            }

            var texture = Texture2D.whiteTexture;
            patchSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                1f);
            patchSprite.name = "Runtime Terrain Patch";
            return patchSprite;
        }

        private static string GetTerrainLabel(TerrainSpeedEffect terrainType)
        {
            return terrainType == TerrainSpeedEffect.Ice ? "ICE" : "MUD";
        }

        private static Font ResolveFont(Transform canvasRoot)
        {
            var existing = canvasRoot != null ? canvasRoot.GetComponentInChildren<Text>(true) : null;
            return existing != null && existing.font != null
                ? existing.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
