using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>
/// A stop of a scripted route: a floor point (<paramref name="Point"/>), or a placed actor (<paramref name="Target"/>),
/// reached where the game's use test lets the walker use it when it has a Use entry, else on the floor under it. Both
/// null: the map has no actor of that name.
/// </summary>
public sealed record AiRouteStop(string Name, FVector? Point, Actor? Target);

/// <summary>A scripted route for a bot: its stops in order and where its report lines go.</summary>
public sealed record AiRoute(IReadOnlyList<AiRouteStop> Stops, Action<string> Report);

/// <summary>
/// A bot that walks a scripted route instead of playing: the side's own navigation (walks, crouches, drops, ladders,
/// pipes, ledges, zip lines and the GEs), closed doors opened as the other bots open them (<see cref="DoorAi"/>), no
/// fighting and no gadgets. It reports as it goes: where it started, each stop reached and when, or where it got
/// stuck and why. The map editor's route check reads these lines. Lifts are not ridden.
/// </summary>
public sealed class RouteAi : AiBrain
{
    /// <summary>How long the walker steps on toward an entry it arrived next to but cannot use yet.</summary>
    private const float ApproachSeconds = 1.5f;

    private readonly AiRoute _route;
    private int _next;
    private FVector? _goal;
    private float? _approachUntil;
    private bool _started;
    private bool _finished;

    public RouteAi(AiController bot, AiRoute route)
        : base(bot)
    {
        _route = route ?? throw new ArgumentNullException(nameof(route));
    }

    /// <summary>The stops reached so far.</summary>
    public int Reached => _next;

    public bool Finished => _finished;

    protected override bool Spy => Bot.Pawn is SPawnAttaque;

    public override string Status => _finished ? $"route done, {_next} of {_route.Stops.Count} reached" : $"route to stop {_next + 1} of {_route.Stops.Count}";

    protected override int Choose(float now) => 0;

    /// <summary>The walker lost its pawn: a route under way ends there.</summary>
    public override void Reset()
    {
        base.Reset();
        if (_started && !_finished)
        {
            Stuck(Bot.World.Now, "the walker died");
        }
    }

    protected override void Act(float now, ref AiPlan plan)
    {
        if (_finished)
        {
            plan.Keys = default;
            return;
        }

        if (!_started)
        {
            _started = true;
            Report($"start at {Point(Feet)}, {_route.Stops.Count} stop{(_route.Stops.Count == 1 ? "" : "s")}");
        }

        AiRouteStop stop = _route.Stops[_next];
        if (_goal is null)
        {
            if (FloorOf(stop, out string? problem) is not { } goal)
            {
                Stuck(now, problem!);
                return;
            }

            _goal = goal;
        }

        if (!MoveTo(_goal.Value, now, ref plan, sneak: false))
        {
            Stuck(now, Follower.Plans == 0 ? "no way there on the side's navigation" : "it could not get on from here after planning again");
            return;
        }

        // A ladder exit or jump can pass through the arrival radius before landing. Clearing the path then
        // makes the next stop plan from mid-animation, where there may be no reachable floor node yet.
        if (!Follower.Arrived || Pawn.Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        if (stop.Target is SBasicGamePlayObject { InteractionObj: { } entry } && !CanUse(entry, Pawn.Location))
        {
            // The walk ends a few units off the floor it planned for, which can be outside the use cylinder: step on
            // toward the entry for a moment, as a player would.
            _approachUntil ??= now + ApproachSeconds;
            if (now < _approachUntil)
            {
                FVector to = entry.Location - Pawn.Location;
                plan.Keys = default;
                plan.Keys.BT_Up = 1;
                plan.Yaw = new FVector(to.X, to.Y, 0f).Rotation().Yaw & 0xFFFF;
                plan.Tolerance = 4000;
                return;
            }

            Stuck(now, $"next to {stop.Name}, but the game's use test refuses it from where the walker stands");
            return;
        }

        Report($"reached {_next + 1} {stop.Name} at {Seconds(now)} s");
        Follower.Clear();
        _goal = null;
        _approachUntil = null;
        if (++_next == _route.Stops.Count)
        {
            Report($"done: reached {_next} of {_route.Stops.Count} in {Seconds(now)} s");
            _finished = true;
        }
    }

    /// <summary>The floor to walk to for <paramref name="stop"/>, or null and why there is none.</summary>
    private FVector? FloorOf(AiRouteStop stop, out string? problem)
    {
        problem = null;
        if (stop.Point is { } point)
        {
            return point;
        }

        if (stop.Target is SBasicGamePlayObject { InteractionObj: { } entry })
        {
            if (UseFloor(entry) is { } floor)
            {
                return floor;
            }

            problem = $"no floor the walker gets to from here passes the game's use test for {stop.Name}";
            return null;
        }

        if (stop.Target is { } actor)
        {
            int node = Follower.Graph.NearestNode(actor.Location, actor.CollisionHeight);
            if (node >= 0)
            {
                return Follower.Graph.Node(node).Floor;
            }

            problem = $"no floor of the side's navigation under {stop.Name}";
            return null;
        }

        problem = $"the map has no actor {stop.Name}";
        return null;
    }

    /// <summary>The cheapest floor the walker gets to from where it stands from which the game's use test passes.</summary>
    private FVector? UseFloor(SInteractObject entry)
    {
        NavGraph graph = Follower.Graph;
        int start = graph.NearestNode(Pawn.Location, Pawn.CollisionHeight);
        if (start < 0)
        {
            return null;
        }

        float[] costs = ReachabilityCosts(start, float.MaxValue);
        // Floors from 220 below the entry to 20 above it (a switch on a wall, a box on a shelf).
        List<int> near = NodesAround(entry.Location - new FVector(0f, 0f, 100f), Math.Max(entry.CollisionRadius, entry.ItRadius) + Pawn.CollisionRadius);
        FVector? best = null;
        float bestCost = float.MaxValue;
        foreach (int n in near)
        {
            FVector floor = graph.Node(n).Floor;
            if (float.IsFinite(costs[n]) && costs[n] < bestCost && CanUse(entry, floor + new FVector(0f, 0f, Pawn.CollisionHeight)))
            {
                best = floor;
                bestCost = costs[n];
            }
        }

        return best;
    }

    private bool CanUse(SInteractObject entry, FVector pawnLocation) =>
        (!entry.bInteractInFront || entry.IsOnFrontSide(pawnLocation)) &&
        InteractionReach.From(entry, Pawn, pawnLocation, Pawn.CollisionRadius, Pawn.CollisionHeight);

    private void Stuck(float now, string why)
    {
        AiRouteStop stop = _route.Stops[_next];
        string at = Bot.Pawn is { } pawn ? Point(NavFollower.Feet(pawn)) : "?";
        Report($"stuck before {_next + 1} {stop.Name} at {at} after {Seconds(now)} s: {why}");
        Report($"done: reached {_next} of {_route.Stops.Count}");
        Follower.Clear();
        _finished = true;
    }

    private void Report(string line) => _route.Report(line);

    private static string Seconds(float now) => now.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Point(FVector p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0},{p.Y:0},{p.Z:0})");
}
