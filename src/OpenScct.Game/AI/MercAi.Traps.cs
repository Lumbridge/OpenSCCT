using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

public sealed partial class MercAi
{
    private float _trapNext;
    private float _trapUntil;
    private bool _trapApproach;
    private bool _trapCrouched;
    private FVector _trapWall;
    private FVector _trapFloor;
    private SGadMultiMinesLauncher? _trapLauncher;
    private int _trapSlot;
    private float _trapVerifyUntil;
    private int _trapBeforeCharge;
    private readonly record struct TrapSite(FVector Floor, float Value);
    private readonly record struct TrapCandidate(FVector Wall, FVector Normal, float EyeHeight, bool Crouched, float Score);

    // Guarding mercs inspect nearby walls, approach a clear placement, then use the same
    // selected gadget and animation-driven placement as a human. Combat always interrupts.
    private bool DefendWithTrap(float now, ref AiPlan plan)
    {
        if (Pawn.bSleeping || Pawn.bLyingOnGround || Pawn.bStickyShocking || Pawn.bGasMask ||
            Pawn.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)) return false;
        if (_trapApproach && (now > _trapUntil || _trapLauncher is null || _trapLauncher.bDeleteMe || _trapLauncher.Charge <= 0))
            _trapApproach = false;
        if (_trapVerifyUntil > 0f)
        {
            bool placed = _trapLauncher is { } launcher && launcher.Charge < _trapBeforeCharge;
            if (!placed && now < _trapVerifyUntil) return false;
            _trapVerifyUntil = 0f;
            _trapNext = now + (placed ? 7f : 2f) * Bot.Personality.Tempo;
        }

        if (!_trapApproach)
        {
            if (now < _trapNext || Difficulty.GadgetUse <= 0f) return false;
            _trapNext = now + 4f * Bot.Personality.Tempo;
            if (!ChooseTrap()) return false;
            float best = float.MaxValue;
            float bestRouteLength = 0f;
            int preferredHeight = Bot.Random.Rand(4);
            var candidates = new List<TrapCandidate>();
            foreach (TrapSite site in TrapSites())
            for (int height = 0; height < 4; height++)
            for (int i = 0; i < 16; i++)
            {
                bool crouched = height < 2;
                float eyeHeight = crouched ? Pawn.CrouchHeight + ((SPAWNDEFENSE)Pawn).BaseCrouchEyeHeight :
                    Follower.Graph.Profile.StandingHeight + Pawn.DefaultObject.BaseEyeHeight;
                float offset = height switch { 0 => -24f, 1 => 0f, 2 => -20f, _ => 12f };
                FVector eye = site.Floor + new FVector(0, 0, eyeHeight + offset);
                FVector direction = new FRotator(0, i * 4096, 0).Vector();
                if (!World.Collision.LineCheck(out var hit, Pawn, eye + direction * 400f, eye, default,
                    SGadMultiMinesLauncher.WallTraceFlags) || hit.bStartPenetrating || MathF.Abs(hit.Normal.Z) > .2f ||
                    hit.Actor is Mover || OccupiedWall(hit.Location)) continue;

                float distance = (hit.Location - eye).Size();
                if (distance < 70f) continue;
                float score = (hit.Location - Feet).Size() * .35f + MathF.Abs(distance - 120f) * 1.1f -
                    site.Value + (height == preferredHeight ? 0f : 110f);
                candidates.Add(new(hit.Location, hit.Normal, eyeHeight, crouched, score));
            }

            // Cheap wall probes rank first. Only the best candidates incur a path search.
            var approachRoute = new NavFollower(World, spy: false);
            foreach (TrapCandidate candidate in candidates.OrderBy(c => c.Score)
                .DistinctBy(c => ((int)(c.Wall.X / 16f), (int)(c.Wall.Y / 16f), (int)(c.Wall.Z / 16f), c.Crouched)).Take(36))
            {
                if (!TrapApproach(candidate.Wall, candidate.Normal, approachRoute, out FVector floor)) continue;
                FVector reach = candidate.Wall - (floor + new FVector(0, 0, candidate.EyeHeight));
                if (reach.Size() > 52f || reach.SafeNormal().Z > Equipment.K.MineMaxViewUp) continue;
                float routeLength = approachRoute.Remaining(Pawn.Location);
                float score = candidate.Score + routeLength * .75f;
                if (score >= best || routeLength > 1450f) continue;
                best = score;
                bestRouteLength = routeLength;
                _trapWall = candidate.Wall;
                _trapFloor = floor;
                _trapCrouched = candidate.Crouched;
            }
            if (best == float.MaxValue) return false;
            _trapApproach = true;
            _trapUntil = now + MathF.Max(8f, bestRouteLength / 150f + 6f);
            Follower.Clear();
        }
        if (NavFollower.Horizontal(Feet - _trapFloor) > 8f || MathF.Abs(Feet.Z - _trapFloor.Z) > 30f)
        {
            if (!MoveTo(_trapFloor, now, ref plan, sneak: false))
            {
                _trapApproach = false;
                return false;
            }

            if (Doors.Busy)
            {
                _trapUntil = now + 6f;
                return true;
            }

            // The navigation arrival radius is wider than the remaining few steps to the mine's 55 uu reach.
            if (!Follower.Arrived) return true;
            plan.MoveYaw = (_trapFloor - Feet).Rotation().Yaw;
            plan.Keys.BT_Up = 1;
            return true;
        }

