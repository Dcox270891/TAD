using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Environment;
using UnityEngine;

namespace TAD.Navigation
{
    /// <summary>
    /// Builds static, size-aware flow fields from the Food Store. Every attacker can then obtain
    /// its next fastest step without running an individual path search each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmRouteMapper : MonoBehaviour
    {
        private const int NoHoleRouteKey = -1;
        private const float SizeKeyScale = 1000f;
        private const int MaxRecoveryCellRadius = 8;
        private const float MapBoundaryEpsilon = 0.0001f;

        [SerializeField]
        [Tooltip("The target all attacking animals try to reach. If blank, the first Food Store in the scene is used.")]
        private FoodStore foodStore;

        [SerializeField, Min(0.01f)]
        [Tooltip("The attacker diameter used to build the startup route field.")]
        private float defaultAttackerSize = 1f;

        [SerializeField]
        [Tooltip("Optional attacker data to cache at level startup. Basic Attackers prewarmed by a Spawn Pool are discovered automatically as well.")]
        private AttackerDefinition[] attackerDefinitionsToPrecompute = Array.Empty<AttackerDefinition>();

        [SerializeField, Min(0.05f)]
        [Tooltip("The desired spacing between route cells. Smaller values make tighter routes at a higher startup cost.")]
        private float cellSize = 0.25f;

        [SerializeField]
        [Tooltip("Fallback size shown before routes are built. At runtime, the route map uses the Main Camera's visible area.")]
        private Vector2 mapSize = new(20f, 12f);

        [SerializeField, Min(0f)]
        [Tooltip("Extra clearance left around walls, in world units.")]
        private float obstaclePadding;

        [SerializeField, Range(1, 32)]
        [Tooltip("Maximum flow-field cells a route may skip when a clear line smooths a wall corner.")]
        private int cornerSmoothingLookAheadCells = 8;

        private readonly Dictionary<RouteFieldKey, RouteField> routeFields = new();
        private readonly List<int> precomputedAttackerSizeKeys = new();
        private readonly List<int> holeSizeKeys = new();
        private FarmWall[] walls = Array.Empty<FarmWall>();
        private int columns;
        private int rows;
        private Vector2 gridMinimum;
        private Vector2 runtimeMapSize;
        private Vector2 actualCellSize;
        private bool hasBuiltMap;

        public FoodStore FoodStore => foodStore;

        /// <summary>The world-space dimensions mapped from the Main Camera's visible area.</summary>
        public Vector2 MapSize => hasBuiltMap ? runtimeMapSize : mapSize;

        /// <summary>The camera-visible rectangle used by the latest route-map build.</summary>
        public Rect VisibleMapBounds => hasBuiltMap
            ? new Rect(gridMinimum, runtimeMapSize)
            : new Rect((Vector2)transform.position - (mapSize * 0.5f), mapSize);

        /// <summary>Number of immutable route fields prepared for this level.</summary>
        public int CachedRouteFieldCount => routeFields.Count;

        /// <summary>Number of attacker sizes with routes cached during the latest level scan.</summary>
        public int PrecomputedAttackerSizeCount => precomputedAttackerSizeKeys.Count;

        /// <summary>
        /// Copies the attacker diameters whose route fields were prepared during the latest level scan.
        /// This is useful for editor-only route visualisation without exposing the mutable cache itself.
        /// </summary>
        public int CopyPrecomputedAttackerSizes(List<float> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var sizeKey in precomputedAttackerSizeKeys)
            {
                results.Add(GetSizeFromKey(sizeKey));
            }

            return results.Count;
        }

