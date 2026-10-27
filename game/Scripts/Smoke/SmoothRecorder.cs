using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScctGame.Play;

namespace OpenScctGame.Smoke;

/// <summary>What the player saw in one frame, as the <see cref="SmoothRecorder"/> read it after every view was drawn.</summary>
public readonly record struct SmoothFrame
{
    public int Index { get; init; }

    public string Phase { get; init; }

    public int Cap { get; init; }

    /// <summary>Wall clock at the end of the frame's process pass, in milliseconds.</summary>
    public double TimeMs { get; init; }

    /// <summary>The frame's delta time, in milliseconds.</summary>
    public double DtMs { get; init; }

    public long Ticks { get; init; }

    /// <summary>The drawn camera (uu, rotation units, horizontal FOV degrees).</summary>
    public FRotator Rotation { get; init; }

    public FVector Location { get; init; }

    public float Fov { get; init; }

    /// <summary>The simulation's view after the last tick.</summary>
    public FRotator SimRotation { get; init; }

    public float SimFov { get; init; }

    public string State { get; init; }

    /// <summary>The scope sights' aim point on the 640 x 480 canvas, or null.</summary>
    public Vector2? Sights { get; init; }

    /// <summary>The drawn position (0 to 1) of the view model's sniping channel (6), or -1.</summary>
    public float ScopeChannel { get; init; }

    /// <summary>Mouse motion events that reached the scene this frame, and their summed horizontal pixels.</summary>
    public int Motions { get; init; }

    public float MotionDx { get; init; }

    /// <summary>Garbage collections (generation 0, 1, 2) that ran during the frame.</summary>
    public int Gc0 { get; init; }

    public int Gc1 { get; init; }

    public int Gc2 { get; init; }

    public double SimMs { get; init; }

    /// <summary>Time the garbage collector paused the process during the frame, in milliseconds.</summary>
    public double GcPauseMs { get; init; }

    /// <summary>Managed bytes allocated during the frame (all threads).</summary>
    public long Allocated { get; init; }

    public double ViewMs { get; init; }
}

/// <summary>
/// Records what the player sees each frame, after the play scene and the HUD have drawn it: the node runs last in the
/// process pass (highest priority value) and sees the mouse events as the scene does. It only reads.
/// </summary>
public partial class SmoothRecorder : Node
{
    private readonly List<SmoothFrame> _frames = new();
    private int _motions;
    private float _motionDx;
    private int _gc0;
    private int _gc1;
    private int _gc2;
    private ulong _lastUsec;
    private TimeSpan _pause;
    private long _allocated;

    public SmoothRecorder()
    {
        Name = "SmoothRecorder";
        ProcessPriority = int.MaxValue;
        ProcessMode = ProcessModeEnum.Always;
    }

    public PlayRoot? Root { get; set; }

    /// <summary>The label the frames are tagged with.</summary>
    public string Phase { get; set; } = "";

    /// <summary>The frame cap the frames are tagged with (0: none).</summary>
    public int Cap { get; set; }

    /// <summary>Recording on (frames are kept).</summary>
    public bool Recording { get; set; }

    public IReadOnlyList<SmoothFrame> Frames => _frames;

    public override void _Ready()
    {
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        _pause = GC.GetTotalPauseDuration();
        _allocated = GC.GetTotalAllocatedBytes(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _motions++;
            _motionDx += motion.Relative.X;
        }
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes(false);
        if (Recording && Root is { Session: { } session, Camera: { } camera })
        {
            (FVector location, FRotator rotation, float fov) = camera.Current;
            _frames.Add(new SmoothFrame
            {
                Index = _frames.Count,
                Phase = Phase,
                Cap = Cap,
                TimeMs = now / 1000.0,
                DtMs = _lastUsec == 0 ? delta * 1000.0 : (now - _lastUsec) / 1000.0,
                Ticks = session.TicksRun,
                Rotation = rotation,
                Location = location,
                Fov = fov,
                SimRotation = session.View.Rotation,
                SimFov = session.View.Fov,
                State = session.LocalController?.GetStateName().ToString() ?? "",
                Sights = Root.Hud?.Snapshot.SpotReticule,
                ScopeChannel = Root.FirstPerson?.ChannelFrame(6) ?? -1f,
                Motions = _motions,
                MotionDx = _motionDx,
                Gc0 = gc0 - _gc0,
                Gc1 = gc1 - _gc1,
                Gc2 = gc2 - _gc2,
                SimMs = Root.Stats.LastSimMs,
                ViewMs = Root.Stats.LastViewMs,
                GcPauseMs = (pause - _pause).TotalMilliseconds,
                Allocated = allocated - _allocated,
            });
        }

        _lastUsec = now;
        _motions = 0;
        _motionDx = 0f;
        _gc0 = gc0;
        _gc1 = gc1;
        _gc2 = gc2;
        _pause = pause;
        _allocated = allocated;
    }
}
