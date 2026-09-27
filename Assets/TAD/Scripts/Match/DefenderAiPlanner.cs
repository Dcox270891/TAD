using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using UnityEngine;

namespace TAD.Match
{
    /// <summary>
    /// Immutable snapshot of one attacker group that is waiting in the next wave. The route is copied
    /// when the plan is built so defender decisions never mutate FarmRouteMapper's cached path data.
    /// </summary>
    public readonly struct DefenderAiRoutePlan
    {
        public DefenderAiRoutePlan(
            AttackerDefinition definition,
            int attackerCount,
            IReadOnlyList<Vector3> routePoints)
        {
            Definition = definition;
            AttackerCount = Mathf.Max(0, attackerCount);
            if (definition == null || routePoints == null || routePoints.Count < 2)
            {
                RoutePoints = Array.Empty<Vector2>();
                RouteLength = 0f;
                return;
            }

            var copiedRoute = new Vector2[routePoints.Count];
            var routeLength = 0f;
            for (var index = 0; index < routePoints.Count; index++)
            {
                copiedRoute[index] = routePoints[index];
                if (index > 0)
                {
                    routeLength += Vector2.Distance(copiedRoute[index - 1], copiedRoute[index]);
                }
            }

            RoutePoints = copiedRoute;
            RouteLength = routeLength;
        }

        public AttackerDefinition Definition { get; }
        public int AttackerCount { get; }
        public IReadOnlyList<Vector2> RoutePoints { get; }
        public float RouteLength { get; }

        public bool HasUsableRoute =>
            Definition != null &&
            AttackerCount > 0 &&
            RoutePoints != null &&
            RoutePoints.Count >= 2 &&
            RouteLength > 0.001f;
    }

    /// <summary>Small runtime snapshot used to avoid stacking AI farmhands over existing coverage.</summary>
    public readonly struct DefenderAiCoverage
    {
        public DefenderAiCoverage(Vector2 position, float range)
        {
            Position = position;
            Range = Mathf.Max(0f, range);
        }

        public Vector2 Position { get; }
        public float Range { get; }
    }

    /// <summary>The map position selected by the route-aware defender placement policy.</summary>
    public readonly struct DefenderAiPlacementDecision
    {
        public DefenderAiPlacementDecision(Vector2 position, float score)
        {
            Position = position;
            Score = score;
            IsValid = true;
        }

        public Vector2 Position { get; }
        public float Score { get; }
        public bool IsValid { get; }
    }

    /// <summary>The legal weapon selected for a farmhand's permanent level-three upgrade.</summary>
    public readonly struct DefenderAiWeaponDecision
    {
        public DefenderAiWeaponDecision(WeaponDefinition weapon, float score, string reason)
        {
            Weapon = weapon;
            Score = score;
            Reason = reason ?? string.Empty;
        }

        public WeaponDefinition Weapon { get; }
        public float Score { get; }
        public string Reason { get; }
        public bool IsValid => Weapon != null;
    }

    /// <summary>
    /// Deterministic, data-driven defender policy. It measures coverage against the routes that are
    /// already queued for the next wave, so future AI profiles can alter only this policy while the
    /// existing pool, bank, placement, and level-three purchase APIs remain authoritative.
    /// </summary>
    public static class DefenderAiPlanner
    {
        private const float MinimumRouteLength = 0.001f;
        private const float PlacementTieTolerance = 0.001f;
        private const float FarmHandPressurePerUnit = 100f;

        /// <summary>
        /// Estimates how many farmhands are useful for the planned groups, constrained by the caller's
        /// normal level cap. A stronger, faster, or cover-skipping group creates more pressure than
        /// the same number of basic animals.
        /// </summary>
        public static int GetSuggestedFarmHandCount(
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes,
            int maximumFarmHands)
        {
            if (plannedRoutes == null || plannedRoutes.Count == 0 || maximumFarmHands <= 0)
            {
                return 0;
            }

            var totalPressure = 0f;
            for (var index = 0; index < plannedRoutes.Count; index++)
            {
                totalPressure += GetRoutePressure(plannedRoutes[index]);
            }

            if (totalPressure <= 0f)
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.CeilToInt(totalPressure / FarmHandPressurePerUnit),
                1,
                Mathf.Max(1, maximumFarmHands));
        }