        /// <summary>
        /// Creates the cached fastest route from a position to the Food Store for the specified attacker diameter.
        /// The method only reads the startup route cache; it never rebuilds the map during rendering.
        /// </summary>
        public bool TryGetFastestRoute(Vector3 startingPosition, float attackerSize, List<Vector3> routePoints)
        {
            if (routePoints == null)
            {
                throw new ArgumentNullException(nameof(routePoints));
            }

            routePoints.Clear();
            if (!hasBuiltMap || foodStore == null)
            {
                return false;
            }

            var safeAttackerSize = Mathf.Max(0.01f, attackerSize);
            var attackerSizeKey = GetSizeKey(safeAttackerSize);
            var requiredHoleSizeKey = GetRequiredHoleSizeKey(attackerSizeKey);
            var requiredHoleSize = GetRequiredHoleSize(requiredHoleSizeKey);
            var radius = (safeAttackerSize * 0.5f) + obstaclePadding;
            var startPosition2D = (Vector2)startingPosition;
            var destination = foodStore.transform.position;

            if (!TryGetCellIndex(startPosition2D, out var currentIndex))
            {
                if (!IsClearSegment(startPosition2D, destination, radius, requiredHoleSize))
                {
                    return false;
                }

                routePoints.Add(startingPosition);
                routePoints.Add(destination);
                return true;
            }

            var routeKey = new RouteFieldKey(attackerSizeKey, requiredHoleSizeKey);
            if (!routeFields.TryGetValue(routeKey, out var field) || !field.Walkable[currentIndex] || field.GoalIndex < 0)
            {
                return false;
            }

            routePoints.Add(startingPosition);
            var remainingSteps = field.NextIndices.Length;
            while (currentIndex != field.GoalIndex && remainingSteps-- > 0)
            {
                var nextIndex = field.SmoothedNextIndices[currentIndex];
                if (nextIndex < 0)
                {
                    routePoints.Clear();
                    return false;
                }

                var nextCell = GetCellCenter(nextIndex);
                // Smoothed links are generated from cell centres, while this first segment may begin
                // at an arbitrary spawn position. Verify the actual segment before committing to it so
                // a diagonal cannot cut across the corner of a wall.
                if (!IsClearSegment(routePoints[^1], nextCell, radius, requiredHoleSize))
                {
                    nextIndex = field.NextIndices[currentIndex];
                    if (nextIndex < 0)
                    {
                        routePoints.Clear();
                        return false;
                    }

                    nextCell = GetCellCenter(nextIndex);
                    if (!IsClearSegment(routePoints[^1], nextCell, radius, requiredHoleSize))
                    {
                        routePoints.Clear();
                        return false;
                    }
                }

                currentIndex = nextIndex;
                var nextPoint = new Vector3(nextCell.x, nextCell.y, startingPosition.z);
                if ((routePoints[^1] - nextPoint).sqrMagnitude > 0.0001f)
                {
                    routePoints.Add(nextPoint);
                }
            }

            if (currentIndex != field.GoalIndex ||
                !IsClearSegment(routePoints[^1], destination, radius, requiredHoleSize))
            {
                routePoints.Clear();
                return false;
            }

            if ((routePoints[^1] - destination).sqrMagnitude > 0.0001f)
            {
                routePoints.Add(destination);
            }

            return routePoints.Count >= 2;
        }

        private void Start()
        {
            RebuildRoutes();
        }

        /// <summary>
        /// Scans the active level and generates the startup route field. Call again after changing walls at runtime.
        /// </summary>
        public bool RebuildRoutes()
        {
            foodStore ??= UnityEngine.Object.FindAnyObjectByType<FoodStore>();
            if (foodStore == null)
            {
                Debug.LogWarning("Farm Route Mapper could not find a Food Store, so no attacker routes were generated.", this);
                hasBuiltMap = false;
                return false;
            }

            cellSize = Mathf.Max(0.05f, cellSize);
            defaultAttackerSize = Mathf.Max(0.01f, defaultAttackerSize);
            cornerSmoothingLookAheadCells = Mathf.Clamp(cornerSmoothingLookAheadCells, 1, 32);

            if (!TryGetMainCameraBounds(out gridMinimum, out runtimeMapSize))
            {
                hasBuiltMap = false;
                return false;
            }

            columns = Mathf.Max(1, Mathf.CeilToInt(runtimeMapSize.x / cellSize));
            rows = Mathf.Max(1, Mathf.CeilToInt(runtimeMapSize.y / cellSize));

            // Keeping odd counts puts a cell at the visible camera centre, making central wall openings reliable.
            if (columns % 2 == 0)
            {
                columns++;
            }

            if (rows % 2 == 0)
            {
                rows++;
            }

            actualCellSize = new Vector2(runtimeMapSize.x / columns, runtimeMapSize.y / rows);
            walls = UnityEngine.Object.FindObjectsByType<FarmWall>(FindObjectsInactive.Exclude);
            foreach (var wall in walls)
            {
                wall.PrepareCollisionGeometry();
            }

            routeFields.Clear();
            CollectStartupRouteData();
            hasBuiltMap = true;
            BuildStartupRouteFields();
            return true;
        }

