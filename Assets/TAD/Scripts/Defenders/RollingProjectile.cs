using System.Collections.Generic;
using TAD.Attackers;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// A melee projectile that keeps its original firing direction, accelerating until it reaches its configured maximum speed.
    /// It can damage each attacker once and is only returned after its timed lifetime ends.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RollingProjectile : FarmHandProjectileBase
    {
        private readonly HashSet<BasicAttacker> attackersDamaged = new();
        private Vector2 travelDirection;
        private float travelSpeed;
        private float remainingLifetime;

        protected override WeaponProjectileBehavior ProjectileBehavior => WeaponProjectileBehavior.Rolling;

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null)
            {
                return;
            }

            travelSpeed = Mathf.MoveTowards(
                travelSpeed,
                Definition.ProjectileSpeed,
                Definition.RollingAcceleration * Time.fixedDeltaTime);
            Body.MovePosition(Body.position + (travelDirection * travelSpeed * Time.fixedDeltaTime));

            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime <= 0f)
            {
                ReturnToPool();
            }
        }

        protected override bool OnLaunched(Vector2 origin, BasicAttacker target)
        {
            if (!Definition.UsesTimedLifetime || Definition.ProjectileLifetime <= 0f)
            {
                return false;
            }

            var targetDelta = target.CurrentPosition - origin;
            if (targetDelta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            travelDirection = targetDelta.normalized;
            travelSpeed = Definition.ProjectileSpeed * Definition.InitialSpeedMultiplier;
            remainingLifetime = Definition.ProjectileLifetime;
            SetFacing(travelDirection);
            SetHitColliderEnabled(true);
            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryDamageAttackerOnce(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryDamageAttackerOnce(other);
        }

        private void TryDamageAttackerOnce(Collider2D other)
        {
            var attacker = GetValidAttacker(other);
            if (attacker != null && attackersDamaged.Add(attacker))
            {
                TryDamageAttacker(attacker);
            }
        }

        protected override void ResetSpecializedState()
        {
            attackersDamaged.Clear();
            travelDirection = Vector2.zero;
            travelSpeed = 0f;
            remainingLifetime = 0f;
        }
    }
}