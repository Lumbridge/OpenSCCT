using System;
using Godot;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;

namespace OpenScctGame.Options;

/// <summary>
/// Keeps the player's settings in effect in the play scene: the session's mouse look, the frame cap of the local
/// player's side (Reloaded's fps_spy / fps_merc), the audio bus volumes (again when the sound system adds buses), the
/// HUD switches, the widescreen field of view (Hor+ and the merc's ws_fov cap, or the stretched 4:3 picture and HUD)
/// and the controller. F12 opens the options over the game (Reloaded's settings overlay); saving there
/// applies at once.
/// </summary>
public partial class OptionsRuntime : Node
{
    private int _busCount = -1;
    private GameSettings _settings = SettingsStore.Current;
    private OptionsMenu? _overlay;
    private CanvasLayer? _overlayLayer;
    private bool _inputWasEnabled = true;
    private FaithfulView? _view;

    public OptionsRuntime()
    {
        Name = "OptionsRuntime";
    }

    public PlaySession? Session { get; set; }

    public PlayInput? PlayInput { get; set; }

    public HudLayer? Hud { get; set; }

    public GamepadInput? Gamepad { get; private set; }

    public GameSettings Settings => _settings;

    /// <summary>Adds the runtime (and its controller node) under <paramref name="parent"/>.</summary>
    public static OptionsRuntime Attach(Node parent, PlaySession session, PlayInput? input, HudLayer? hud)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(session);
        var runtime = new OptionsRuntime { Session = session, PlayInput = input, Hud = hud };
        parent.AddChild(runtime);
        return runtime;
    }

    /// <summary>The frame caps apply (not in headless or smoke runs, which run as fast as they can).</summary>
    public bool CapFrames { get; set; }

    public override void _Ready()
    {
        CapFrames = DisplayServer.GetName() != "headless" && GetNodeOrNull<App>(App.NodePath) is not { IsSmokeRun: true };
        Gamepad = new GamepadInput { PlayInput = PlayInput, Session = Session };
        AddChild(Gamepad);
        SettingsStore.Saved += OnSaved;
        if (Hud is not null)
        {
            Hud.InitialConnectedPlayers = _settings.ShowScoreboard;
            Hud.ConnectedPlayersToggled += OnConnectedPlayers;
        }

        Apply(_settings);
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSaved;
        if (Hud is not null)
        {
            Hud.ConnectedPlayersToggled -= OnConnectedPlayers;
        }

        // Outside the game Reloaded uses the merc cap.
        if (CapFrames)
        {
            SettingsApplier.ApplyFrameCap(_settings, merc: true);
        }
    }

    private void OnSaved(GameSettings s) => Apply(s);

    /// <summary>Reloaded remembers the scoreboard's state.</summary>
    private void OnConnectedPlayers(bool shown)
    {
        if (_settings.ShowScoreboard != shown)
        {
            _settings.ShowScoreboard = shown;
            SettingsStore.Save(_settings);
        }
    }

    /// <summary>Applies <paramref name="s"/> to the session, the HUD, the pad and the audio.</summary>
    public void Apply(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _settings = s;
        if (Session is { } session)
        {
            session.Mouse = SettingsApplier.MouseLookOf(s, session.Mouse);
        }

        if (Hud is not null)
        {
            Hud.Options.ShowPing = s.ShowPing;
            Hud.Options.StretchHud = !s.Widescreen;
            Hud.ShowFps = s.ShowFps;
        }

        if (Gamepad is not null)
        {
            Gamepad.Settings = s;
        }

        SettingsApplier.ApplyAudio(s);
        _busCount = AudioServer.BusCount;
    }

    public override void _Process(double delta)
    {
        if (CapFrames)
        {
            bool merc = Session?.LocalPawn is SPAWNDEFENSE || (Session?.Settings.Side == PlaySide.Merc && Session.LocalPawn is null);
            SettingsApplier.ApplyFrameCap(_settings, merc);
        }

        if (GetParent() is PlayRoot { View: { } view } && !ReferenceEquals(view, _view))
        {
            _view = view;
            view.VerticalFovPolicy = VerticalFov;
        }

        if (_view is not null)
        {
            _view.StretchPastWidest = _settings.Widescreen;
        }

        if (AudioServer.BusCount != _busCount)
        {
            SettingsApplier.ApplyAudio(_settings);
            _busCount = AudioServer.BusCount;
        }
    }

    /// <summary>True while the local player sees through a merc's eyes.</summary>
    public bool MercFirstPerson => Session?.LocalPawn is SPAWNDEFENSE && Session.LocalController is { bBehindView: false };

    /// <summary>True unless the local controller is in a view Reloaded leaves uncapped (sticky camera, camera network).</summary>
    public bool FovCapApplies => ViewFov.CapApplies(Session?.LocalController?.GetStateName().ToString());

    private float VerticalFov(float fov43, float aspect) => ViewFov.Vertical(fov43, aspect, _settings, FovCapApplies);

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F12 } && _overlay is null)
        {
            OpenOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>The options over the game (F12); the player's controls stop while they are open.</summary>
    public void OpenOverlay()
    {
        if (_overlay is not null)
        {
            return;
        }

        if (PlayInput is { Target: { } target } input)
        {
            _inputWasEnabled = input.Enabled;
            input.ClearHeld(target);
            input.Enabled = false;
            input.ReleasePointer();
        }

        _overlayLayer = new CanvasLayer { Name = "OptionsOverlay", Layer = HudLayer.DefaultLayer + 5 };
        _overlay = new OptionsMenu { Overlay = true };
        _overlay.Closed += CloseOverlay;
        _overlayLayer.AddChild(_overlay);
        AddChild(_overlayLayer);
    }

    private void CloseOverlay()
    {
        _overlayLayer?.QueueFree();
        _overlayLayer = null;
        _overlay = null;
        if (PlayInput is { } input)
        {
            input.Enabled = _inputWasEnabled;
        }
    }
}
