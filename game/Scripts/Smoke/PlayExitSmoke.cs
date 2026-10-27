using System;
using System.Globalization;
using Godot;
using OpenScctGame.Play;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-exit</c>: two play sessions in one process, each left the way the in-game menu leaves one
/// (the session stops, the main menu opens, the play scene is freed), then the game quits. A crash at exit happens after
/// the verdict line, so the run passes only when the process also ends with code 0 and prints no crash report:
/// <c>tools/ci/godot-smoke.ps1</c> checks the exit code, and a windowed run shows what the player sees (audio, HUD and
/// renderer on). <c>SVM_EXIT_SESSIONS</c> sets the number of sessions (default 2).
/// </summary>
public sealed class PlayExitSmoke : SmokeTest
{
    public const string TestName = "play-exit";

    /// <summary>Seconds each session plays once its local pawn exists.</summary>
    public const double PlaySeconds = 4.0;

    /// <summary>Seconds on the main menu between sessions.</summary>
    public const double MenuSeconds = 1.0;

    private PlayRoot? _root;
    private int _sessions;
    private int _done;
    private double _phaseStart = -1;
    private bool _onMenu;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_sessions == 0)
        {
            _sessions = int.TryParse(System.Environment.GetEnvironmentVariable("SVM_EXIT_SESSIONS"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 2;
        }

        if (_onMenu)
        {
            if (context.ElapsedSeconds - _phaseStart < MenuSeconds)
            {
                return SmokeResult.Running;
            }

            _onMenu = false;
            if (_done >= _sessions)
            {
                return SmokeResult.Pass(string.Create(CultureInfo.InvariantCulture, $"sessions={_done}"));
            }
        }

        if (_root is null)
        {
            _root = PlayInspectSmoke.StartPlay(context, bots: 1);
            _phaseStart = -1;
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return SmokeResult.Fail("session " + (_done + 1) + ": " + error);
        }

        if (!_root.IsLoaded || _root.Session?.LocalPawn is null)
        {
            return SmokeResult.Running;
        }

        if (_phaseStart < 0)
        {
            _phaseStart = context.ElapsedSeconds;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_EXIT session {_done + 1} playing"));
        }

        if (context.ElapsedSeconds - _phaseStart < PlaySeconds)
        {
            return SmokeResult.Running;
        }

        // PlayMenu.Leave: stop the session, open the main menu, free the play scene.
        _root.Session?.Stop();
        FrontEnd.ShowMainMenu(context.Tree);
        if (GodotObject.IsInstanceValid(_root) && !_root.IsQueuedForDeletion())
        {
            _root.QueueFree();
        }

        _root = null;
        _done++;
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_EXIT session {_done} left"));
        _onMenu = true;
        _phaseStart = context.ElapsedSeconds;
        return SmokeResult.Running;
    }
}
