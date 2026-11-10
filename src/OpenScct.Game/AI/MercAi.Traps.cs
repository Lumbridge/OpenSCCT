using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

public sealed partial class MercAi
{
    /// <summary>Heights above the floor at the wall a mine is tried at (uu): shin to above the head.</summary>
    private static readonly float[] TrapHeights = [54f, 64f, 76f, 88f, 100f, 116f, 130f, 143f, 156f];

    /// <summary>The bot stands this far from the wall (uu): just clear of its 35 uu cylinder.</summary>
    private const float TrapStandOff = 38f;

    /// <summary>The placement is planned within this reach of the eye (uu), inside the launcher's 55.</summary>
    private const float TrapPlanReach = 51.5f;

    /// <summary>At the wall the bot places within this reach (uu), and steps in when the eye is farther.</summary>
    private const float TrapUseReach = 53.5f;

    /// <summary>An objective counts a trap within this distance (uu).</summary>
    private const float TrapCoverRange = 500f;

    private float _trapNext;
    private float _trapUntil;
    private bool _trapApproach;
    private bool _trapCrouched;
    private FVector _trapWall;
    private FVector _trapFloor;
    private float _trapAtWallSince = -1f;
    private FVector _trapBadWall;
    private float _trapBadUntil = -100f;
    private SGadMultiMinesLauncher? _trapLauncher;
    private int _trapSlot;
    private bool _preferTracker;

    /// <summary>Mines and trackers this bot started to place (the AltFire accepted at a wall).</summary>
    public int TrapsPlaced { get; private set; }

    // Guarding mercs inspect nearby walls, approach a clear placement, then use the same
    // selected gadget and animation-driven placement as a human. Combat always interrupts.
    private bool DefendWithTrap(float now, ref AiPlan plan)
    {
        if (Pawn.bSleeping || Pawn.bLyingOnGround || Pawn.bStickyShocking || Pawn.bGasMask ||
            Pawn.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)) return false;
        if (_trapApproach && (now > _trapUntil || _trapLauncher is null || _trapLauncher.bDeleteMe || _trapLauncher.Charge <= 0))
        {
            // Out of time at this wall: another one next time.
            if (now > _trapUntil) GiveUpWall(now);
            _trapApproach = false;
        }

        if (!_trapApproach)
        {
            if (now < _trapNext || Difficulty.GadgetUse <= 0f) return false;
            _trapNext = now + 8f;
            if (!ChooseTrap() || !PlanTrap(now, out float routeLength)) return false;
            _trapApproach = true;
            _trapAtWallSince = -1f;
            _trapUntil = now + MathF.Max(8f, routeLength / 150f + 6f);
            Follower.Clear();
        }

        if (_trapAtWallSince < 0f && NavFollower.Horizontal(Feet - _trapFloor) > 8f)
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

        // At the wall: from here the bot only turns, changes stance and steps in, as a player lines a mine up.
        if (_trapAtWallSince < 0f) _trapAtWallSince = now;
        FVector reach = _trapWall - TrapEye;
        FRotator aim = reach.Rotation();
        plan.Yaw = aim.Yaw & 0xFFFF;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 65536;
        plan.Crouched = _trapCrouched;

        // The stance clip runs in its own walking state, and the eye eases to the new height over several frames.
        // Reach and pitch are judged only once both have settled, so a low mine is not lined up from the standing eye.
        if (Pawn.bIsCrouched != _trapCrouched || MathF.Abs(Pawn.EyeHeight - Pawn.BaseEyeHeight) > 2f) return true;

        if (reach.SafeNormal().Z > Equipment.K.MineMaxViewUp)
        {
            // Too steep from here (a step, a slope): stepping in would only steepen it.
            GiveUpWall(now);
            return true;
        }

        if (reach.Size() > TrapUseReach)
        {
            // A little short of the wall: step in until the eye is within reach (the cylinder stops at the wall).
            plan.MoveYaw = new FVector(reach.X, reach.Y, 0f).Rotation().Yaw & 0xFFFF;
            plan.Keys.BT_Up = 1;
            if (now - _trapAtWallSince > 2.5f) GiveUpWall(now);
            return true;
        }

