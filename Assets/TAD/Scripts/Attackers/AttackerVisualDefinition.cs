using System;
using UnityEngine;

namespace TAD.Attackers
{
    /// <summary>
    /// Reusable 16-direction visual data for one attacker type. Sprites are stored direction-major:
    /// all movement frames for one direction, then all death frames for that same direction.
    /// </summary>
    [CreateAssetMenu(menuName = "TAD/Attackers/Attacker Visual Definition", fileName = "AttackerVisualDefinition")]
    public sealed class AttackerVisualDefinition : ScriptableObject
    {
        public const int DirectionCount = 16;
        public const int MovementFrameCount = 2;
        public const int DeathFrameCount = 4;

        [SerializeField] private Sprite[] movementSprites = new Sprite[DirectionCount * MovementFrameCount];
        [SerializeField] private Sprite[] deathSprites = new Sprite[DirectionCount * DeathFrameCount];
        [SerializeField, Min(0.01f)] private float movementFrameDuration = 0.16f;
        [SerializeField, Min(0.01f)] private float deathFrameDuration = 0.12f;

        public float MovementFrameDuration => movementFrameDuration;
        public float DeathFrameDuration => deathFrameDuration;
        public bool HasMovementSprites => HasCompleteSprites(movementSprites, DirectionCount * MovementFrameCount);
        public bool HasDeathSprites => HasCompleteSprites(deathSprites, DirectionCount * DeathFrameCount);

        public Sprite GetMovementSprite(int directionIndex, int frameIndex)
        {
            return GetSprite(movementSprites, DirectionCount, MovementFrameCount, directionIndex, frameIndex);
        }

        public Sprite GetDeathSprite(int directionIndex, int frameIndex)
        {
            return GetSprite(deathSprites, DirectionCount, DeathFrameCount, directionIndex, frameIndex);
        }

        private static Sprite GetSprite(
            Sprite[] sprites,
            int directionCount,
            int frameCount,
            int directionIndex,
            int frameIndex)
        {
            if (sprites == null || sprites.Length < directionCount * frameCount)
            {
                return null;
            }

            var safeDirection = ((directionIndex % directionCount) + directionCount) % directionCount;
            var safeFrame = ((frameIndex % frameCount) + frameCount) % frameCount;
            return sprites[(safeDirection * frameCount) + safeFrame];
        }

        private static bool HasCompleteSprites(Sprite[] sprites, int requiredCount)
        {
            if (sprites == null || sprites.Length < requiredCount)
            {
                return false;
            }

            for (var index = 0; index < requiredCount; index++)
            {
                if (sprites[index] == null)
                {
                    return false;
                }
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            movementFrameDuration = Mathf.Max(0.01f, movementFrameDuration);
            deathFrameDuration = Mathf.Max(0.01f, deathFrameDuration);

            if (movementSprites == null || movementSprites.Length != DirectionCount * MovementFrameCount)
            {
                Array.Resize(ref movementSprites, DirectionCount * MovementFrameCount);
            }

            if (deathSprites == null || deathSprites.Length != DirectionCount * DeathFrameCount)
            {
                Array.Resize(ref deathSprites, DirectionCount * DeathFrameCount);
            }
        }
#endif
    }
}
