using System;
using Godot;

namespace OpenScctGame.Render;

/// <summary>Render first-use effects in a private world while the play scene is loading.</summary>
public partial class GameplayFxWarmup : SubViewport
{
    private int _frames;
    private bool _subscribed;
    private Action? _completed;
    public Node3D Samples { get; } = new() { Name = "Samples" };

    public GameplayFxWarmup()
    {
        Name = "GameplayFxWarmup";
        Size = new Vector2I(64, 64);
        OwnWorld3D = true;
        RenderTargetUpdateMode = UpdateMode.Always;
        AddChild(Samples);
        AddChild(new Camera3D { Position = new Vector3(0f, 0f, 3f), Current = true, Far = 1000f });
    }

    public static GameplayFxWarmup For(Viewport target) => new()
    {
        // MSAA and motion-vector variants must match the viewport used for gameplay.
        Msaa3D = target.Msaa3D,
        UseTaa = target.UseTaa,
        ScreenSpaceAA = target.ScreenSpaceAA,
        UseDebanding = target.UseDebanding,
    };

    public void Begin(Action completed)
    {
        _completed = completed;
        if (DisplayServer.GetName() == "headless") Finish();
        else
        {
            _subscribed = true;
            RenderingServer.FramePostDraw += AfterDraw;
        }
    }

    private void AfterDraw()
    {
        // Let CPU particles emit and render their instanced geometry as well as the static samples.
        if (++_frames >= 3) Finish();
    }

    private void Finish()
    {
        Unsubscribe();
        RenderTargetUpdateMode = UpdateMode.Disabled;
        var completed = _completed;
        _completed = null;
        completed?.Invoke();
        QueueFree();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        RenderingServer.FramePostDraw -= AfterDraw;
        _subscribed = false;
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        _completed = null;
    }
}
