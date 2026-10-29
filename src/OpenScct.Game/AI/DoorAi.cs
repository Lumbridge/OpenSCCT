using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>
/// Closed doors on a walker's route. The graphs take doors as open, so a route can run into a closed mover. Looking a
/// little way along the route, a door that is opening is waited for; a door a switch of the walker's side opens (an
/// <see cref="SInterrupteur"/> whose Event is the door's Tag, on the walker's side of it, such as the hack panels by
/// Cinema's glass doors) is opened as a player would: walk to the switch, hold Use until the menu opens, move the
/// selection to the switch with S, let go, and stay off the keys while the hack runs. A door that bumping opens is
/// left to the walk. A door nobody here can open (or a switch that did not open it) is shut for the side's plans for a
/// while and the route goes round (a pipe, another door).
/// </summary>
public sealed class DoorAi
{
    private const ETraceFlags MoverFlags = ETraceFlags.TRACE_Movers;

    /// <summary>How far along the route (uu) a door is looked for.</summary>
    private const float LookAhead = 220f;

    /// <summary>Seconds between looks along the route.</summary>
    private const float ScanPeriod = 0.25f;

    /// <summary>Farthest switch (uu, straight) taken for a door.</summary>
    private const float SwitchReach = 1500f;

    private static readonly Name NAME_s_MenuInteraction = new("s_MenuInteraction");

    private enum Phase : byte
    {
        None,
        Wait,
        Walk,
        Face,
        Press,
        Menu,
        Hack,
    }

    private readonly AiController _bot;
    private readonly NavFollower _route;
    private readonly NavFollower _walk;
    private readonly bool _spy;
    private readonly Dictionary<Mover, float> _givenUp = new(ReferenceEqualityComparer.Instance);
    private readonly List<SInterrupteur> _switches = new();
    private readonly List<Trigger> _triggers = new();
    private bool _gathered;
    private Phase _phase;
    private float _phaseAt;
    private float _nextScan;
    private float _lastTick = -10f;
    private Mover? _door;
    private SInterrupteur? _switch;
    private FVector _hitAt;
    private FVector _hitNormal;
    private FVector _spot;
    private int _tries;
    private bool _downHeld;

