using UnityEngine;

namespace TAD.Environment
{
    /// <summary>
    /// Identifies the authored grass tile that defines a farm's visual ground.
    ///
    /// This component owns no colliders, but its visible SpriteRenderer footprint is also the
    /// navigation fallback for scenes without an authored <c>FarmPlayableArea</c>. This means the
    /// part of the screen that looks like farm grass is the only part animals and farmhands may
    /// occupy. It deliberately preserves its scene-authored transform and size. Camera-only
    /// surplus belongs to <see cref="FarmViewportBorder"/>, not to the walkable-looking grass.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class GameplayGrassBackdrop : MonoBehaviour
    {
        // Retained for existing shared-core instances. It is intentionally no longer used to move
        // or resize the grass; the level's authored FarmPlayableArea defines the playable extent.
        [SerializeField, HideInInspector] private Camera gameplayCamera;
        [SerializeField] private SpriteRenderer grassRenderer;

        /// <summary>
        /// Legacy reference retained for serialized compatibility. It cannot resize or reposition
        /// the grass; <see cref="FarmViewportBorder"/> owns exterior viewport coverage.
        /// </summary>
        public Camera GameplayCamera => gameplayCamera;

        /// <summary>The tiled renderer used to draw the grass ground.</summary>
        public SpriteRenderer GrassRenderer => grassRenderer;

        private void Awake()
        {
            ResolveReferences();
            EnsureTiledRendering();
        }

        /// <summary>
        /// Supplies explicit references for an authored level or a focused test. The camera is
        /// preserved only for backwards compatibility; it cannot make grass cover the screen.
        /// </summary>
        public void Configure(Camera camera = null, SpriteRenderer renderer = null)
        {
            gameplayCamera = camera ?? gameplayCamera;
            grassRenderer = renderer ?? grassRenderer;
            ResolveReferences();
            EnsureTiledRendering();
        }

        /// <summary>
        /// Compatibility entry point for older callers. It now preserves the authored grass
        /// footprint instead of fitting it to the camera. Exterior coverage is provided by
        /// <see cref="FarmViewportBorder"/>.
        /// </summary>
        public void FitToCameraViewport()
        {
            ResolveReferences();
            EnsureTiledRendering();
        }

        /// <summary>
        /// Legacy diagnostic. Authored grass may intentionally be smaller than the camera, so a
        /// valid tiled renderer—not camera coverage—is the supported contract.
        /// </summary>
        public bool CoversCurrentViewport()
        {
            ResolveReferences();
            return grassRenderer != null && grassRenderer.drawMode == SpriteDrawMode.Tiled;
        }

        private void ResolveReferences()
        {
            grassRenderer ??= GetComponent<SpriteRenderer>();
            gameplayCamera ??= Camera.main;
        }

        private void EnsureTiledRendering()
        {
            if (grassRenderer != null)
            {
                grassRenderer.drawMode = SpriteDrawMode.Tiled;
            }
        }
    }
}
