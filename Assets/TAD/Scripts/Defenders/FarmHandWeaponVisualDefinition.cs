using System;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Directional held-weapon and firing artwork tied to one reusable gameplay WeaponDefinition.
    /// The weapon data remains authoritative for combat; this asset changes presentation only.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Defenders/Visuals/Farmhand Weapon Visual", fileName = "FarmHandWeaponVisual")]
    public sealed class FarmHandWeaponVisualDefinition : ScriptableObject
    {
        public const int DirectionCount = 16;
        public const int MovementFrameCount = 2;
        public const int FiringFrameCount = 3;

        [SerializeField] private WeaponDefinition weapon;
        [SerializeField] private Sprite[] movementSprites = new Sprite[DirectionCount * MovementFrameCount];
        [SerializeField] private Sprite[] firingSprites = new Sprite[DirectionCount * FiringFrameCount];
        [SerializeField, Min(0.01f)] private float firingFrameDuration = 0.09f;

        public WeaponDefinition Weapon => weapon;
        public float FiringFrameDuration => Mathf.Max(0.01f, firingFrameDuration);

        public bool HasMovementSprites
        {
            get
            {
                return movementSprites != null &&
                       movementSprites.Length >= DirectionCount * MovementFrameCount &&
                       movementSprites[0] != null;
            }
        }

        public bool HasFiringSprites
        {
            get
            {
                return firingSprites != null &&
                       firingSprites.Length >= DirectionCount * FiringFrameCount &&
                       firingSprites[0] != null;
            }
        }

        public Sprite GetMovementSprite(int directionIndex, int frameIndex)
        {
            return GetSprite(movementSprites, directionIndex, frameIndex, MovementFrameCount);
        }

        public Sprite GetFiringSprite(int directionIndex, int frameIndex)
        {
            return GetSprite(firingSprites, directionIndex, frameIndex, FiringFrameCount);
        }

        private static Sprite GetSprite(Sprite[] sprites, int directionIndex, int frameIndex, int frameCount)
        {
            if (sprites == null || sprites.Length == 0)
            {
                return null;
            }

            directionIndex = (directionIndex % DirectionCount + DirectionCount) % DirectionCount;
            frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);
            var index = (directionIndex * frameCount) + frameIndex;
            return index >= 0 && index < sprites.Length ? sprites[index] : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (movementSprites == null || movementSprites.Length != DirectionCount * MovementFrameCount)
            {
                Array.Resize(ref movementSprites, DirectionCount * MovementFrameCount);
            }

            if (firingSprites == null || firingSprites.Length != DirectionCount * FiringFrameCount)
            {
                Array.Resize(ref firingSprites, DirectionCount * FiringFrameCount);
            }

            firingFrameDuration = Mathf.Max(0.01f, firingFrameDuration);
        }
#endif
    }
}
