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
        private Vector2 intendedTravelDirection;
        private float travelSpeed;
        private float remainingLifetime;
        private float remainingDistanceToBounce;
        private int completedBounceCount;
        private bool usesBounceBehaviour;

        protected override ProjectileBehavior ExpectedProjectileBehavior => ProjectileBehavior.Rolling;

        protected override bool SupportsProjectileBehavior(ProjectileBehavior projectileBehavior)
        {
            // The same pooled rolling visual is intentionally reused for the Bounce final evolution.
            return projectileBehavior == ProjectileBehavior.Rolling || projectileBehavior == ProjectileBehavior.Bouncing;
        }

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null)
            {
                return;
            }

            var acceleration = Definition.RollingAcceleration;
            travelSpeed = acceleration > 0f
                ? Mathf.MoveTowards(
                    travelSpeed,
                    Definition.ProjectileSpeed,
                    acceleration * Time.fixedDeltaTime)
                : Definition.ProjectileSpeed;
            var moveDistance = travelSpeed * Time.fixedDeltaTime;
            var nextPosition = Body.position + (travelDirection * moveDistance);
            if (IsAttackPathBlockedByWall(Body.position, nextPosition))
            {
                ReturnToPool();
                return;
            }

            Body.MovePosition(nextPosition);

            if (usesBounceBehaviour && !UpdateBounceImpacts(moveDistance, nextPosition))
            {
                return;
            }

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
            intendedTravelDirection = travelDirection;
            travelSpeed = Definition.ProjectileSpeed * Definition.InitialSpeedMultiplier;
            remainingLifetime = Definition.ProjectileLifetime;
            usesBounceBehaviour = Definition.ProjectileBehavior == ProjectileBehavior.Bouncing;
            remainingDistanceToBounce = Definition.BounceDistance;
            completedBounceCount = 0;
            SetFacing(travelDirection);
            // Bounce damage occurs when the projectile hits the ground. A trigger hit between
            // ground impacts must not make it disappear or apply an unintended direct hit.
            SetHitColliderEnabled(!usesBounceBehaviour);
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
            if (usesBounceBehaviour)
            {
                return;
            }

            var attacker = GetValidAttacker(other);
            if (attacker != null && attackersDamaged.Add(attacker))
            {
                TryApplyImpactEffects(attacker, transform.position, applyPrimaryDamage: true);
            }
        }

        /// <summary>Returns false when the configured final bounce has completed and the pool owns the projectile again.</summary>
        private bool UpdateBounceImpacts(float moveDistance, Vector2 impactPosition)
        {
            remainingDistanceToBounce -= moveDistance;
            while (remainingDistanceToBounce <= 0f)
            {
                TryApplyAreaImpact(impactPosition);
                completedBounceCount++;
                if (completedBounceCount >= Definition.MaximumBounceCount)
                {
                    ReturnToPool();
                    return false;
                }

                remainingDistanceToBounce += Definition.BounceDistance;
                var directionSign = (completedBounceCount & 1) == 0 ? 1f : -1f;
                travelDirection = Rotate(intendedTravelDirection, Definition.BounceAngle * directionSign);
                SetFacing(travelDirection);
            }

            return true;
        }

        private static Vector2 Rotate(Vector2 direction, float angleDegrees)
        {
            var radians = angleDegrees * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(radians);
            var sine = Mathf.Sin(radians);
            return new Vector2(
                (direction.x * cosine) - (direction.y * sine),
                (direction.x * sine) + (direction.y * cosine)).normalized;
        }

        protected override void ResetSpecializedState()
        {
            attackersDamaged.Clear();
            travelDirection = Vector2.zero;
            intendedTravelDirection = Vector2.zero;
            travelSpeed = 0f;
            remainingLifetime = 0f;
            remainingDistanceToBounce = 0f;
            completedBounceCount = 0;
            usesBounceBehaviour = false;
        }
    }
}