        /// <summary>
        /// Keeps a world position inside the Main Camera area used by the route mapper.
        /// Returns false when a route map cannot be built, so callers never fall back to moving off-screen.
        /// </summary>
        public bool TryClampToVisibleMap(Vector2 position, out Vector2 clampedPosition)
        {
            clampedPosition = position;
            if (!hasBuiltMap && !RebuildRoutes())
            {
                return false;
            }

            var maximum = gridMinimum + runtimeMapSize;
            var minimumX = gridMinimum.x + MapBoundaryEpsilon;
            var minimumY = gridMinimum.y + MapBoundaryEpsilon;
            var maximumX = maximum.x - MapBoundaryEpsilon;
            var maximumY = maximum.y - MapBoundaryEpsilon;
            clampedPosition = new Vector2(
                Mathf.Clamp(position.x, minimumX, maximumX),
                Mathf.Clamp(position.y, minimumY, maximumY));
            return true;
        }

        /// <summary>Gets the movement direction for an attacker at a world position, using its diameter for clearance.</summary>
        public bool TryGetNextDirection(Vector2 currentPosition, float attackerSize, out Vector2 direction)
        {
            direction = Vector2.zero;
            if (!hasBuiltMap && !RebuildRoutes())
            {
                return false;
            }

            var safeAttackerSize = Mathf.Max(0.01f, attackerSize);
            var attackerSizeKey = GetSizeKey(safeAttackerSize);
            var requiredHoleSizeKey = GetRequiredHoleSizeKey(attackerSizeKey);
            var requiredHoleSize = GetRequiredHoleSize(requiredHoleSizeKey);
            var radius = (safeAttackerSize * 0.5f) + obstaclePadding;

            if (!TryGetCellIndex(currentPosition, out var currentIndex))
            {
                // Attackers are clamped before lookup. Reject positions beyond the displayed map rather than
                // using a direct fallback that could send an animal outside the camera view.
                return false;
            }

            var routeKey = new RouteFieldKey(attackerSizeKey, requiredHoleSizeKey);
            if (routeFields.TryGetValue(routeKey, out var field))
            {
                if (field.Walkable[currentIndex])
                {
                    var smoothedNextIndex = field.SmoothedNextIndices[currentIndex];
                    if (TryGetClearDirectionToCell(
                            currentPosition,
                            smoothedNextIndex,
                            radius,
                            requiredHoleSize,
                            out direction))
                    {
                        return true;
                    }

                    var immediateNextIndex = field.NextIndices[currentIndex];
                    if (immediateNextIndex != smoothedNextIndex &&
                        TryGetClearDirectionToCell(
                            currentPosition,
                            immediateNextIndex,
                            radius,
                            requiredHoleSize,
                            out direction))
                    {
                        return true;
                    }
                }

                if (TryGetRecoveryDirection(currentPosition, currentIndex, field, radius, requiredHoleSize, out direction))
                {
                    return true;
                }
            }

            // Only terminal or unexpectedly unmapped positions take this small sampled fallback. It keeps the
            // normal movement path to an O(1) flow-field lookup while ensuring an animal continues into the
            // Food Store whenever a clear line is available.
            return TryGetClearDirectDirection(currentPosition, radius, requiredHoleSize, out direction);
        }

        private void CollectStartupRouteData()
        {
            precomputedAttackerSizeKeys.Clear();
            holeSizeKeys.Clear();

            AddUniqueKey(precomputedAttackerSizeKeys, GetSizeKey(defaultAttackerSize));

            if (attackerDefinitionsToPrecompute != null)
            {
                foreach (var definition in attackerDefinitionsToPrecompute)
                {
                    if (definition != null)
                    {
                        AddUniqueKey(precomputedAttackerSizeKeys, GetSizeKey(definition.Size));
                    }
                }
            }

            // Spawn Pool prewarming occurs in Awake, before this component's Start, so inactive pooled animals
            // are available to the initial scan without making the pool navigation-specific.
            var pooledAttackers = UnityEngine.Object.FindObjectsByType<BasicAttacker>(FindObjectsInactive.Include);
            foreach (var attacker in pooledAttackers)
            {
                if (attacker != null && attacker.Definition != null)
                {
                    AddUniqueKey(precomputedAttackerSizeKeys, GetSizeKey(attacker.Size));
                }
            }

            foreach (var wall in walls)
            {
                if (wall != null && wall.gameObject.activeInHierarchy && wall.HasHole && wall.HoleSize > 0f)
                {
                    AddUniqueKey(holeSizeKeys, GetSizeKey(wall.HoleSize));
                }
            }

            precomputedAttackerSizeKeys.Sort();
            holeSizeKeys.Sort();
        }

