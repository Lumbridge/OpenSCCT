using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Bots;

/// <summary>
/// A spy that walks, crouched, from one merc start to the next (the map's PlayerStarts of team 1 it can reach),
/// stopping in the open 350 to 550 uu short of each and standing there for <see cref="BotK.RaidLoiterSeconds"/>,
/// starting at its own offset. It never uses anything: it is the target of the eliminations script.
/// </summary>
public sealed class SpyRaidBrain : BotBrain
{
    private readonly int _offset;
    private readonly List<FVector> _points = new();
    private BotNavigator? _nav;
    private NavGrid? _grid;
    private Pawn? _pawn;
    private int _leg;
    private float _crouchPressAt = -10f;
    private float _loiterUntil;

    public SpyRaidBrain(int offset)
    {
        _offset = offset;
    }

    /// <summary>Merc starts reached.</summary>
    public int Visits { get; private set; }

    /// <summary>How far from a merc start the spy stops.</summary>
    public const float StopMin = 350f;

    public const float StopMax = 550f;

    private static FVector? Near(NavGrid grid, FVector start)
    {
        FVector? best = null;
        float bestScore = float.MaxValue;
        foreach (FVector n in grid.Nodes)
        {
            float dz = System.MathF.Abs(n.Z - start.Z);
            float d = BotNavigator.Horizontal(n - start);
            if (dz > 40f || d < StopMin || d > StopMax)
            {
                continue;
            }

            float score = System.MathF.Abs(d - (StopMin + StopMax) * 0.5f) + dz;
            if (score < bestScore)
            {
                bestScore = score;
                best = n;
            }
        }

        return best;
    }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"raider leg {_leg}/{_points.Count} visits {Visits} plans {_nav?.Plans ?? 0}");

    public override void Reset() => _nav?.Clear();

    public override BotInput Think(BotContext context)
    {
        _grid ??= context.Nav.Get(context.Map, spy: true);
        _nav ??= new BotNavigator(_grid, BotNavigator.Seed(context.Session.Settings.PlayerName)) { GiveWayAfter = 2 };
        float now = context.Now;
        if (!ReferenceEquals(_pawn, context.Pawn))
        {
            _pawn = context.Pawn;
            _loiterUntil = 0f;
            if (_points.Count == 0)
            {
                var scratch = new List<FVector>();
                foreach (PlayerStart start in context.World.Level.AllActors<PlayerStart>())
                {
                    if (start.TeamNumber != 1 || !start.bEnabled || start.bMercDeathMatchStart)
                    {
                        continue;
                    }

                    // Stop short of the start (the merc stands on it): a floor point on its level, out in the open.
                    int node = _grid.NearestNode(start.Location, 0f);
                    if (node >= 0 && Near(_grid, _grid.Nodes[node]) is { } stop &&
                        !float.IsPositiveInfinity(PathLength(_grid, context.Pawn, stop, scratch)))
                    {
                        _points.Add(stop);
                    }
                }
            }

            if (_points.Count == 0)
            {
                return BotInput.Idle;
            }

            _leg = _offset % _points.Count;
            _nav.SetGoal(_points[_leg]);
        }

        if (_points.Count == 0)
        {
            return BotInput.Idle;
        }

        if (now < _loiterUntil)
        {
            return BotInput.Idle;
        }

        BotInput input = _nav.Steer(context.Pawn, now);
        if (_nav.Arrived || _nav.Failed)
        {
            if (_nav.Arrived)
            {
                Visits++;
                _loiterUntil = now + BotK.RaidLoiterSeconds;
            }

            _leg = (_leg + 1) % _points.Count;
            _nav.SetGoal(_points[_leg]);
        }

        if (!context.Pawn.bIsCrouched && context.Pawn.Physics == EPhysics.PHYS_Walking && now - _crouchPressAt > SpyBrain.CrouchRetrySeconds)
        {
            _crouchPressAt = now;
            input.bCrouch = 1;
        }

        return input;
    }
}
