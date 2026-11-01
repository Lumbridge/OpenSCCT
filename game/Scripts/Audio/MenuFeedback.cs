using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>Quiet, content-independent feedback for buttons in pages and in-game overlays.</summary>
public partial class MenuFeedback : Node
{
    private readonly Dictionary<BaseButton, (Action Hover, Action Focus, Action Click)> _buttons = new();
    private AudioStreamPlayer? _hover;
    private AudioStreamPlayer? _click;
    private ulong _lastFeedback;
    private ulong _lastPointerMotion;
    private ulong _lastNavigation;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (DisplayServer.GetName() == "headless" || System.Environment.GetEnvironmentVariable("SVM_AUDIO_MUTE") is "1" or "true")
            return;

        _ = new ReverbBuses(null);
        Options.SettingsApplier.ApplyAudio(Options.SettingsStore.Current);
        _hover = Player("Hover", Tone(0.038, 920, 680), -23f);
        _click = Player("Click", Tone(0.070, 640, 420), -18f);
        GetTree().NodeAdded += Attach;
        GetTree().NodeRemoved += Detach;
        Walk(GetTree().Root);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
            _lastPointerMotion = Time.GetTicksMsec();
        if (@event.IsActionPressed("ui_up") || @event.IsActionPressed("ui_down") ||
            @event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_right") ||
            @event.IsActionPressed("ui_focus_next") || @event.IsActionPressed("ui_focus_prev"))
            _lastNavigation = Time.GetTicksMsec();
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= Attach;
        GetTree().NodeRemoved -= Detach;
        foreach (BaseButton button in new List<BaseButton>(_buttons.Keys))
            Detach(button);
    }

    private AudioStreamPlayer Player(string name, AudioStream stream, float volume)
    {
        var player = new AudioStreamPlayer { Name = name, Bus = ReverbBuses.FlatBus, Stream = stream, VolumeDb = volume };
        AddChild(player);
        return player;
    }

    private void Walk(Node node)
    {
        Attach(node);
        foreach (Node child in node.GetChildren())
            Walk(child);
    }

    private void Attach(Node node)
    {
        if (node is not BaseButton button || _buttons.ContainsKey(button))
            return;
        Action hover = () => Hover(button, _lastPointerMotion);
        Action focus = () => Hover(button, _lastNavigation);
        Action click = () =>
        {
            _lastFeedback = Time.GetTicksMsec();
            _hover?.Stop();
            _click?.Play();
        };
        button.MouseEntered += hover;
        button.FocusEntered += focus;
        button.Pressed += click;
        _buttons.Add(button, (hover, focus, click));
    }

    private void Detach(Node node)
    {
        if (node is not BaseButton button || !_buttons.Remove(button, out var handlers))
            return;
        button.MouseEntered -= handlers.Hover;
        button.FocusEntered -= handlers.Focus;
        button.Pressed -= handlers.Click;
    }

    private void Hover(BaseButton button, ulong inputTime)
    {
        ulong now = Time.GetTicksMsec();
        // Ignore initial/programmatic focus and pages appearing beneath a stationary pointer.
        // One shared cooldown also prevents mouse focus and hover from doubling up.
        if (button.Disabled || !button.IsVisibleInTree() || inputTime == 0 || now - inputTime > 150 || now - _lastFeedback < 65)
            return;
        _lastFeedback = now;
        _hover?.Play();
    }

    private static AudioStreamWav Tone(double duration, double startHz, double endHz)
    {
        const int rate = 44100;
        int samples = (int)(duration * rate);
        var data = new byte[samples * 2];
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / rate;
            double progress = (double)i / (samples - 1);
            phase += Math.Tau * (startHz + (endHz - startHz) * progress) / rate;
            // Rounded attack and a tapered decay keep these tiny tones soft, without sample-edge clicks.
            double envelope = Math.Min(1, t / 0.004) * Math.Pow(1 - progress, 2);
            short sample = (short)(short.MaxValue * envelope * (0.85 * Math.Sin(phase) + 0.15 * Math.Sin(2 * phase)));
            data[i * 2] = (byte)(sample & 255);
            data[i * 2 + 1] = (byte)((sample >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
