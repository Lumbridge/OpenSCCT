using System;
using Godot;

namespace OpenScctGame.Options;

/// <summary>
/// The automatic render scale (auto_scale_fps): lowers the 3D resolution a step at a time while the frame rate is
/// under the target, and raises it again when there is room, between 50% and the Render scale setting. Both renderers
/// read the root viewport's scale; Fast resamples its frame in the pass that shows it, so a lower scale costs it
/// nothing extra. Added under the root window at start.
/// </summary>
public partial class DynamicRenderScale : Node
{
    public const string NodeName = "DynamicRenderScale";

    public const float MinScale = 0.5f;
    private const float Step = 0.05f;
    // The frame rate is read over windows this long; the frames just after a change (targets made again) are left out.
    private const double WindowSeconds = 0.5, SettleSeconds = 0.25;
    // Under this share of the target the scale goes down; a raise is kept only while the rate stays over it.
    private const double Low = 0.94;
    private const double FirstRaiseWait = 4.0, LongestRaiseWait = 60.0;
    // A step down that gains less than this is not the GPU's doing: the scale goes back and holds for a while.
    private const double LeastGain = 1.02, FloorSeconds = 15.0;

    private float _scale = 1f, _floor = MinScale;
    private double _windowTime, _settle, _sinceGood, _raiseWait = FirstRaiseWait, _floorLeft, _rateBeforeDrop;
    private int _windowFrames;
    private bool _dropped, _raised, _active;

    public DynamicRenderScale()
    {
        Name = NodeName;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>The settings the target is taken from (the current ones when null).</summary>
    public GameSettings? Settings { get; set; }

    /// <summary>The scale in use.</summary>
    public float Scale => _scale;

    /// <summary>Adds the node under <paramref name="parent"/> (deferred, so it works while the tree is being set up).</summary>
    public static DynamicRenderScale Attach(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.GetNodeOrNull<DynamicRenderScale>(NodeName) is { } existing)
        {
            return existing;
        }

        var node = new DynamicRenderScale();
        parent.CallDeferred(Node.MethodName.AddChild, node);
        return node;
    }

    /// <summary>
    /// The frame rate the scale can be asked to hold: the target, but no more than the frame cap or, with V-Sync on,
    /// the screen's refresh rate (the game cannot draw faster than either).
    /// </summary>
    public static double Reachable(int target, int frameCap, bool vsync, double refreshHz)
    {
        double rate = target;
        if (frameCap > 0) rate = Math.Min(rate, frameCap);
        if (vsync && refreshHz > 1.0) rate = Math.Min(rate, refreshHz);
        return rate;
    }

    public override void _Process(double delta)
    {
        GameSettings s = Settings ?? SettingsStore.Current;
        Viewport root = GetTree().Root;
        float highest = Math.Clamp(s.RenderScale, MinScale, 1f);
        if (s.AutoScaleFps <= 0)
        {
            // Off: the setting's own scale once, then the viewport is left alone (SettingsApplier and the tools set it).
            if (_active)
            {
                _active = false;
                Reset(highest);
                Apply(root);
            }

            return;
        }

        if (!_active)
        {
            _active = true;
            Reset(highest);
        }

        _scale = Math.Clamp(_scale, MinScale, highest);
        // A load or a pause says nothing about the renderer's speed.
        if (delta > 0.25)
        {
            _windowTime = 0;
            _windowFrames = 0;
            Apply(root);
            return;
        }

        if (_settle > 0)
        {
            _settle -= delta;
            Apply(root);
            return;
        }

        _windowTime += delta;
        _windowFrames++;
        if (_floorLeft > 0)
        {
            _floorLeft -= delta;
            if (_floorLeft <= 0) _floor = MinScale;
        }

        if (_windowTime >= WindowSeconds)
        {
            double rate = _windowFrames / _windowTime;
            _windowTime = 0;
            _windowFrames = 0;
            bool vsync = DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled;
            double target = Reachable(s.AutoScaleFps, Engine.MaxFps, vsync, DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen()));
            Decide(rate, target, highest);
        }

        Apply(root);
    }

    private void Decide(double rate, double target, float highest)
    {
        if (rate < target * Low)
        {
            _sinceGood = 0;
            if (_raised)
            {
                // The raise just tried did not hold: back down, and wait longer before the next try.
                _raised = false;
                _raiseWait = Math.Min(LongestRaiseWait, _raiseWait * 2.0);
                Set(_scale - Step, highest);
                _dropped = false;
                return;
            }

            if (_dropped && rate < _rateBeforeDrop * LeastGain)
            {
                // The last step down bought nothing: the processor or the cap holds the rate, not the resolution.
                _dropped = false;
                _floor = Math.Min(highest, _scale + Step);
                _floorLeft = FloorSeconds;
                Set(_floor, highest);
                return;
            }

            if (_scale > _floor + 0.001f)
            {
                _rateBeforeDrop = rate;
                _dropped = true;
                // Far under the target: two steps at once.
                Set(_scale - (rate < target * 0.8 ? 2f * Step : Step), highest);
            }

            return;
        }

        _dropped = false;
        if (_raised)
        {
            // The raise held.
            _raised = false;
            _raiseWait = FirstRaiseWait;
        }

        if (_scale >= highest - 0.001f) return;
        _sinceGood += WindowSeconds;
        // With room to spare by the pixel count (an uncapped rate well over the target) the raise needs no wait.
        float next = Math.Min(highest, _scale + Step);
        bool room = rate * (_scale * _scale) / (next * next) >= target * 1.03;
        if (!room && _sinceGood < _raiseWait) return;
        _sinceGood = 0;
        _raised = !room;
        Set(next, highest);
    }

    private void Set(float scale, float highest)
    {
        scale = MathF.Round(Math.Clamp(scale, Math.Max(MinScale, _floor), highest) / Step) * Step;
        scale = Math.Clamp(scale, MinScale, highest);
        if (Math.Abs(scale - _scale) < 0.001f) return;
        _scale = scale;
        _settle = SettleSeconds;
        _windowTime = 0;
        _windowFrames = 0;
    }

    private void Reset(float scale)
    {
        _scale = scale;
        _floor = MinScale;
        _floorLeft = 0;
        _settle = 0;
        _windowTime = 0;
        _windowFrames = 0;
        _sinceGood = 0;
        _raiseWait = FirstRaiseWait;
        _dropped = _raised = false;
    }

    private void Apply(Viewport root)
    {
        if (Math.Abs(root.Scaling3DScale - _scale) > 0.0005f) root.Scaling3DScale = _scale;
    }
}