        if (NavFollower.YawError(Pc.Rotation.Yaw & 0xffff, aim.Yaw & 0xffff) > 200 ||
            NavFollower.YawError(Pc.Rotation.Pitch & 0xffff, aim.Pitch & 0xffff) > 200 ||
            NavFollower.Horizontal(Pawn.Velocity) > 5f) return true;
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
            _preferTracker = !_preferTracker;
            TrapsPlaced++;
            // The clip takes about 1.5 s; then the next wall (another objective of the sector, or the second trap here).
            _trapNext = now + 4f;
            _trapApproach = false;
        }
        else
        {
            // The launcher refuses this spot (a mine or a terminal too near, no stickable surface).
            GiveUpWall(now);
        }

        return true;
    }

    /// <summary>The wall the bot could not use is left alone for a minute, so the next look picks another.</summary>
    private void GiveUpWall(float now)
    {
        _trapBadWall = _trapWall;
        _trapBadUntil = now + 60f;
        _trapApproach = false;
        _trapNext = now + 3f;
    }

    /// <summary>
    /// Picks the wall, the height and the stance of the next trap: walls within 400 uu round each site that wants a
    /// trap, at heights from the shin to above the head, the lower ones placed crouched. A height band is preferred per
    /// placement (low most often, as players hide them), with the nearest reachable height taken where the band cannot
    /// be had. One route is planned per wall, not per height.
    /// </summary>
    private bool PlanTrap(float now, out float bestRouteLength)
    {
        bestRouteLength = 0f;
        float best = float.MaxValue;
        float roll = Bot.Random.FRand();
        float wanted = roll < 0.5f ? 56f : roll < 0.75f ? 95f : 150f;
        float crouchEye = Pawn.CrouchHeight + ((SPAWNDEFENSE)Pawn).BaseCrouchEyeHeight;
        float standEye = Follower.Graph.Profile.StandingHeight + Pawn.DefaultObject.BaseEyeHeight;
        var approachRoute = new NavFollower(World, spy: false);
        foreach (FVector site in TrapSites())
        for (int i = 0; i < 16; i++)
        {
            FVector direction = new FRotator(0, i * 4096, 0).Vector();
            bool planned = false;
            bool reachable = false;
            FVector plannedWall = default;
            FVector floor = default;
            float routeLength = 0f;
            foreach (float height in TrapHeights)
            {
                FVector from = site + new FVector(0f, 0f, height);
                if (!World.Collision.LineCheck(out var hit, Pawn, from + direction * 400f, from, default,
                    SGadMultiMinesLauncher.WallTraceFlags) || hit.bStartPenetrating || MathF.Abs(hit.Normal.Z) > .2f || hit.Actor is Mover) continue;
                float distance = (hit.Location - from).Size();
                if (distance < 80f || OccupiedWall(hit.Location) ||
                    (now < _trapBadUntil && (hit.Location - _trapBadWall).SizeSquared() < 120f * 120f)) continue;
                // Tables and pillars can obstruct a straight walk while leaving an ordinary route around them.
                // Plan to supported floor at the wall instead of rejecting every non-straight approach.
                if (!planned || NavFollower.Horizontal(hit.Location - plannedWall) > 6f)
                {
                    planned = true;
                    plannedWall = hit.Location;
                    reachable = TrapApproach(hit.Location, hit.Normal, approachRoute, out floor);
                    routeLength = reachable ? approachRoute.Remaining(Pawn.Location) : 0f;
                }

                if (!reachable) continue;
                // Slopes and raised platforms can change the available height at the actual placement floor.
                bool crouched;
                if (TrapInReach(hit.Location, floor, crouchEye)) crouched = true;
                else if (TrapInReach(hit.Location, floor, standEye)) crouched = false;
                else continue;
                float score = routeLength + distance * .5f + MathF.Abs(hit.Location.Z - floor.Z - wanted) * 4f;
                if (score >= best) continue;
                best = score;
                bestRouteLength = routeLength;
                _trapWall = hit.Location;
                _trapFloor = floor;
                _trapCrouched = crouched;
            }
        }

        return best < float.MaxValue;
    }

    /// <summary>A wall point within the planned reach of an eye <paramref name="eyeHeight"/> above <paramref name="floor"/>, not pitched up past the launcher's limit.</summary>
    private static bool TrapInReach(FVector wall, FVector floor, float eyeHeight)
    {
        FVector reach = wall - (floor + new FVector(0f, 0f, eyeHeight));
        return reach.Size() <= TrapPlanReach && reach.SafeNormal().Z <= Equipment.K.MineMaxViewUp - 0.03f;
    }

    private FVector TrapEye => Pawn.Location + Pawn.WalkBob + new FVector(0, 0, Pawn.EyeHeight);

    /// <summary>
    /// Where a trap is wanted within 700 uu: the use spots of objectives that have fewer traps near them than the bot
    /// wants (its own sector first), then heal stations without one.
    /// </summary>
    private IEnumerable<FVector> TrapSites()
    {
        bool any = false;
        foreach (SObjectiveTrigger objective in World.Level.AllActors<SObjectiveTrigger>())
        {
            if (Defensible(objective) && (objective.Location - Pawn.Location).SizeSquared() < 700f * 700f &&
                NeedsTrap(objective) && GuardSpot(objective, out FVector spot))
            {
                any = true;
                yield return spot;
            }
        }

        // An objective comes first: the heal station only once those near are covered.
        if (any) yield break;
        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
        {
            if (station.bDeleteMe || station.bHidden || (station.Location - Pawn.Location).SizeSquared() >= 700f * 700f ||
                TrapsNear(station.Location) > 0) continue;
            FVector location = station.InteractionObj?.Location ?? station.Location;
            int node = Follower.Graph.NearestNode(location, 0f, 200f);
            if (node >= 0) yield return Follower.Graph.Node(node).Floor;
        }
    }

    /// <summary>Mines and trackers within <see cref="TrapCoverRange"/> of <paramref name="at"/>, as a merc sees them on the map.</summary>
    private int TrapsNear(FVector at)
    {
        int count = 0;
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe && (mine.Location - at).SizeSquared() < TrapCoverRange * TrapCoverRange) count++;
        return count;
    }

    /// <summary>
    /// Fewer traps near the objective than wanted: two each (a mine and a tracker) while the bot's sector is one or two
    /// objectives, one each over a wider sector, so the charges go round.
    /// </summary>
    private bool NeedsTrap(SObjectiveTrigger objective) => TrapsNear(objective.Location) < (_sector.Count <= 2 ? 2 : 1);

    /// <summary>A launcher with a charge and room under its owner's limit, without choosing it.</summary>
    private bool CanTrap()
    {
        if (Difficulty.GadgetUse <= 0f) return false;
        for (SGadget? gadget = Pawn.MyGadget; gadget is not null; gadget = gadget.MyGadget)
        {
            if (PawnInventory.IsSelectable(gadget) && gadget is SGadMultiMinesLauncher { Charge: > 0, bDeleteMe: false } launcher &&
                (launcher is SGadMultiPresDetectorLauncher ? Pc.NbTrapMineInMapIOwn : Pc.NbBasicMineInMapIOwn) < launcher.iNbMineMax)
                return true;
        }

        return false;
    }

    private bool TrapApproach(FVector wall, FVector normal, NavFollower route, out FVector floor)
    {
        floor = default;
        // Leave room inside the 55 uu reach for a pitched view: just clear of the cylinder, so the low and the high
        // placements stay within reach.
        FVector approach = wall + new FVector(normal.X, normal.Y, 0f).SafeNormal() * TrapStandOff;
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
                if (placed < launcher.iNbMineMax && (_trapLauncher is null || tracker == _preferTracker))
                {
                    _trapLauncher = launcher;
                    _trapSlot = slot;
                }
            }
            slot++;
        }
        return _trapLauncher is not null;
    }

    private bool Defensible(SObjectiveTrigger objective) => MercDefence.Defensible(World, objective);

    private bool OccupiedWall(FVector wall)
    {
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe && (mine.Location - wall).SizeSquared() < 300f * 300f) return true;
        foreach (SObjectiveTrigger terminal in World.Level.AllActors<SObjectiveTrigger>())
            if ((terminal.Location - wall).SizeSquared() < 80f * 80f) return true;
        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
            if ((station.Location - wall).SizeSquared() < 80f * 80f) return true;
        return false;
    }
}
