using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

public sealed partial class SpyAi
{
    private SObjectiveTrigger? _otherObjective;
    private readonly Dictionary<SObjectiveTrigger, float> _objectiveFailed = new();
    private FVector _objectiveFloor;
    private FVector _objectiveProgress;
    private float _objectiveProgressAt;
    private float _objectiveUseAt = -1f;
    private bool _objectiveMenuDown;
    private float _objectiveSearchAt;
    private bool _objectiveStanding;

    public SObjectiveTrigger? OtherObjectiveTarget => _otherObjective;

    private static SInteractObject? ObjectiveEntry(SObjectiveTrigger objective) => objective switch
    {
        SDisk disk => disk.PickupInteraction,
        SFlag flag => flag.PickupInteraction,
        _ => objective.InteractionObj,
    };

    private bool AvailableObjective(SObjectiveTrigger objective, float now)
    {
        if (objective.bDeleteMe || objective.bLocked ||
            _objectiveFailed.TryGetValue(objective, out float failed) && now - failed <= 25f) return false;
        // An extraction case is triggering while it waits for its disk. Only the carrier can use it,
        // and carrying a disk takes precedence over fetching another device or planting a bomb.
        if (Pawn.CarriedFlag is { } carried)
            return objective is SFlagDropZone zone && carried.CheckDropZone(zone) && zone.InteractionObj is { bUsable: true };
        if (!objective.bActive || objective.bTriggering) return false;
        return objective switch
        {
            SDisk disk => !disk.bHidden && disk.bPickable && disk.PickupInteraction is { bCanPickup: true },
            SFlag flag => !flag.bHidden && flag.bPickable && flag.FlagCarrier is null && flag.PickupInteraction is { bCanPickup: true },
            SBombTargetObjectiveTrigger bomb => bomb.InteractionObj is { bUsable: true } &&
                PawnInventory.FindGadgetType(Pawn, new ClassRef("SBase.SGadBomb")) is SGadBomb { Charge: > 0 },
            _ => false,
        };
    }

    private bool FlagUseFloor(SInteractObject entry, out FVector floor)
    {
        SFlag.GetStandSpot(entry, Pawn, out FVector spot, out _);
        FVector top = new(spot.X, spot.Y, entry.Location.Z + Follower.Graph.Profile.StepUp + 2f);
        FVector bottom = new(spot.X, spot.Y, entry.Location.Z - 200f);
        const ETraceFlags flags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
            ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others;
        floor = default;
        if (!World.Collision.LineCheck(out FCheckResult hit, NavBuilder.CollisionProbe, bottom, top, new FVector(4, 4, 2), flags) ||
            hit.bStartPenetrating || hit.Normal.Z < Follower.Graph.Profile.MinFloorNormalZ) return false;
        floor = new FVector(spot.X, spot.Y, hit.Location.Z - 2f);
        FVector center = floor + new FVector(0, 0, AiK.StandingHeight + 2f);
        // The animation walks to this exact front point. A reachable sample behind the rack is insufficient,
        // and crouching beside a raised pedestal can put the interaction below the top's line of sight.
        return entry.IsOnFrontSide(center) &&
            NavFollower.Horizontal(center - entry.Location) < entry.CollisionRadius + Pawn.CollisionRadius - 4f &&
            MathF.Abs(center.Z - entry.Location.Z) < entry.CollisionHeight + AiK.StandingHeight - 4f &&
            Follower.Clear(floor, floor, crouched: false) && World.LineOpen(center, entry.Location);
    }

