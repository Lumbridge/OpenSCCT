using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OpenScctGame.Controls;

public readonly record struct BindingResolution(ulong EventId, string? Key, IReadOnlyList<BindingInputEvent> Events);

/// <summary>
/// Observes physical input before Godot's UI dispatch. Consumers share one immutable event result; only
/// PlayInput's unhandled stage accepts gameplay presses. Releases cannot be swallowed by a later UI handler.
/// </summary>
public sealed partial class BindingInput : Node
{
    private const string EventToken = "_svm_binding_input_event";
    private readonly BindingInputState _state = new();
    private readonly HashSet<string> _gameplayBlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _voiceAll = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _voiceTeam = new(StringComparer.OrdinalIgnoreCase);
    private KeyLayout _layout = DefaultKeyLayout.Create();
    private BindingResolution _last;
    private Window? _window;
    private int _muteEdges;
    private static int _captureDepth;
    private static KeyLayout? _requestedLayout;
    public static BindingInput? Current { get; private set; }
    public static bool HasConfiguredLayout => _requestedLayout is not null;
    public KeyLayout Layout => _layout;
    public event Action<string>? Released;
    public bool VoiceAllHeld => _voiceAll.Count > 0;
    public bool VoiceTeamHeld => _voiceTeam.Count > 0;
    public bool CaptureSuspended => _captureDepth > 0;
    public ulong LastEventId => _last.EventId;

    public override void _Ready()
    {
        Current = this;
        if (_requestedLayout is { } requested) _layout = requested;
        _window = GetTree().Root;
        _window.WindowInput += Observe;
        _window.FocusExited += OnFocusLost;
        _window.FocusEntered += OnFocusReturned;
    }

    public override void _ExitTree()
    {
        if (_window is { } window)
        {
            window.WindowInput -= Observe;
            window.FocusExited -= OnFocusLost;
            window.FocusEntered -= OnFocusReturned;
        }
        ResetAll();
        if (Current == this) Current = null;
    }

    public static void SetLayout(KeyLayout layout)
    {
        _requestedLayout = layout;
        if (Current is not { } current || ReferenceEquals(current._layout, layout)) return;
        if (current._layout.ToIni() == layout.ToIni())
        {
            current._layout = layout;
            return;
        }
        current.ResetAll();
        current._layout = layout;
        current._last = default;
    }

    /// <summary>Raw capture still receives events, while binding actions and voice shortcuts stay silent.</summary>
    public static IDisposable SuspendCapture()
    {
        _captureDepth++;
        Current?.ResetAll();
        return new CaptureLease();
    }

