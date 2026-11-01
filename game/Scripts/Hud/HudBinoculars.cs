using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The binocular pages of the HUD page manager (spec/hud/binoculars.md rule 1): while the spy is in s_Binoculars_Att
/// P_BinocularsATT replaces P_HudATT, while the merc is in s_Binoculars_Def (or its reloading child state)
/// P_BinocularsDEF replaces P_HudDEF. The native manager makes a new page object each time, so a page starts afresh
/// when its view is entered.
/// </summary>
public sealed class HudBinocularPages : IHudPageOverride
{
    private HudBinocularMode _last;

    public HudBinocularPages(HudAssets assets, HudOptions options)
    {
        ArgumentNullException.ThrowIfNull(assets);
        Spy = assets.Hud.Pages.ContainsKey(SpyBinocularsPage.PageName) ? new SpyBinocularsPage(assets, options) : null;
        Merc = assets.Hud.Pages.ContainsKey(MercBinocularsPage.PageName) ? new MercBinocularsPage(assets, options) : null;
    }

    public SpyBinocularsPage? Spy { get; }

    public MercBinocularsPage? Merc { get; }

    public HudPage? ActivePage(OpenScct.Game.SBase.SPlayerController? pc, HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        HudBinocularMode mode = s.Valid && !s.Spectator.Active ? s.Binoculars.Mode : HudBinocularMode.None;
        if (mode != _last)
        {
            _last = mode;
            if (mode == HudBinocularMode.Spy)
            {
                Spy?.Enter();
            }
            else if (mode == HudBinocularMode.Merc)
            {
                Merc?.Enter();
            }
        }

        return mode switch
        {
            HudBinocularMode.Spy => Spy,
            HudBinocularMode.Merc => Merc,
            _ => null,
        };
    }

    /// <summary>
    /// The merc's aim point (SHud s_H_Binoculars_Def.PostRender, rule 11): the point 1000 uu out along
    /// MyDesiredRotation projected to the 640 x 480 canvas, clamped to the middle half of the width and the middle
    /// 60 percent of the height.
    /// </summary>
    [Uc("SBase.SHud.s_H_Binoculars_Def.PostRender", Sha1 = "d36ad5cbc38bdc5bc05f36519bc719ef99ed0bb7")]
    [PortOf("uc:SBase.SHud:s_H_Binoculars_Def.PostRender", Spec = "spec/hud/binoculars.md#rules")]
    public static Vector2? AimPoint(FVector? aim, Func<FVector, Vector2?>? projector, Vector2 screen, bool stretch = false)
    {
        if (aim is not { } a || projector?.Invoke(a) is not { } p || screen.X <= 0f || screen.Y <= 0f)
        {
            return null;
        }

        float scale = screen.Y / MagmaRenderer.PageHeight;
        float x = stretch ? p.X * MagmaRenderer.PageWidth / screen.X
            : MagmaRenderer.PageWidth * 0.5f + (p.X - screen.X * 0.5f) / scale;
        float y = p.Y / scale;
        return new Vector2(
            Math.Clamp(x, MagmaRenderer.PageWidth * 0.25f, MagmaRenderer.PageWidth * 0.75f),
            Math.Clamp(y, MagmaRenderer.PageHeight * 0.2f, MagmaRenderer.PageHeight * 0.8f));
    }
}

/// <summary>
/// The merc's scope aim point (the point <see cref="HudK.BinocularAimDistance"/> out along MyDesiredRotation from the
/// eye, as <see cref="HudSimReader"/> reads it) sampled after every client tick and drawn between the last two samples
/// by the frame's interpolation alpha, so the sights move with the drawn camera instead of stepping at the 60 Hz tick
/// against it. A jump longer than <see cref="CutDistance"/> (a respawn) is drawn at once.
/// </summary>
public sealed class HudBinocularAim
{
    public const float CutDistance = 512f;

    private FVector _previous;
    private FVector _current;
    private int _samples;