        /// <summary>
        /// Selects a visible, collision-free candidate that covers the greatest amount of incoming
        /// route pressure while penalizing locations already protected by a farmhand.
        /// </summary>
        public static bool TryChoosePlacement(
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes,
            IReadOnlyList<Vector2> candidates,
            IReadOnlyList<DefenderAiCoverage> existingCoverage,
            float farmHandRange,
            out DefenderAiPlacementDecision decision)
        {
            decision = default;
            if (plannedRoutes == null || plannedRoutes.Count == 0 ||
                candidates == null || candidates.Count == 0 || farmHandRange <= 0f)
            {
                return false;
            }

            var bestIndex = -1;
            var bestScore = 0f;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var score = ScorePlacementCandidate(
                    candidate,
                    plannedRoutes,
                    existingCoverage,
                    farmHandRange);

                if (score > bestScore + PlacementTieTolerance ||
                    (Mathf.Abs(score - bestScore) <= PlacementTieTolerance &&
                     IsPlacementTieBreakWinner(candidate, bestIndex >= 0 ? candidates[bestIndex] : default)))
                {
                    bestIndex = index;
                    bestScore = score;
                }
            }

            if (bestIndex < 0 || bestScore <= 0f)
            {
                return false;
            }

            decision = new DefenderAiPlacementDecision(candidates[bestIndex], bestScore);
            return true;
        }

        /// <summary>
        /// Chooses the most effective affordable weapon against the current planned routes. The
        /// route distance within each weapon's range is combined with DPS, attacker archetype, cover
        /// permissions, and the projectile's area/behavioural strengths.
        /// </summary>
        public static bool TryChooseWeapon(
            Vector2 farmHandPosition,
            float progressionDamageMultiplier,
            IReadOnlyList<WeaponDefinition> affordableOptions,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes,
            out DefenderAiWeaponDecision decision)
        {
            decision = default;
            if (affordableOptions == null || affordableOptions.Count == 0 ||
                plannedRoutes == null || plannedRoutes.Count == 0)
            {
                return false;
            }

            var bestWeapon = default(WeaponDefinition);
            var bestScore = 0f;
            for (var index = 0; index < affordableOptions.Count; index++)
            {
                var weapon = affordableOptions[index];
                if (!IsUsableWeapon(weapon))
                {
                    continue;
                }

                var score = ScoreWeapon(
                    weapon,
                    farmHandPosition,
                    Mathf.Max(0.01f, progressionDamageMultiplier),
                    plannedRoutes);
                if (score > bestScore + PlacementTieTolerance ||
                    (Mathf.Abs(score - bestScore) <= PlacementTieTolerance &&
                     WinsWeaponTieBreak(weapon, bestWeapon)))
                {
                    bestWeapon = weapon;
                    bestScore = score;
                }
            }

            if (bestWeapon == null || bestScore <= 0f)
            {
                return false;
            }

            decision = new DefenderAiWeaponDecision(
                bestWeapon,
                bestScore,
                BuildWeaponReason(bestWeapon, plannedRoutes));
            return true;
        }

        private static float ScorePlacementCandidate(
            Vector2 candidate,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes,
            IReadOnlyList<DefenderAiCoverage> existingCoverage,
            float farmHandRange)
        {
            var score = 0f;
            for (var index = 0; index < plannedRoutes.Count; index++)
            {
                var route = plannedRoutes[index];
                if (!route.HasUsableRoute)
                {
                    continue;
                }

                var coveredDistance = GetRouteDistanceInsideRange(route.RoutePoints, candidate, farmHandRange);
                if (coveredDistance <= 0f)
                {
                    continue;
                }

                score += GetRoutePressure(route) * (coveredDistance / Mathf.Max(MinimumRouteLength, route.RouteLength));
            }

            return score * GetExistingCoverageMultiplier(candidate, existingCoverage, farmHandRange);
        }

        private static float ScoreWeapon(
            WeaponDefinition weapon,
            Vector2 farmHandPosition,
            float progressionDamageMultiplier,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes)
        {
            var projectile = weapon.ProjectileDefinition;
            var projectileTypeMultiplier = projectile != null && projectile.ProjectileType == ProjectileType.Ranged
                ? 1.12f
                : 0.90f;
            var damagePerSecond = weapon.Damage * progressionDamageMultiplier * weapon.AttackSpeed;
            var totalScore = 0f;

            for (var routeIndex = 0; routeIndex < plannedRoutes.Count; routeIndex++)
            {
                var route = plannedRoutes[routeIndex];
                if (!route.HasUsableRoute || route.Definition.MovementSpeed <= 0f)
                {
                    continue;
                }

                var distanceInRange = GetRouteDistanceInsideRange(
                    route.RoutePoints,
                    farmHandPosition,
                    weapon.Range);
                if (distanceInRange <= 0f)
                {
                    continue;
                }

                var timeInRange = distanceInRange / Mathf.Max(0.01f, route.Definition.MovementSpeed);
                var damagePotential = damagePerSecond * timeInRange * route.AttackerCount;
                var archetypeMultiplier = GetWeaponArchetypeMultiplier(
                    weapon,
                    projectile,
                    route.Definition);
                var behaviorMultiplier = GetBehaviorMultiplier(projectile, route.AttackerCount);
                totalScore += damagePotential * archetypeMultiplier * behaviorMultiplier * projectileTypeMultiplier;
            }

            // The bank check occurs before this policy is called. Dividing by square-root cost keeps
            // spending efficient without making a cheap low-impact weapon win over a clear counter.
            return totalScore / Mathf.Sqrt(Mathf.Max(1f, weapon.Cost));
        }

        private static float GetRoutePressure(DefenderAiRoutePlan route)
        {
            if (!route.HasUsableRoute || route.Definition == null)
            {
                return 0f;
            }

            var definition = route.Definition;
            var speedMultiplier = 1f + Mathf.Clamp(definition.MovementSpeed * 0.06f, 0f, 0.5f);
            var tierMultiplier = 1f + ((Mathf.Max(1, definition.ChallengeTier) - 1) * 0.10f);
            var archetypeMultiplier = definition.Archetype switch
            {
                AttackerArchetype.Fast => 1.15f,
                AttackerArchetype.Strong => 1.35f,
                AttackerArchetype.Flying => 1.20f,
                _ => 1f
            };

            return route.AttackerCount *
                   Mathf.Max(0f, definition.MaximumHealth) *
                   speedMultiplier *
                   tierMultiplier *
                   archetypeMultiplier;
        }

        private static float GetExistingCoverageMultiplier(
            Vector2 candidate,
            IReadOnlyList<DefenderAiCoverage> existingCoverage,
            float farmHandRange)
        {
            if (existingCoverage == null || existingCoverage.Count == 0)
            {
                return 1f;
            }

            var multiplier = 1f;
            for (var index = 0; index < existingCoverage.Count; index++)
            {
                var coverage = existingCoverage[index];
                if (coverage.Range <= 0f)
                {
                    continue;
                }

                var combinedRange = Mathf.Max(0.01f, farmHandRange + coverage.Range);
                var overlap = Mathf.Clamp01(1f - (Vector2.Distance(candidate, coverage.Position) / combinedRange));
                multiplier *= Mathf.Lerp(1f, 0.25f, overlap);
            }

            return Mathf.Max(0.05f, multiplier);
        }

        private static float GetWeaponArchetypeMultiplier(
            WeaponDefinition weapon,
            ProjectileDefinition projectile,
            AttackerDefinition definition)
        {
            if (weapon == null || definition == null)
            {
                return 0f;
            }

            var projectileType = projectile != null ? projectile.ProjectileType : ProjectileType.Melee;
            return definition.Archetype switch
            {
                AttackerArchetype.Fast => 1f + Mathf.Clamp(weapon.AttackSpeed * 0.08f, 0f, 0.25f),
                AttackerArchetype.Strong => 1f + Mathf.Clamp(weapon.Damage * 0.02f, 0f, 0.35f),
                AttackerArchetype.Flying => projectileType == ProjectileType.Ranged ? 1.20f : 0.80f,
                _ => 1f
            };
        }

        private static float GetBehaviorMultiplier(ProjectileDefinition projectile, int attackerCount)
        {
            if (projectile == null)
            {
                return 1f;
            }

            return projectile.ProjectileBehavior switch
            {
                ProjectileBehavior.PoleSweep => 1f + Mathf.Min(0.35f, attackerCount * 0.06f),
                ProjectileBehavior.Rolling => 1f + Mathf.Min(0.25f, attackerCount * 0.05f),
                ProjectileBehavior.Trap => attackerCount > 1 ? 1.20f : 0.85f,
                _ => 1f
            };
        }

        private static float GetRouteDistanceInsideRange(
            IReadOnlyList<Vector2> routePoints,
            Vector2 centre,
            float range)
        {
            if (routePoints == null || routePoints.Count < 2 || range <= 0f)
            {
                return 0f;
            }

            var distance = 0f;
            for (var pointIndex = 1; pointIndex < routePoints.Count; pointIndex++)
            {
                distance += GetSegmentLengthInsideRange(
                    routePoints[pointIndex - 1],
                    routePoints[pointIndex],
                    centre,
                    range);
            }

            return distance;
        }

        private static float GetSegmentLengthInsideRange(
            Vector2 start,
            Vector2 end,
            Vector2 centre,
            float radius)
        {
            var segment = end - start;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= 0.000001f || radius <= 0f)
            {
                return 0f;
            }

            var closestT = Mathf.Clamp01(Vector2.Dot(centre - start, segment) / segmentLengthSquared);
            var closestPoint = start + (segment * closestT);
            var radiusSquared = radius * radius;
            var closestDistanceSquared = (centre - closestPoint).sqrMagnitude;
            if (closestDistanceSquared >= radiusSquared)
            {
                return 0f;
            }

            var halfSpan = Mathf.Sqrt((radiusSquared - closestDistanceSquared) / segmentLengthSquared);
            var enterT = Mathf.Clamp01(closestT - halfSpan);
            var exitT = Mathf.Clamp01(closestT + halfSpan);
            return segment.magnitude * Mathf.Max(0f, exitT - enterT);
        }

        private static bool IsUsableWeapon(WeaponDefinition weapon)
        {
            return weapon != null &&
                   weapon.Range > 0f &&
                   weapon.Damage > 0f &&
                   weapon.AttackSpeed > 0f;
        }

        private static bool IsPlacementTieBreakWinner(Vector2 contender, Vector2 incumbent)
        {
            if (contender.x < incumbent.x - PlacementTieTolerance)
            {
                return true;
            }

            return Mathf.Abs(contender.x - incumbent.x) <= PlacementTieTolerance &&
                   contender.y < incumbent.y;
        }

        private static bool WinsWeaponTieBreak(WeaponDefinition contender, WeaponDefinition incumbent)
        {
            if (incumbent == null)
            {
                return true;
            }

            if (contender.Cost != incumbent.Cost)
            {
                return contender.Cost < incumbent.Cost;
            }

            return string.CompareOrdinal(contender.DisplayName, incumbent.DisplayName) < 0;
        }

        private static string BuildWeaponReason(
            WeaponDefinition weapon,
            IReadOnlyList<DefenderAiRoutePlan> plannedRoutes)
        {
            var hasFlying = false;
            var hasStrong = false;
            var highestGroupCount = 0;
            for (var index = 0; index < plannedRoutes.Count; index++)
            {
                var definition = plannedRoutes[index].Definition;
                if (definition == null)
                {
                    continue;
                }

                hasFlying |= definition.Archetype == AttackerArchetype.Flying;
                hasStrong |= definition.Archetype == AttackerArchetype.Strong;
                highestGroupCount = Mathf.Max(highestGroupCount, plannedRoutes[index].AttackerCount);
            }

            var projectile = weapon != null ? weapon.ProjectileDefinition : null;
            if (hasFlying && projectile != null && projectile.ProjectileType == ProjectileType.Ranged)
            {
                return "ranged coverage best answers the queued flying route";
            }

            if (hasStrong && weapon != null && weapon.Damage >= 15f)
            {
                return "higher damage best answers the queued strong attackers";
            }

            if (highestGroupCount > 1 && projectile != null &&
                projectile.ProjectileBehavior != ProjectileBehavior.Direct)
            {
                return "its projectile behaviour best covers the queued group size";
            }

            return "best route coverage and damage per defender resource";
        }
    }
}
