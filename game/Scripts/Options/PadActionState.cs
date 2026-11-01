using System;
using System.Collections.Generic;

namespace OpenScctGame.Options;

/// <summary>The current gameplay facts needed by Reloaded's contextual controller actions.</summary>
public readonly record struct PadActionContext(
    bool InCamera = false,
    bool IsSpy = true,
    bool WeaponDrawn = false,
    bool CloakSelected = false,
    bool CloakActive = false,
    bool Scoped = false,
    bool Slot2IsCloak = false,
    bool CameraTransition = false);

/// <summary>
/// Expands contextual pad bindings into independent actions. Trigger presses act immediately; their short
/// releases and 200 ms hold callbacks are separate. ReleaseAll aborts without synthesizing a tap.
/// Call Update every frame, including frames without button edges. Returned lists are reused on the next call.
/// </summary>
public sealed class PadActionState
{
    private sealed class Press(string action)
    {
        public readonly string Action = action;
        public double Seconds;
        public bool HeldCallback;
    }

    private readonly Dictionary<string, Press> _presses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _held = new(StringComparer.Ordinal);
    private readonly List<PadBindingEvent> _events = new();
    private double _clock;
    private double _cameraGadgetAt = double.NegativeInfinity;

    public IReadOnlyList<PadBindingEvent> Update(
        IReadOnlyList<PadBindingEvent> events, double delta, PadActionContext context)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events.Clear();
        double elapsed = double.IsFinite(delta) && delta > 0 ? delta : 0;
        _clock += elapsed;
        foreach ((string control, Press press) in _presses)
        {
            press.Seconds += elapsed;
            if (press.HeldCallback || press.Seconds < PadBindingState.HoldSeconds) continue;
            press.HeldCallback = true;
            if (press.Action == "Pad:spygadget" && context.CloakSelected)
                Pulse(control + ":hold", "Action:cloakquick");
        }

        foreach (PadBindingEvent ev in events)
        {
            if (!IsContextual(ev.Action))
            {
                Hold(ev.Control, ev.Action, ev.Pressed);
                continue;
            }

            if (ev.Pressed)
            {
                if (_presses.ContainsKey(ev.Control)) continue;
                _presses.Add(ev.Control, new Press(ev.Action));
                switch (ev.Action)
                {
                    case "Pad:spyfire":
                        if (context.IsSpy && (context.InCamera || context.CameraTransition)) Pulse(ev.Control + ":camera", "Action:cameragas");
                        else Hold(ev.Control, "Action:fire", true);
                        break;
                    case "Pad:spygadget":
                        if (context.IsSpy && context.InCamera) _cameraGadgetAt = _clock;
                        Hold(ev.Control, "Action:altfire", true);
                        break;
                    case "Pad:mercgadget":
                        Hold(ev.Control, "Action:altfire", true);
                        if (context.Scoped) Hold(ev.Control + ":breath", "Action:breath", true);
                        break;
                    case "Pad:crouch":
                        // Reloaded clears the crouch pulse on its next callback and calls AltFire in camera
                        // views. Emit the exit directly so a delayed pulse cannot crouch the player after exit.
                        if (context.InCamera) Pulse(ev.Control + ":camera", "Action:cameraexit");
                        else Hold(ev.Control, "Action:crouch", true);
                        break;
                    case "Pad:longroll":
                        Hold(ev.Control, "Action:longroll", true);
                        // The queued crouch-release callback is registered even when the crouch latch is off.
                        if (context.InCamera) Pulse(ev.Control + ":camera", "Action:cameraexit");
                        break;
                }
            }
            else if (_presses.Remove(ev.Control, out Press? press))
            {
                Release(ev.Control);
                Release(ev.Control + ":breath");
                if (press.Seconds >= PadBindingState.HoldSeconds) continue;
                if (press.Action == "Pad:spyfire") Pulse(ev.Control + ":tap", "Action:quick1");
                else if (press.Action == "Pad:spygadget") SpyGadgetTap(ev.Control, context);
            }
        }
        return _events;
    }

    /// <summary>Releases all emitted held actions, including ordinary bindings, without invoking release commands.</summary>
    public IReadOnlyList<PadBindingEvent> ReleaseAll()
    {
        _events.Clear();
        foreach ((string control, string action) in _held) _events.Add(new(control, action, false));
        _held.Clear();
        _presses.Clear();
        _clock = 0;
        _cameraGadgetAt = double.NegativeInfinity;
        return _events;
    }

    private void SpyGadgetTap(string control, PadActionContext context)
    {
        if (context.CloakSelected && context.CloakActive)
        {
            Pulse(control + ":tap", "Action:cloakquick");
            return;
        }
        if (context.WeaponDrawn || _clock - _cameraGadgetAt <= PadBindingState.HoldSeconds) return;
        if (context.Slot2IsCloak || context.CloakSelected) Pulse(control + ":select", "Action:slot2");
        if (context.Slot2IsCloak) Pulse(control + ":cloak", "Action:cloakquick");
        Pulse(control + ":tap", "Action:quick2");
    }

    private static bool IsContextual(string action) => action is
        "Pad:spyfire" or "Pad:spygadget" or "Pad:mercgadget" or "Pad:crouch" or "Pad:longroll";

    private void Hold(string control, string action, bool down)
    {
        if (!down) { Release(control); return; }
        if (_held.TryGetValue(control, out string? previous))
        {
            if (previous == action) return;
            Release(control);
        }
        _held.Add(control, action);
        _events.Add(new(control, action, true));
    }

    private void Release(string control)
    {
        if (_held.Remove(control, out string? action)) _events.Add(new(control, action, false));
    }

    private void Pulse(string control, string action)
    {
        _events.Add(new(control, action, true));
        _events.Add(new(control, action, false));
    }
}
