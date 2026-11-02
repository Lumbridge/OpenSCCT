using System;
using System.Linq;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

public partial class FrameRateSlider : VBoxContainer
{
    private readonly NotchedSlider _slider = new()
    {
        MinValue = DisplayFrameCap.Min, MaxValue = DisplayFrameCap.Max, Step = 1,
        Notches = DisplayFrameCap.Presets.Select(v => (double)v).ToArray(),
        Scrollable = false, CustomMinimumSize = new Vector2(160, 24),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
    private readonly SpinBox _number = new()
    {
        MinValue = DisplayFrameCap.Min, MaxValue = DisplayFrameCap.Max, Step = 1,
        SelectAllOnFocus = true, CustomMinimumSize = new Vector2(90, 0),
    };
    private readonly Label _status = new() { ClipText = true };
    public string Text => _status.Text;
    public event Action<int>? Changed;

    public FrameRateSlider()
    {
        var row = new HBoxContainer();
        row.AddChild(_slider);
        row.AddChild(_number);
        AddChild(row);
        var modes = new HFlowContainer();
        var monitor = new Button { Text = "Match refresh rate" };
        var unlimited = new Button { Text = "Unlimited" };
        modes.AddChild(monitor);
        modes.AddChild(unlimited);
        AddChild(modes);
        AddChild(_status);
        _slider.ValueChanged += v => Select((int)_slider.Resolve(v));
        _slider.DragEnded += _ => Select((int)_slider.Resolve(_slider.Value, released: true));
        _number.ValueChanged += v => Select((int)v);
        monitor.Pressed += () => Select(DisplayFrameCap.Monitor);
        unlimited.Pressed += () => Select(DisplayFrameCap.Unlimited);
    }

    public void Select(int fps) => Changed?.Invoke(DisplayFrameCap.Clamp(fps));

    public void Refresh(int fps, double hz, bool mixed)
    {
        _status.Text = mixed ? "Per-role limits" : DisplayFrameCap.Label(fps, hz);
        int numeric = fps > 0 ? fps : Math.Clamp(DisplayFrameCap.ScreenFps(hz), DisplayFrameCap.Min, DisplayFrameCap.Max);
        _slider.SetValueNoSignal(numeric);
        _number.SetValueNoSignal(numeric);
    }
}
