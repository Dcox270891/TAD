using TAD.Attackers;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// A ranged ground trap that travels to its selected in-range placement point, then waits until an attacker activates it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrapProjectile : FarmHandProjectileBase
    {
        private Vector2 placementPosition;
        private float travelSpeed;
        private bool isArmed;

        protected override ProjectileBehavior ExpectedProjectileBehavior => ProjectileBehavior.Trap;

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null || isArmed)
            {
                return;
            }

            var nextPosition = Vector2.MoveTowards(
                Body.position,
                placementPosition,
                travelSpeed * Time.fixedDeltaTime);
            Body.MovePosition(nextPosition);
            if ((nextPosition - placementPosition).sqrMagnitude <= 0.0001f)
            {
                ArmTrap();
            }
        }

        protected override bool OnLaunched(Vector2 origin, BasicAttacker target)
        {
            if (Definition.ProjectileLifetimeMode != ProjectileLifetimeMode.UntilActivated)
            {
                return false;
            }

            var targetDelta = target.CurrentPosition - origin;
            placementPosition = target.CurrentPosition;
            travelSpeed = Definition.ProjectileSpeed;
            isArmed = false;
            SetHitColliderEnabled(false);

            if (targetDelta.sqrMagnitude > 0.0001f)
            {
                SetFacing(targetDelta.normalized);
                return true;
            }

            Body.position = placementPosition;
            ArmTrap();
            return true;
        }

        private void ArmTrap()
        {
            if (isArmed)
            {
                return;
            }

            isArmed = true;
            if (Body != null)
            {
                Body.position = placementPosition;
            }

            SetHitColliderEnabled(true);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryActivate(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Covers a chicken that was already standing on the placement point when the trap arms.
            TryActivate(other);
        }

        private void TryActivate(Collider2D other)
        {
            if (!isArmed)
            {
                return;
            }

            var attacker = GetValidAttacker(other);
            if (attacker != null && TryDamageAttacker(attacker))
            {
                ReturnToPool();
            }
        }

        protected override void ResetSpecializedState()
        {
            placementPosition = Vector2.zero;
            travelSpeed = 0f;
            isArmed = false;
        }
    }
}