        private void BuildStartupRouteFields()
        {
            // Build the all-walls baseline first, before considering a single hole. It remains cached even when
            // a shorter eligible-opening route is also available.
            foreach (var attackerSizeKey in precomputedAttackerSizeKeys)
            {
                GetOrCreateRouteField(attackerSizeKey, NoHoleRouteKey);
            }

            // One field for a hole threshold evaluates every opening at least that wide in one Dijkstra pass.
            // That makes the selected field naturally choose the fastest size-2 hole over any size-1 hole when
            // it is the shorter overall route.
            foreach (var holeSizeKey in holeSizeKeys)
            {
                foreach (var attackerSizeKey in precomputedAttackerSizeKeys)
                {
                    if (attackerSizeKey <= holeSizeKey)
                    {
                        GetOrCreateRouteField(attackerSizeKey, holeSizeKey);
                    }
                }
            }
        }

        private RouteField GetOrCreateRouteField(int attackerSizeKey, int requiredHoleSizeKey)
        {
            var routeKey = new RouteFieldKey(attackerSizeKey, requiredHoleSizeKey);
            if (!routeFields.TryGetValue(routeKey, out var field))
            {
                field = BuildRouteField(GetSizeFromKey(attackerSizeKey), GetRequiredHoleSize(requiredHoleSizeKey));
                routeFields.Add(routeKey, field);
            }

            return field;
        }

        private RouteField BuildRouteField(float attackerSize, float requiredHoleSize)
        {
            var field = new RouteField(columns * rows);
            var radius = (attackerSize * 0.5f) + obstaclePadding;
            for (var index = 0; index < field.Walkable.Length; index++)
            {
                field.Walkable[index] = IsWalkable(GetCellCenter(index), radius, requiredHoleSize);
            }

            var goalIndex = FindClosestWalkableCell(foodStore.transform.position, field.Walkable);
            if (goalIndex < 0)
            {
                Debug.LogWarning("Farm Route Mapper found no walkable route cell near the Food Store.", this);
                return field;
            }

            field.GoalIndex = goalIndex;

            var distances = new float[field.Walkable.Length];
            Array.Fill(distances, float.PositiveInfinity);
            distances[goalIndex] = 0f;

            var openCells = new MinHeap();
            openCells.Push(goalIndex, 0f);

            while (openCells.Count > 0)
            {
                var current = openCells.Pop();
                if (current.Cost > distances[current.Index])
                {
                    continue;
                }

                for (var yOffset = -1; yOffset <= 1; yOffset++)
                {
                    for (var xOffset = -1; xOffset <= 1; xOffset++)
                    {
                        if (xOffset == 0 && yOffset == 0 || !TryGetNeighbourIndex(current.Index, xOffset, yOffset, out var neighbour))
                        {
                            continue;
                        }

                        if (!field.Walkable[neighbour] || !CanTraverse(neighbour, current.Index, xOffset, yOffset, radius, requiredHoleSize, field.Walkable))
                        {
                            continue;
                        }

                        var cost = distances[current.Index] + Vector2.Distance(GetCellCenter(current.Index), GetCellCenter(neighbour));
                        if (cost >= distances[neighbour])
                        {
                            continue;
                        }

                        distances[neighbour] = cost;
                        field.NextIndices[neighbour] = current.Index;
                        openCells.Push(neighbour, cost);
                    }
                }
            }

            BuildSmoothedNextIndices(field, radius, requiredHoleSize);

            return field;
        }