    public DoorAi(AiController bot, NavFollower route, bool spy)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _spy = spy;
        _walk = new NavFollower(bot.World, spy);
    }

    /// <summary>True while a door is being dealt with (the walk to the switch, the hack, the wait).</summary>
    public bool Busy => _phase != Phase.None;

    /// <summary>Doors opened through a switch (diagnostics).</summary>
    public int Opened { get; private set; }

    public string Status => _phase == Phase.None ? "-" : $"{_phase} {_door?.Tag}";

    public void Reset()
    {
        _phase = Phase.None;
        _door = null;
        _switch = null;
        _walk.Clear();
    }

    /// <summary>
    /// One tick of the route's walk: true when the door handling owns the keys this tick (the plan is filled), false
    /// when the route should be steered as usual.
    /// </summary>
    public bool Steer(float now, ref AiPlan plan)
    {
        SBasePawn? pawn = _bot.Pawn;
        if (pawn is null || pawn.Health <= 0)
        {
            Reset();
            return false;
        }

        // The brain left the walk for a while (another goal): start afresh.
        if (_phase != Phase.None && now - _lastTick > 0.5f)
        {
            Reset();
        }

        _lastTick = now;
        if (_phase == Phase.None)
        {
            if (now < _nextScan)
            {
                return false;
            }

            _nextScan = now + ScanPeriod;
            if (!Look(pawn, now))
            {
                return false;
            }
        }

        return Act(pawn, now, ref plan);
    }

    // ---- looking along the route -----------------------------------------------------------------------------

    private bool Look(SBasePawn pawn, float now)
    {
        if (pawn.Physics != EPhysics.PHYS_Walking || !_route.HasGoal || _route.Failed || _route.ActiveLink >= 0 ||
            !DoorAhead(pawn, out Mover? door, out FCheckResult hit))
        {
            return false;
        }

        _door = door;
        _hitAt = hit.Location;
        var n = new FVector(hit.Normal.X, hit.Normal.Y, 0f);
        _hitNormal = n.Size() > 0.3f ? n.SafeNormal() : (pawn.Location - hit.Location).SafeNormal();
        _hitNormal.Z = 0f;
        _tries = 0;
        if (door!.bOpening)
        {
            Enter(Phase.Wait, now);
            return true;
        }

        if (_givenUp.TryGetValue(door, out float until) && now < until)
        {
            Shut(pawn, now);
            return false;
        }

        if (OpensByWalking(door))
        {
            return false;
        }

        if (!FindSwitch(pawn, door))
        {
            Shut(pawn, now);
            return false;
        }

        Enter(Phase.Walk, now);
        return true;
    }

    /// <summary>The first closed mover the route meets within <see cref="LookAhead"/>, at the pawn's waist.</summary>
    private bool DoorAhead(SBasePawn pawn, out Mover? door, out FCheckResult hit)
    {
        door = null;
        hit = default;
        IReadOnlyList<NavStep> path = _route.Path;
        FVector lift = new(0f, 0f, pawn.CollisionHeight);
        FVector from = pawn.Location;
        float walked = 0f;
        for (int k = _route.NextIndex; k < path.Count && walked < LookAhead; k++)
        {
            if (path[k].Link >= 0)
            {
                break;
            }

            FVector to = path[k].Point + lift;
            if (_bot.World.Collision.LineCheck(out hit, pawn, to, from, default, MoverFlags) && MoverOf(hit) is { } m)
            {
                door = m;
                return true;
            }

            walked += NavFollower.Horizontal(to - from);
            from = to;
        }

        return false;
    }

    private Mover? MoverOf(in FCheckResult hit)
    {
        if (hit.Actor is Mover m)
        {
            return m;
        }

        // A hit with no actor bound: the nearest mover to the hit point.
        Mover? best = null;
        float bestD = 400f * 400f;
        foreach (Mover a in _bot.World.Level.DynamicActors<Mover>())
        {
            float d = FVector.DistSquared(a.Location, hit.Location);
            if (!a.bDeleteMe && d < bestD)
            {
                bestD = d;
                best = a;
            }
        }

        return best;
    }

    /// <summary>
    /// A mover that opens when a pawn walks into it or stands on it (its state), or that a touch trigger of the level
    /// fires (Event = its Tag): the walk opens it.
    /// </summary>
    private bool OpensByWalking(Mover door)
    {
        string state = door.GetStateName().ToString();
        if (state is "BumpOpenTimed" or "StandOpenTimed" or "BumpButton")
        {
            return true;
        }

        Gather();
        foreach (Trigger t in _triggers)
        {
            if (!t.bDeleteMe && door.Tag != Name.None && t.Event == door.Tag)
            {
                return true;
            }
        }

        return false;
    }

    private void Gather()
    {
        if (_gathered)
        {
            return;
        }

        _gathered = true;
        foreach (SInterrupteur s in _bot.World.Level.AllActors<SInterrupteur>())
        {
            _switches.Add(s);
        }

        foreach (Trigger t in _bot.World.Level.AllActors<Trigger>())
        {
            _triggers.Add(t);
        }
    }

    /// <summary>
    /// The nearest switch that opens <paramref name="door"/>, usable by the walker's side, with its use spot on the
    /// walker's side of the door and a route to it.
    /// </summary>
    private bool FindSwitch(SBasePawn pawn, Mover door)
    {
        _switch = null;
        if (door.Tag == Name.None)
        {
            return false;
        }

        Gather();
        PawnType type = ObjectiveNatives.PawnTypeOf(_bot.Controller);
        NavGraph g = _walk.Graph;
        float best = SwitchReach;
        foreach (SInterrupteur s in _switches)
        {
            if (s.bDeleteMe || s.Event != door.Tag || s.TriggerInteraction is not { bUsable: true, bInUse: false } entry ||
                !ObjectiveNatives.UsableByAllows(entry.UsableBy, type))
            {
                continue;
            }

            s.GetUseSpot(pawn, out FVector spot, out _);
            FVector side = spot - _hitAt;
            if ((side.X * _hitNormal.X) + (side.Y * _hitNormal.Y) <= 0f)
            {
                continue;
            }

            float d = NavFollower.Horizontal(spot - pawn.Location);
            int node = g.NearestNode(new FVector(spot.X, spot.Y, s.Location.Z), 0f);
            if (d >= best || node < 0)
            {
                continue;
            }

            FVector floor = new(spot.X, spot.Y, g.Node(node).Floor.Z);
            if (!_walk.SetGoal(pawn, floor))
            {
                continue;
            }

            best = d;
            _switch = s;
            _spot = floor;
        }

        // The last plan may be to another switch: plan to the one chosen.
        return _switch is not null && _walk.SetGoal(pawn, _spot);
    }

    // ---- acting --------------------------------------------------------------------------------------------------

    private bool Act(SBasePawn pawn, float now, ref AiPlan plan)
    {
        SPlayerController pc = _bot.Controller;
        Mover? door = _door;
        if (door is null || door.bDeleteMe)
        {
            Reset();
            return false;
        }

        // Someone opened it (a mate's hack, a timer): go on.
        if (_phase is Phase.Walk or Phase.Face && (door.bOpening || door.KeyNum != 0))
        {
            Done(pawn, now);
            return false;
        }

        float t = now - _phaseAt;
        switch (_phase)
        {
            case Phase.Wait:
                // Opening: stand until it is out of the way.
                plan.Keys = default;
                plan.Crouched = null;
                if (t > 0.3f && (!DoorAhead(pawn, out Mover? still, out _) || !ReferenceEquals(still, door) || t > 4f))
                {
                    Done(pawn, now);
                    return false;
                }

                return true;

            case Phase.Walk:
            {
                NavCommand c = _walk.Steer(pawn, now, pc.Rotation.Yaw & 0xFFFF, sneak: false);
                plan.Keys = c.Keys;
                plan.Yaw = c.Yaw ?? plan.Yaw;
                plan.Tolerance = c.Tolerance;
                plan.Crouched = c.Crouched ?? plan.Crouched;
                if (_walk.Arrived || (NavFollower.Horizontal(_spot - pawn.Location) < 20f && pawn.Physics == EPhysics.PHYS_Walking))
                {
                    Enter(Phase.Face, now);
                    plan.Keys = default;
                }
                else if (_walk.Failed || t > 15f)
                {
                    GiveUp(pawn, now);
                    return false;
                }

                return true;
            }

            case Phase.Face:
            {
                SInterrupteur s = _switch!;
                plan.Keys = default;
                plan.Crouched = null;
                FaceSwitch(pawn, s, ref plan);
                bool listed = Entry(pc, s) is not null;
                if (!listed && t > 0.8f)
                {
                    // Not in the switch's reach yet: a slow step toward it.
                    plan.Keys.BT_Up = 1;
                    plan.Keys.FastSpeed = false;
                }

                if (t > 3f)
                {
                    GiveUp(pawn, now);
                    return false;
                }

                if (listed && t > 0.3f && NavFollower.Horizontal(pawn.Velocity) < 5f &&
                    NavFollower.YawError(pc.Rotation.Yaw & 0xFFFF, plan.Yaw ?? 0) < 1500)
                {
                    Enter(Phase.Press, now);
                    plan.Keys.bUsing = 1;
                }

                return true;
            }

            case Phase.Press:
                // Use held opens the menu (the walking states' DoInteraction).
                plan.Keys = default;
                plan.Crouched = null;
                FaceSwitch(pawn, _switch!, ref plan);
                if (pc.IsInState(NAME_s_MenuInteraction))
                {
                    Enter(Phase.Menu, now);
                    _downHeld = false;
                    plan.Keys.bUsing = 1;
                    return true;
                }

                if (t > 1f)
                {
                    // Nothing opened: let go, and try again from the spot.
                    if (++_tries > 3)
                    {
                        GiveUp(pawn, now);
                        return false;
                    }

                    Enter(Phase.Face, now);
                    return true;
                }

                plan.Keys.bUsing = 1;
                return true;

            case Phase.Menu:
            {
                plan.Keys = default;
                plan.Crouched = null;
                plan.Yaw = null;
                if (!pc.IsInState(NAME_s_MenuInteraction))
                {
                    Enter(Phase.Hack, now);
                    return true;
                }

                SInteractObject? entry = Entry(pc, _switch!);
                plan.Keys.bUsing = 1;
                if (t > 4f)
                {
                    // The switch never came up (out of reach, or taken): let go and leave the door.
                    plan.Keys.bUsing = 0;
                    GiveUp(pawn, now);
                    return true;
                }

                if (entry is null)
                {
                    return true;
                }

                if (ReferenceEquals(pc.IManager?.GetCurrentInteraction(), entry))
                {
                    // Let go on the switch: the hack starts (s_Hack_GoTo walks the pawn to the spot).
                    plan.Keys.bUsing = 0;
                    Enter(Phase.Hack, now);
                    return true;
                }

                // S moves the selection on its rising edge: press, release, press.
                _downHeld = !_downHeld;
                plan.Keys.BT_Down = _downHeld ? (byte)1 : (byte)0;
                return true;
            }

            case Phase.Hack:
            {
                // The hack states own the pawn: no keys (a move over 1000 or the fire mode cancels the hack).
                plan.Keys = default;
                plan.Yaw = null;
                plan.Crouched = null;
                if (door.bOpening || door.KeyNum != 0)
                {
                    if (!HackStates(pc))
                    {
                        Opened++;
                        Done(pawn, now);
                        return false;
                    }

                    return true;
                }

                if (HackStates(pc) || pc.IsInState(NAME_s_MenuInteraction))
                {
                    if (t > 15f)
                    {
                        GiveUp(pawn, now);
                        return false;
                    }

                    return true;
                }

                if (t > 1.5f)
                {
                    // The hack did not open it (cut short, or refused): once more, then the door is shut for us.
                    if (++_tries > 2)
                    {
                        GiveUp(pawn, now);
                        return false;
                    }

                    _walk.SetGoal(pawn, _spot);
                    Enter(Phase.Walk, now);
                }

                return true;
            }
        }

        return false;
    }

    private static bool HackStates(SPlayerController pc) =>
        pc.GetStateName().ToString().StartsWith("s_Hack", StringComparison.Ordinal) ||
        pc.GetStateName().ToString().StartsWith("s_Maintain", StringComparison.Ordinal) ||
        pc.GetStateName().ToString().StartsWith("s_InteractWithObject", StringComparison.Ordinal);

    /// <summary>The switch's entry in the controller's menu list (touched), or null.</summary>
    private static SInteractObject? Entry(SPlayerController pc, SInterrupteur s)
    {
        if (pc.IManager is not { } manager || s.InteractionObj is not { } obj)
        {
            return null;
        }

        foreach (SInteractObject? e in manager.Interactions)
        {
            if (ReferenceEquals(e, obj))
            {
                return obj.CanUse(pc) ? obj : null;
            }
        }

        return null;
    }

    private void FaceSwitch(SBasePawn pawn, SInterrupteur s, ref AiPlan plan)
    {
        FRotator r = (s.Location - AiSight.Eye(pawn)).Rotation();
        plan.Yaw = r.Yaw & 0xFFFF;
        plan.Pitch = r.Pitch;
        plan.Tolerance = 65536;
    }

    private void Enter(Phase phase, float now)
    {
        _phase = phase;
        _phaseAt = now;
    }

    /// <summary>The door is open (or out of the way): the route goes on from here (planned again after a walk to a switch).</summary>
    private void Done(SBasePawn pawn, float now)
    {
        bool walked = _switch is not null;
        _phase = Phase.None;
        _switch = null;
        _walk.Clear();
        _route.Resume();
        if (walked)
        {
            _route.Replan(pawn);
        }

        _nextScan = now + 1f;
    }

    /// <summary>The door could not be opened: shut for the side's plans for a while.</summary>
    private void GiveUp(SBasePawn pawn, float now)
    {
        if (_door is { } door)
        {
            _givenUp[door] = now + AiK.BlockedEdgeSeconds;
        }

        _phase = Phase.None;
        _switch = null;
        _walk.Clear();
        _route.Resume();
        Shut(pawn, now);
    }

    /// <summary>Leaves the edges across the door out of the side's plans and plans the route again.</summary>
    private void Shut(SBasePawn pawn, float now)
    {
        // The trace ran at the waist: the floor is a pawn's half height under the hit.
        _route.BlockAcross(_hitAt.Z - pawn.CollisionHeight, _hitAt, _hitNormal, AiK.DoorBlockHalfWidth);
        _route.Replan(pawn);
        _nextScan = now + ScanPeriod;
    }
}
