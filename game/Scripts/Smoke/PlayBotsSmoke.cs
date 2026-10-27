using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-bots</c>: SteeE Story with 3 server bots next to the player (a 2v2). The player stands
/// still; the client must draw a pawn for every bot and at least two of them must walk away from where they
/// were 10 s into the match, so bots are seen in play and not only in headless matches.
/// </summary>
public sealed class PlayBotsSmoke : PlaySmoke
{
    public const string TestName = "play-bots";

    public const int BotCount = 3;

    /// <summary>How far a bot pawn must get from where it was first drawn to count as moving (uu).</summary>
    public const float MoveDistance = 300f;

    /// <summary>Ticks before positions are taken, so pawns placed at their starts after the first replication do not count as moving.</summary>
    public const int SettleTicks = 60 * 10;

    private readonly Dictionary<SBasePawn, FVector> _firstSeen = new(ReferenceEqualityComparer.Instance);

    public override string Name => TestName;

    protected override int Bots => BotCount;

    protected override bool Piloted => false;

    protected override int MaxTicks => 60 * 90;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc) => null;

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        if (root.Pawns is not { } pawns || root.Session is not { } session)
        {
            return null;
        }

        SBasePawn? local = session.LocalPawn;
        int drawn = 0;
        int moved = 0;
        foreach (SimPawnView view in pawns.Views)
        {
            if (view.Pawn is not { } pawn || ReferenceEquals(pawn, local))
            {
                continue;
            }

            drawn++;
            if (root.Stats.Ticks < SettleTicks)
            {
                continue;
            }

            if (!_firstSeen.TryGetValue(pawn, out FVector first))
            {
                _firstSeen[pawn] = pawn.Location;
            }
            else if ((pawn.Location - first).SizeSquared() > MoveDistance * MoveDistance)
            {
                moved++;
            }
        }

        if (drawn >= BotCount && moved >= 2)
        {
            return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture,
                $"bots drawn={drawn} moving={moved} at tick {root.Stats.Ticks}"));
        }

        return root.Stats.Ticks >= MaxTicks
            ? SmokeResult.Fail(string.Create(CultureInfo.InvariantCulture, $"bots drawn={drawn} moving={moved} after {MaxTicks} ticks"))
            : null;
    }
}