    /// <summary>After each client tick: the aim point of <paramref name="pc"/>'s pawn, or none without a pawn.</summary>
    public void OnTick(OpenScct.Game.SBase.SPlayerController? pc)
    {
        if (pc?.Pawn is not { } pawn)
        {
            _samples = 0;
            return;
        }

        pc.MyDesiredRotation.GetAxes(out FVector x, out _, out _);
        _previous = _current;
        _current = pawn.Location + OpenScct.Game.Combat.CombatServices.Ports(pc).EyePosition(pawn) + (x * HudK.BinocularAimDistance);
        _samples++;
    }

    /// <summary>The aim point <paramref name="alpha"/> of the way from the previous tick's to the last one's, or null before a sample.</summary>
    public FVector? At(float alpha)
    {
        if (_samples == 0)
        {
            return null;
        }

        if (_samples == 1 || (_current - _previous).SizeSquared() > CutDistance * CutDistance)
        {
            return _current;
        }

        float t = Math.Clamp(alpha, 0f, 1f);
        return _previous + ((_current - _previous) * t);
    }
}

/// <summary>The parts both binocular page objects share: the fresh start, the zoom label, the speak box, key names.</summary>
public abstract class BinocularsPage : HudPage
{
    private readonly string _zoomArea;
    private readonly string[] _hiddenWidgets;
    private readonly string[] _shownAreas;

    protected BinocularsPage(HudAssets assets, string pageName, HudOptions? options, string zoomArea, string[] shownAreas,
        string[] hiddenWidgets)
        : base(assets, pageName, options)
    {
        _zoomArea = zoomArea;
        _shownAreas = shownAreas;
        _hiddenWidgets = hiddenWidgets;

        // The spy binocular artwork fills the screen. The merc scope overrides this for circular widescreen optics.
        Renderer.StretchAll = true;
    }

    /// <summary>The zoom step the labels show: trunc(fBinocularVisionFOV x 0.1), 1 at the page's creation.</summary>
    public int ZoomIndex { get; private set; } = 1;

    public MagmaNode? Zoom => Area(_zoomArea);

    /// <summary>
    /// The page object's creation (rule 2): every area at frame 0, the frame, sound and zoom areas shown and played,
    /// the console-only texts and the unused key help hidden, the zoom label "x2" with the step recorded as 1, the
    /// invitation icon hidden, and the key names written.
    /// </summary>
    public virtual void Enter()
    {
        foreach (MagmaNode child in Root.Children)
        {
            child.SetFrame(0);
            child.Playing = false;
            child.SetVisible(false);
            child.PositionOverride = null;
        }

        foreach (string area in _shownAreas)
        {
            Area(area)?.Restart();
        }

        MagmaNode? zoom = Zoom;
        if (zoom is not null)
        {
            foreach (string w in _hiddenWidgets)
            {
                zoom.Widget(w).Hidden = true;
            }

            SetText(zoom, "ZoomValue", ZoomLabel(3));
        }

        ZoomIndex = 1;
        if (Area("Speak") is { } speak)
        {
            speak.Widget("Element4").Hidden = true;
        }

        Hide(Area("GameInvite"));
        WriteBindings();
    }

    /// <summary>The zoom label of a step: 1 is "x6" (FOV 10), 2 is "x4" (FOV 20), anything else "x2" (FOV 30).</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsATT init 0x10b35190 / P_BinocularsDEF init 0x10b33d70: strings x6, x4, x2")]
    public static string ZoomLabel(int index) => index switch
    {
        1 => "x6",
        2 => "x4",
        _ => "x2",
    };

    /// <summary>The zoom step of a field of view.</summary>
    public static int ZoomIndexOf(float fov) => (int)(fov * 0.1f);

    /// <summary>Rule 3: on a change of step the label changes; the page reacts through <see cref="OnZoom"/>.</summary>
    protected void DriveZoom(HudSnapshot s)
    {
        int index = ZoomIndexOf(s.Binoculars.Fov);
        if (index == ZoomIndex)
        {
            return;
        }

        OnZoom(index, ZoomIndex);
        SetText(Zoom, "ZoomValue", ZoomLabel(index));
        ZoomIndex = index;
    }

