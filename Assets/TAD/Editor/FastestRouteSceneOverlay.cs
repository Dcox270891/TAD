#if UNITY_EDITOR
using System.Collections.Generic;
using TAD.Navigation;
using TAD.Waves;
using UnityEditor;
using UnityEngine;

namespace TAD.Editor
{
    /// <summary>
    /// Draws cached attacker routes in the Unity Scene view while the level is running.
    /// It is compiled into the editor only, so it cannot appear in the Game view or a player build.
    /// </summary>
    [InitializeOnLoad]
    internal static class FastestRouteSceneOverlay
    {
        private static readonly List<float> AttackerSizes = new();
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

            var routeMappers = Object.FindObjectsByType<FarmRouteMapper>(FindObjectsInactive.Exclude);
            var waveControllers = Object.FindObjectsByType<WaveController>(FindObjectsInactive.Exclude);
            for (var mapperIndex = 0; mapperIndex < routeMappers.Length; mapperIndex++)
            {
                var routeMapper = routeMappers[mapperIndex];
                routeMapper.CopyPrecomputedAttackerSizes(AttackerSizes);

                foreach (var waveController in waveControllers)
                {
                    for (var sizeIndex = 0; sizeIndex < AttackerSizes.Count; sizeIndex++)
                    {
                        var attackerSize = AttackerSizes[sizeIndex];
                        if (!routeMapper.TryGetFastestRoute(waveController.SpawnPosition, attackerSize, RoutePoints))
                        {
                            continue;
                        }

                        var hue = Mathf.Repeat((mapperIndex * 0.27f) + (sizeIndex * 0.6180339f), 1f);
                        var color = Color.HSVToRGB(hue, 0.8f, 1f);
                        using (new Handles.DrawingScope(color))
                        {
                            Handles.DrawAAPolyLine(3f, RoutePoints.ToArray());
                            Handles.Label(
                                RoutePoints[0] + (Vector3.up * (0.2f + (sizeIndex * 0.15f))),
                                $"Fastest route - size {attackerSize:0.##}");
                        }
                    }
                }
            }
        }
    }
}
#endif
