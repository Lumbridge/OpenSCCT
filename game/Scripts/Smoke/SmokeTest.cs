using Godot;

namespace OpenScctGame.Smoke;

public enum SmokeState
{
    Running,
    Passed,
    Failed,
}

public readonly record struct SmokeResult(SmokeState State, string Detail)
{
    public static SmokeResult Running => new(SmokeState.Running, "");

    public static SmokeResult Pass(string detail = "") => new(SmokeState.Passed, detail);

    public static SmokeResult Fail(string reason) => new(SmokeState.Failed, reason);
}

/// <summary>What a smoke test can see while it runs.</summary>
public sealed class SmokeContext(SceneTree tree, App app)
{
    public SceneTree Tree { get; } = tree;

    public App App { get; } = app;

    /// <summary>Frames processed since the test started, counting the current one.</summary>
    public int Frame { get; internal set; }

    /// <summary>Wall-clock seconds since the test started.</summary>
    public double ElapsedSeconds { get; internal set; }
}

/// <summary>
/// A named check run headless with <c>-- --svm-smoke=name</c>. Step is called once per frame
/// until it returns Passed or Failed, so a test can span many frames (load a map, run bots...).
/// </summary>
public abstract class SmokeTest
{
    public abstract string Name { get; }

    public abstract SmokeResult Step(SmokeContext context);
}