    protected abstract void OnZoom(int index, int previous);

    /// <summary>
    /// The speak box (rule 7): while the controller has a voice chat target the box shows "Hack"; when it was hidden
    /// it is shown, rewound and played. Without a target a shown box hides.
    /// </summary>
    protected void DriveSpeak(HudSnapshot s, string textWidget)
    {
        MagmaNode? speak = Area("Speak");
        if (speak is null)
        {
            return;
        }

        string prompt = s.Binoculars.HackPrompt;
        if (prompt.Length == 0)
        {
            Hide(speak);
            return;
        }

        SetText(speak, textWidget, prompt);
        if (!speak.Visible)
        {
            speak.Restart();
        }
    }

    /// <summary>The name of the key bound to <paramref name="command"/>: the layout's, or the stock one without a layout.</summary>
    protected string KeyName(string command, string stockKey)
    {
        if (Options.KeyName is { } name)
        {
            return name(command) ?? "";
        }

        return Assets.KeyDisplayName(stockKey);
    }

    /// <summary>"Zoom In / Zoom Out:" takes the wheel keys as "%s / %s" (rule 6).</summary>
    protected string ZoomKeys() =>
        string.Create(CultureInfo.InvariantCulture, $"{KeyName("WheelUp", "MouseWheelUp")} / {KeyName("WheelDown", "MouseWheelDown")}");

    /// <summary>Writes the key help texts (the page's bindings function, run at creation and when the keys change).</summary>
    protected abstract void WriteBindings();

    /// <summary>The home rect of a widget of <paramref name="node"/> (its first key).</summary>
    protected static Rect4 Home(MagmaNode node, string widget)
    {
        int i = node.Area.WidgetIndex(widget);
        return i >= 0 && node.Area.Widgets[i].Keys.Count > 0 ? node.Area.Widgets[i].Keys[0].Rect : default;
    }
}

/// <summary>
/// P_BinocularsATT, the spy's binocular view (spec/hud/binoculars.md rules 2 to 7): the goggle frame, the sound
/// meter, the zoom scale with its label and marker, the key help, the Hack prompt, and on the canvas the objective
/// markers of <see cref="HudBinocularMarkers"/>.
/// </summary>
public sealed class SpyBinocularsPage : BinocularsPage
{
    public const string PageName = "P_BinocularsATT";

    /// <summary>How far the zoom marker moves from its home for x6 (right) and x2 (left).</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsATT init 0x10b35190: zoomInside x + 0x23 and x - 0x23")]
    public const float ZoomMarkerStep = 35f;

    private string _keys = "";

