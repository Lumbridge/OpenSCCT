using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play;

namespace OpenScctGame.Smoke;

/// <summary>
/// One step of a <see cref="KeyPilot"/> timeline: for <see cref="Seconds"/>, the actions in <see cref="Hold"/> are held,
/// those in <see cref="Tap"/> are tapped once at the start, and the mouse moves by <see cref="LookPerSecond"/> pixels per
/// second (right and down positive, as the mouse reports it), in pixels of a 1280 x 720 window (scaled to the real one).
/// </summary>
public sealed record KeyStep(string Label, double Seconds)
{
    public IReadOnlyList<string> Hold { get; init; } = [];

    public IReadOnlyList<string> Tap { get; init; } = [];

    public Vector2 LookPerSecond { get; init; }
}

/// <summary>
/// Plays the local player through the real input path: it presses the keys the player's bindings give for named
/// actions, as Godot input events (<see cref="Input.ParseInputEvent"/>), so they reach <see cref="PlayInput"/> exactly as
/// a keyboard and mouse do, then the client controller, the loopback and the listen server. A test driver: it never
/// writes the session's input or the simulation. See docs/TESTING-PLAY.md.
/// </summary>
public sealed class KeyPilot
{
    /// <summary>A key no layout binds, sent first to find out whether parsed events reach the scene.</summary>
    public const Key MarkerKey = Key.F24;

    /// <summary>Frames a tapped key stays down.</summary>
    public const int TapFrames = 3;

    /// <summary>Microseconds a tapped key stays down at least (a few simulation ticks, whatever the frame rate).</summary>
    public const ulong TapMicros = 70_000;

    /// <summary>The window size <see cref="KeyStep.LookPerSecond"/> is given for.</summary>
    public const float ReferenceWidth = 1280f;

    /// <summary>
    /// Rotation units per mouse count divided out of an aim error to get counts: the mouse's turn per count at the
    /// reference width with a look multiplier of 1.
    /// </summary>
    public static float UnitsPerCount => OpenScctGame.Play.Session.MouseLook.UnitsPerCount * ReferenceWidth / 640f;

    public const float ReferenceHeight = 720f;

    /// <summary>Frames to wait for the marker key or the pointer capture before choosing the fallback.</summary>
    public const int SetupFrames = 6;

