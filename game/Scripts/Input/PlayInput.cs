using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Play.Session;
using PawnType = OpenScct.Core.Engine.PawnType;

namespace OpenScctGame.Controls;

/// <summary>
/// Turns keyboard and mouse into the local player's per-tick input (<see cref="LocalInput"/>) through the side's
/// <see cref="InputBindings"/>: a key's buttons are held while it is down (a press shorter than a tick still counts
/// for one tick), its exec commands run once on the press, its axes feed while it is down, and the mouse counts of a
/// tick are summed with no smoothing or clamp (Reloaded's modern mouse). The pointer is captured when play starts
/// and released with Escape; while it is free, no game input is read.
/// </summary>
public partial class PlayInput : Node
{
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _pressedSinceTick = new();
    private object? _actionPawn;
    private readonly InputActionState _actionState = new();
    private readonly HashSet<string> _physicalSources = new(StringComparer.OrdinalIgnoreCase);
    private BindingInput? _bindingInput;
    private InputBindings? _bindings;
    public Func<OpenScct.Game.SBase.SPlayerController?>? Controller { get; set; }
    public TeamPingInput TeamPing { get; } = new();
    private float _mouseX;
    private float _mouseY;
    private bool? _padWalk;
    private bool _captureOnStart = true;
    private bool _waitForJoinFrame = true;
    private bool _requestJoinFocus = true;
    private bool _recaptureOnFocus;
    private bool _foregroundLost;
    private bool _activationClickConsumed;
    internal Func<bool>? ForegroundProbe { get; set; }
    private bool HasPlayFocus => ForegroundProbe?.Invoke() ?? (Pointer.Detached || InputFocusTrace.IsForeground(GetWindow()));
    private readonly bool _traceInput = System.Environment.GetEnvironmentVariable("SVM_INPUT_TRACE") == "1";
    private ulong _traceAt;
    private int _receivedKeys, _receivedMotion, _receivedButtons, _playedKeys, _playedMotion;

    public override void _Input(InputEvent @event)
    {
        if (!_traceInput) return;
        if (@event is InputEventKey) _receivedKeys++;
        if (@event is InputEventMouseMotion) _receivedMotion++;
        if (@event is InputEventMouseButton) _receivedButtons++;
    }

    public PlayInput()
    {
        Name = "PlayInput";
    }

    public InputBindings? Bindings
    {
        get => _bindings;
        set
        {
            if (!ReferenceEquals(_bindings, value))
            {
                TeamPing.Cancel();
                LocalActionsReset?.Invoke();
            }
            _bindings = value;
            if (value is not null) BindingInput.SetLayout(value.Layout);
        }
    }

    public override void _Ready()
    {
        _bindingInput = BindingInput.Current;
        if (_bindingInput is not null)
        {
            _bindingInput.Released += ReleasePhysicalSource;
            _bindingInput.Resetting += TeamPing.Cancel;
        }
    }

    public override void _ExitTree()
    {
        if (_bindingInput is not null)
        {
            _bindingInput.Released -= ReleasePhysicalSource;
            _bindingInput.Resetting -= TeamPing.Cancel;
        }
        TeamPing.Cancel();
        if (Target is { } target) ClearHeld(target);
    }

    private void ReleasePhysicalSource(string key)
    {
        if (_physicalSources.Remove(key)) Key(key, false);
    }

    /// <summary>The input the held keys are written to (released together when the pointer is freed).</summary>
    public LocalInput? Target { get; set; }

    /// <summary>Controller interface commands bypass keyboard bindings and their unrelated actions.</summary>
    public event Action<string>? LocalCommand;

    /// <summary>Local interface actions that need paired presses/releases and independent held sources.</summary>
    public event Action<string, string, bool>? LocalAction;
    public event Action? LocalActionsReset;
    public void SendLocalAction(string command, string source, bool pressed) => LocalAction?.Invoke(command, source, pressed);

    /// <summary>A local overlay may consume controller commands while gameplay is paused.</summary>
    public Func<string, bool>? ModalCommand { get; set; }
    public Func<bool>? ModalInputActive { get; set; }
    public void SendLocalCommand(string command)
    {
        if (ModalCommand?.Invoke(command) != true) LocalCommand?.Invoke(command);
    }