    /// <summary>Deliver synthetic input in order through the same observer, including consumed UI releases.</summary>
    public static void Inject(InputEvent input)
    {
        // ParseInputEvent normally buffers. Observe immediately before its own dispatch, never ahead of
        // an older buffered physical event or of the release belonging to a later synthetic event.
        Input.FlushBufferedEvents();
        Current?.Resolve(input);
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    private sealed class CaptureLease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _captureDepth = Math.Max(0, _captureDepth - 1);
            Current?.ResetAll();
        }
    }

    public static string? ResolveKey(InputEvent input, InputBindings? bindings = null)
    {
        if (Current is { } current)
        {
            return current.Resolve(input).Key;
        }
        if (!input.IsPressed() || input is InputEventKey { Echo: true } || GodotKeys.ToIk(input) is not { } physical) return null;
        return bindings is null ? GodotKeys.ToBinding(input)
            : BindingKey.Select(bindings.Layout, physical, GodotKeys.ModifiersOf(input));
    }

    /// <summary>Idempotent for one Godot event, including directly pushed events in runtime tests.</summary>
    public BindingResolution Resolve(InputEvent input)
    {
        if (!HasObserved(input)) Observe(input);
        return _last;
    }

    public bool HasObserved(InputEvent input) => _last.EventId != 0 && _last.EventId == EventId(input);
    private static ulong EventId(InputEvent input) => input.HasMeta(EventToken)
        ? unchecked((ulong)(long)input.GetMeta(EventToken)) : input.GetInstanceId();

    public bool MayAcceptGameplay(string input)
    {
        if (CaptureSuspended || !BindingKey.TryParse(input, out var key) || _gameplayBlocked.Contains(key.Base)) return false;
        return !_gameplayBlocked.Any(blocked => (BindingKey.ModifierOf(blocked) & key.Modifiers) != 0);
    }

    public void ResetGameplay() => _gameplayBlocked.UnionWith(_state.PhysicalDown);
    public bool PhysicalHeld(string key) => _state.PhysicalDown.Contains(key);
    public bool GameplayPhysicalHeld(string key) => !_gameplayBlocked.Contains(key) && PhysicalHeld(key);
    public int TakeMuteEdges() { int edges = _muteEdges; _muteEdges = 0; return edges; }

    private void Observe(InputEvent input)
    {
        ulong id = EventId(input);
        if (_last.EventId == id) return;
        // Godot localizes mouse events by copying them. Their metadata survives the transform, so every
        // consumer sees the raw-window dispatch token rather than treating the copy as another press.
        if (!input.HasMeta(EventToken)) input.SetMeta(EventToken, unchecked((long)id));
        _last = new BindingResolution(id, null, Array.Empty<BindingInputEvent>());
        if (input is InputEventKey { Echo: true } || input is not (InputEventKey or InputEventMouseButton) ||
            GodotKeys.ToIk(input) is not { } physical) return;
        bool pressed = input.IsPressed();
        if (!pressed) _gameplayBlocked.Remove(physical);
        var events = _state.Update(_layout, physical, pressed, GodotKeys.ModifiersOf(input), CaptureSuspended);
        string? selected = events.FirstOrDefault(e => e.Pressed).Key;
        _last = new BindingResolution(id, selected, events);
        foreach (var ev in events)
        {
            if (!ev.Pressed) Released?.Invoke(ev.Key);
            UpdateVoice(ev);
        }
    }

    private void UpdateVoice(BindingInputEvent input)
    {
        foreach (KeyAction action in _layout.Resolve(input.Key))
        {
            if (action.Kind != KeyActionKind.Command) continue;
            if (action.Name.Equals("PushToTalk", StringComparison.OrdinalIgnoreCase)) SetHeld(_voiceAll, input);
            else if (action.Name.Equals("TeamPushToTalk", StringComparison.OrdinalIgnoreCase)) SetHeld(_voiceTeam, input);
            else if (input.Pressed && action.Name.Equals("ToggleVoiceMute", StringComparison.OrdinalIgnoreCase)) _muteEdges++;
        }
    }

    private static void SetHeld(HashSet<string> sources, BindingInputEvent input)
    {
        if (input.Pressed) sources.Add(input.Key); else sources.Remove(input.Key);
    }

    public void ResetAll()
    {
        foreach (var ev in _state.Reset()) Released?.Invoke(ev.Key);
        _gameplayBlocked.UnionWith(_state.PhysicalDown);
        _voiceAll.Clear();
        _voiceTeam.Clear();
        _muteEdges = 0;
        _last = new BindingResolution(_last.EventId, null, Array.Empty<BindingInputEvent>());
    }

    private void OnFocusLost() => ResetAll();
    private void OnFocusReturned() => RestoreFocus();

    /// <summary>Also used by the native foreground guard when Godot's window focus signal stays stale.</summary>
    public void RestoreFocus()
    {
        // Releases made outside the application may never arrive as events.
        var held = new HashSet<string>(GodotKeys.HeldPhysicalInputs(), StringComparer.OrdinalIgnoreCase);
        _state.Reconcile(held);
        _state.BlockHeld(held);
        _gameplayBlocked.RemoveWhere(key => !held.Contains(key));
        _gameplayBlocked.UnionWith(held);
        _last = default;
    }
}