        /// <summary>
        /// Replaces tight cell-by-cell turns with bounded, wall-safe line-of-sight waypoints.
        /// This work runs during the level-start map build so attacker movement stays a constant-time lookup.
        /// </summary>
        private void BuildSmoothedNextIndices(RouteField field, float radius, float requiredHoleSize)
        {
            for (var index = 0; index < field.NextIndices.Length; index++)
            {
                var bestIndex = field.NextIndices[index];
                if (bestIndex < 0)
                {
                    continue;
                }

                var currentIndex = bestIndex;
                var origin = GetCellCenter(index);
                for (var step = 1; step < cornerSmoothingLookAheadCells; step++)
                {
                    var candidateIndex = field.NextIndices[currentIndex];
                    if (candidateIndex < 0 ||
                        !IsClearSegment(origin, GetCellCenter(candidateIndex), radius, requiredHoleSize))
                    {
                        break;
                    }

                    bestIndex = candidateIndex;
                    currentIndex = candidateIndex;
                }

                field.SmoothedNextIndices[index] = bestIndex;
            }
        }

        private bool CanTraverse(int fromIndex, int toIndex, int xOffset, int yOffset, float radius, float requiredHoleSize, bool[] walkable)
        {
            if (xOffset != 0 && yOffset != 0)
            {
                if (!TryGetNeighbourIndex(fromIndex, xOffset, 0, out var horizontal) ||
                    !TryGetNeighbourIndex(fromIndex, 0, yOffset, out var vertical) ||
                    !walkable[horizontal] || !walkable[vertical])
                {
                    return false;
                }
            }

            var midpoint = (GetCellCenter(fromIndex) + GetCellCenter(toIndex)) * 0.5f;
            return IsWalkable(midpoint, radius, requiredHoleSize);
        }

