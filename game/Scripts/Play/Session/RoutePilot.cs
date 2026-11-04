using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScctGame.Play.Session;

/// <summary>Something that plays the local player instead of the keyboard and mouse, one tick at a time.</summary>
public interface IPlayPilot
{
    /// <summary>Runs one tick; called from <see cref="PlaySession.BeforeClientTick"/>.</summary>
    void Tick(PlaySession session);
}

/// <summary>
/// Walks the local player through a list of points in order (<c>--svm-route</c>), one <see cref="ScriptedPilot"/> leg
/// per point, and reports as it goes: each point reached and when, or where the player got stuck and why. The map
/// editor's route check reads these lines (<c>SVM_ROUTE ...</c>). A test driver, not part of the game.
/// </summary>
public sealed class RoutePilot : IPlayPilot
{
    public const string Prefix = "SVM_ROUTE";

    private readonly IReadOnlyList<FVector> _points;
    private readonly Action<string> _report;
    private ScriptedPilot? _leg;
    private int _next;
    private bool _started;
    private bool _finished;

    public RoutePilot(IReadOnlyList<FVector> points, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(report);
        _points = points;
        _report = report;
    }

    /// <summary>The points reached so far.</summary>
    public int Reached => _next;

    public void Tick(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_finished)
        {
            session.Input.Set("BT_Up", false);
            return;
        }

        if (_leg is null)
        {
            FVector goal = _points[_next];
            FVector face = _next + 1 < _points.Count ? _points[_next + 1] : goal;
            _leg = new ScriptedPilot(_ => (goal, face), PilotArrival.FaceOnly);
        }

        _leg.Tick(session);
        if (!_started && _leg.Phase != PilotPhase.WaitForPawn && session.LocalController?.Pawn is { } pawn)
        {
            // The player is in play: where the walk starts from.
            Report($"start at {Point(pawn.Location)}, {_points.Count} points");
            _started = true;
        }

        double seconds = session.TicksRun / (double)Sim.Hz;
        if (_leg.Phase == PilotPhase.Done)
        {
            Report($"reached {_next + 1} {Point(_points[_next])} at {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            _leg = null;
            if (++_next == _points.Count)
            {
                Report($"done: reached {_next} of {_points.Count} in {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
                _finished = true;
            }
        }
        else if (_leg.Phase == PilotPhase.Failed)
        {
            FVector? at = (session.LocalController?.Pawn as SBasePawn)?.Location;
            Report($"stuck before {_next + 1} {Point(_points[_next])} at {(at is { } a ? Point(a) : "?")} after {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s: {_leg.Failure}");
            Report($"done: reached {_next} of {_points.Count}");
            _finished = true;
        }
    }

    private void Report(string line) => _report($"{Prefix} {line}");

    private static string Point(FVector p) => string.Create(CultureInfo.InvariantCulture, $"({p.X:0},{p.Y:0},{p.Z:0})");
}