    public SpyBinocularsPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options, "ZoomAtt", ["frame", "SoundAnimation", "ZoomAtt", "Speak"],
            ["IconZoomUp", "IconZoomDown", "textZoomAtt1", "textZoomAtt2", "PCtextZoomAtt3", "PCtextZoomAtt3Binding"])
    {
    }

    public override HudSide Side => HudSide.Spy;

    /// <summary>The objective markers drawn over the page.</summary>
    public HudBinocularMarkers Markers { get; } = new();

    /// <summary>World point to screen pixels, for the markers.</summary>
    public Func<FVector, Vector2?>? Projector { get; set; }

    protected override void Drive(HudSnapshot s, float dt)
    {
        DriveZoom(s);
        DriveSpeak(s, "_TextSpeakATT");
        string keys = ZoomKeys() + KeyName("SwitchBinocularVision", "B");
        if (keys != _keys)
        {
            WriteBindings();
        }
    }

    protected override void OnZoom(int index, int previous)
    {
        if (Zoom is not { } zoom)
        {
            return;
        }

        Rect4 home = Home(zoom, "zoomInside");
        float dx = index switch
        {
            1 => ZoomMarkerStep,
            2 => 0f,
            _ => -ZoomMarkerStep,
        };
        zoom.Widget("zoomInside").Pos = new Vector2(home.L + dx, home.T);
    }

    protected override void WriteBindings()
    {
        MagmaNode? zoom = Zoom;
        string exit = KeyName("SwitchBinocularVision", "B");
        _keys = ZoomKeys() + exit;
        SetText(zoom, "PCtextZoomAtt1Binding", ZoomKeys());
        SetText(zoom, "PCtextZoomAtt2Binding", exit);
        SetText(zoom, "PCtextZoomAtt3Binding", KeyName("bSpecialsChat", "H"));
    }

    /// <summary><c>SHud s_H_Binoculars_Att.PostRender</c>: the binocular markers while Enhanced Reality is on (rule 12).</summary>
    [Uc("SBase.SHud.s_H_Binoculars_Att.PostRender", Sha1 = "ec4f239b9d4ed6ce15b4e2d91daaddcc4c4eb1b2")]
    [PortOf("uc:SBase.SHud:s_H_Binoculars_Att.PostRender", Spec = "spec/hud/binoculars.md#rules")]
    protected override void DrawCanvas(Vector2 screen, HudSnapshot s)
    {
        Markers.Projector = Projector;
        Markers.UseKey = KeyName("bUsing", "Shift");
        Markers.Draw(s, screen, Assets);
        DrawList.Items.AddRange(Markers.DrawList.Items);
    }
}

/// <summary>
/// P_BinocularsDEF, the merc's rifle scope (spec/hud/binoculars.md rules 2, 3 and 8 to 11): the scope rings that
/// close or open on a zoom step, the sights and axes at the aim point, the breath gauge, the range finder, the zoom
/// label, the key help, the reticle message and the alert box.
/// </summary>
public sealed class MercBinocularsPage : BinocularsPage
{
    public const string PageName = "P_BinocularsDEF";

    /// <summary>The frame's zoom-in animation (frames 10 to 19) and zoom-out animation (20 to 29).</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: SetTime(frame x 10) zooming in, x 20 zooming out, then Play")]
    public const int ZoomInFrame = 10;

    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0")]
    public const int ZoomOutFrame = 20;

    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: sights corners 0xff44b2fd")]
    public static readonly Color SightsColour = new(253 / 255f, 178 / 255f, 68 / 255f, 1f);

    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: sights corners 0xff0000ff for 0.4 s after a hit")]
    public static readonly Color SightsHitColour = new(1f, 0f, 0f, 1f);

    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: gauge frame trunc((5 - BinocularHoldBreathTime) x 4)")]
    public const float BreathFull = 5f;

    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0")]
    public const float BreathFramesPerSecond = 4f;

    private string _keys = "";
    private bool _alert;