        private bool IsWalkable(Vector2 worldPosition, float radius, float requiredHoleSize)
        {
            if (!IsWithinVisibleMap(worldPosition))
            {
                return false;
            }

            foreach (var wall in walls)
            {
                if (wall != null && wall.gameObject.activeInHierarchy && wall.BlocksCircleForRoute(worldPosition, radius, requiredHoleSize))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetMainCameraBounds(out Vector2 minimum, out Vector2 size)
        {
            minimum = Vector2.zero;
            size = Vector2.zero;

            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("Farm Route Mapper needs a Main Camera to map the visible play area.", this);
                return false;
            }

            var mapPlane = new Plane(Vector3.forward, transform.position);
            if (!TryGetCameraViewportPoint(mainCamera, mapPlane, Vector2.zero, out var lowerLeft) ||
                !TryGetCameraViewportPoint(mainCamera, mapPlane, Vector2.right, out var lowerRight) ||
                !TryGetCameraViewportPoint(mainCamera, mapPlane, Vector2.up, out var upperLeft) ||
                !TryGetCameraViewportPoint(mainCamera, mapPlane, Vector2.one, out var upperRight))
            {
                Debug.LogWarning("Farm Route Mapper could not project the Main Camera onto its 2D map plane.", this);
                return false;
            }

            minimum = Vector2.Min(Vector2.Min(lowerLeft, lowerRight), Vector2.Min(upperLeft, upperRight));
            var maximum = Vector2.Max(Vector2.Max(lowerLeft, lowerRight), Vector2.Max(upperLeft, upperRight));
            size = maximum - minimum;
            if (size.x < cellSize || size.y < cellSize)
            {
                Debug.LogWarning("Farm Route Mapper found a Main Camera view that is too small to build a route map.", this);
                return false;
            }

            return true;
        }

        private static bool TryGetCameraViewportPoint(Camera camera, Plane mapPlane, Vector2 viewportPoint, out Vector2 worldPoint)
        {
            var ray = camera.ViewportPointToRay(viewportPoint);
            if (!mapPlane.Raycast(ray, out var distance))
            {
                worldPoint = Vector2.zero;
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        private bool IsWithinVisibleMap(Vector2 worldPosition)
        {
            var maximum = gridMinimum + runtimeMapSize;
            return worldPosition.x >= gridMinimum.x && worldPosition.x <= maximum.x &&
                   worldPosition.y >= gridMinimum.y && worldPosition.y <= maximum.y;
        }

        private bool TryGetClearDirectionToCell(
            Vector2 currentPosition,
            int targetIndex,
            float radius,
            float requiredHoleSize,
            out Vector2 direction)
        {
            direction = Vector2.zero;
            if (targetIndex < 0)
            {
                return false;
            }

            var target = GetCellCenter(targetIndex);
            var delta = target - currentPosition;
            if (delta.sqrMagnitude <= 0.0001f ||
                !IsClearSegment(currentPosition, target, radius, requiredHoleSize))
            {
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        private bool TryGetRecoveryDirection(
            Vector2 currentPosition,
            int currentIndex,
            RouteField field,
            float radius,
            float requiredHoleSize,
            out Vector2 direction)
        {
            direction = Vector2.zero;
            var currentColumn = currentIndex % columns;
            var currentRow = currentIndex / columns;
            var minimumCellSize = Mathf.Max(0.01f, Mathf.Min(actualCellSize.x, actualCellSize.y));
            var searchRadius = Mathf.Min(MaxRecoveryCellRadius, Mathf.CeilToInt(radius / minimumCellSize) + 2);
            var closestDistance = float.PositiveInfinity;
            var closestCell = Vector2.zero;

            for (var rowOffset = -searchRadius; rowOffset <= searchRadius; rowOffset++)
            {
                var candidateRow = currentRow + rowOffset;
                if (candidateRow < 0 || candidateRow >= rows)
                {
                    continue;
                }

                for (var columnOffset = -searchRadius; columnOffset <= searchRadius; columnOffset++)
                {
                    var candidateColumn = currentColumn + columnOffset;
                    if (candidateColumn < 0 || candidateColumn >= columns)
                    {
                        continue;
                    }

                    var candidateIndex = (candidateRow * columns) + candidateColumn;
                    if (!field.Walkable[candidateIndex] || field.NextIndices[candidateIndex] < 0)
                    {
                        continue;
                    }

                    var candidateCell = GetCellCenter(candidateIndex);
                    var distance = (candidateCell - currentPosition).sqrMagnitude;
                    if (distance <= 0.0001f || distance >= closestDistance ||
                        !IsClearSegment(currentPosition, candidateCell, radius, requiredHoleSize))
                    {
                        continue;
                    }

                    closestDistance = distance;
                    closestCell = candidateCell;
                }
            }

            if (closestDistance == float.PositiveInfinity)
            {
                return false;
            }

            direction = (closestCell - currentPosition).normalized;
            return direction.sqrMagnitude > 0f;
        }

        private bool TryGetClearDirectDirection(Vector2 currentPosition, float radius, float requiredHoleSize, out Vector2 direction)
        {
            direction = Vector2.zero;
            var destination = (Vector2)foodStore.transform.position;
            var delta = destination - currentPosition;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            if (!IsClearSegment(currentPosition, destination, radius, requiredHoleSize))
            {
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        private bool IsClearSegment(Vector2 startPosition, Vector2 destination, float radius, float requiredHoleSize)
        {
            var delta = destination - startPosition;
            var sampleSpacing = Mathf.Max(0.01f, Mathf.Min(actualCellSize.x, actualCellSize.y) * 0.5f);
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / sampleSpacing));
            for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
            {
                var samplePosition = Vector2.Lerp(startPosition, destination, sampleIndex / (float)sampleCount);
                if (!IsWalkable(samplePosition, radius, requiredHoleSize))
                {
                    return false;
                }
            }

            return true;
        }

        private int GetRequiredHoleSizeKey(int attackerSizeKey)
        {
            foreach (var holeSizeKey in holeSizeKeys)
            {
                if (holeSizeKey >= attackerSizeKey)
                {
                    return holeSizeKey;
                }
            }

            return NoHoleRouteKey;
        }

        private static float GetRequiredHoleSize(int requiredHoleSizeKey)
        {
            return requiredHoleSizeKey == NoHoleRouteKey ? float.PositiveInfinity : GetSizeFromKey(requiredHoleSizeKey);
        }

        private static int GetSizeKey(float size)
        {
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.001f, size) * SizeKeyScale));
        }

        private static float GetSizeFromKey(int sizeKey)
        {
            return sizeKey / SizeKeyScale;
        }

        private static void AddUniqueKey(List<int> keys, int key)
        {
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        private int FindClosestWalkableCell(Vector2 position, bool[] walkable)
        {
            var closestIndex = -1;
            var closestDistance = float.PositiveInfinity;
            for (var index = 0; index < walkable.Length; index++)
            {
                if (!walkable[index])
                {
                    continue;
                }

                var distance = ((Vector2)GetCellCenter(index) - position).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = index;
                }
            }

            return closestIndex;
        }

        private bool TryGetCellIndex(Vector2 worldPosition, out int index)
        {
            var local = worldPosition - gridMinimum;
            var column = Mathf.FloorToInt(local.x / actualCellSize.x);
            var row = Mathf.FloorToInt(local.y / actualCellSize.y);
            if (column < 0 || column >= columns || row < 0 || row >= rows)
            {
                index = -1;
                return false;
            }

            index = (row * columns) + column;
            return true;
        }

        private bool TryGetNeighbourIndex(int index, int xOffset, int yOffset, out int neighbourIndex)
        {
            var column = (index % columns) + xOffset;
            var row = (index / columns) + yOffset;
            if (column < 0 || column >= columns || row < 0 || row >= rows)
            {
                neighbourIndex = -1;
                return false;
            }

            neighbourIndex = (row * columns) + column;
            return true;
        }

        private Vector2 GetCellCenter(int index)
        {
            var column = index % columns;
            var row = index / columns;
            return gridMinimum + new Vector2(
                (column + 0.5f) * actualCellSize.x,
                (row + 0.5f) * actualCellSize.y);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.8f);
            var displayedMapSize = hasBuiltMap ? runtimeMapSize : mapSize;
            var centre = hasBuiltMap ? gridMinimum + (runtimeMapSize * 0.5f) : (Vector2)transform.position;
            Gizmos.DrawWireCube(centre, displayedMapSize);
        }

        private sealed class RouteField
        {
            public RouteField(int cellCount)
            {
                Walkable = new bool[cellCount];
                NextIndices = new int[cellCount];
                SmoothedNextIndices = new int[cellCount];
                Array.Fill(NextIndices, -1);
                Array.Fill(SmoothedNextIndices, -1);
            }

            public bool[] Walkable { get; }

            public int[] NextIndices { get; }

            public int[] SmoothedNextIndices { get; }

            public int GoalIndex { get; set; } = -1;
        }

        private readonly struct RouteFieldKey : IEquatable<RouteFieldKey>
        {
            public RouteFieldKey(int attackerSizeKey, int requiredHoleSizeKey)
            {
                AttackerSizeKey = attackerSizeKey;
                RequiredHoleSizeKey = requiredHoleSizeKey;
            }

            public int AttackerSizeKey { get; }

            public int RequiredHoleSizeKey { get; }

            public bool Equals(RouteFieldKey other)
            {
                return AttackerSizeKey == other.AttackerSizeKey && RequiredHoleSizeKey == other.RequiredHoleSizeKey;
            }

            public override bool Equals(object obj)
            {
                return obj is RouteFieldKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (AttackerSizeKey * 397) ^ RequiredHoleSizeKey;
            }
        }

        private readonly struct HeapNode
        {
            public HeapNode(int index, float cost)
            {
                Index = index;
                Cost = cost;
            }

            public int Index { get; }

            public float Cost { get; }
        }

        private sealed class MinHeap
        {
            private readonly List<HeapNode> nodes = new();

            public int Count => nodes.Count;

            public void Push(int index, float cost)
            {
                var node = new HeapNode(index, cost);
                nodes.Add(node);
                var child = nodes.Count - 1;
                while (child > 0)
                {
                    var parent = (child - 1) / 2;
                    if (nodes[parent].Cost <= node.Cost)
                    {
                        break;
                    }

                    nodes[child] = nodes[parent];
                    child = parent;
                }

                nodes[child] = node;
            }

            public HeapNode Pop()
            {
                var minimum = nodes[0];
                var last = nodes[^1];
                nodes.RemoveAt(nodes.Count - 1);
                if (nodes.Count == 0)
                {
                    return minimum;
                }

                var parent = 0;
                while (true)
                {
                    var left = (parent * 2) + 1;
                    if (left >= nodes.Count)
                    {
                        break;
                    }

                    var right = left + 1;
                    var smallerChild = right < nodes.Count && nodes[right].Cost < nodes[left].Cost ? right : left;
                    if (nodes[smallerChild].Cost >= last.Cost)
                    {
                        break;
                    }

                    nodes[parent] = nodes[smallerChild];
                    parent = smallerChild;
                }

                nodes[parent] = last;
                return minimum;
            }
        }
    }
}
