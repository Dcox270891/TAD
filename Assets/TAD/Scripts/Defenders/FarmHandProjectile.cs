using TAD.Attackers;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// Standard pooled projectile used by Unarmed throws and Air Rifle bullets.
    /// It travels on its launch direction until it hits an attacker or its timed lifetime ends.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmHandProjectile : FarmHandProjectileBase
    {
        private Vector2 travelDirection;
        private float travelSpeed;
        private float remainingLifetime;

        protected override ProjectileBehavior ExpectedProjectileBehavior => ProjectileBehavior.Direct;

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null)
            {
                return;
            }

            var nextPosition = Body.position + (travelDirection * travelSpeed * Time.fixedDeltaTime);
            if (IsAttackPathBlockedByWall(Body.position, nextPosition))
            {
                ReturnToPool();
                return;
            }

            Body.MovePosition(nextPosition);
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                ReturnToPool();
            }
        }

        protected override bool OnLaunched(Vector2 origin, BasicAttacker target)
        {
            var delta = target.CurrentPosition - origin;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            travelDirection = delta.normalized;
            travelSpeed = Definition.ProjectileSpeed;
            remainingLifetime = Definition.ProjectileLifetime;
            SetFacing(travelDirection);
            SetHitColliderEnabled(true);
            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var attacker = GetValidAttacker(other);
            if (attacker != null && TryApplyImpactEffects(attacker, transform.position, applyPrimaryDamage: true))
            {
                ReturnToPool();
            }
        }

        protected override void ResetSpecializedState()
        {
            travelDirection = Vector2.zero;
            travelSpeed = 0f;
            remainingLifetime = 0f;
        }
    }
}
