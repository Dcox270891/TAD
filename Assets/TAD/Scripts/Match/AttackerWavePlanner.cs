using System;
using System.Collections.Generic;
using TAD.Attackers;
using TAD.Defenders;
using UnityEngine;

namespace TAD.Match
{
    /// <summary>
    /// Immutable route data used by the attacker AI when it compares the currently affordable
    /// animal groups. Keeping this separate from the scene lets future AI difficulty profiles
    /// adjust the scoring policy without changing the wave queue or attacker data.
    /// </summary>
    public readonly struct AttackerAiCandidate
    {
        public AttackerAiCandidate(AttackerDefinition definition, IReadOnlyList<Vector3> routePoints)
        {
            Definition = definition;
            if (routePoints == null || routePoints.Count < 2)
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
        public IReadOnlyList<Vector2> RoutePoints { get; }
        public float RouteLength { get; }
        public bool HasUsableRoute => Definition != null && RoutePoints != null && RoutePoints.Count >= 2 && RouteLength > 0.001f;
    }

    /// <summary>Snapshot of one active farmhand's practical damage threat for one planned wave.</summary>
    public readonly struct AttackerAiDefenderThreat
    {
        public AttackerAiDefenderThreat(
            Vector2 position,
            float range,
            float damagePerSecond,
            ProjectileType projectileType,
            ProjectileBehavior projectileBehavior)
        {
            Position = position;
            Range = Mathf.Max(0f, range);
            DamagePerSecond = Mathf.Max(0f, damagePerSecond);
            ProjectileType = projectileType;
            ProjectileBehavior = projectileBehavior;
        }

        public Vector2 Position { get; }
        public float Range { get; }
        public float DamagePerSecond { get; }
        public ProjectileType ProjectileType { get; }
        public ProjectileBehavior ProjectileBehavior { get; }
    }

    /// <summary>The one group selected by the attacker AI for the normal queue API.</summary>
    public readonly struct AttackerAiDecision
    {
        public AttackerAiDecision(AttackerDefinition definition, float score, string reason)
        {
            Definition = definition;
            Score = score;
            Reason = reason ?? string.Empty;
        }

        public AttackerDefinition Definition { get; }
        public float Score { get; }
        public string Reason { get; }
        public bool IsValid => Definition != null;
    }

    /// <summary>
    /// Chooses an affordable attacker group by estimating how much of its cached route lies inside
    /// the current farmhands' ranges. It is deliberately a deterministic heuristic rather than a
    /// combat simulation: the queue remains authoritative and future difficulty levels can replace
    /// only this policy while retaining the same data-driven roster and economy integration.
    /// </summary>
    public static class AttackerWavePlanner
    {
        private const float MinimumRemainingHealthFraction = 0.05f;
        private const float ImmediateRepeatPenalty = 0.20f;
        private const float EarlierRepeatPenalty = 0.70f;
        private const float ImmediateArchetypePenalty = 0.70f;
        private const float EarlierArchetypePenalty = 0.90f;

        public static bool TryChoose(
            IReadOnlyList<AttackerAiCandidate> candidates,
            IReadOnlyList<AttackerAiDefenderThreat> defenderThreats,
            IReadOnlyList<AttackerDefinition> recentSelections,
            int day,
            int groupIndex,
            out AttackerAiDecision decision)
        {
            decision = default;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            var mostRecent = recentSelections != null && recentSelections.Count > 0
                ? recentSelections[0]
                : null;
            var canAvoidImmediateRepeat = mostRecent != null && HasDifferentDefinition(candidates, mostRecent);

            var bestScore = float.NegativeInfinity;
            var bestCandidate = default(AttackerAiCandidate);
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var definition = candidate.Definition;
                if (!candidate.HasUsableRoute || definition == null || definition.MaximumHealth <= 0f ||
                    definition.MovementSpeed <= 0f || definition.SendSize <= 0)
                {
                    continue;
                }

                // Visible variety matters while testing and prevents the planner from getting stuck
                // on one nominally efficient card. Repeating remains possible when it is genuinely
                // the only affordable path-valid choice.
                if (canAvoidImmediateRepeat && IsSameDefinition(definition, mostRecent))
                {
                    continue;
                }

                var score = ScoreCandidate(candidate, defenderThreats, recentSelections);
                if (score > bestScore + 0.001f ||
                    (Mathf.Abs(score - bestScore) <= 0.001f && WinsTieBreak(definition, bestCandidate.Definition, day, groupIndex)))
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            if (!bestCandidate.HasUsableRoute || bestCandidate.Definition == null)
            {
                return false;
            }

            decision = new AttackerAiDecision(
                bestCandidate.Definition,
                bestScore,
                BuildReason(bestCandidate, defenderThreats));
            return true;
        }

        private static float ScoreCandidate(
            AttackerAiCandidate candidate,
            IReadOnlyList<AttackerAiDefenderThreat> defenderThreats,
            IReadOnlyList<AttackerDefinition> recentSelections)
        {
            var definition = candidate.Definition;
            var expectedDamage = EstimateDamageTaken(candidate, defenderThreats);
            var minimumRemainingHealth = definition.MaximumHealth * MinimumRemainingHealthFraction;
            var expectedRemainingHealth = Mathf.Max(minimumRemainingHealth, definition.MaximumHealth - expectedDamage);
            var groupBreakthrough = expectedRemainingHealth * definition.SendSize;
            var speedBonus = 1f + Mathf.Clamp(definition.MovementSpeed * 0.05f, 0f, 0.45f);
            var incomeValue = definition.WaveIncome * 2f;
            var tierValue = definition.ChallengeTier * 0.35f;
            var counterValue = GetArchetypeCounterValue(candidate, expectedDamage, defenderThreats);
            var cost = Mathf.Max(1, definition.SendCost);
            var baseScore = ((groupBreakthrough * speedBonus) + incomeValue + tierValue + counterValue) / cost;

            return baseScore * GetVarietyMultiplier(definition, recentSelections);
        }

        private static float EstimateDamageTaken(
            AttackerAiCandidate candidate,
            IReadOnlyList<AttackerAiDefenderThreat> defenderThreats)
        {
            if (defenderThreats == null || defenderThreats.Count == 0)
            {
                return 0f;
            }

            var totalDamage = 0f;
            var routePoints = candidate.RoutePoints;
            var speed = Mathf.Max(0.01f, candidate.Definition.MovementSpeed);
            for (var threatIndex = 0; threatIndex < defenderThreats.Count; threatIndex++)
            {
                var threat = defenderThreats[threatIndex];
                if (threat.Range <= 0f || threat.DamagePerSecond <= 0f)
                {
                    continue;
                }

                var distanceInRange = 0f;
                for (var pointIndex = 1; pointIndex < routePoints.Count; pointIndex++)
                {
                    distanceInRange += GetSegmentLengthInsideRange(
                        routePoints[pointIndex - 1],
                        routePoints[pointIndex],
                        threat.Position,
                        threat.Range);
                }

                var timeInRange = distanceInRange / speed;
                totalDamage += threat.DamagePerSecond * timeInRange * GetThreatReliability(threat);
            }

            return totalDamage;
        }

        private static float GetSegmentLengthInsideRange(Vector2 start, Vector2 end, Vector2 centre, float radius)
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
            return (end - start).magnitude * Mathf.Max(0f, exitT - enterT);
        }

        private static float GetThreatReliability(AttackerAiDefenderThreat threat)
        {
            var behaviourReliability = threat.ProjectileBehavior switch
            {
                ProjectileBehavior.Trap => 0.60f,
                ProjectileBehavior.PoleSweep => 0.90f,
                ProjectileBehavior.Rolling => 0.85f,
                _ => 1f
            };

            // Melee attacks are blocked by every wall, while ranged attacks can still target through
            // half cover. The cached route already honors cover for the animal itself, so this modest
            // adjustment avoids treating an obscured melee defender as a guaranteed hit source.
            return threat.ProjectileType == ProjectileType.Melee
                ? behaviourReliability * 0.85f
                : behaviourReliability;
        }

        private static float GetArchetypeCounterValue(
            AttackerAiCandidate candidate,
            float expectedDamage,
            IReadOnlyList<AttackerAiDefenderThreat> defenderThreats)
        {
            var definition = candidate.Definition;
            var expectedHealthPressure = Mathf.Clamp01(expectedDamage / Mathf.Max(0.01f, definition.MaximumHealth));
            var groupScale = definition.MaximumHealth * definition.SendSize;
            return definition.Archetype switch
            {
                AttackerArchetype.Fast => groupScale * expectedHealthPressure * 0.08f,
                AttackerArchetype.Strong => groupScale * expectedHealthPressure * 0.14f,
                AttackerArchetype.Flying => groupScale * GetFlyingRouteAdvantage(candidate) * 0.18f,
                _ => defenderThreats == null || defenderThreats.Count == 0 ? groupScale * 0.03f : 0f
            };
        }

        private static float GetFlyingRouteAdvantage(AttackerAiCandidate candidate)
        {
            if (candidate.Definition.Archetype != AttackerArchetype.Flying || candidate.RoutePoints.Count < 2)
            {
                return 0f;
            }

            var directDistance = Vector2.Distance(candidate.RoutePoints[0], candidate.RoutePoints[candidate.RoutePoints.Count - 1]);
            if (directDistance <= 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01((candidate.RouteLength / directDistance) - 1f);
        }

        private static float GetVarietyMultiplier(AttackerDefinition candidate, IReadOnlyList<AttackerDefinition> recentSelections)
        {
            if (recentSelections == null)
            {
                return 1f;
            }

            var multiplier = 1f;
            for (var index = 0; index < recentSelections.Count; index++)
            {
                var prior = recentSelections[index];
                if (prior == null)
                {
                    continue;
                }

                if (IsSameDefinition(candidate, prior))
                {
                    multiplier *= index == 0 ? ImmediateRepeatPenalty : EarlierRepeatPenalty;
                }
                else if (candidate.Archetype == prior.Archetype)
                {
                    multiplier *= index == 0 ? ImmediateArchetypePenalty : EarlierArchetypePenalty;
                }
            }

            return multiplier;
        }

        private static bool HasDifferentDefinition(IReadOnlyList<AttackerAiCandidate> candidates, AttackerDefinition current)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var definition = candidates[index].Definition;
                if (candidates[index].HasUsableRoute && definition != null && !IsSameDefinition(definition, current))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameDefinition(AttackerDefinition left, AttackerDefinition right)
        {
            return left == right ||
                   (left != null && right != null &&
                    string.Equals(left.AttackerId, right.AttackerId, StringComparison.Ordinal));
        }

        private static bool WinsTieBreak(AttackerDefinition contender, AttackerDefinition incumbent, int day, int groupIndex)
        {
            if (incumbent == null)
            {
                return true;
            }

            return GetStableOrder(contender, day, groupIndex) < GetStableOrder(incumbent, day, groupIndex);
        }

        private static int GetStableOrder(AttackerDefinition definition, int day, int groupIndex)
        {
            var value = (day * 17) + (groupIndex * 31);
            var id = definition != null ? definition.AttackerId : string.Empty;
            for (var index = 0; index < id.Length; index++)
            {
                value = (value * 31) + id[index];
            }

            return value & int.MaxValue;
        }

        private static string BuildReason(AttackerAiCandidate candidate, IReadOnlyList<AttackerAiDefenderThreat> defenderThreats)
        {
            if (defenderThreats == null || defenderThreats.Count == 0)
            {
                return "best affordable food pressure with no active farmhand threat";
            }

            return candidate.Definition.Archetype switch
            {
                AttackerArchetype.Fast => "speed reduces the time spent inside farmhand ranges",
                AttackerArchetype.Strong => "durability best offsets the current farmhand damage",
                AttackerArchetype.Flying => "flight profile is using its shortest cover-aware route",
                _ => "best estimated food pressure per gold against the current farmhands"
            };
        }
    }
}