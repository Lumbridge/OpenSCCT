using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Bots;

/// <summary>Where a merc patrols.</summary>
public enum EPatrol : byte
{
    /// <summary>Stays at the spawn.</summary>
    None,

    /// <summary>Between the map's terminals, in walking order from the spawn.</summary>
    Terminals,

    /// <summary>To the spies' spawn points and back to the terminals (hunting).</summary>
    SpySpawns,
}

/// <summary>A merc's route script: the patrol, which part of it this merc takes, and whether it shoots on sight.</summary>
public sealed record MercScript
{
    public EPatrol Patrol { get; init; } = EPatrol.Terminals;

    /// <summary>The first patrol point (so two mercs walk different legs).</summary>
    public int Offset { get; init; }

    /// <summary>Never fires (a merc that only walks its patrol).</summary>
    public bool HoldFire { get; init; }

    public static MercScript Guard(int offset, bool holdFire = false) => new() { Patrol = EPatrol.Terminals, Offset = offset, HoldFire = holdFire };

    public static MercScript Hunt(int offset) => new() { Patrol = EPatrol.SpySpawns, Offset = offset };

    public static MercScript StayHome => new() { Patrol = EPatrol.None, HoldFire = true };

    /// <summary>Stays at the spawn and shoots on sight.</summary>
    public static MercScript HoldGround => new() { Patrol = EPatrol.None };
}

/// <summary>
/// A merc bot: walks its patrol and, unless it holds fire, stops and shoots at the nearest spy it can see (a line of
/// sight test over the map's geometry, within <see cref="BotK.MercEngageRange"/> for a spy standing still and
/// <see cref="BotK.MercMovingTargetRange"/> for a walking one), aiming at the spy's chest and firing the rifle in
/// bursts.
/// </summary>
public sealed class MercBrain : BotBrain
{
    private readonly MercScript _script;
    private readonly List<FVector> _patrol = new();
    private BotNavigator? _nav;
    private NavGrid? _grid;
    private int _leg;
    private SBasePawn? _pawn;
    private float _burstStart = -1f;
    private float _pauseUntil;

