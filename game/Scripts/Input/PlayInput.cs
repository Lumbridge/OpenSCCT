using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Controls;

/// <summary>
/// Turns keyboard and mouse into the local player's per-tick input (<see cref="LocalInput"/>) through the side's
/// <see cref="InputBindings"/>: a key's buttons are held while it is down (a press shorter than a tick still counts
/// for one tick), its exec commands run once on the press, its axes feed while it is down, and the mouse counts of a
/// tick are summed with no smoothing or clamp (Reloaded's modern mouse). The pointer is captured on the first click
/// and released with Escape; while it is free, no game input is read.
/// </summary>
public partial class PlayInput : Node
{
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _pressedSinceTick = new();
    private float _mouseX;
    private float _mouseY;

    public PlayInput()
    {
        Name = "PlayInput";
    }

    public InputBindings? Bindings { get; set; }

    /// <summary>The input the held keys are written to (released together when the pointer is freed).</summary>
    public LocalInput? Target { get; set; }

    /// <summary>When false (scripted runs), real input is ignored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Capture the pointer on the first click (off for headless runs).</summary>
    public bool CapturePointer { get; set; } = true;

    /// <summary>
    /// When true (the window), game input is read only while the pointer is captured. Off for headless runs, where
    /// there is no pointer to capture.
    /// </summary>
    public bool RequireCapture { get; set; } = true;

    /// <summary>The mouse counts gathered since the last tick took them (the drawn view turns by them ahead of the tick).</summary>
    public (float X, float Y) PendingMouse => (_mouseX, _mouseY);

    public bool IsCaptured => OpenScctGame.Controls.Pointer.Mode == global::Godot.Input.MouseModeEnum.Captured;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || Bindings is null)
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: global::Godot.Key.Escape })
        {
            ReleasePointer();
            GetViewport().SetInputAsHandled();
            return;
        }

        // The click that brings the window back to the front only refocuses it: it never reaches the game.
        if (@event is InputEventMouseButton refocus && Time.GetTicksMsec() < _eatClickUntilMsec)
        {
            if (refocus.Pressed)
            {
                _eatClickUntilMsec = 0;
                _eatenButton = refocus.ButtonIndex;
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: false } eatenRelease && eatenRelease.ButtonIndex == _eatenButton)
        {
            _eatenButton = MouseButton.None;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (RequireCapture && !IsCaptured)
        {
            if (CapturePointer && @event is InputEventMouseButton { Pressed: true })
            {
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        Read(@event);
    }

    private void Read(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _mouseX += motion.Relative.X;
                _mouseY -= motion.Relative.Y;
                break;
            case InputEventKey { Echo: true }:
                return;
            case InputEventKey or InputEventMouseButton:
                if (GodotKeys.ToIk(@event) is { } ik)
                {
                    bool pressed = @event.IsPressed();
                    Key(ik, pressed);
                }

                break;
        }
    }

    /// <summary>Frees the pointer and releases every held key.</summary>
    public void ReleasePointer()
    {
        OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
        if (Target is { } target)
        {
            ClearHeld(target);
        }
        else
        {
            _down.Clear();
        }
    }

    /// <summary>A key went down or up (IK name), as from the keyboard.</summary>
    public void Key(string ik, bool pressed)
    {
        if (pressed)
        {
            if (!_down.Add(ik) && !ik.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pending.Add((ik, true));
        }
        else if (_down.Remove(ik))
        {
            _pending.Add((ik, false));
        }

        // The wheel has no release.
        if (pressed && ik.StartsWith("MouseWheel", StringComparison.OrdinalIgnoreCase))
        {
            _down.Remove(ik);
        }
    }

    private readonly List<(string Ik, bool Pressed)> _pending = new();

    /// <summary>
    /// Writes the input gathered since the last tick into <paramref name="input"/>: presses run their commands and
    /// set their buttons, releases clear them after the tick has seen them, the mouse counts are handed over.
    /// Call it once per client tick, before the tick.
    /// </summary>
    public void FillTick(LocalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Bindings is null)
        {
            return;
        }

        // Buttons released earlier, that were held for the last tick, go up now.
        foreach (int i in _pressedSinceTick)
        {
            input[i] = 0;
        }

        _pressedSinceTick.Clear();
        float up = 0f;
        foreach ((string ik, bool pressed) in _pending)
        {
            foreach (KeyAction a in Bindings.Actions(ik))
            {
                switch (a.Kind)
                {
                    case KeyActionKind.Button when LocalInput.IndexOf(a.Name) is var b and >= 0:
                        if (pressed)
                        {
                            input[b] = 1;
                        }
                        else if (!IsButtonHeldElsewhere(a.Name, ik))
                        {
                            // Keep it down for this tick if it was pressed since the last one.
                            _pressedSinceTick.Add(b);
                        }

                        break;
                    case KeyActionKind.Command when pressed:
                        input.Commands.Add(new InputCommand(a.Name, a.Arg));
                        break;
                }
            }
        }

        _pending.Clear();
        foreach (string ik in _down)
        {
            foreach (KeyAction a in Bindings.Actions(ik))
            {
                if (a.Kind == KeyActionKind.Axis && string.Equals(a.Name, "aUp", StringComparison.OrdinalIgnoreCase))
                {
                    up += a.Speed;
                }
            }
        }

        input.Up = up;
        input.MouseX += _mouseX;
        input.MouseY += _mouseY;
        _mouseX = 0f;
        _mouseY = 0f;
    }

    /// <summary>Releases every button (focus lost, pointer released).</summary>
    public void ClearHeld(LocalInput input)
    {
        _down.Clear();
        _pending.Clear();
        _pressedSinceTick.Clear();
        input.Clear();
    }

    private bool IsButtonHeldElsewhere(string button, string releasedKey)
    {
        foreach (string ik in _down)
        {
            if (string.Equals(ik, releasedKey, StringComparison.OrdinalIgnoreCase) || Bindings is null)
            {
                continue;
            }

            foreach (KeyAction a in Bindings.Actions(ik))
            {
                if (a.Kind == KeyActionKind.Button && string.Equals(a.Name, button, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>How long after the window regains focus a mouse press is taken as the refocusing click.</summary>
    private const ulong RefocusClickMsec = 250;

    private ulong _eatClickUntilMsec;
    private MouseButton _eatenButton = MouseButton.None;

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && Target is { } target)
        {
            ClearHeld(target);
        }
        else if (what == NotificationWMWindowFocusIn)
        {
            _eatClickUntilMsec = Time.GetTicksMsec() + RefocusClickMsec;
        }
    }
}
