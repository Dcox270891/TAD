#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TAD.Attackers;
using TAD.Navigation;
using TAD.Pooling;
using TAD.Waves;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>Draws cached attacker routes in the Unity Scene view while the level is running.</summary>
    [InitializeOnLoad]
    internal static class FastestRouteSceneOverlay
    {
        private static readonly List<AttackerRouteDescriptor> AttackerRoutes = new();
        private static readonly List<Vector3> RoutePoints = new();

        static FastestRouteSceneOverlay()
        {
            SceneView.duringSceneGui -= DrawRoutes;
            SceneView.duringSceneGui += DrawRoutes;
        }

        private static void DrawRoutes(SceneView sceneView)
        {
            if (!Application.isPlaying || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var routeMappers = Object.FindObjectsByType<SpawnPool>(FindObjectsInactive.Exclude)
                .Select(pool => pool.RouteMapper)
                .Where(mapper => mapper != null)
                .Distinct()
                .ToArray();
            var waveControllers = Object.FindObjectsByType<WaveController>(FindObjectsInactive.Exclude);
            for (var mapperIndex = 0; mapperIndex < routeMappers.Length; mapperIndex++)
            {
                var routeMapper = routeMappers[mapperIndex];
                routeMapper.CopyPrecomputedAttackerRoutes(AttackerRoutes);
                foreach (var waveController in waveControllers)
                {
                    for (var routeIndex = 0; routeIndex < AttackerRoutes.Count; routeIndex++)
                    {
                        var route = AttackerRoutes[routeIndex];
                        if (!routeMapper.TryGetFastestRoute(
                                waveController.SpawnPosition,
                                route.AttackerSize,
                                route.TraversalProfile,
                                route.IgnoresTerrainEffects,
                                RoutePoints,
                                out _))
                        {
                            continue;
                        }

                        var hue = Mathf.Repeat((mapperIndex * 0.27f) + (routeIndex * 0.6180339f), 1f);
                        var color = Color.HSVToRGB(hue, 0.8f, 1f);
                        using (new Handles.DrawingScope(color))
                        {
                            Handles.DrawAAPolyLine(3f, RoutePoints.ToArray());
                            var profileLabel = route.TraversalProfile == AttackerTraversalProfile.IgnoresHalfCover
                                ? "flying"
                                : "ground";
                            var terrainLabel = route.IgnoresTerrainEffects ? "terrain immune" : "terrain aware";
                            Handles.Label(
                                RoutePoints[0] + (Vector3.up * (0.2f + (routeIndex * 0.15f))),
                                $"Fastest route - {profileLabel}, {terrainLabel}, size {route.AttackerSize:0.##}");
                        }
                    }
                }
            }
        }
    }
}
#endif
