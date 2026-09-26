using System.Collections.Generic;
using TAD.Attackers;
using UnityEngine;

namespace TAD.Defenders
{
    /// <summary>
    /// A single long melee pole that follows its farmhand and sweeps through a configurable 180-degree arc.
    /// Each attacker can be damaged once per sweep, while contact never removes the pole itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PoleSweepProjectile : FarmHandProjectileBase
    {
        private readonly HashSet<BasicAttacker> attackersDamaged = new();
        private Vector2 lastFacing;
        private float remainingLifetime;
        private float sweepDuration;

        protected override ProjectileBehavior ExpectedProjectileBehavior => ProjectileBehavior.PoleSweep;

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null)
            {
                return;
            }

            remainingLifetime -= Time.fixedDeltaTime;
            UpdateSweep(Mathf.Clamp01(1f - (remainingLifetime / sweepDuration)), false);
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

            lastFacing = targetDelta.normalized;
            remainingLifetime = Definition.ProjectileLifetime;
            sweepDuration = Definition.ProjectileLifetime;
            SetHitColliderEnabled(true);
            UpdateSweep(0f, true);
            return true;
        }

        private void UpdateSweep(float progress, bool setImmediately)
        {
            if (SourceFarmHand == null || Definition == null)
            {
                return;
            }

            if (IsValidTarget(Target))
            {
                var targetDelta = Target.CurrentPosition - (Vector2)SourceFarmHand.transform.position;
                if (targetDelta.sqrMagnitude > 0.0001f)
                {
                    lastFacing = targetDelta.normalized;
                }
            }

            var baseAngle = Mathf.Atan2(lastFacing.y, lastFacing.x) * Mathf.Rad2Deg;
            var sweepAngle = Definition.PoleSweepAngle;
            var swingAngle = baseAngle - (sweepAngle * 0.5f) + (sweepAngle * progress);
            var direction = new Vector2(
                Mathf.Cos(swingAngle * Mathf.Deg2Rad),
                Mathf.Sin(swingAngle * Mathf.Deg2Rad));
            var reach = Mathf.Max(0.01f, AttackRange);
            var position = (Vector2)SourceFarmHand.transform.position + (direction * (reach * 0.5f));

            transform.localScale = new Vector3(
                BaseLocalScale.x,
                BaseLocalScale.y * reach,
                BaseLocalScale.z);
            if (setImmediately)
            {
                Body.position = position;
            }
            else
            {
                Body.MovePosition(position);
            }

            SetFacing(direction);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryDamageAttackerOnce(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // A moving attacker can already overlap a pole when the sweep begins.
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
            lastFacing = Vector2.zero;
            remainingLifetime = 0f;
            sweepDuration = 0f;
        }
    }
}