    public MercBinocularsPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options, "ZoomDef", ["frame", "ZoomDef", "Reload", "Speak"],
            ["IconZoomUp", "IconZoomDown", "textZoomDef1", "textZoomDef2", "textZoomDef3", "PCtextZoomDef4", "PCtextZoomDef4Binding"])
    {
        // Keep the centred 4:3 optics and text at height-based scale. The renderer extends border quads
        // (the tint and axes) to the viewport edges; the explicit legacy StretchHud option still applies.
        Renderer.StretchAll = false;
    }

    public override HudSide Side => HudSide.Merc;

    /// <summary>The frame of the breath gauge last set.</summary>
    public int BreathFrame { get; private set; }

    public override void Enter()
    {
        base.Enter();
        _alert = false;
        SetText(Zoom, "distance", "");
        if (Area("Reload") is { } reload)
        {
            reload.Widget("messageReticule").Hidden = true;
            SetText(reload, "messageReticule", "");
        }
    }

    protected override void Drive(HudSnapshot s, float dt)
    {
        HudBinoculars b = s.Binoculars;
        MagmaNode? zoom = Zoom;
        if (b.Distance is { } d && zoom is not null)
        {
            SetText(zoom, "distance", DistanceText(d, s.DistanceScale > 0f ? s.DistanceScale : HudK.DefaultDistanceScale));
        }

        DriveZoom(s);
        Vector2 spot = s.SpotReticule ?? HudK.ReticleCentre;
        if (zoom is not null)
        {
            zoom.Widget("sights").Color = b.Hit ? SightsHitColour : SightsColour;
            Rect4 home = Home(zoom, "sights");
            float halfW = MathF.Truncate(HudK.ReticleCentre.X - home.L), halfH = MathF.Truncate(HudK.ReticleCentre.Y - home.T);
            Vector2 at = b.HoldBreath ? HudK.ReticleCentre : spot;
            if (b.HoldBreath)
            {
                zoom.Widget("sights").Pos = new Vector2(HudK.ReticleCentre.X - halfW, HudK.ReticleCentre.Y - halfH);
            }
            else
            {
                zoom.Widget("sights").Pos = new Vector2(MathF.Truncate(spot.X - halfW), MathF.Truncate(spot.Y - halfH));
            }

            zoom.Widget("verticalAxis").Pos = new Vector2(MathF.Truncate(at.X), 0f);
            zoom.Widget("horizontalAxis").Pos = new Vector2(0f, MathF.Truncate(at.Y));

            if (zoom.Find("breathGauge") is { } gauge)
            {
                BreathFrame = (int)((BreathFull - b.BreathTime) * BreathFramesPerSecond);
                gauge.SetFrame(BreathFrame);
                gauge.Playing = false;
            }
        }

        DriveSpeak(s, "_TextSpeakDEF");
        DriveReticleMessage(s, spot);
        DriveAlert(s.ObjectiveCountdowns.Count > 0);
        string keys = ZoomKeys() + KeyName("SwitchBinocularVision", "B") + KeyName("bHoldBreath", "RightMouse");
        if (keys != _keys)
        {
            WriteBindings();
        }
    }

    /// <summary>Rule 9: "%02i.%02i" of the whole distance units and the uu left over.</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: a = trunc(d x fDistanceUnit), b = trunc(d - a / fDistanceUnit)")]
    public static string DistanceText(float d, float unit)
    {
        int a = (int)(d * unit);
        int b = (int)(d - a / unit);
        return string.Create(CultureInfo.InvariantCulture, $"{a:00}.{b:00}");
    }

    protected override void OnZoom(int index, int previous)
    {
        if (Area("frame") is { } frame)
        {
            frame.SetFrame(index < previous ? ZoomInFrame : ZoomOutFrame);
            frame.Playing = true;
        }
    }

    protected override void WriteBindings()
    {
        MagmaNode? zoom = Zoom;
        string exit = KeyName("SwitchBinocularVision", "B");
        string breath = KeyName("bHoldBreath", "RightMouse");
        _keys = ZoomKeys() + exit + breath;
        SetText(zoom, "PCtextZoomDef1Binding", ZoomKeys());
        SetText(zoom, "PCtextZoomDef2Binding", exit);
        SetText(zoom, "PCtextZoomDef3Binding", breath);
        SetText(zoom, "PCtextZoomDef4Binding", KeyName("bSpecialsChat", "H"));
    }

    /// <summary>
    /// The reticle message (rule 10): shown while SHud.ReticuleMessage lasts, moved by the aim point's offset from
    /// the page centre.
    /// </summary>
    private void DriveReticleMessage(HudSnapshot s, Vector2 spot)
    {
        if (Area("Reload") is not { } reload)
        {
            return;
        }

        bool shown = s.ReticleMessage.Length > 0;
        reload.Widget("messageReticule").Hidden = !shown;
        if (!shown)
        {
            return;
        }

        Rect4 home = Home(reload, "messageReticule");
        reload.Widget("messageReticule").Pos = new Vector2(home.L + MathF.Truncate(spot.X - HudK.ReticleCentre.X),
            home.T + MathF.Truncate(spot.Y - HudK.ReticleCentre.Y));
        SetText(reload, "messageReticule", s.ReticleMessage);
    }

    /// <summary>The alert box as on the merc page (spec/hud/merc-reticle.md rule 13; its trigger here is inferred).</summary>
    private void DriveAlert(bool alert)
    {
        MagmaNode? box = Area("Alert");
        if (alert && !_alert)
        {
            box?.Restart();
        }
        else if (!alert)
        {
            Hide(box);
        }

        _alert = alert;
    }
}

