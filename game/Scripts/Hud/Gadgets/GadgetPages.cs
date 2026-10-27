using System;
using System.Globalization;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud.Gadgets;

/// <summary>
/// A full gadget page (P_StickyCam, P_CamNetWork) and its page switch (spec/equipment/gadget-hud.md rule 5): active
/// while the local controller's HUD is in the page's view state; the page starts afresh each time the view is entered,
/// as the native page object is made on entry.
/// </summary>
public abstract class GadgetViewPage : HudPage, IHudPageOverride
{
    private bool _active;

    protected GadgetViewPage(HudAssets assets, string pageName, HudOptions? options, GadgetView view)
        : base(assets, pageName, options)
    {
        View = view;
    }

    /// <summary>The view this page shows.</summary>
    public GadgetView View { get; }

    /// <summary>Times the view was entered (the page made afresh).</summary>
    public int Entries { get; private set; }

    /// <summary>The controller the page reads this frame.</summary>
    protected SPlayerController? Controller { get; private set; }

    public HudPage? ActivePage(SPlayerController? pc, HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        bool active = s.Valid && !s.Spectator.Active && GadgetHudRules.ViewOf(pc) == View;
        Controller = pc;
        if (active && !_active)
        {
            Entries++;
            Enter();
        }

        _active = active;
        return active ? this : null;
    }

    /// <summary>The native page init: every area at frame 0 and hidden, then the page's own areas restarted.</summary>
    protected virtual void Enter()
    {
        foreach (MagmaNode child in Root.Children)
        {
            child.SetFrame(0);
            child.Playing = false;
            child.SetVisible(false);
            child.PositionOverride = null;
        }

        Hide(Area("GameInvite"));
    }

    /// <summary>The name of the key bound to <paramref name="action"/>: the layout's, or the stock key without a layout.</summary>
    protected string KeyName(string action, string stockKey)
    {
        if (Options.KeyName is { } name)
        {
            return name(action) ?? "";
        }

        return stockKey.Length == 0 ? "" : Assets.KeyDisplayName(stockKey);
    }

    /// <summary>"zoom in / zoom out" key names (rules 7 and 12).</summary>
    protected string ZoomKeys() =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyName("WheelUp", "MouseWheelUp")} / {KeyName("WheelDown", "MouseWheelDown")}");

    /// <summary>The exit key: the one bound to AltFire (the stock layout's right mouse button).</summary>
    protected string ExitKey() => KeyName("AltFire", "RightMouse");

    /// <summary>The Use key (gas, next camera).</summary>
    protected string UseKey() => KeyName("bUsing", "Shift");
}

/// <summary>
/// P_StickyCam, the spy's sticky cam view (gadget-hud.md rules 6 to 8): the scope frame, sweep and interference bars
/// (Magma loops), with the key help. No per-frame data: the field of view is the only zoom feedback.
/// </summary>
public sealed class StickyCamPage : GadgetViewPage
{
    public const string PageName = "P_StickyCam";

    private static readonly string[] ConsoleWidgets =
        ["IconStickyCamUp", "IconStickyCamA", "IconStickyCamB", "textStickyCam1", "textStickyCam2", "textStickyCam3"];

    private string _keys = "";

    public StickyCamPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options, GadgetView.StickyCam)
    {
        // A full-screen scope view like the binoculars: stretched over the whole screen, not kept in the 4:3 middle.
        Renderer.StretchAll = true;
    }

    public override HudSide Side => HudSide.Spy;

    public MagmaNode? Frame => Area("StickyCam");

    protected override void Enter()
    {
        base.Enter();
        if (Frame is { } frame)
        {
            frame.Restart();
            foreach (string w in ConsoleWidgets)
            {
                frame.Widget(w).Hidden = true;
            }

            Hide(frame.Find("cadre"));
        }

        WriteBindings();
    }

    protected override void Drive(HudSnapshot s, float dt)
    {
        if (Keys() != _keys)
        {
            WriteBindings();
        }
    }

    private string Keys() => ZoomKeys() + "|" + UseKey() + "|" + ExitKey();

    private void WriteBindings()
    {
        _keys = Keys();
        SetText(Frame, "PCtextStickyCam1Binding", ZoomKeys());
        SetText(Frame, "PCtextStickyCam2Binding", UseKey());
        SetText(Frame, "PCtextStickyCam3Binding", ExitKey());
    }
}

/// <summary>
/// P_CamNetWork, the merc's camera network view (gadget-hud.md rules 9 to 13): the camera frame, the camera name, the
/// ALERT box while a spy hacks, and the key help.
/// </summary>
public sealed class CamNetworkPage : GadgetViewPage
{
    public const string PageName = "P_CamNetWork";

    private static readonly string[] ConsoleWidgets =
        ["IconStickyCamUp", "IconCamNetA", "IconCamNetB", "IconCamNetX", "textCamNetwork1", "textCamNetwork2", "textCamNetwork3", "textCamNetwork5"];

    private string _keys = "";

    public CamNetworkPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options, GadgetView.CamNetwork)
    {
        // Full-screen camera view, stretched like the sticky cam and binoculars.
        Renderer.StretchAll = true;
    }

    public override HudSide Side => HudSide.Merc;

    public MagmaNode? Frame => Area("Camnetwork");

    /// <summary>The camera name shown.</summary>
    public string CameraName { get; private set; } = "";

    /// <summary>Whether the ALERT box is up.</summary>
    public bool Alert => Area("Alert") is { Visible: true };

    protected override void Enter()
    {
        base.Enter();
        CameraName = "";
        if (Frame is { } frame)
        {
            frame.Restart();
            foreach (string w in ConsoleWidgets)
            {
                frame.Widget(w).Hidden = true;
            }

            SetText(frame, "textCamNetwork4", "");
        }

        WriteBindings();
    }

    protected override void Drive(HudSnapshot s, float dt)
    {
        if (GadgetHudRules.CamNetworkName(Controller) is { } name && name != CameraName)
        {
            CameraName = name;
            SetText(Frame, "textCamNetwork4", name);
        }

        SetShown(Area("Alert"), GadgetHudRules.CamNetworkAlert(Controller?.myHUD as SHud));
        if (Keys() != _keys)
        {
            WriteBindings();
        }
    }

    /// <summary>Next / previous camera: the Use key, and the back-to-wall key after it when one is bound (rule 12).</summary>
    private string CameraKeys()
    {
        string use = UseKey();
        string btw = KeyName("bUseBackToWall", "");
        return btw.Length > 0 ? use + " / " + btw : use;
    }

    private string Keys() => ZoomKeys() + "|" + CameraKeys() + "|" + ExitKey();

    private void WriteBindings()
    {
        _keys = Keys();
        SetText(Frame, "PCtextCamNetwork1Binding", ZoomKeys());
        SetText(Frame, "PCtextCamNetwork2Binding", CameraKeys());
        SetText(Frame, "PCtextCamNetwork3Binding", ExitKey());
    }
}
