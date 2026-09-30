using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Environment;
using UnityEngine;
namespace TAD.Navigation
{
public readonly struct AttackerRouteDescriptor
{
    public AttackerRouteDescriptor(
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        bool ignoresTerrainEffects = false)
    {
        AttackerSize = attackerSize;
        TraversalProfile = traversalProfile;
        IgnoresTerrainEffects = ignoresTerrainEffects;
    }
    public float AttackerSize { get; }
    public AttackerTraversalProfile TraversalProfile { get; }
    /// <summary>True for flying attackers, whose movement is never changed by mud or ice.</summary>
    public bool IgnoresTerrainEffects { get; }
}

/// <summary>
/// Immutable timing snapshot for one already-mapped route. Costs are expressed as travel seconds at
/// movement speed one, so both AIs can reason about the same mud/ice exposure without performing
/// physics queries or independently recreating the route mapper's terrain rules.
/// </summary>
public readonly struct FarmRouteTiming
{
    private const float MinimumMovementSpeed = 0.01f;
    private const float MinimumSegmentLengthSquared = 0.000001f;

    private readonly Vector2[] segmentStarts;
    private readonly Vector2[] segmentEnds;
    private readonly float[] segmentUnitSpeedCosts;

    /// <summary>
    /// Builds timing from route samples. Each unit-speed cost describes the matching segment between
    /// consecutive points; callers normally receive this from <see cref="FarmRouteMapper"/>.
    /// </summary>
    public FarmRouteTiming(
        IReadOnlyList<Vector3> routePoints,
        IReadOnlyList<float> segmentCostsAtSpeedOne)
    {
        if (routePoints == null || routePoints.Count < 2 ||
            segmentCostsAtSpeedOne == null || segmentCostsAtSpeedOne.Count != routePoints.Count - 1)
        {
            segmentStarts = Array.Empty<Vector2>();
            segmentEnds = Array.Empty<Vector2>();
            segmentUnitSpeedCosts = Array.Empty<float>();
            Distance = 0f;
            UnitSpeedTravelCost = 0f;
            return;
        }

        var segmentCount = routePoints.Count - 1;
        segmentStarts = new Vector2[segmentCount];
        segmentEnds = new Vector2[segmentCount];
        segmentUnitSpeedCosts = new float[segmentCount];
        var totalDistance = 0f;
        var totalCost = 0f;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = (Vector2)routePoints[index];
            var end = (Vector2)routePoints[index + 1];
            var cost = Mathf.Max(0f, segmentCostsAtSpeedOne[index]);
            segmentStarts[index] = start;
            segmentEnds[index] = end;
            segmentUnitSpeedCosts[index] = cost;
            totalDistance += Vector2.Distance(start, end);
            totalCost += cost;
        }

        Distance = totalDistance;
        UnitSpeedTravelCost = totalCost;
    }

    /// <summary>Geometric length of the sampled route.</summary>
    public float Distance { get; }

    /// <summary>Travel time for this route when movement speed is exactly one world unit per second.</summary>
    public float UnitSpeedTravelCost { get; }

    public bool IsValid => segmentStarts != null && segmentStarts.Length > 0 && UnitSpeedTravelCost > 0.0001f;

    public float GetTravelTime(float movementSpeed)
    {
        return IsValid ? UnitSpeedTravelCost / Mathf.Max(MinimumMovementSpeed, movementSpeed) : 0f;
    }

    /// <summary>
    /// Calculates the amount of route travel time spent inside a circular defender range. The route
    /// mapper splits its timing samples at sub-cell intervals, which keeps mud and ice exposure local
    /// even when the visible path has a smoothed corner.
    /// </summary>
    public float GetTravelTimeInsideCircle(Vector2 centre, float radius, float movementSpeed)
    {
        if (!IsValid || radius <= 0f)
        {
            return 0f;
        }

        var costInsideCircle = 0f;
        for (var index = 0; index < segmentStarts.Length; index++)
        {
            var fractionInsideCircle = GetSegmentFractionInsideCircle(
                segmentStarts[index],
                segmentEnds[index],
                centre,
                radius);
            costInsideCircle += segmentUnitSpeedCosts[index] * fractionInsideCircle;
        }

        return costInsideCircle / Mathf.Max(MinimumMovementSpeed, movementSpeed);
    }

