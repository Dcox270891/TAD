using System;
using System.Collections.Generic;
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
        [SerializeField]
        [Tooltip("The target all attacking animals try to reach. If blank, the first Food Store in the scene is used.")]
        private FoodStore foodStore;

        [SerializeField, Min(0.01f)]
        [Tooltip("The attacker diameter used to build the startup route field.")]
        private float defaultAttackerSize = 1f;

        [SerializeField, Min(0.05f)]
        [Tooltip("The desired spacing between route cells. Smaller values make tighter routes at a higher startup cost.")]
        private float cellSize = 0.25f;

        [SerializeField]
        [Tooltip("World-space width and height covered by the route map, centered on this object.")]
        private Vector2 mapSize = new(20f, 12f);

        [SerializeField, Min(0f)]
        [Tooltip("Extra clearance left around walls, in world units.")]
        private float obstaclePadding;

        private readonly Dictionary<int, RouteField> routeFields = new();
        private FarmWall[] walls = Array.Empty<FarmWall>();
        private int columns;
        private int rows;
        private Vector2 gridMinimum;
        private Vector2 actualCellSize;
        private bool hasBuiltMap;

        public FoodStore FoodStore => foodStore;

        public Vector2 MapSize => mapSize;

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

            mapSize.x = Mathf.Max(cellSize, mapSize.x);
            mapSize.y = Mathf.Max(cellSize, mapSize.y);
            cellSize = Mathf.Max(0.05f, cellSize);
            defaultAttackerSize = Mathf.Max(0.01f, defaultAttackerSize);

            columns = Mathf.Max(1, Mathf.CeilToInt(mapSize.x / cellSize));
            rows = Mathf.Max(1, Mathf.CeilToInt(mapSize.y / cellSize));

            // Keeping odd counts puts a cell exactly at the mapper's origin, making central wall openings reliable.
            if (columns % 2 == 0)
            {
                columns++;
            }

            if (rows % 2 == 0)
            {
                rows++;
            }

            actualCellSize = new Vector2(mapSize.x / columns, mapSize.y / rows);
            gridMinimum = (Vector2)transform.position - (mapSize * 0.5f);
            walls = UnityEngine.Object.FindObjectsByType<FarmWall>();
            foreach (var wall in walls)
            {
                wall.PrepareCollisionGeometry();
            }

            routeFields.Clear();
            hasBuiltMap = true;
            GetOrCreateRouteField(defaultAttackerSize);
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

            if (!TryGetCellIndex(currentPosition, out var currentIndex))
            {
                return false;
            }

            var field = GetOrCreateRouteField(Mathf.Max(0.01f, attackerSize));
            var nextIndex = field.NextIndices[currentIndex];
            if (nextIndex < 0)
            {
                return false;
            }

            direction = (GetCellCenter(nextIndex) - currentPosition).normalized;
            return direction.sqrMagnitude > 0f;
        }

        private RouteField GetOrCreateRouteField(float attackerSize)
        {
            var sizeKey = Mathf.RoundToInt(attackerSize * 1000f);
            if (!routeFields.TryGetValue(sizeKey, out var field))
            {
                field = BuildRouteField(sizeKey / 1000f);
                routeFields.Add(sizeKey, field);
            }

            return field;
        }

        private RouteField BuildRouteField(float attackerSize)
        {
            var field = new RouteField(columns * rows);
            var radius = (attackerSize * 0.5f) + obstaclePadding;
            for (var index = 0; index < field.Walkable.Length; index++)
            {
                field.Walkable[index] = IsWalkable(GetCellCenter(index), radius);
            }

            var goalIndex = FindClosestWalkableCell(foodStore.transform.position, field.Walkable);
            if (goalIndex < 0)
            {
                Debug.LogWarning("Farm Route Mapper found no walkable route cell near the Food Store.", this);
                return field;
            }

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

                        if (!field.Walkable[neighbour] || !CanTraverse(neighbour, current.Index, xOffset, yOffset, radius, field.Walkable))
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

            return field;
        }

        private bool CanTraverse(int fromIndex, int toIndex, int xOffset, int yOffset, float radius, bool[] walkable)
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
            return IsWalkable(midpoint, radius);
        }

        private bool IsWalkable(Vector2 worldPosition, float radius)
        {
            foreach (var wall in walls)
            {
                if (wall != null && wall.gameObject.activeInHierarchy && wall.BlocksCircle(worldPosition, radius))
                {
                    return false;
                }
            }

            return true;
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
            Gizmos.DrawWireCube(transform.position, mapSize);
        }

        private sealed class RouteField
        {
            public RouteField(int cellCount)
            {
                Walkable = new bool[cellCount];
                NextIndices = new int[cellCount];
                Array.Fill(NextIndices, -1);
            }

            public bool[] Walkable { get; }

            public int[] NextIndices { get; }
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