    public MercBrain(MercScript script)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
    }

    /// <summary>Bursts started at a visible spy.</summary>
    public int Bursts { get; private set; }

    /// <summary>Frames a spy was in sight.</summary>
    public int SightFrames { get; private set; }

    /// <summary>Times the merc gave way to another pawn (backed off and waited, then went on to the same post).</summary>
    public int GiveWays => _nav?.GiveWays ?? 0;

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"merc leg {_leg}/{_patrol.Count} bursts {Bursts} sight {SightFrames} gave way {GiveWays}");

    public override void Reset()
    {
        _burstStart = -1f;
        _nav?.Clear();
    }

    public override BotInput Think(BotContext context)
    {
        _grid ??= context.Nav.Get(context.Map, spy: false);
        // A merc gives way at the first stuck spell blamed on a pawn: in a doorway he backs out before a spy does.
        _nav ??= new BotNavigator(_grid, BotNavigator.Seed(context.Session.Settings.PlayerName)) { GiveWayAfter = 1 };
        if (!ReferenceEquals(_pawn, context.Pawn))
        {
            _pawn = context.Pawn;
            Reset();
            if (_patrol.Count == 0)
            {
                BuildPatrol(context);
                _leg = _patrol.Count == 0 ? 0 : _script.Offset % _patrol.Count;
            }

            if (_patrol.Count > 0)
            {
                _nav.SetGoal(_patrol[_leg]);
            }
        }

        float now = context.Now;
        if (!_script.HoldFire && FindTarget(context) is { } spy)
        {
            SightFrames++;
            return Shoot(context, spy, now);
        }

        if (_burstStart >= 0f)
        {
            _burstStart = -1f;
        }

        if (_patrol.Count == 0)
        {
            return BotInput.Idle;
        }

        // A merc held up by another pawn gives way (the navigator backs him off and waits) and then goes on to the
        // same post. Turning back to the previous post instead made two mercs in one doorway turn at different
        // times, each into the other, and stay there.
        BotInput walk = _nav.Steer(context.Pawn, now);
        if (_nav.Arrived || _nav.Failed)
        {
            _leg = (_leg + 1) % _patrol.Count;
            _nav.SetGoal(_patrol[_leg]);
        }

        return walk;
    }

    private void BuildPatrol(BotContext context)
    {
        var points = new List<FVector>();
        if (_script.Patrol == EPatrol.SpySpawns)
        {
            foreach (PlayerStart start in context.World.Level.AllActors<PlayerStart>())
            {
                if (start.TeamNumber == 0 && start.bEnabled)
                {
                    AddFloor(points, start.Location);
                }
            }
        }

        if (_script.Patrol is EPatrol.Terminals or EPatrol.SpySpawns)
        {
            foreach (SComputerObjectiveTrigger t in Terminals(context))
            {
                if (UseSpotFloor(context, _grid!, t, out FVector floor, out _) && Beside(floor) is { } guard)
                {
                    points.Add(guard);
                }
            }
        }

        // Keep the points the merc can walk to, in the order they were found.
        var scratch = new List<FVector>();
        foreach (FVector p in points)
        {
            if (!float.IsPositiveInfinity(PathLength(_grid!, context.Pawn, p, scratch)))
            {
                _patrol.Add(p);
            }
        }
    }

    // A guard post beside a use spot (150 to 300 uu from it, on its floor), so the merc does not stand on it.
    private FVector? Beside(FVector spot)
    {
        FVector? best = null;
        float bestScore = float.MaxValue;
        foreach (FVector n in _grid!.Nodes)
        {
            float dz = MathF.Abs(n.Z - spot.Z);
            float d = BotNavigator.Horizontal(n - spot);
            if (dz > 40f || d < BotK.GuardMinDistance || d > BotK.GuardMaxDistance)
            {
                continue;
            }

            float score = MathF.Abs(d - (BotK.GuardMinDistance + BotK.GuardMaxDistance) * 0.5f) + dz;
            if (score < bestScore)
            {
                bestScore = score;
                best = n;
            }
        }

        return best;
    }

    private void AddFloor(List<FVector> points, FVector location)
    {
        int node = _grid!.NearestNode(location, 0f);
        if (node >= 0)
        {
            points.Add(_grid.Nodes[node]);
        }
    }

    private SBasePawn? FindTarget(BotContext context)
    {
        FVector eye = context.Pawn.Location + new FVector(0f, 0f, BotK.EyeHeight);
        SBasePawn? best = null;
        float bestDist = BotK.MercEngageRange * BotK.MercEngageRange;
        foreach (SPawnAttaque spy in context.World.Level.AllActors<SPawnAttaque>())
        {
            if (spy.bDeleteMe || spy.bHidden || spy.Health <= 0 || ReferenceEquals(spy, context.Pawn))
            {
                continue;
            }

            float d2 = (spy.Location - eye).SizeSquared();
            bool still = spy.Velocity.SizeSquared() < BotK.StillSpeed * BotK.StillSpeed;
            if (d2 < bestDist && (still || d2 < BotK.MercMovingTargetRange * BotK.MercMovingTargetRange) &&
                context.CanSee(eye, spy.Location) && !TeammateInTheWay(context, eye, spy.Location))
            {
                best = spy;
                bestDist = d2;
            }
        }

        return best;
    }

    // A merc does not fire through another merc: one standing within 60 uu of the line, nearer than the target.
    private static bool TeammateInTheWay(BotContext context, FVector eye, FVector target)
    {
        FVector d = target - eye;
        float len2 = d.SizeSquared();
        if (len2 <= 0f)
        {
            return false;
        }

        foreach (SPAWNDEFENSE mate in context.World.Level.AllActors<SPAWNDEFENSE>())
        {
            if (ReferenceEquals(mate, context.Pawn) || mate.bDeleteMe)
            {
                continue;
            }

            float t = (mate.Location - eye).Dot(d) / len2;
            if (t <= 0f || t >= 1f)
            {
                continue;
            }

            if ((eye + d * t - mate.Location).SizeSquared() < BotK.FriendlyClearance * BotK.FriendlyClearance)
            {
                return true;
            }
        }

        return false;
    }

    private BotInput Shoot(BotContext context, SBasePawn spy, float now)
    {
        FVector eye = context.Pawn.Location + new FVector(0f, 0f, BotK.EyeHeight);
        FRotator aim = (spy.Location - new FVector(0f, 0f, BotK.MercAimDrop) - eye).Rotation();
        var view = new FRotator(aim.Pitch, aim.Yaw, 0);
        var input = new BotInput { View = view, Aim = view };
        if (now < _pauseUntil)
        {
            return input;
        }

        if (_burstStart < 0f)
        {
            _burstStart = now;
            Bursts++;
            input.FirePressed = true;
            input.FireHeld = true;
            return input;
        }

        if (now - _burstStart < BotK.MercBurstSeconds)
        {
            input.FireHeld = true;
            return input;
        }

        _burstStart = -1f;
        _pauseUntil = now + BotK.MercBurstPause;
        return input;
    }
}