    private readonly PlayInput _input;
    private readonly Viewport _viewport;
    private readonly KeyPilotProbe _probe;
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Ik, int Frame, ulong Usec)> _tapReleases = [];
    private readonly List<string> _notes = [];
    private readonly List<string> _missing = [];
    private readonly List<KeyStep> _steps = [];
    private readonly bool _headless;
    private int _setup;
    private int _setupFrames;
    private bool _pushInput;
    private int _frame;
    private int _stepIndex = -1;
    private double _stepElapsed;
    private ulong _lastUsec;

    private KeyPilot(PlayInput input, Viewport viewport, KeyPilotProbe probe)
    {
        _input = input;
        _viewport = viewport;
        _probe = probe;
        _headless = DisplayServer.GetName() == "headless";
    }

    /// <summary>
    /// A pilot for the play scene's keyboard and mouse: turns its <see cref="PlayInput"/> on (headless runs leave it
    /// off), seeds the InputMap from the bindings and adds the probe node. The root must be loaded.
    /// </summary>
    public static KeyPilot Attach(PlayRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        PlayInput input = root.Controls ?? throw new InvalidOperationException("the play scene has no PlayInput (not loaded?)");
        if (input.Bindings is null)
        {
            throw new InvalidOperationException("the play scene's PlayInput has no bindings");
        }

        input.Enabled = true;
        input.Bindings.SeedInputMap();
        var probe = new KeyPilotProbe();
        root.AddChild(probe);
        return new KeyPilot(input, root.GetViewport(), probe);
    }

    public InputBindings Bindings => _input.Bindings!;

    /// <summary>The label of the running timeline step, or "" before the first and after the last.</summary>
    public string Step => _stepIndex >= 0 && _stepIndex < _steps.Count ? _steps[_stepIndex].Label : "";

    /// <summary>True when the delivery and capture are set up (the timeline only starts then).</summary>
    public bool Ready => _setup >= 2;

    /// <summary>True when a timeline was given and has run out.</summary>
    public bool Done => _steps.Count > 0 && _stepIndex >= _steps.Count;

    /// <summary>Events the probe saw reach the scene.</summary>
    public int Delivered => _probe.Seen;

    /// <summary>Events sent so far.</summary>
    public int Sent { get; private set; }

    /// <summary>How the events were delivered and the pointer captured.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Actions the bindings have no key for.</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <summary>
    /// The IK key (prefix removed) the bindings use for <paramref name="action"/>: an input byte (<c>BT_Up</c>), an exec
    /// command with or without its argument (<c>Jump</c>, <c>SelectGadget 1</c>), an alias the layout names
    /// (<c>Crouch</c>), or a key itself (<c>IK_Escape</c>). The key with the fewest other actions wins. Null when no
    /// key is bound to it.
    /// </summary>
    public string? KeyOf(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (action.StartsWith("IK_", StringComparison.OrdinalIgnoreCase))
        {
            string key = action[3..];
            return GodotKeys.ToEvent(key) is null ? null : key;
        }

        string name = action.Trim();
        int? arg = null;
        int space = name.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0 && int.TryParse(name[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int a))
        {
            arg = a;
            name = name[..space];
        }

        string? best = null;
        int bestCount = int.MaxValue;
        foreach (KeyValuePair<string, string> binding in Bindings.Layout.Bindings)
        {
            if (GodotKeys.ToEvent(binding.Key) is null)
            {
                continue;
            }

            bool match = false;
            foreach (string part in binding.Value.Split('|'))
            {
                if (string.Equals(part.Trim(), action.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    match = true;
                }
            }

            IReadOnlyList<KeyAction> actions = Bindings.Actions(binding.Key);
            foreach (KeyAction k in actions)
            {
                if (string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase) && (arg is null || arg == k.Arg))
                {
                    match = true;
                }
            }

            if (match && actions.Count < bestCount)
            {
                best = binding.Key;
                bestCount = actions.Count;
            }
        }

        return best;
    }

    /// <summary>Holds the key of <paramref name="action"/>; false (and listed in <see cref="Missing"/>) when none is bound.</summary>
    public bool Press(string action)
    {
        if (Resolve(action) is not { } ik)
        {
            return false;
        }

        if (_held.Add(ik))
        {
            Send(Make(ik, true));
        }

        return true;
    }

    /// <summary>Releases the key of <paramref name="action"/> if it is held.</summary>
    public void Release(string action)
    {
        if (KeyOf(action) is { } ik && _held.Remove(ik))
        {
            Send(Make(ik, false));
        }
    }

    /// <summary>Presses the key of <paramref name="action"/> and releases it <see cref="TapFrames"/> frames and <see cref="TapMicros"/> later.</summary>
    public bool Tap(string action)
    {
        if (Resolve(action) is not { } ik)
        {
            return false;
        }

        if (_held.Contains(ik))
        {
            return true;
        }

        Send(Make(ik, true));
        _tapReleases.Add((ik, _frame + TapFrames, Time.GetTicksUsec() + TapMicros));
        return true;
    }

    /// <summary>Moves the mouse by (<paramref name="dx"/>, <paramref name="dy"/>) pixels, right and down positive.</summary>
    public void Look(float dx, float dy)
    {
        if (dx == 0f && dy == 0f)
        {
            return;
        }

        Vector2 centre = _viewport.GetVisibleRect().Size * 0.5f;
        Send(new InputEventMouseMotion { Relative = new Vector2(dx, dy), ScreenRelative = new Vector2(dx, dy), Position = centre, GlobalPosition = centre });
    }

    /// <summary>Releases every held key.</summary>
    public void ReleaseAll()
    {
        foreach (string ik in _held)
        {
            Send(Make(ik, false));
        }

        _held.Clear();
    }

    /// <summary>Runs <paramref name="steps"/> in order once the pilot is <see cref="Ready"/>.</summary>
    public void Play(IEnumerable<KeyStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps.Clear();
        _steps.AddRange(steps);
        _stepIndex = -1;
    }

    /// <summary>Once per frame: the setup, the taps due for release, the timeline and its mouse movement.</summary>
    public void Update()
    {
        ulong now = Time.GetTicksUsec();
        double dt = _lastUsec == 0 ? 0.0 : (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        _frame++;
        for (int i = _tapReleases.Count - 1; i >= 0; i--)
        {
            if (_frame >= _tapReleases[i].Frame && now >= _tapReleases[i].Usec)
            {
                if (!_held.Contains(_tapReleases[i].Ik))
                {
                    Send(Make(_tapReleases[i].Ik, false));
                }

                _tapReleases.RemoveAt(i);
            }
        }

        if (!Ready)
        {
            Setup();
            return;
        }

        if (_steps.Count == 0 || Done)
        {
            return;
        }

        if (_stepIndex < 0)
        {
            Enter(0);
            return;
        }

        _stepElapsed += dt;
        KeyStep step = _steps[_stepIndex];
        // Timeline looks are in pixels of a 1280 x 720 window: the input mapping scales the turn by the viewport
        // size (MouseLook), so the counts are scaled back and a look turns the same in any window.
        Vector2 size = _viewport.GetVisibleRect().Size;
        float sx = size.X >= 1f ? ReferenceWidth / size.X : 1f;
        float sy = size.Y >= 1f ? ReferenceHeight / size.Y : 1f;
        Look(step.LookPerSecond.X * (float)dt * sx, step.LookPerSecond.Y * (float)dt * sy);
        if (_stepElapsed >= step.Seconds)
        {
            Enter(_stepIndex + 1);
        }
    }

    private void Enter(int index)
    {
        _stepIndex = index;
        _stepElapsed = 0.0;
        if (index >= _steps.Count)
        {
            ReleaseAll();
            return;
        }

        KeyStep step = _steps[index];
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string action in step.Hold)
        {
            if (Resolve(action) is { } ik)
            {
                wanted.Add(ik);
            }
        }

        foreach (string ik in new List<string>(_held))
        {
            if (!wanted.Contains(ik))
            {
                _held.Remove(ik);
                Send(Make(ik, false));
            }
        }

        foreach (string ik in wanted)
        {
            if (_held.Add(ik))
            {
                Send(Make(ik, true));
            }
        }

        foreach (string action in step.Tap)
        {
            Tap(action);
        }
    }

    /// <summary>
    /// Step 0: send the marker key and see whether it reaches the scene, else deliver through the viewport. Step 1:
    /// with a window, click to capture the pointer as a player does; without one (or when the capture does not take)
    /// read input without the capture.
    /// </summary>
    private void Setup()
    {
        _setupFrames++;
        if (_setup == 0)
        {
            if (_setupFrames == 1)
            {
                Send(new InputEventKey { PhysicalKeycode = MarkerKey, Keycode = MarkerKey, Pressed = true });
                Send(new InputEventKey { PhysicalKeycode = MarkerKey, Keycode = MarkerKey, Pressed = false });
                return;
            }

            if (_probe.MarkerSeen > 0)
            {
                _notes.Add("delivery: Input.ParseInputEvent reaches the scene");
                _notes.Add(string.Create(CultureInfo.InvariantCulture, $"viewport {_viewport.GetVisibleRect().Size}"));
                NextSetup();
            }
            else if (_setupFrames > SetupFrames)
            {
                _pushInput = true;
                _notes.Add("delivery: Input.ParseInputEvent did not reach the scene, events go to Viewport.PushInput (Input state is still parsed)");
                NextSetup();
            }

            return;
        }

        if (_headless)
        {
            _input.RequireCapture = false;
            _notes.Add("capture: no window, PlayInput.RequireCapture off");
            NextSetup();
            return;
        }

        if (_setupFrames == 1)
        {
            if (_input.IsCaptured)
            {
                _notes.Add("capture: already captured");
                NextSetup();
                return;
            }

            Send(Make("LeftMouse", true));
            Send(Make("LeftMouse", false));
            return;
        }

        if (_input.IsCaptured)
        {
            _notes.Add("capture: the click captured the pointer");
            NextSetup();
        }
        else if (_setupFrames > SetupFrames)
        {
            _input.RequireCapture = false;
            _notes.Add("capture: the click did not capture the pointer (window without focus?), PlayInput.RequireCapture off");
            NextSetup();
        }
    }

    private void NextSetup()
    {
        _setup++;
        _setupFrames = 0;
    }

    private string? Resolve(string action)
    {
        if (KeyOf(action) is { } ik)
        {
            return ik;
        }

        if (!_missing.Contains(action))
        {
            _missing.Add(action);
        }

        return null;
    }

    private InputEvent Make(string ik, bool pressed)
    {
        InputEvent ev = GodotKeys.ToEvent(ik) ?? throw new ArgumentException($"no Godot event for IK_{ik}", nameof(ik));
        switch (ev)
        {
            case InputEventKey key:
                key.Keycode = key.PhysicalKeycode;
                key.Pressed = pressed;
                break;
            case InputEventMouseButton button:
                Vector2 centre = _viewport.GetVisibleRect().Size * 0.5f;
                button.Pressed = pressed;
                button.Position = centre;
                button.GlobalPosition = centre;
                button.ButtonMask = pressed ? (MouseButtonMask)(1 << ((int)button.ButtonIndex - 1)) : 0;
                break;
        }

        return ev;
    }

    private void Send(InputEvent ev)
    {
        Sent++;
        if (_pushInput)
        {
            // Keep the Input singleton's state (IsKeyPressed, actions) as a real event would, then run the scene's passes.
            Input.ParseInputEvent(ev);
            _viewport.PushInput(ev, true);
        }
        else
        {
            Input.ParseInputEvent(ev);
        }
    }
}
