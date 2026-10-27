using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Bots;

/// <summary>
/// A hub visitor (an OpenSCCT addition, spec/additions/hub.md): walks from one random floor point of the hub's walk
/// graph to another, stops there a while, and walks on, sometimes running, a spy sometimes crouched. The points are
/// drawn from the bot's own seeded stream, so a run plays the same way again. <see cref="HomeShare"/> of the walks
/// end near the plaza, so the crowd stays denser there, as players gather where they spawn.
/// </summary>
public sealed class HubWanderBrain : BotBrain
{
    private readonly NavGrid _grid;
    private readonly SimRandom _random;
    private readonly FVector _home;
    private readonly float _homeRadius;
    private readonly bool _spy;
    private BotNavigator? _nav;
    private Pawn? _pawn;
    private float _loiterUntil;
    private bool _run;
    private float _crouchPressAt = -10f;
    private bool _wantCrouch;
    private bool _walking;

    public HubWanderBrain(NavGrid grid, int seed, bool spy, FVector home, float homeRadius = 1600f)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _random = new SimRandom((ulong)(uint)seed, 13, 1);
        _spy = spy;
        _home = home;
        _homeRadius = homeRadius;
    }

    /// <summary>Share of walks that end within the home radius of the plaza.</summary>
    public float HomeShare { get; init; } = 0.5f;

    /// <summary>Walks finished.</summary>
    public int Arrivals { get; private set; }

    /// <summary>Walks given up (no path, or stuck for good).</summary>
    public int Failures { get; private set; }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"wander arrivals {Arrivals} failures {Failures} plans {_nav?.Plans ?? 0}");

    public override void Reset()
    {
        _nav?.Clear();
        _pawn = null;
        _walking = false;
    }

    public override BotInput Think(BotContext context)
    {
        _nav ??= new BotNavigator(_grid, BotNavigator.Seed(context.Session.Settings.PlayerName));
        float now = context.Now;
        if (!ReferenceEquals(_pawn, context.Pawn))
        {
            _pawn = context.Pawn;
            _loiterUntil = now + _random.RandRange(0.5f, 2f);
            _nav.Clear();
            _walking = false;
        }

        if (now < _loiterUntil)
        {
            return BotInput.Idle;
        }

        if (!_walking)
        {
            if (!NextGoal(context.Pawn))
            {
                _loiterUntil = now + 1f;
                return BotInput.Idle;
            }

            _walking = true;
        }

        BotInput input = _nav.Steer(context.Pawn, now);
        if (_nav.Arrived || _nav.Failed)
        {
            if (_nav.Arrived)
            {
                Arrivals++;
            }
            else
            {
                Failures++;
            }

            _nav.Clear();
            _walking = false;
            _loiterUntil = now + _random.RandRange(1f, 4f);
            return BotInput.Idle;
        }

        input.FastSpeed = _run;
        if (_spy && context.Pawn.Physics == EPhysics.PHYS_Walking && context.Pawn.bIsCrouched != _wantCrouch &&
            now - _crouchPressAt > SpyBrain.CrouchRetrySeconds)
        {
            _crouchPressAt = now;
            input.bCrouch = 1;
        }

        return input;
    }

    private bool NextGoal(Pawn pawn)
    {
        IReadOnlyList<FVector> nodes = _grid.Nodes;
        if (nodes.Count == 0)
        {
            return false;
        }

        bool home = _random.FRand() < HomeShare;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            FVector n = nodes[_random.Rand(nodes.Count)];
            if (home && BotNavigator.Horizontal(n - _home) > _homeRadius)
            {
                continue;
            }

            if (BotNavigator.Horizontal(n - pawn.Location) < 300f)
            {
                continue;
            }

            _run = _random.FRand() < 0.35f;
            _wantCrouch = _spy && _random.FRand() < 0.3f;
            _nav!.SetGoal(n);
            return true;
        }

        return false;
    }
}
