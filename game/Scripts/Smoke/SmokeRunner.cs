using System;
using Godot;

namespace OpenScctGame.Smoke;

/// <summary>
/// Runs one smoke test and quits. Prints "SVM_SMOKE name OK" and exits with 0 on success,
/// "SVM_SMOKE name FAIL reason" and exits with 1 on failure, or exits with 2 for an unknown name.
/// </summary>
public partial class SmokeRunner : Node
{
    public const int ExitPassed = 0;
    public const int ExitFailed = 1;
    public const int ExitUnknownTest = 2;

    private readonly string _name;
    private readonly double _timeoutSeconds;
    private SmokeTest? _test;
    private SmokeContext? _context;
    private ulong _startUsec;
    private bool _finished;

    // Godot needs a parameterless constructor for every script type.
    public SmokeRunner()
        : this("", LaunchOptions.DefaultSmokeTimeoutSeconds)
    {
    }

    public SmokeRunner(string name, double timeoutSeconds)
    {
        _name = name;
        _timeoutSeconds = timeoutSeconds;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _test = SmokeTests.Create(_name);
        if (_test is null)
        {
            Finish(ExitUnknownTest, $"FAIL unknown smoke test (known: {string.Join(", ", SmokeTests.Names)})");
            return;
        }
        _context = new SmokeContext(GetTree(), App.From(this));
        _startUsec = Time.GetTicksUsec();
        GD.Print($"SVM_SMOKE {_name} START");
    }

    public override void _Process(double delta)
    {
        if (_finished || _test is null || _context is null)
        {
            return;
        }

        _context.Frame++;
        _context.ElapsedSeconds = (Time.GetTicksUsec() - _startUsec) / 1_000_000.0;

        SmokeResult result;
        try
        {
            result = _test.Step(_context);
        }
        catch (Exception ex)
        {
            result = SmokeResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            GD.PrintErr(ex.ToString());
        }

        if (result.State == SmokeState.Running && _context.ElapsedSeconds > _timeoutSeconds)
        {
            result = SmokeResult.Fail($"timed out after {_timeoutSeconds} s");
        }

        switch (result.State)
        {
            case SmokeState.Passed:
                Finish(ExitPassed, Suffix("OK", result.Detail));
                break;
            case SmokeState.Failed:
                Finish(ExitFailed, Suffix("FAIL", result.Detail));
                break;
            default:
                break;
        }
    }

    private static string Suffix(string verdict, string detail) =>
        detail.Length == 0 ? verdict : $"{verdict} {detail}";

    private void Finish(int exitCode, string verdictLine)
    {
        _finished = true;
        SetProcess(false);
        string line = $"SVM_SMOKE {_name} {verdictLine}";
        if (exitCode == ExitPassed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PrintErr(line);
        }

        // Godot wrappers nothing holds any more (images the render checks read back) are finalized now, while the
        // engine still runs: finalized after its shutdown they crashed the exit (0xC0000005 in GodotObject.Finalize).
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GetTree().Quit(exitCode);
    }
}
