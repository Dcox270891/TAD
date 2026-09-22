using UnityEngine;

namespace TAD.Environment
{
    public enum FarmWallCover
    {
        Half,
        Full
    }

    /// <summary>
    /// Describes the tactical cover and passable opening of one reusable farm wall.
    /// Pathfinding queries this component rather than inferring rules from wall art or names.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class FarmWall : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Half cover allows a farmhand's ranged weapon or trap to attack across it. Full cover blocks all attacks across it.")]
        private FarmWallCover cover = FarmWallCover.Full;

        [SerializeField]
        [Tooltip("Whether the wall has an opening that an animal may use for navigation.")]
        private bool hasHole;

        [SerializeField, Min(0f)]
        [Tooltip("Width of the wall opening in world units. An animal can use it only when its diameter is no greater than this value.")]
        private float holeSize;

        public FarmWallCover Cover => cover;

        public bool HasHole => hasHole;

        public float HoleSize => hasHole ? holeSize : 0f;

        public bool BlocksAllAttacks => cover == FarmWallCover.Full;

        public bool AllowsFarmhandRangedAttack => cover == FarmWallCover.Half;

        public bool CanAnimalPassThrough(float animalDiameter)
        {
            return hasHole && animalDiameter >= 0f && animalDiameter <= holeSize;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            holeSize = Mathf.Max(0f, holeSize);
        }
#endif
    }
}