/// <summary>
/// The spy's objective markers inside the binoculars (<c>SHud.DrawObjectivesBinocularsAtt</c>, spec/hud/binoculars.md
/// rules 12 to 17), drawn on the 640 x 480 SHud canvas stretched over the screen: the trigger nearest the reticle
/// gets the lock panel with its name, distance and lock hint; the others an icon with pulsing brackets; triggers off
/// the screen an arrow on its edge.
/// </summary>
public sealed class HudBinocularMarkers
{
    public const string Package = "SHudTextures";
    public const string IconAtlas = "MN_boutons";
    public const string PanelTexture = "BinocularA_all";
    public const string ArrowTexture = "Att_Arrow";

    [Provenance(ProvenanceKind.Decoded, "SHud s_H_Binoculars_Att.PostRender: DrawObjectivesBinocularsAtt(C, ClipX * 0.46, ClipY * 0.45)")]
    public static readonly Vector2 Target = new(MagmaRenderer.PageWidth * 0.46f, MagmaRenderer.PageHeight * 0.45f);

    /// <summary>Margins: half the arrow texture plus 52 and 51 canvas units (Att_Arrow is 32 x 32).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawObjectivesBinocularsAtt 0x10a90f70: USize * 0.5 + 52, VSize * 0.5 + 51")]
    public static readonly Vector2 Margin = new(16f + 52f, 16f + 51f);

    [Provenance(ProvenanceKind.Decoded, "DrawObjectivesBinocularsAtt 0x10a90f70: lock box sx - 40 < TargetX < sx, sy - 40 < TargetY < sy")]
    public const float LockBox = 40f;

    [Provenance(ProvenanceKind.Decoded, "DrawObjectivesBinocularsAtt 0x10a90f70: panel tile (0, 64, 128, 64) at (sx - 73, sy - 43), 300 x 60")]
    public static readonly Rect2 PanelTile = new(0f, 64f, 128f, 64f);

    /// <summary>Canvas text height of C.TinyFont (not decoded; the ER labels' height).</summary>
    [Provenance(ProvenanceKind.Estimated, "C.TinyFont height not decoded; the Enhanced Reality labels use 13 canvas units")]
    public const float TextHeight = 13f;

    /// <summary>Marker colours before the canvas doubles them (rule 14).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawObjectivesBinocularsAtt 0x10a90f70: bTriggering (96, 0, 0), bLocked (96, 96, 0), else (100, 123, 92)")]
    public static readonly Color Triggering = HudK.CanvasColour(96, 0, 0);

    public static readonly Color Locked = HudK.CanvasColour(96, 96, 0);

    public static readonly Color Idle = HudK.CanvasColour(100, 123, 92);

    /// <summary>The canvas colour the panel and the brackets draw with: 128 grey, doubled.</summary>
    [Provenance(ProvenanceKind.Estimated, "DrawObjectivesBinocularsAtt: the brackets set 128 grey; the panel keeps the colour left by the previous draw")]
    public static readonly Color Grey = HudK.CanvasColour(128, 128, 128);

    /// <summary>World point to screen pixels; null behind the camera.</summary>
    public Func<FVector, Vector2?>? Projector { get; set; }

    /// <summary>The name of the Use key, for the hint's <c>\USE</c> token (the game expands it to the key name).</summary>
    public string UseKey { get; set; } = "";

    /// <summary>The key token of the lock hints.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBase.int SHud BinocularsAttLock / BinocularAttLocked: \\USE")]
    public const string UseToken = @"\USE";

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The name the lock panel showed in the last draw (the trigger Use would lock), or null.</summary>
    public string? Candidate { get; private set; }

