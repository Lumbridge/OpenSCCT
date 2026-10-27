using System;
using Godot;

namespace OpenScctGame.Options;

/// <summary>
/// Keeps the frame cap on the refresh rate of the screen the window is on (docs/RENDERING.md, frame pacing): every
/// frame it checks which screen the window is on, every second it reads that screen's refresh rate again (a mode
/// change on the same screen), and when either changed it applies the current side's cap again
/// (<see cref="SettingsApplier.ApplyFrameCap"/>). Added under the root window at start; the frame-rate smoke adds
/// its own.
/// </summary>
public partial class DisplayFramePacer : Node
{
    public const string NodeName = "DisplayFramePacer";

    private const double RereadSeconds = 1.0;

    private double _sinceRead;

    public DisplayFramePacer()
    {
        Name = NodeName;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>The settings the cap is taken from (the current ones when null).</summary>
    public GameSettings? Settings { get; set; }

    /// <summary>Times the screen or its rate changed and the cap was applied again.</summary>
    public int Changes { get; private set; }

    /// <summary>Adds a pacer under <paramref name="parent"/> (deferred, so it works while the tree is being set up).</summary>
    public static DisplayFramePacer Attach(Node parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.GetNodeOrNull<DisplayFramePacer>(NodeName) is { } existing)
        {
            return existing;
        }

        var pacer = new DisplayFramePacer();
        parent.CallDeferred(Node.MethodName.AddChild, pacer);
        return pacer;
    }

    public override void _Ready()
    {
        SettingsApplier.RefreshMonitor(force: true);
        Apply();
    }

    public override void _Process(double delta)
    {
        _sinceRead += delta;
        bool force = _sinceRead >= RereadSeconds;
        if (force)
        {
            _sinceRead = 0;
        }

        if (SettingsApplier.RefreshMonitor(force))
        {
            Changes++;
            GD.Print($"SVM_DISPLAY screen {SettingsApplier.MonitorScreen + 1} at {SettingsApplier.MonitorHz:F2} Hz");
            Apply();
        }
    }

    private void Apply() => SettingsApplier.ApplyFrameCap(Settings ?? SettingsStore.Current, SettingsApplier.LastMerc);
}