    /// <summary>The gamepad must obey the same native focus guard as keyboard input.</summary>
    public bool HasInputFocus => !RequireCapture || HasPlayFocus;

    /// <summary>While an analog stick drives movement, its run threshold owns walk/run for that tick.</summary>
    public void SetPadWalk(bool? walk) => _padWalk = walk;

    /// <summary>When false (scripted runs), real input is ignored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Capture the pointer for play and on a refocusing click (off for headless runs).</summary>
    public bool CapturePointer { get; set; } = true;

    /// <summary>
    /// When true (the window), game input is read only while the pointer is captured. Off for headless runs, where
    /// there is no pointer to capture.
    /// </summary>
    public bool RequireCapture { get; set; } = true;

    /// <summary>The mouse counts gathered since the last tick took them (the drawn view turns by them ahead of the tick).</summary>
    public (float X, float Y) PendingMouse => (_mouseX, _mouseY);

    public bool IsCaptured => OpenScctGame.Controls.Pointer.Mode == global::Godot.Input.MouseModeEnum.Captured;

    public override void _Process(double delta)
    {
        if (_traceInput && Time.GetTicksMsec() >= _traceAt)
        {
            _traceAt = Time.GetTicksMsec() + 1000;
            var pc = Controller?.Invoke();
            GD.Print($"SVM_INPUT enabled={Enabled} captured={IsCaptured} focus={GetWindow().HasFocus()} pendingCapture={_captureOnStart} gui={GetViewport().GuiGetFocusOwner()?.GetPath()} hover={GetViewport().GuiGetHoveredControl()?.GetPath()} received={_receivedKeys}/{_receivedMotion}/{_receivedButtons} played={_playedKeys}/{_playedMotion} state={pc?.GetStateName()} move={pc?.BT_Up} turn={pc?.aTurn} {InputFocusTrace.Native(GetWindow())}");
            _receivedKeys = _receivedMotion = _receivedButtons = _playedKeys = _playedMotion = 0;
        }
        // HasFocus can remain true after a Windows foreground change. Capturing then routes
        // buttons to this inactive window without delivering keyboard or raw mouse movement.
        bool hasPlayFocus = HasPlayFocus;
        if (!hasPlayFocus)
        {
            LosePlayFocus();
        }
        else if (_foregroundLost)
        {
            _foregroundLost = false;
            BindingInput.Current?.RestoreFocus();
            _eatClickUntilMsec = _activationClickConsumed ? 0 : Time.GetTicksMsec() + RefocusClickMsec;
            if (_recaptureOnFocus)
            {
                _recaptureOnFocus = false;
                QueueRefocusCapture();
            }
        }
        if (TeamPing.Active) TeamPing.Advance(Time.GetTicksMsec(), Enabled && (!RequireCapture || hasPlayFocus && IsCaptured));
        if (!_captureOnStart) return;
        if (!Enabled || !CapturePointer || (DisplayServer.GetName() == "headless" && !Pointer.Detached))
        {
            _captureOnStart = false;
            return;
        }
        if (_waitForJoinFrame)
        {
            _waitForJoinFrame = false;
            return;
        }
        // Request native keyboard focus once after the outgoing scene has left the tree. Capturing
        // the mouse alone can leave Windows delivering clicks but not keyboard/relative motion.
        if (_requestJoinFocus)
        {
            _requestJoinFocus = false;
            if (!Pointer.Detached) GetWindow().GrabFocus();
        }
        if (!HasPlayFocus) return;
        CaptureForPlay();
    }

    private void CaptureForPlay()
    {
        _captureOnStart = false;
        _recaptureOnFocus = false;
        GetViewport().GuiReleaseFocus();
        // Reapply capture even if Godot still remembers Captured across a native focus change.
        Pointer.Release();
        Pointer.Capture();
    }

    private void QueueRefocusCapture()
    {
        // Let Windows finish activation and the activating click before changing native capture.
        // Releasing/reacquiring capture inside that click can race its own native capture cleanup.
        _requestJoinFocus = false;
        _waitForJoinFrame = true;
        _captureOnStart = true;
    }