    private static float GetSegmentFractionInsideCircle(Vector2 start, Vector2 end, Vector2 centre, float radius)
    {
        var segment = end - start;
        var segmentLengthSquared = segment.sqrMagnitude;
        if (segmentLengthSquared <= MinimumSegmentLengthSquared || radius <= 0f)
        {
            return 0f;
        }

        var closestT = Mathf.Clamp01(Vector2.Dot(centre - start, segment) / segmentLengthSquared);
        var closestPoint = start + (segment * closestT);
        var radiusSquared = radius * radius;
        var closestDistanceSquared = (centre - closestPoint).sqrMagnitude;
        if (closestDistanceSquared >= radiusSquared)
        {
            return 0f;
        }

        var halfSpan = Mathf.Sqrt((radiusSquared - closestDistanceSquared) / segmentLengthSquared);
        return Mathf.Max(0f, Mathf.Clamp01(closestT + halfSpan) - Mathf.Clamp01(closestT - halfSpan));
    }
}

[DisallowMultipleComponent]
public sealed class FarmRouteMapper : MonoBehaviour
{
    private const float SizeKeyScale = 1000f;
    private const int MaxRecoveryCellRadius = 8;
    private const float MapBoundaryEpsilon = 0.0001f;
    [SerializeField]
    [Tooltip("The target all attacking animals try to reach. If blank, the first Food Store in the scene is used.")]
    private FoodStore foodStore;
    [SerializeField, Min(0.01f)]
    [Tooltip("The attacker diameter used to build the default grounded startup route field.")]
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
    private readonly Dictionary<RouteKey, RouteField> routeFields = new();
    private readonly List<RouteKey> precomputedRouteKeys = new();
    private FarmWall[] walls = Array.Empty<FarmWall>();
    // Terrain is discovered once with the rest of the static level geometry. Runtime movement still
    // uses trigger overlaps on BasicAttacker; this cache exists only for startup path weighting and
    // the low-frequency AI preparation decisions.
    private TerrainSpeedModifier[] terrainSpeedModifiers = Array.Empty<TerrainSpeedModifier>();
    private int columns;
    private int rows;
    private Vector2 gridMinimum;
    private Vector2 runtimeMapSize;
    private Vector2 actualCellSize;
    private bool hasBuiltMap;
    public FoodStore FoodStore => foodStore;
    public Vector2 MapSize => hasBuiltMap ? runtimeMapSize : mapSize;
    public Rect VisibleMapBounds => hasBuiltMap
        ? new Rect(gridMinimum, runtimeMapSize)
        : new Rect((Vector2)transform.position - (mapSize * 0.5f), mapSize);
    public int CachedRouteFieldCount => routeFields.Count;
    public int PrecomputedAttackerSizeCount => precomputedRouteKeys.Count;

