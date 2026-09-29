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
        [SerializeField, Min(0f)]
        [Tooltip("Short presentation-only delay that keeps a triggered trap visible for its detonation animation.")]
        private float detonationVisualDuration;
        private bool isDetonating;
        private float detonationCompleteTime;

        /// <summary>True once this pooled trap reaches its destination and can damage attackers.</summary>
        public bool IsArmed => isArmed;

        /// <summary>True during the short post-trigger visual window before the pooled trap is recycled.</summary>
        public bool IsDetonating => isDetonating;

        /// <summary>Raised when the trap changes between its travelling and armed visual states.</summary>
        public event System.Action<bool> ArmedStateChanged;

        /// <summary>Raised after the trap's gameplay impact has resolved so presentation can show an explosion.</summary>
        public event System.Action Detonated;

        protected override ProjectileBehavior ExpectedProjectileBehavior => ProjectileBehavior.Trap;

        private void FixedUpdate()
        {
            if (!IsLaunched || Body == null)
            {
                return;
            }

            if (isDetonating)
            {
                if (Time.time >= detonationCompleteTime)
                {
                    ReturnToPool();
                }

                return;
            }

            if (isArmed)
            {
                return;
            }

            var nextPosition = Vector2.MoveTowards(
                Body.position,
                placementPosition,
                travelSpeed * Time.fixedDeltaTime);
            if (IsAttackPathBlockedByWall(Body.position, nextPosition))
            {
                ReturnToPool();
                return;
            }

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
            isDetonating = false;
            detonationCompleteTime = 0f;
            SetHitColliderEnabled(false);
            ArmedStateChanged?.Invoke(false);

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
            ArmedStateChanged?.Invoke(true);
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
            if (!isArmed || isDetonating)
            {
                return;
            }

            var attacker = GetValidAttacker(other);
            if (attacker != null && TryApplyImpactEffects(attacker, placementPosition, applyPrimaryDamage: true))
            {
                BeginDetonation();
            }
        }

        private void BeginDetonation()
        {
            if (isDetonating)
            {
                return;
            }

            isArmed = false;
            isDetonating = true;
            SetHitColliderEnabled(false);
            if (Body != null)
            {
                Body.linearVelocity = Vector2.zero;
                Body.angularVelocity = 0f;
            }

            Detonated?.Invoke();
            if (detonationVisualDuration <= 0f)
            {
                ReturnToPool();
                return;
            }

            detonationCompleteTime = Time.time + detonationVisualDuration;
        }

        protected override void ResetSpecializedState()
        {
            placementPosition = Vector2.zero;
            travelSpeed = 0f;
            isArmed = false;
            isDetonating = false;
            detonationCompleteTime = 0f;
        }
    }
}