    private void LosePlayFocus()
    {
        if (!_foregroundLost)
        {
            _foregroundLost = true;
            TeamPing.Cancel();
            BindingInput.Current?.ResetAll();
            _activationClickConsumed = false;
            // Windows may have released native capture before this notification or foreground check.
            // Keep the app's capture intent, while menu releases continue to opt out of recapture.
            _recaptureOnFocus |= Enabled && CapturePointer && (_captureOnStart || Pointer.CaptureRequested);
            _captureOnStart = false;
            _eatenButton = MouseButton.None;
            if (Target is { } target) ClearHeld(target);
        }

        if (Enabled && CapturePointer && IsCaptured) Pointer.Release();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || Bindings is null)
        {
            return;
        }

        if (RequireCapture && !HasPlayFocus)
        {
            LosePlayFocus();
            if (CapturePointer && @event is InputEventMouseButton { Pressed: true } activation)
            {
                _eatenButton = activation.ButtonIndex;
                _activationClickConsumed = true;
                _eatClickUntilMsec = 0;
                if (!Pointer.Detached) GetWindow().GrabFocus();
                QueueRefocusCapture();
                GetViewport().SetInputAsHandled();
            }
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
                _activationClickConsumed = true;
                // Restore play after the native activation event has finished, but never execute this click.
                if (CapturePointer && !IsCaptured) QueueRefocusCapture();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton eatenRelease && eatenRelease.ButtonIndex == _eatenButton)
        {
            if (!eatenRelease.Pressed) _eatenButton = MouseButton.None;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (RequireCapture && !IsCaptured)
        {
            if (CapturePointer && @event is InputEventMouseButton { Pressed: true })
            {
                _eatenButton = ((InputEventMouseButton)@event).ButtonIndex;
                QueueRefocusCapture();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        Read(@event);
    }

    private void Read(InputEvent @event)
    {
        if (_traceInput && @event is InputEventKey) _playedKeys++;
        if (_traceInput && @event is InputEventMouseMotion) _playedMotion++;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                if (TeamPing.WheelOpen)
                {
                    TeamPing.MouseMotion(motion.Relative.X, motion.Relative.Y);
                    break;
                }
                _mouseX += motion.Relative.X;
                _mouseY -= motion.Relative.Y;
                break;
            case InputEventKey { Echo: true }:
                return;
            case InputEventKey or InputEventMouseButton:
                if (BindingInput.Current is { } session)
                {
                    foreach (var ev in session.Resolve(@event).Events)
                    {
                        if (ev.Pressed && session.MayAcceptGameplay(ev.Key))
                        {
                            _physicalSources.Add(ev.Key);
                            Key(ev.Key, true);
                        }
                        else if (!ev.Pressed) ReleasePhysicalSource(ev.Key);
                    }
                }
                else if (GodotKeys.ToIk(@event) is { } ik)
                {
                    bool pressed = @event.IsPressed();
                    Key(ik, pressed);
                }

                break;
        }
    }

    /// <summary>Resume play after a modal closes, once its controls have left the input path.</summary>
    public void ResumePointer()
    {
        Enabled = true;
        QueueRefocusCapture();
    }

    /// <summary>Frees the pointer and releases every held key.</summary>
    public void ReleasePointer()
    {
        TeamPing.Cancel();
        _captureOnStart = false;
        _recaptureOnFocus = false;
        OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
        if (Target is { } target)
        {
            ClearHeld(target);
        }
        else
        {
            BindingInput.Current?.ResetGameplay();
            _physicalSources.Clear();
            _down.Clear();
        }
    }

    /// <summary>A key went down or up (IK name), as from the keyboard.</summary>
    public void Key(string ik, bool pressed)
    {
        if (pressed)
        {
            if (!_down.Add(ik) && !GodotKeys.IsPulse(ik))
            {
                return;
            }

            TryStartTeamPing(ik);
            _pending.Add((ik, true));
        }
        else if (_down.Remove(ik))
        {
            if (TeamPing.Active) TeamPing.Release(ik, Time.GetTicksMsec());
            _pending.Add((ik, false));
        }

        // The wheel has no release.
        if (pressed && GodotKeys.IsPulse(ik))
        {
            _down.Remove(ik);
            _pending.Add((ik, false));
        }
    }

    private void TryStartTeamPing(string key)
    {
        if (TeamPing.Context is null || GodotKeys.IsPulse(key)) return;
        foreach (KeyAction action in ActionsOf(key))
        {
            if (!IsTeamPing(action)) continue;
            if (Enabled && HasInputFocus && (!RequireCapture || IsCaptured))
            {
                bool merc = Controller?.Invoke()?.SBPawn?.SPawnType is { } type
                    ? type == PawnType.SPAWNDEFENSE : Bindings?.Side == PlaySide.Merc;
                TeamPing.Press(key, Time.GetTicksMsec(), merc);
            }
            return;
        }
    }

    private static bool IsTeamPing(KeyAction action) => action.Kind == KeyActionKind.Command &&
        action.Name.Equals(UI.ControlActions.PingCommand, StringComparison.OrdinalIgnoreCase);

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

        object? pawn = Controller?.Invoke()?.SBPawn;
        if (!ReferenceEquals(pawn, _actionPawn))
        {
            _actionPawn = pawn;
            _actionState.Clear();
            input.Commands.Add(new InputCommand("SetWalk", 0));
            input.Commands.Add(new InputCommand("SetScope", 0));
            input.Commands.Add(new InputCommand("SetSpyFirstPerson", 0));
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
            string physical = BindingKey.TryParse(ik, out var binding) ? binding.Base : ik;
            if (pressed && string.Equals(physical, "MouseWheelUp", StringComparison.OrdinalIgnoreCase))
            {
                input.Wheel++;
            }
            else if (pressed && string.Equals(physical, "MouseWheelDown", StringComparison.OrdinalIgnoreCase))
            {
                input.Wheel--;
            }

            foreach (KeyAction a in ActionsOf(ik))
            {
                if (IsTeamPing(a)) continue;
                if (a.Kind == KeyActionKind.Command && a.Name.Equals(UI.ControlActions.AutoHackCommand, StringComparison.OrdinalIgnoreCase))
                {
                    // A setting, not a pawn command: the HUD's commands flip it and show the message.
                    if (pressed) SendLocalCommand(UI.ControlActions.AutoHackCommand);
                    continue;
                }
                bool merc = Bindings.Side == PlaySide.Merc;
                var behavior = Options.SettingsStore.Current.LookOf(merc, false);
                if (!merc && a.Kind == KeyActionKind.Button && a.Name.Equals("bCloak", StringComparison.OrdinalIgnoreCase) &&
                    GodotKeys.IsPulse(ik))
                {
                    if (pressed) input.Commands.Add(new InputCommand("ToggleCloak"));
                    continue;
                }
                if (a.Kind == KeyActionKind.Command && a.Name.Equals("SetSpyFirstPerson", StringComparison.OrdinalIgnoreCase))
                {
                    if (!merc && pressed)
                    {
                        _actionState.SetSpyFirstPerson(a.Arg != 0);
                        input.Commands.Add(new InputCommand("SetSpyFirstPerson", a.Arg != 0 ? 1 : 0));
                    }
                    continue;
                }
                if (a.Kind == KeyActionKind.Command && a.Name.Equals("SwitchSpyPerspective", StringComparison.OrdinalIgnoreCase))
                {
                    if (!merc)
                    {
                        bool firstPerson = _actionState.SpyPerspective(ik, pressed, behavior.PerspectiveBehavior == "Hold",
                            GodotKeys.IsPulse(ik));
                        input.Commands.Add(new InputCommand("SetSpyFirstPerson", firstPerson ? 1 : 0));
                    }
                    continue;
                }
                if (a.Kind == KeyActionKind.Command && a.Name is "Walk" or "ChangeSpeed")
                {
                    bool pulse = GodotKeys.IsPulse(ik);
                    if (pulse && Controller?.Invoke()?.StateNode is OpenScct.Game.SBase.SPlayerController.IWheel)
                    {
                        if (pressed) input.Commands.Add(new InputCommand("WheelDown"));
                    }
                    else
                    {
                        bool walk = _actionState.Walk(ik, pressed, behavior.WalkBehavior == "Hold", pulse);
                        input.Commands.Add(new InputCommand("SetWalk", walk ? 1 : 0));
                    }
                    continue;
                }
                if (a.Kind == KeyActionKind.Command && a.Name == "Run" && pressed && Controller?.Invoke()?.StateNode is not OpenScct.Game.SBase.SPlayerController.IWheel) _actionState.Run();
                if (merc && behavior.ScopeBehavior == "Hold" && a.Kind == KeyActionKind.Command && a.Name == "SwitchBinocularVision" && !GodotKeys.IsPulse(ik))
                {
                    bool scope = _actionState.Scope(ik, pressed);
                    input.Commands.Add(new InputCommand("SetScope", scope ? 1 : 0));
                    continue;
                }
                switch (a.Kind)
                {
                    case KeyActionKind.Button when LocalInput.IndexOf(a.Name) is var b and >= 0:
                        if (pressed)
                        {
                            input[b] = 1;
                        }
                        else if (!IsButtonHeld(a.Name))
                        {
                            // _down includes later events in this batch: a quick re-press of this key
                            // must keep holding its buttons after the delayed release would run.
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
        if (_padWalk is { } padWalk) input.Commands.Add(new InputCommand("SetWalk", padWalk ? 1 : 0));
        if (_actionState.ScopeHeld) input.Commands.Add(new InputCommand("SetScope", 1));
        if (_actionState.SpyFirstPersonRequested && Bindings.Side == PlaySide.Spy)
            input.Commands.Add(new InputCommand("SetSpyFirstPerson", 1));
        foreach (string ik in _down)
        {
            foreach (KeyAction a in ActionsOf(ik))
            {
                if (a.Kind == KeyActionKind.Axis && string.Equals(a.Name, "aUp", StringComparison.OrdinalIgnoreCase))
                {
                    up += a.Speed;
                }
            }
        }

        input.Up = up;
        input.Fast = _down.Contains("Shift") || BindingInput.Current?.GameplayPhysicalHeld("Shift") == true;
        input.Slow = _down.Contains("Alt") || BindingInput.Current?.GameplayPhysicalHeld("Alt") == true;
        input.MouseX += _mouseX;
        input.MouseY += _mouseY;
        _mouseX = 0f;
        _mouseY = 0f;
    }

    private IReadOnlyList<KeyAction> ActionsOf(string key) => key.StartsWith(UI.ControlActions.VirtualPrefix, StringComparison.Ordinal)
        ? UI.ControlActions.VirtualActions(key) : Bindings?.Actions(key) ?? Array.Empty<KeyAction>();

    /// <summary>Releases every button (focus lost, pointer released).</summary>
    public void ClearHeld(LocalInput input)
    {
        TeamPing.Cancel();
        LocalActionsReset?.Invoke();
        BindingInput.Current?.ResetGameplay();
        _physicalSources.Clear();
        _padWalk = null;
        _actionState.Clear();
        _down.Clear();
        _pending.Clear();
        _pressedSinceTick.Clear();
        _mouseX = 0f;
        _mouseY = 0f;
        input.Clear();
        input.Commands.Add(new InputCommand("SetWalk", 0));
        input.Commands.Add(new InputCommand("SetScope", 0));
        input.Commands.Add(new InputCommand("SetSpyFirstPerson", 0));
    }

    private bool IsButtonHeld(string button)
    {
        foreach (string ik in _down)
        {
            foreach (KeyAction a in ActionsOf(ik))
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
        if (what == NotificationWMWindowFocusOut)
        {
            LosePlayFocus();
        }
        else if (what == NotificationWMWindowFocusIn)
        {
            _foregroundLost = false;
            _eatClickUntilMsec = _activationClickConsumed ? 0 : Time.GetTicksMsec() + RefocusClickMsec;
            if (_recaptureOnFocus)
            {
                _recaptureOnFocus = false;
                QueueRefocusCapture();
            }
        }
    }
}