        if (OccupiedWall(_trapWall))
        {
            _trapApproach = false;
            return false;
        }

        FRotator aim = (_trapWall - TrapEye).Rotation();
        plan.Yaw = aim.Yaw;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 1000;
        plan.Crouched = _trapCrouched;
        // Crouching changes the eye over several frames. Settle before checking reach or firing,
        // and wait for pitch as well as yaw so a low target is not placed at the old eye level.
        if (Pawn.bIsCrouched != _trapCrouched || MathF.Abs(Pawn.EyeHeight - Pawn.BaseEyeHeight) > 2f ||
            (_trapWall - TrapEye).Size() > 54f || (_trapWall - TrapEye).SafeNormal().Z > Equipment.K.MineMaxViewUp ||
            NavFollower.YawError(Pc.Rotation.Yaw & 0xffff, aim.Yaw) > 200 ||
            NavFollower.YawError(Pc.Rotation.Pitch & 0xffff, aim.Pitch) > 200) return true;
        if (!ReferenceEquals(Pawn.SelectedGadget, _trapLauncher))
        {
            plan.Keys.Press(AiCommand.SelectGadget, _trapSlot);
            return true;
        }
        _trapLauncher!.UseMine();
        // A tracker beam needs an opposite wall; the presence detector does not.
        if (_trapLauncher.CanUse[_trapLauncher.iCurrentMineType] == 0)
            for (int i = 0; i < _trapLauncher.NbIT - 2; i++)
                if (_trapLauncher.CanUse[i] != 0) { _trapLauncher.iCurrentMineType = i; break; }
        if (_trapLauncher.HitActor is not null && (_trapLauncher.HitLocation - _trapWall).SizeSquared() < 8f * 8f &&
            _trapLauncher.CanUse[_trapLauncher.iCurrentMineType] != 0)
        {
            plan.Keys.Press(AiCommand.AltFire);
            _trapBeforeCharge = _trapLauncher.Charge;
            _trapVerifyUntil = now + 4f;
        }
        _trapApproach = false;
        return true;
    }

    private FVector TrapEye => Pawn.Location + Pawn.WalkBob + new FVector(0, 0, Pawn.EyeHeight);

    private IEnumerable<TrapSite> TrapSites()
    {
        var objectives = new List<SObjectiveTrigger>();
        foreach (SObjectiveTrigger objective in World.Level.AllActors<SObjectiveTrigger>())
            if (Defensible(objective) && (objective.Location - Pawn.Location).SizeSquared() < 1100f * 1100f)
                objectives.Add(objective);
        foreach (SObjectiveTrigger objective in objectives.OrderBy(o => (o.Location - Pawn.Location).SizeSquared()).Take(3))
        {
            if (!GuardSpot(objective, out FVector spot)) continue;
            yield return new(spot, 210f);
            foreach (TrapSite lane in TrapLanes(spot)) yield return lane;
        }

        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
        {
            if (station.bDeleteMe || station.bHidden || (station.Location - Pawn.Location).SizeSquared() >= 700f * 700f) continue;
            FVector location = station.InteractionObj?.Location ?? station.Location;
            int node = Follower.Graph.NearestNode(location, 0f, 200f);
            if (node >= 0) yield return new(Follower.Graph.Node(node).Floor, 30f);
        }
    }

    private IEnumerable<TrapSite> TrapLanes(FVector objective)
    {
        // Find narrow carriageways entering the objective area. Require floor, a sightline to the
        // area being defended and a wall within mine reach of the lane, then favor separate approaches.
        var sites = new List<TrapSite>();
        NavGraph graph = Follower.Graph;
        List<int> nodes = NodesAround(objective, 490f);
        var height = new FVector(0, 0, graph.Profile.StandingHeight);
        int stride = Math.Max(1, nodes.Count / 64);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            FVector floor = graph.Node(nodes[i]).Floor;
            FVector delta = floor - objective;
            if (MathF.Abs(delta.Z) > 80f || delta.SizeSquared() < 190f * 190f ||
                !World.LineOpen(floor + height, objective + height)) continue;
            FVector side = new FVector(-delta.Y, delta.X, 0).SafeNormal();
            FVector eye = floor + height;
            if (!World.Collision.LineCheck(out var left, Pawn, eye + side * 185f, eye, default, ETraceFlags.TRACE_World) ||
                !World.Collision.LineCheck(out var right, Pawn, eye - side * 185f, eye, default, ETraceFlags.TRACE_World)) continue;
            if (left.bStartPenetrating || right.bStartPenetrating || left.Actor is Mover || right.Actor is Mover) continue;
            float width = (left.Location - right.Location).Size();
            if (width < Follower.Graph.Profile.Radius * 2f) continue;
            sites.Add(new(floor, 360f - width * .45f));
        }

        TrapSite? first = null;
        foreach (TrapSite site in sites.OrderByDescending(x => x.Value))
        {
            if (first is { } best && (site.Floor - best.Floor).SizeSquared() < 240f * 240f) continue;
            yield return site;
            if (first is not null) yield break;
            first = site;
        }
    }

    private bool TrapApproach(FVector wall, FVector normal, NavFollower route, out FVector floor)
    {
        floor = default;
        // Leave room inside the 55 uu reach for a pitched view and the final walking tolerance.
        FVector approach = wall + normal * 40f;
        FVector top = approach + new FVector(0f, 0f, 40f);
        FVector bottom = approach - new FVector(0f, 0f, 240f);
        if (!World.Collision.LineCheck(out FCheckResult ground, Pawn, bottom, top, new FVector(4f, 4f, 2f),
            SGadMultiMinesLauncher.WallTraceFlags) || ground.bStartPenetrating || ground.Normal.Z < .7f)
            return false;

        floor = ground.Location - new FVector(0f, 0f, 2f);
        if (!route.SetGoal(Pawn, floor) || route.Path.Count == 0 || (route.Path[^1].Point - floor).SizeSquared() > 4f)
            return false;

        FVector from = Pawn.Location;
        foreach (NavStep step in route.Path)
        {
            FVector to = step.Point + new FVector(0f, 0f, Pawn.CollisionHeight);
            // The static graph assumes doors are open. Do not choose a placement beyond a closed door.
            if (World.Collision.LineCheck(out _, Pawn, to, from, default, ETraceFlags.TRACE_Movers)) return false;
            from = to;
        }

        return true;
    }

    private bool ChooseTrap()
    {
        _trapLauncher = null;
        int slot = 0;
        for (SGadget? gadget = Pawn.MyGadget; gadget is not null && slot < 8; gadget = gadget.MyGadget)
        {
            if (!PawnInventory.IsSelectable(gadget)) continue;
            if (gadget is SGadMultiMinesLauncher { Charge: > 0, bDeleteMe: false } launcher)
            {
                bool tracker = launcher is SGadMultiPresDetectorLauncher;
                int placed = tracker ? Pc.NbTrapMineInMapIOwn : Pc.NbBasicMineInMapIOwn;
                if (placed < launcher.iNbMineMax && (_trapLauncher is null || !tracker && _trapLauncher is SGadMultiPresDetectorLauncher))
                {
                    _trapLauncher = launcher;
                    _trapSlot = slot;
                }
            }
            slot++;
        }
        return _trapLauncher is not null;
    }

    private bool Defensible(SObjectiveTrigger objective) =>
        !objective.bDeleteMe && !objective.complete && objective.Objective?.ObjectiveTaken != true &&
        (objective is SComputerObjectiveTrigger terminal ? !World.IsTaken(terminal) :
            objective.bActive && (objective is SBombTargetObjectiveTrigger || objective is SDisk { bHidden: false, bPickable: true }));

    private bool OccupiedWall(FVector wall)
    {
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe && (mine.Location - wall).SizeSquared() < 220f * 220f) return true;
        foreach (SObjectiveTrigger terminal in World.Level.AllActors<SObjectiveTrigger>())
            if ((terminal.Location - wall).SizeSquared() < 80f * 80f) return true;
        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
            if ((station.Location - wall).SizeSquared() < 80f * 80f) return true;
        return false;
    }
}