    private SObjectiveTrigger? FindOtherObjective(float now)
    {
        if (_otherObjective is { } current && AvailableObjective(current, now)) return current;
        _otherObjective = null;
        if (now < _objectiveSearchAt) return null;
        _objectiveSearchAt = now + 1f;
        var candidates = new List<SObjectiveTrigger>();
        foreach (SObjectiveTrigger candidate in World.Level.AllActors<SObjectiveTrigger>())
            if (AvailableObjective(candidate, now)) candidates.Add(candidate);
        if (candidates.Count == 0) return null;
        NavGraph graph = Follower.Graph;
        int start = graph.NearestNode(Pawn.Location, Pawn.CollisionHeight);
        float[] costs = ReachabilityCosts(start);
        float best = float.MaxValue;
        foreach (SObjectiveTrigger candidate in candidates)
        {
            if (!AvailableObjective(candidate, now) || ObjectiveEntry(candidate) is not { } entry) continue;
            bool standing = candidate is SFlag or SFlagDropZone;
            FVector useFloor = default;
            if (standing && !FlagUseFloor(entry, out useFloor)) continue;
            // Use a reachable floor inside the real interaction cylinder. A nearest-node snap can choose
            // the ground beneath a tower disk, or the wrong side of a cabin wall.
            var nodes = new List<int>();
            graph.NodesNear(standing ? useFloor : entry.Location,
                standing ? graph.Profile.Spacing * 5f : Math.Max(100f, entry.CollisionRadius + Pawn.CollisionRadius), nodes);
            foreach (int node in nodes)
            {
                if (!float.IsFinite(costs[node]) || costs[node] >= best) continue;
                FVector floor = graph.Node(node).Floor;
                if (standing)
                {
                    float cost = costs[node] + NavFollower.Horizontal(floor - useFloor);
                    if (cost >= best || !Follower.Clear(floor, useFloor, crouched: true)) continue;
                    best = cost;
                    _otherObjective = candidate;
                    _objectiveFloor = useFloor;
                    _objectiveStanding = true;
                    continue;
                }
                FVector center = floor + new FVector(0, 0, AiK.SpyCrouchHeight);
                if (NavFollower.Horizontal(center - entry.Location) > entry.CollisionRadius + Pawn.CollisionRadius - 32f ||
                    MathF.Abs(center.Z - entry.Location.Z) > entry.CollisionHeight + AiK.SpyCrouchHeight - 4f ||
                    !World.LineOpen(center, entry.Location)) continue;
                best = costs[node];
                _otherObjective = candidate;
                _objectiveFloor = floor;
                _objectiveStanding = false;
            }
        }
        if (_otherObjective is not null)
        {
            _objectiveProgress = Pawn.Location;
            _objectiveProgressAt = now;
            _objectiveUseAt = -1f;
        }
        return _otherObjective;
    }

    private bool InOtherObjectiveState()
    {
        string state = Pc.GetStateName().ToString();
        return state.StartsWith("s_Pickup", StringComparison.Ordinal) || state.StartsWith("s_PlaceBomb", StringComparison.Ordinal) ||
            state is "s_FlagAndDropZone_GoTo" or "s_PutFlagInDropZone";
    }

    private void FailOtherObjective(float now)
    {
        if (_otherObjective is { } objective) _objectiveFailed[objective] = now;
        _otherObjective = null;
        _objectiveUseAt = -1f;
        Follower.Clear();
    }

    private void OtherObjective(float now, ref AiPlan plan)
    {
        if (InOtherObjectiveState())
        {
            plan.Keys = default;
            plan.Yaw = null;
            plan.Crouched = null;
            return;
        }
        SObjectiveTrigger? target = FindOtherObjective(now);
        if (target is null || ObjectiveEntry(target) is not { } entry) return;
        if ((Pawn.Location - _objectiveProgress).SizeSquared() > 120f * 120f)
        {
            _objectiveProgress = Pawn.Location;
            _objectiveProgressAt = now;
        }
        if (now - _objectiveProgressAt > 15f && Follower.ActiveLink < 0)
        {
            FailOtherObjective(now);
            return;
        }
        if (_objectiveUseAt < 0f)
        {
            if (!MoveTo(_objectiveFloor, now, ref plan, true, Difficulty.Stealth, DangerPenalty(Difficulty.Stealth)))
            {
                if (Follower.Failed) FailOtherObjective(now);
                return;
            }
            if (!Follower.Arrived) return;
            _objectiveUseAt = now;
        }
        plan.Keys = default;
        plan.Crouched = _objectiveStanding ? false : null;
        LookAt(entry.Location, ref plan);
        if (Pawn.WeaponLoaded != 0 || Pawn.bWeaponTransit) return; // The shared holster step puts the gun away.
        if (now - _objectiveUseAt > 5f)
        {
            FailOtherObjective(now);
            return;
        }
        if (!Pc.IsInState(NAME_s_MenuInteraction))
        {
            // Walking consumes Use on its rising edge. Settle first, then retry a rejected press;
            // holding from the final movement frame can otherwise leave the menu closed forever.
            float elapsed = now - _objectiveUseAt;
            if (elapsed > .4f && Pawn.Velocity.SizeSquared() < 4f)
                plan.Keys.bUsing = (elapsed - .4f) % .6f < .15f ? (byte)1 : (byte)0;
            return;
        }
        plan.Keys.bUsing = 1;
        if (ReferenceEquals(Pc.IManager?.GetCurrentInteraction(), entry))
        {
            plan.Keys.bUsing = 0;
            return;
        }
        _objectiveMenuDown = !_objectiveMenuDown;
        plan.Keys.BT_Down = _objectiveMenuDown ? (byte)1 : (byte)0;
    }
}
