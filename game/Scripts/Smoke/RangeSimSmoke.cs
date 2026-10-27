using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=range-sim</c>: TestRange played without any game content, the check CI runs on a TestRange it
/// generates with <c>tools/testmap/generate.py --no-classes</c> (no install there: no character meshes, HUD package,
/// sounds or key layout files). Launched as <c>--svm-play=TestRange --side=spy --rules=playlevel --bots=3</c> runs it
/// (host thread, wall clock). The player walks forward and back with the layout's keys (KeyPilot: Godot input events,
/// PlayInput, the client, the loopback, the server), and the local pawn must move; the three bots must be on the
/// client and at least two of them must move 300 uu. What is drawn is not checked (the TestRange smokes with content
/// do that): this covers the generator, the SVMap loader, collision, the bots' navigation and the netcode.
/// </summary>
public sealed class RangeSimSmoke : SmokeTest
{
    public const string TestName = "range-sim";

    private const int BotCount = 3;
    private const float BotMoveDistance = 300f;
    private const float WalkDistance = 50f;
    private const double MaxSeconds = 120.0;
    private const int TeardownFrames = 6;

    private readonly Dictionary<SBasePawn, FVector> _firstSeen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SBasePawn> _moved = new(ReferenceEqualityComparer.Instance);
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private FVector _walkFrom;
    private float _walked;
    private double _start = -1;
    private SmokeResult _final;
    private int _teardown = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _root = TestRange.Start(context, PlaySide.Spy, BotCount);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return context.ElapsedSeconds > MaxSeconds ? Finish(SmokeResult.Fail("TestRange did not load")) : SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (_start < 0)
        {
            _start = context.ElapsedSeconds;
        }

        double elapsed = context.ElapsedSeconds - _start;
        SBasePawn? local = session.LocalPawn;
        if (_keys is null && local is not null && elapsed > 2.0)
        {
            _keys = KeyPilot.Attach(_root);
            _keys.Play([new KeyStep("forward", 2.0) { Hold = ["BT_Up"] }, new KeyStep("back", 2.0) { Hold = ["BT_Down"] }]);
            _walkFrom = local.Location;
        }

        if (_keys is not null && local is not null)
        {
            _keys.Update();
            _walked = MathF.Max(_walked, Horizontal(local.Location - _walkFrom));
        }

        int onClient = 0;
        foreach (SBasePawn pawn in session.Client.World.Level.AllActors<SBasePawn>())
        {
            if (pawn.bDeleteMe || ReferenceEquals(pawn, local) || pawn.Health <= 0)
            {
                continue;
            }

            onClient++;
            if (elapsed < 10.0)
            {
                continue;
            }

            if (!_firstSeen.TryGetValue(pawn, out FVector first))
            {
                _firstSeen[pawn] = pawn.Location;
            }
            else if ((pawn.Location - first).SizeSquared() > BotMoveDistance * BotMoveDistance)
            {
                _moved.Add(pawn);
            }
        }

        bool walked = _keys is { Done: true } && _walked > WalkDistance;
        bool enough = walked && onClient >= BotCount && _moved.Count >= 2;
        if (!enough && elapsed < MaxSeconds)
        {
            return SmokeResult.Running;
        }

        string bots = session.Bots is null ? "" : string.Join("; ", session.Bots.Bots.Select(b => $"{b.Controller.PlayerReplicationInfo?.PlayerName}: {b.Status}"));
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"player walked {_walked:F0} uu with the keys (missing [{string.Join(",", _keys?.Missing ?? [])}]), bots on the client {onClient}, moving {_moved.Count} after {elapsed:F1} s; {bots}");
        GD.Print("SVM_RANGE sim " + summary);
        return Finish(enough ? SmokeResult.Pass(summary) : SmokeResult.Fail(summary));
    }

    private static float Horizontal(FVector v) => MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
