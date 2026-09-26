using System;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Reusable 16-direction, two-frame visual data for one cosmetic layer of a farmhand.
    /// Body, hair, and outfit assets each use this same layout so they can be mixed freely.
    /// </summary>
    public abstract class FarmHandDirectionalLayerDefinition : ScriptableObject
    {
        public const int DirectionCount = 16;
        public const int MovementFrameCount = 2;

        [SerializeField] private Sprite[] movementSprites = new Sprite[DirectionCount * MovementFrameCount];
        [SerializeField, Min(0.01f)] private float movementFrameDuration = 0.16f;

        public float MovementFrameDuration => Mathf.Max(0.01f, movementFrameDuration);

        public bool HasMovementSprites
        {
            get
            {
                return movementSprites != null &&
                       movementSprites.Length >= DirectionCount * MovementFrameCount &&
                       movementSprites[0] != null;
            }
        }

        public Sprite GetMovementSprite(int directionIndex, int frameIndex)
        {
            if (movementSprites == null || movementSprites.Length == 0)
            {
                return null;
            }

            directionIndex = WrapDirectionIndex(directionIndex);
            frameIndex = Mathf.Clamp(frameIndex, 0, MovementFrameCount - 1);
            var index = (directionIndex * MovementFrameCount) + frameIndex;
            return index >= 0 && index < movementSprites.Length ? movementSprites[index] : null;
        }

        protected virtual void OnValidate()
        {
            if (movementSprites == null || movementSprites.Length != DirectionCount * MovementFrameCount)
            {
                Array.Resize(ref movementSprites, DirectionCount * MovementFrameCount);
            }

            movementFrameDuration = Mathf.Max(0.01f, movementFrameDuration);
        }

        private static int WrapDirectionIndex(int directionIndex)
        {
            return (directionIndex % DirectionCount + DirectionCount) % DirectionCount;
        }
    }
}