    /// <summary>Copies the static terrain tiles captured by the most recent map rebuild.</summary>
    public int CopyTerrainSpeedModifiers(List<TerrainSpeedModifier> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        results.Clear();
        foreach (var terrainModifier in terrainSpeedModifiers)
        {
            if (terrainModifier != null && terrainModifier.isActiveAndEnabled)
            {
                results.Add(terrainModifier);
            }
        }

        return results.Count;
    }
    public int CopyPrecomputedAttackerSizes(List<float> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }
        results.Clear();
        var addedSizeKeys = new HashSet<int>();
        foreach (var routeKey in precomputedRouteKeys)
        {
            if (addedSizeKeys.Add(routeKey.SizeKey))
            {
                results.Add(GetSizeFromKey(routeKey.SizeKey));
            }
        }
        return results.Count;
    }
    public int CopyPrecomputedAttackerRoutes(List<AttackerRouteDescriptor> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }
        results.Clear();
        foreach (var routeKey in precomputedRouteKeys)
        {
            results.Add(new AttackerRouteDescriptor(
                GetSizeFromKey(routeKey.SizeKey),
                routeKey.TraversalProfile,
                routeKey.IgnoresTerrainEffects));
        }
        return results.Count;
    }
    public bool TryGetFastestRoute(Vector3 startingPosition, float attackerSize, List<Vector3> routePoints)
    {
        return TryGetFastestRoute(
            startingPosition,
            attackerSize,
            AttackerTraversalProfile.Ground,
            ignoresTerrainEffects: false,
            routePoints,
            out _);
    }

    /// <summary>
    /// Returns the fastest terrain-aware route for a concrete attacker definition. Flying attackers
    /// deliberately receive a terrain-immune field so ice and mud never change their route or timing.
    /// </summary>
    public bool TryGetFastestRoute(
        Vector3 startingPosition,
        AttackerDefinition definition,
        List<Vector3> routePoints)
    {
        return TryGetFastestRoute(startingPosition, definition, routePoints, out _);
    }

    /// <summary>Returns a terrain-aware route and the timing data consumed by both AI planners.</summary>
    public bool TryGetFastestRoute(
        Vector3 startingPosition,
        AttackerDefinition definition,
        List<Vector3> routePoints,
        out FarmRouteTiming timing)
    {
        if (definition == null)
        {
            if (routePoints == null)
            {
                throw new ArgumentNullException(nameof(routePoints));
            }

            routePoints.Clear();
            timing = default;
            return false;
        }

        return TryGetFastestRoute(
            startingPosition,
            definition.Size,
            definition.TraversalProfile,
            definition.Archetype == AttackerArchetype.Flying,
            routePoints,
            out timing);
    }

    public bool TryGetFastestRoute(
        Vector3 startingPosition,
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        List<Vector3> routePoints)
    {
        return TryGetFastestRoute(
            startingPosition,
            attackerSize,
            traversalProfile,
            ignoresTerrainEffects: false,
            routePoints,
            out _);
    }

    /// <summary>
    /// Returns the cached fastest route for a size/profile pair. The terrain immunity flag is part of
    /// the cache key, because flight is an attacker archetype rather than a cover traversal profile.
    /// </summary>
    public bool TryGetFastestRoute(
        Vector3 startingPosition,
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        bool ignoresTerrainEffects,
        List<Vector3> routePoints,
        out FarmRouteTiming timing)
    {
        if (routePoints == null)
        {
            throw new ArgumentNullException(nameof(routePoints));
        }
        routePoints.Clear();
        timing = default;
        if (!hasBuiltMap || foodStore == null)
        {
            return false;
        }
        var safeAttackerSize = Mathf.Max(0.01f, attackerSize);
        var routeKey = new RouteKey(GetSizeKey(safeAttackerSize), traversalProfile, ignoresTerrainEffects);
        var radius = (safeAttackerSize * 0.5f) + obstaclePadding;
        var startPosition2D = (Vector2)startingPosition;
        var destination = foodStore.transform.position;
        if (!TryGetCellIndex(startPosition2D, out var currentIndex))
        {
            if (!IsClearSegment(startPosition2D, destination, radius, traversalProfile))
            {
                return false;
            }
            routePoints.Add(startingPosition);
            routePoints.Add(destination);
            timing = BuildRouteTiming(routePoints, ignoresTerrainEffects);
            return timing.IsValid;
        }
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
            // Smoothed links are generated from cell centres. Check the real first segment so an
            // arbitrary spawn cannot cut a wall corner before it joins the cached route.
            if (!IsClearSegment(routePoints[^1], nextCell, radius, traversalProfile))
            {
                nextIndex = field.NextIndices[currentIndex];
                if (nextIndex < 0)
                {
                    routePoints.Clear();
                    return false;
                }
                nextCell = GetCellCenter(nextIndex);
                if (!IsClearSegment(routePoints[^1], nextCell, radius, traversalProfile))
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
            !IsClearSegment(routePoints[^1], destination, radius, traversalProfile))
        {
            routePoints.Clear();
            return false;
        }
        if ((routePoints[^1] - destination).sqrMagnitude > 0.0001f)
        {
            routePoints.Add(destination);
        }
        timing = BuildRouteTiming(routePoints, ignoresTerrainEffects);
        return routePoints.Count >= 2 && timing.IsValid;
    }
    private void Start()
    {
        RebuildRoutes();
    }
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
        terrainSpeedModifiers = UnityEngine.Object.FindObjectsByType<TerrainSpeedModifier>(FindObjectsInactive.Exclude);
        routeFields.Clear();
        CollectStartupRouteData();
        hasBuiltMap = true;
        BuildStartupRouteFields();
        return true;
    }
    public bool TryClampToVisibleMap(Vector2 position, out Vector2 clampedPosition)
    {
        clampedPosition = position;
        if (!hasBuiltMap && !RebuildRoutes())
        {
            return false;
        }
        var maximum = gridMinimum + runtimeMapSize;
        clampedPosition = new Vector2(
            Mathf.Clamp(position.x, gridMinimum.x + MapBoundaryEpsilon, maximum.x - MapBoundaryEpsilon),
            Mathf.Clamp(position.y, gridMinimum.y + MapBoundaryEpsilon, maximum.y - MapBoundaryEpsilon));
        return true;
    }
    public bool TryGetNextDirection(Vector2 currentPosition, float attackerSize, out Vector2 direction)
    {
        return TryGetNextDirection(
            currentPosition,
            attackerSize,
            AttackerTraversalProfile.Ground,
            ignoresTerrainEffects: false,
            out direction);
    }

    /// <summary>Returns a flow-field direction using the concrete attacker's terrain immunity.</summary>
    public bool TryGetNextDirection(
        Vector2 currentPosition,
        AttackerDefinition definition,
        out Vector2 direction)
    {
        if (definition == null)
        {
            direction = Vector2.zero;
            return false;
        }

        return TryGetNextDirection(
            currentPosition,
            definition.Size,
            definition.TraversalProfile,
            definition.Archetype == AttackerArchetype.Flying,
            out direction);
    }

    public bool TryGetNextDirection(
        Vector2 currentPosition,
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        out Vector2 direction)
    {
        return TryGetNextDirection(
            currentPosition,
            attackerSize,
            traversalProfile,
            ignoresTerrainEffects: false,
            out direction);
    }

    public bool TryGetNextDirection(
        Vector2 currentPosition,
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        bool ignoresTerrainEffects,
        out Vector2 direction)
    {
        direction = Vector2.zero;
        if (!hasBuiltMap && !RebuildRoutes())
        {
            return false;
        }
        var safeAttackerSize = Mathf.Max(0.01f, attackerSize);
        var routeKey = new RouteKey(GetSizeKey(safeAttackerSize), traversalProfile, ignoresTerrainEffects);
        var radius = (safeAttackerSize * 0.5f) + obstaclePadding;
        if (!TryGetCellIndex(currentPosition, out var currentIndex))
        {
            return false;
        }
        if (routeFields.TryGetValue(routeKey, out var field))
        {
            if (field.Walkable[currentIndex])
            {
                var smoothedNextIndex = field.SmoothedNextIndices[currentIndex];
                if (TryGetClearDirectionToCell(currentPosition, smoothedNextIndex, radius, traversalProfile, out direction))
                {
                    return true;
                }
                var immediateNextIndex = field.NextIndices[currentIndex];
                if (immediateNextIndex != smoothedNextIndex &&
                    TryGetClearDirectionToCell(currentPosition, immediateNextIndex, radius, traversalProfile, out direction))
                {
                    return true;
                }
            }
            if (TryGetRecoveryDirection(currentPosition, currentIndex, field, radius, traversalProfile, out direction))
            {
                return true;
            }
        }
        return TryGetClearDirectDirection(currentPosition, radius, traversalProfile, out direction);
    }
    private void CollectStartupRouteData()
    {
        precomputedRouteKeys.Clear();
        AddUniqueKey(new RouteKey(
            GetSizeKey(defaultAttackerSize),
            AttackerTraversalProfile.Ground,
            ignoresTerrainEffects: false));
        if (attackerDefinitionsToPrecompute != null)
        {
            foreach (var definition in attackerDefinitionsToPrecompute)
            {
                if (definition != null)
                {
                    AddUniqueKey(new RouteKey(
                        GetSizeKey(definition.Size),
                        definition.TraversalProfile,
                        definition.Archetype == AttackerArchetype.Flying));
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
                AddUniqueKey(new RouteKey(
                    GetSizeKey(attacker.Size),
                    attacker.TraversalProfile,
                    attacker.IsFlying));
            }
        }
        precomputedRouteKeys.Sort();
    }
    private void BuildStartupRouteFields()
    {
        foreach (var routeKey in precomputedRouteKeys)
        {
            GetOrCreateRouteField(routeKey);
        }
    }
    private RouteField GetOrCreateRouteField(RouteKey routeKey)
    {
        if (!routeFields.TryGetValue(routeKey, out var field))
        {
            field = BuildRouteField(
                GetSizeFromKey(routeKey.SizeKey),
                routeKey.TraversalProfile,
                routeKey.IgnoresTerrainEffects);
            routeFields.Add(routeKey, field);
        }
        return field;
    }
    private RouteField BuildRouteField(
        float attackerSize,
        AttackerTraversalProfile traversalProfile,
        bool ignoresTerrainEffects)
    {
        var field = new RouteField(columns * rows);
        var radius = (attackerSize * 0.5f) + obstaclePadding;
        for (var index = 0; index < field.Walkable.Length; index++)
        {
            field.Walkable[index] = IsWalkable(GetCellCenter(index), radius, traversalProfile);
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
                    if ((xOffset == 0 && yOffset == 0) ||
                        !TryGetNeighbourIndex(current.Index, xOffset, yOffset, out var neighbour) ||
                        !field.Walkable[neighbour] ||
                        !CanTraverse(neighbour, current.Index, radius, traversalProfile, field.Walkable))
                    {
                        continue;
                    }
                    var cost = distances[current.Index] + CalculateUnitSpeedTravelCost(
                        GetCellCenter(current.Index),
                        GetCellCenter(neighbour),
                        ignoresTerrainEffects);
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
        field.TravelCosts = distances;
        BuildSmoothedNextIndices(field, radius, traversalProfile, ignoresTerrainEffects);
        return field;
    }
    private void BuildSmoothedNextIndices(
        RouteField field,
        float radius,
        AttackerTraversalProfile traversalProfile,
        bool ignoresTerrainEffects)
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
                    !IsClearSegment(origin, GetCellCenter(candidateIndex), radius, traversalProfile))
                {
                    break;
                }

                // The normal path is terrain-time optimal. A visual smoothing shortcut is accepted
                // only when it is no slower than the cells it replaces; this prevents a corner line
                // from silently cutting through mud after Dijkstra deliberately chose an ice lane.
                var preservedTravelCost = field.TravelCosts[index] - field.TravelCosts[candidateIndex];
                var shortcutTravelCost = CalculateUnitSpeedTravelCost(
                    origin,
                    GetCellCenter(candidateIndex),
                    ignoresTerrainEffects);
                if (shortcutTravelCost > preservedTravelCost + 0.0001f)
                {
                    break;
                }

                bestIndex = candidateIndex;
                currentIndex = candidateIndex;
            }
            field.SmoothedNextIndices[index] = bestIndex;
        }
    }

    /// <summary>
    /// Splits the sparse movement path into short timing samples. Keeping the presentation route sparse
    /// preserves smooth attacker movement, while the timing snapshot can still distinguish a defender
    /// covering the muddy half of a long straight segment from one covering its dry half.
    /// </summary>
    private FarmRouteTiming BuildRouteTiming(IReadOnlyList<Vector3> routePoints, bool ignoresTerrainEffects)
    {
        if (routePoints == null || routePoints.Count < 2)
        {
            return default;
        }

        var timingPoints = new List<Vector3> { routePoints[0] };
        var segmentCosts = new List<float>();
        var sampleSpacing = GetTerrainTimingSampleSpacing();
        for (var routeIndex = 1; routeIndex < routePoints.Count; routeIndex++)
        {
            var start = routePoints[routeIndex - 1];
            var end = routePoints[routeIndex];
            var segmentLength = Vector2.Distance(start, end);
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(segmentLength / sampleSpacing));
            for (var sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                var sampleStart = Vector3.Lerp(start, end, (sampleIndex - 1) / (float)sampleCount);
                var sampleEnd = Vector3.Lerp(start, end, sampleIndex / (float)sampleCount);
                if ((sampleEnd - sampleStart).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                timingPoints.Add(sampleEnd);
                segmentCosts.Add(CalculateUnitSpeedTravelCost(
                    sampleStart,
                    sampleEnd,
                    ignoresTerrainEffects));
            }
        }

        return new FarmRouteTiming(timingPoints, segmentCosts);
    }

    /// <summary>Calculates travel seconds at speed one for a terrain-aware line segment.</summary>
    private float CalculateUnitSpeedTravelCost(
        Vector2 startPosition,
        Vector2 destination,
        bool ignoresTerrainEffects)
    {
        var delta = destination - startPosition;
        var distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return 0f;
        }

        if (ignoresTerrainEffects || terrainSpeedModifiers == null || terrainSpeedModifiers.Length == 0)
        {
            return distance;
        }

        var sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / GetTerrainTimingSampleSpacing()));
        var sampleDistance = distance / sampleCount;
        var travelCost = 0f;
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var sampleMidpoint = Vector2.Lerp(
                startPosition,
                destination,
                (sampleIndex + 0.5f) / sampleCount);
            travelCost += sampleDistance / GetTerrainMovementMultiplierAt(
                sampleMidpoint,
                ignoresTerrainEffects);
        }

        return travelCost;
    }

    private float GetTerrainTimingSampleSpacing()
    {
        var smallestCellDimension = Mathf.Min(actualCellSize.x, actualCellSize.y);
        if (smallestCellDimension <= 0.0001f)
        {
            smallestCellDimension = cellSize;
        }

        // Half a route cell keeps a tile boundary local in the resulting AI timing without making
        // startup mapping costly on the mobile target maps.
        return Mathf.Max(0.01f, smallestCellDimension * 0.5f);
    }

    private float GetTerrainMovementMultiplierAt(Vector2 worldPosition, bool ignoresTerrainEffects)
    {
        if (ignoresTerrainEffects || terrainSpeedModifiers == null || terrainSpeedModifiers.Length == 0)
        {
            return 1f;
        }

        var multiplier = 1f;
        foreach (var terrainModifier in terrainSpeedModifiers)
        {
            if (terrainModifier != null && terrainModifier.ContainsWorldPoint(worldPosition))
            {
                multiplier *= terrainModifier.MovementSpeedMultiplier;
            }
        }

        // Mirrors BasicAttacker's safety clamp for the unusual case of overlapping authored terrain.
        return Mathf.Clamp(multiplier, 0.05f, 3f);
    }

    private bool CanTraverse(
        int fromIndex,
        int toIndex,
        float radius,
        AttackerTraversalProfile traversalProfile,
        bool[] walkable)
    {
        var fromColumn = fromIndex % columns;
        var fromRow = fromIndex / columns;
        var toColumn = toIndex % columns;
        var toRow = toIndex / columns;
        if (fromColumn != toColumn && fromRow != toRow)
        {
            var horizontalIndex = (fromRow * columns) + toColumn;
            var verticalIndex = (toRow * columns) + fromColumn;
            if (!walkable[horizontalIndex] || !walkable[verticalIndex])
            {
                return false;
            }
        }
        var midpoint = (GetCellCenter(fromIndex) + GetCellCenter(toIndex)) * 0.5f;
        return IsWalkable(midpoint, radius, traversalProfile);
    }
    private bool IsWalkable(Vector2 worldPosition, float radius, AttackerTraversalProfile traversalProfile)
    {
        if (!IsWithinVisibleMap(worldPosition))
        {
            return false;
        }
        foreach (var wall in walls)
        {
            if (wall != null && wall.gameObject.activeInHierarchy &&
                wall.BlocksAttackerCircle(worldPosition, radius, traversalProfile))
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
        AttackerTraversalProfile traversalProfile,
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
            !IsClearSegment(currentPosition, target, radius, traversalProfile))
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
        AttackerTraversalProfile traversalProfile,
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
                    !IsClearSegment(currentPosition, candidateCell, radius, traversalProfile))
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
    private bool TryGetClearDirectDirection(
        Vector2 currentPosition,
        float radius,
        AttackerTraversalProfile traversalProfile,
        out Vector2 direction)
    {
        direction = Vector2.zero;
        var destination = (Vector2)foodStore.transform.position;
        var delta = destination - currentPosition;
        if (delta.sqrMagnitude <= 0.0001f ||
            !IsClearSegment(currentPosition, destination, radius, traversalProfile))
        {
            return false;
        }
        direction = delta.normalized;
        return true;
    }
    private bool IsClearSegment(
        Vector2 startPosition,
        Vector2 destination,
        float radius,
        AttackerTraversalProfile traversalProfile)
    {
        var delta = destination - startPosition;
        var sampleSpacing = Mathf.Max(0.01f, Mathf.Min(actualCellSize.x, actualCellSize.y) * 0.5f);
        var sampleCount = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / sampleSpacing));
        for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
        {
            var samplePosition = Vector2.Lerp(startPosition, destination, sampleIndex / (float)sampleCount);
            if (!IsWalkable(samplePosition, radius, traversalProfile))
            {
                return false;
            }
        }
        return true;
    }
    private void AddUniqueKey(RouteKey key)
    {
        if (!precomputedRouteKeys.Contains(key))
        {
            precomputedRouteKeys.Add(key);
        }
    }
    private static int GetSizeKey(float size)
    {
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.001f, size) * SizeKeyScale));
    }
    private static float GetSizeFromKey(int sizeKey)
    {
        return sizeKey / SizeKeyScale;
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
    private readonly struct RouteKey : IEquatable<RouteKey>, IComparable<RouteKey>
    {
        public RouteKey(
            int sizeKey,
            AttackerTraversalProfile traversalProfile,
            bool ignoresTerrainEffects)
        {
            SizeKey = sizeKey;
            TraversalProfile = traversalProfile;
            IgnoresTerrainEffects = ignoresTerrainEffects;
        }
        public int SizeKey { get; }
        public AttackerTraversalProfile TraversalProfile { get; }
        public bool IgnoresTerrainEffects { get; }
        public bool Equals(RouteKey other)
        {
            return SizeKey == other.SizeKey &&
                   TraversalProfile == other.TraversalProfile &&
                   IgnoresTerrainEffects == other.IgnoresTerrainEffects;
        }
        public override bool Equals(object obj)
        {
            return obj is RouteKey other && Equals(other);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(SizeKey, (int)TraversalProfile, IgnoresTerrainEffects);
        }
        public int CompareTo(RouteKey other)
        {
            var sizeComparison = SizeKey.CompareTo(other.SizeKey);
            if (sizeComparison != 0)
            {
                return sizeComparison;
            }

            var profileComparison = TraversalProfile.CompareTo(other.TraversalProfile);
            return profileComparison != 0
                ? profileComparison
                : IgnoresTerrainEffects.CompareTo(other.IgnoresTerrainEffects);
        }
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
        /// <summary>Unit-speed travel cost from each cell to the Food Store.</summary>
        public float[] TravelCosts { get; set; } = Array.Empty<float>();
        public int GoalIndex { get; set; } = -1;
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
