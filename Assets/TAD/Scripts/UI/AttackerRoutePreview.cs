using System.Collections.Generic;
using TAD.Attackers;
using TAD.Navigation;
using UnityEngine;

namespace TAD.UI
{
    /// <summary>World-space route highlight for the currently selected queued attacker group.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class AttackerRoutePreview : MonoBehaviour
    {
        private readonly List<Vector3> routePoints = new();
        private LineRenderer lineRenderer;
        private Material lineMaterial;

        private void Awake()
        {
            var attackerLayer = LayerMask.NameToLayer("Attacker");
            if (attackerLayer >= 0)
            {
                gameObject.layer = attackerLayer;
            }

            lineRenderer = GetComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.widthMultiplier = 0.06f;
            lineRenderer.numCapVertices = 5;
            lineRenderer.numCornerVertices = 5;
            lineRenderer.startColor = new Color(1f, 0.78f, 0.12f, 0.92f);
            lineRenderer.endColor = new Color(1f, 0.32f, 0.08f, 0.92f);
            lineRenderer.sortingOrder = 75;

            var shader = Shader.Find("Sprites/Default") ??
                         Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
                         Shader.Find("Unlit/Color");
            if (shader != null)
            {
                lineMaterial = new Material(shader);
                lineRenderer.sharedMaterial = lineMaterial;
            }

            Hide();
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
            {
                Destroy(lineMaterial);
            }
        }

        /// <summary>Shows the mapper's precomputed fastest route for this attacker's size and traversal profile.</summary>
        public bool Show(FarmRouteMapper routeMapper, Vector3 spawnPosition, AttackerDefinition definition)
        {
            routePoints.Clear();
            if (routeMapper == null || definition == null ||
                !routeMapper.TryGetFastestRoute(spawnPosition, definition, routePoints) ||
                routePoints.Count < 2)
            {
                Hide();
                return false;
            }

            lineRenderer.positionCount = routePoints.Count;
            var renderDepth = routeMapper.transform.position.z - 0.02f;
            for (var index = 0; index < routePoints.Count; index++)
            {
                var routePoint = routePoints[index];
                lineRenderer.SetPosition(index, new Vector3(routePoint.x, routePoint.y, renderDepth));
            }

            lineRenderer.enabled = true;
            return true;
        }

        /// <summary>Hides the preview without changing cached route data.</summary>
        public void Hide()
        {
            if (lineRenderer == null)
            {
                return;
            }

            lineRenderer.positionCount = 0;
            lineRenderer.enabled = false;
        }
    }
}