    [UcNative("ASHud::execDrawObjectivesBinocularsAtt", Exe = 0x10a90f70)]
    [PortOf("nat:SBase.SHud.DrawObjectivesBinocularsAtt", Spec = "spec/hud/binoculars.md#rules")]
    public void Draw(HudSnapshot s, Vector2 screen, HudAssets? assets)
    {
        ArgumentNullException.ThrowIfNull(s);
        DrawList.Clear();
        Candidate = null;
        if (Projector is null || !s.Valid || s.HideHud || !s.EnhancedReality || s.Binoculars.Mode != HudBinocularMode.Spy
            || screen.X <= 0f || screen.Y <= 0f)
        {
            return;
        }

        var k = new Vector2(screen.X / MagmaRenderer.PageWidth, screen.Y / MagmaRenderer.PageHeight);
        Vector2 icons = assets?.TextureSize(Package, IconAtlas) is { } ia ? new Vector2(ia.X, ia.Y) : new Vector2(256f, 128f);
        Vector2 panel = assets?.TextureSize(Package, PanelTexture) is { } pa ? new Vector2(pa.X, pa.Y) : new Vector2(256f, 128f);
        float clipX = MagmaRenderer.PageWidth, clipY = MagmaRenderer.PageHeight;
        bool candidateTaken = false;
        bool pulse = s.TimeSeconds - MathF.Floor(s.TimeSeconds) > 0.5f;
        int n = 0;
        foreach (HudBinocularTarget t in s.Binoculars.Targets)
        {
            n++;
            string path = "BinER/" + n.ToString(CultureInfo.InvariantCulture);
            Color colour = t.Triggering ? Triggering : t.Locked ? Locked : Idle;
            if (Projector(t.Location) is not { } px)
            {
                // Behind the view: the original pushes the arrow to the opposite edge; the projector gives no point.
                continue;
            }

            var p = new Vector2(px.X / k.X, px.Y / k.Y);
            float d = (t.Location - s.PawnLocation).Size();
            bool onScreen = p.X > Margin.X && p.X < clipX - Margin.X && p.Y > Margin.Y && p.Y < clipY - Margin.Y;
            if (!onScreen)
            {
                Vector2 edge = new(Math.Clamp(p.X, Margin.X, clipX - Margin.X - 16f), Math.Clamp(p.Y, Margin.Y, clipY - Margin.Y - 16f));
                float a = MathF.Atan2(p.Y - clipY / 2f, p.X - clipX / 2f);
                Arrow(path + "/arrow", edge, a - MathF.PI / 2f, k, colour);
                continue;
            }

            if (!candidateTaken && p.X - LockBox < Target.X && Target.X < p.X && p.Y - LockBox < Target.Y && Target.Y < p.Y)
            {
                candidateTaken = true;
                string name = t.Kind == HudObjectiveKind.DropZone ? s.Binoculars.DropZoneText : t.Name;
                Candidate = name;
                Image(path + "/panel", PanelTexture, new Rect2(p.X - 73f, p.Y - 43f, 300f, 60f), PanelTile, panel, k, Grey);
                Text(path + "/name", name, new Vector2(p.X - 70f, p.Y - 40f), k, Grey);
                Text(path + "/distance", LockDistance(t, d, s.DistanceScale > 0f ? s.DistanceScale : HudK.DefaultDistanceScale,
                    s.DistanceUnit.Length > 0 ? s.DistanceUnit : HudK.DefaultDistanceUnit), new Vector2(p.X - 69f, p.Y - 20f), k, Grey);
                string hint = (t.Marked ? s.Binoculars.LockedText : s.Binoculars.LockText).Replace(UseToken, UseKey, StringComparison.Ordinal);
                Text(path + "/hint", hint, new Vector2(p.X - 69f, p.Y - 5f), k, Grey);
                continue;
            }

            if (HudMarkers.IconTile(t.Kind) is not { } tile)
            {
                continue;
            }

            Image(path + "/icon", IconAtlas, new Rect2(p.X - 15f, p.Y - 15f, 30f, 30f), new Rect2(tile + Vector2.One, 30f, 30f), icons, k, colour);
            float gap = pulse ? 6f : 13f;
            Image(path + "/tr", IconAtlas, new Rect2(p.X + gap, p.Y - gap - 7f, 7f, 7f), new Rect2(121f, 96f, 7f, 7f), icons, k, Grey);
            Image(path + "/bl", IconAtlas, new Rect2(p.X - gap - 7f, p.Y + gap, 7f, 7f), new Rect2(96f, 121f, 7f, 7f), icons, k, Grey);
        }
    }

