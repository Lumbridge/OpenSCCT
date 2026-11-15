using System;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>Godot retains device ownership. Poll only for named-device removal/reconnection, including in menus.</summary>
public sealed partial class AudioOutput : Node
{
    private static AudioOutput? _instance;
    private static string _requested = "";
    private double _sinceCheck;
    public static bool Headphones { get; private set; }

    public static void Configure(string mode, string device)
    {
        Headphones = mode == "Headphones";
        bool changed = _requested != device;
        _requested = device;
        if (_instance is null && Engine.GetMainLoop() is SceneTree tree)
        {
            _instance = new AudioOutput { Name = "AudioOutput", ProcessMode = ProcessModeEnum.Always };
            tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
            changed = true;
        }
        if (changed) SelectAvailable();
    }

    private static void SelectAvailable()
    {
        string selected = _requested.Length > 0 && Array.IndexOf(AudioServer.GetOutputDeviceList(), _requested) >= 0
            ? _requested : "Default";
        if (AudioServer.OutputDevice != selected) AudioServer.OutputDevice = selected;
    }

    public override void _Process(double delta)
    {
        if ((_sinceCheck += delta) < 2) return;
        _sinceCheck = 0;
        SelectAvailable();
    }

    public override void _ExitTree() { if (_instance == this) _instance = null; }
}
