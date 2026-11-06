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
/// Keeps the player's settings in effect in the play scene: the session's mouse look,
/// the audio bus volumes (again when the sound system adds buses), the
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

    public override void _Ready()
    {
        if (PlayInput is { } actionInput) actionInput.Controller = () => Session?.LocalController;
        Gamepad = new GamepadInput { PlayInput = PlayInput, Session = Session };
        AddChild(Gamepad);
        SettingsStore.Saved += OnSaved;
        if (Hud is not null)
        {
            Hud.Effects.Applied += OnLocalNotify;
        }

        Apply(_settings);
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSaved;
        if (Hud is not null)
        {
            Hud.Effects.Applied -= OnLocalNotify;
        }
    }

    private void OnSaved(GameSettings s) => Apply(s);

    /// <summary>The controller's rumble for the local player's notifications (rl:input.rumble).</summary>
    public PadRumble Rumble { get; } = new();

    private void OnLocalNotify(SPlayerController pc, OpenScct.Core.SimEvent e, float now) => Rumble.OnNotify(pc, e, now, _settings.Pad);

    /// <summary>
    /// The keyboard and mouse clutches (rl:input.clutch): while the key of one is held, the session's mouse look takes
    /// the side's clutch multiplier (the merc has two; the spy only the first).
    /// </summary>
    private void UpdateClutch(PlaySession session)
    {
        bool merc = session.LocalPawn is SPAWNDEFENSE;
        bool typing = GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit;
        bool one = !typing && KeyHeld(_settings.ClutchOneKey);
        bool two = !typing && merc && KeyHeld(_settings.ClutchTwoKey);
        float clutch = _settings.LookOf(merc, pad: false).ClutchOf(one, two);
        if (session.Mouse.Clutch != clutch)
        {
            session.Mouse = session.Mouse with { Clutch = clutch };
        }
    }

    /// <summary>A key or mouse button, by its UE2 name, is down.</summary>
    private static bool KeyHeld(string ik)
    {
        if (ik.Length == 0)
        {
            return false;
        }

        InputEvent? binding = OpenScctGame.Controls.GodotKeys.ToEvent(ik);
        if (binding is InputEventWithModifiers modifiers &&
            ((modifiers.CtrlPressed && !Input.IsPhysicalKeyPressed(Key.Ctrl)) ||
             (modifiers.ShiftPressed && !Input.IsPhysicalKeyPressed(Key.Shift)) ||
             (modifiers.AltPressed && !Input.IsPhysicalKeyPressed(Key.Alt)))) return false;
        return binding switch
        {
            InputEventKey key => Input.IsPhysicalKeyPressed(key.PhysicalKeycode),
            InputEventMouseButton button => Input.IsMouseButtonPressed(button.ButtonIndex),
            _ => false,
        };
    }

    /// <summary>Applies <paramref name="s"/> to the session, the HUD, the pad and the audio.</summary>
    public void Apply(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _settings = s;
        if (Session is { } session)
        {
            session.Mouse = SettingsApplier.MouseLookOf(s, session.Mouse);
            session.SpyRollFreeLook = s.SpyRollFreeLook;
            session.SpyCloakHold = s.SpyKbm.CloakBehavior == "Hold";
        }

        if (Hud is not null)
        {
            Hud.Options.ClassicCrosshair = s.ClassicCrosshair;
            Hud.Options.Crosshair = s.Crosshair.Clone();
            Hud.Options.Layout = s.HudLayout.Clone();
            Hud.Options.StretchHud = !s.WidescreenHud;
            Hud.Markers.Widescreen = s.Widescreen;
            Hud.Markers.WidescreenHud = s.WidescreenHud;
            Hud.Markers.ErSmall = s.WideErSmall;
            Hud.ShowFps = s.ShowFps;
            Hud.ShowNet = s.ShowNet;
            Hud.Fps.Settings = s.FpsTelemetry.Clone();
            Hud.Net.Settings = s.NetworkTelemetry.Clone();
            Hud.Net.ShowLoss = s.ShowNetLoss;
            Hud.ShowLocation = s.ShowLocation;
            Hud.TeammateNames.ShowSpy = s.TeammateNamesSpy;
            Hud.TeammateNames.ShowMerc = s.TeammateNamesMerc;
            Hud.TeammateNames.ShowHealth = s.TeammateHealth;
            Hud.TeammateNames.Opacity = s.TeammateNamesOpacity / 100f;
            Hud.DistanceInFeet = s.HudFeet;
            Hud.Markers.HackAlpha = s.HackAlpha / 255f;
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
        if (Session is { } session && _overlay is null)
        {
            UpdateClutch(session);
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
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F12,
            CtrlPressed: false, ShiftPressed: false, AltPressed: false, MetaPressed: false } && _overlay is null)
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