    /// <summary>The panel's second line: "%.0f %s / %i %%" with a capture percent, else "%.0f %s" (disks and drop zones).</summary>
    [Provenance(ProvenanceKind.Decoded, "DrawObjectivesBinocularsAtt 0x10a90f70: \"%.0f %s / %i %%\" and \"%.0f %s\"")]
    public static string LockDistance(HudBinocularTarget t, float d, float scale, string unit)
    {
        ArgumentNullException.ThrowIfNull(t);
        string distance = (d * scale).ToString("0", CultureInfo.InvariantCulture) + " " + unit;
        bool percent = t.Percent >= 0 && t.Kind is not (HudObjectiveKind.DropZone or HudObjectiveKind.Disk);
        return percent ? distance + " / " + t.Percent.ToString(CultureInfo.InvariantCulture) + " %" : distance;
    }

    private void Image(string path, string texture, Rect2 canvasRect, Rect2 src, Vector2 texSize, Vector2 k, Color colour)
    {
        var tl = new Vector2(canvasRect.Position.X * k.X, canvasRect.Position.Y * k.Y);
        var br = new Vector2(canvasRect.End.X * k.X, canvasRect.End.Y * k.Y);
        float u0 = src.Position.X / texSize.X, v0 = src.Position.Y / texSize.Y, u1 = src.End.X / texSize.X, v1 = src.End.Y / texSize.Y;
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Image,
            Package = Package,
            Texture = texture,
            Points = [tl, new Vector2(br.X, tl.Y), br, new Vector2(tl.X, br.Y)],
            Uvs = [new(u0, v0), new(u1, v0), new(u1, v1), new(u0, v1)],
            Colors = [colour, colour, colour, colour],
        });
    }

    /// <summary>The arrow (it points down in its texture) turned by <paramref name="angle"/> about its centre.</summary>
    private void Arrow(string path, Vector2 centre, float angle, Vector2 k, Color colour)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        Vector2 Corner(float x, float y) => new((centre.X + x * c - y * s) * k.X, (centre.Y + x * s + y * c) * k.Y);
        DrawList.Items.Add(new MagmaDrawItem
        {
            Path = path,
            Kind = MagmaWidgetKind.Image,
            Package = Package,
            Texture = ArrowTexture,
            Points = [Corner(-16f, -16f), Corner(16f, -16f), Corner(16f, 16f), Corner(-16f, 16f)],
            Uvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)],
            Colors = [colour, colour, colour, colour],
        });
    }

    private void Text(string path, string text, Vector2 at, Vector2 k, Color colour)
    {
        if (text.Length == 0)
        {
            return;
        }

        float h = TextHeight * k.Y;
        DrawList.Items.Add(HudText.Item(path, text, new Rect2(at.X * k.X, at.Y * k.Y, 300f * k.X, h * MagmaCanvas.LinePitch), h,
            MagmaAlign.Left, colour, new Color(0f, 0f, 0f, 0f)));
    }

    /// <summary>The marker list as text, for tests.</summary>
    public IEnumerable<string> Paths()
    {
        foreach (MagmaDrawItem i in DrawList.Items)
        {
            yield return i.Path;
        }
    }
}
