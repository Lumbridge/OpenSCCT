using System;
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
    private FVector _trapWall;
    private FVector _trapFloor;
    private SGadMultiMinesLauncher? _trapLauncher;
    private int _trapSlot;
    private bool _preferTracker;

    // Guarding mercs inspect nearby walls, approach a clear placement, then use the same
    // selected gadget and animation-driven placement as a human. Combat always interrupts.
    private bool DefendWithTrap(float now, ref AiPlan plan)
    {
        if (Pawn.bSleeping || Pawn.bLyingOnGround || Pawn.bStickyShocking || Pawn.bGasMask ||
            Pawn.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)) return false;
        if (_trapApproach && (now > _trapUntil || _trapLauncher is null || _trapLauncher.bDeleteMe || _trapLauncher.Charge <= 0))
            _trapApproach = false;
        if (!_trapApproach)
        {
            if (now < _trapNext || Difficulty.GadgetUse <= 0f) return false;
            _trapNext = now + 8f;
            bool nearObjective = false;
            foreach (SObjectiveTrigger terminal in World.Level.AllActors<SObjectiveTrigger>())
                if (Defensible(terminal) && (terminal.Location - Pawn.Location).SizeSquared() < 700f * 700f &&
                    GuardSpot(terminal, out FVector spot) && World.LineOpen(AiSight.Eye(Pawn), spot + new FVector(0, 0, 40))) { nearObjective = true; break; }
            if (!nearObjective || !ChooseTrap()) return false;
            FVector eye = TrapEye;
            float best = float.MaxValue;
            var approachRoute = new NavFollower(World, spy: false);
            for (int i = 0; i < 16; i++)
            {
                FVector direction = new FRotator(0, i * 4096, 0).Vector();
                if (!World.Collision.LineCheck(out var hit, Pawn, eye + direction * 400f, eye, default,
                    SGadMultiMinesLauncher.WallTraceFlags) || MathF.Abs(hit.Normal.Z) > .2f || hit.Actor is Mover) continue;
                float distance = (hit.Location - eye).Size();
                if (distance < 25f || distance >= best || OccupiedWall(hit.Location)) continue;
                // Tables and pillars can obstruct a straight walk while leaving an ordinary route around them.
                // Plan to supported floor at the wall instead of rejecting every non-straight approach.
                if (!TrapApproach(hit.Location, hit.Normal, approachRoute, out FVector floor)) continue;
                float routeLength = approachRoute.Remaining(Pawn.Location);
                if (routeLength >= best) continue;
                best = routeLength;
                _trapWall = hit.Location;
                _trapFloor = floor;
            }
            if (best == float.MaxValue) return false;
            _trapApproach = true;
            _trapUntil = now + MathF.Max(6f, best / 150f + 4f);
            Follower.Clear();
        }
        if ((_trapWall - TrapEye).Size() > 50f)
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
        }

        FRotator aim = (_trapWall - TrapEye).Rotation();
        plan.Yaw = aim.Yaw;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 1000;
        plan.Crouched = false;
        if ((_trapWall - TrapEye).Size() > 50f)
        {
            plan.Keys.BT_Up = 1;
            return true;
        }
        if (NavFollower.YawError(Pc.Rotation.Yaw & 0xffff, plan.Yaw ?? 0) > 500) return true;
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
        if (_trapLauncher.HitActor is not null && _trapLauncher.CanUse[_trapLauncher.iCurrentMineType] != 0)
        {
            plan.Keys.Press(AiCommand.AltFire);
            _preferTracker = !_preferTracker;
            _trapNext = now + 20f;
        }
        _trapApproach = false;
        return true;
    }

    private FVector TrapEye => Pawn.Location + Pawn.WalkBob + new FVector(0, 0, Pawn.EyeHeight);

    private bool TrapApproach(FVector wall, FVector normal, NavFollower route, out FVector floor)
    {
        floor = default;
        FVector approach = wall + normal * 44f;
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

    private bool Defensible(SObjectiveTrigger objective) =>
        !objective.bDeleteMe && !objective.complete && objective.Objective?.ObjectiveTaken != true &&
        (objective is SComputerObjectiveTrigger terminal ? !World.IsTaken(terminal) :
            objective.bActive && (objective is SBombTargetObjectiveTrigger || objective is SDisk { bHidden: false, bPickable: true }));

    private bool OccupiedWall(FVector wall)
    {
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe && (mine.Location - wall).SizeSquared() < 300f * 300f) return true;
        foreach (SObjectiveTrigger terminal in World.Level.AllActors<SObjectiveTrigger>())
            if ((terminal.Location - wall).SizeSquared() < 80f * 80f) return true;
        return false;
    }
}
