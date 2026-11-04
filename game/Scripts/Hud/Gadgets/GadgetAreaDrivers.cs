using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud.Gadgets;

/// <summary>A sound a gadget HUD view asks for (the native page code plays it on the local machine).</summary>
public readonly record struct GadgetHudSound(Actor Source, AssetRef Sound);

/// <summary>
/// The menus of the gadgets (spec/equipment/gadget-hud.md rules 14 to 17): the menu area is shown and restarted
/// while the menu is up; each line takes its text and colour and the arrow sits beside the selected line.
/// </summary>
public abstract class GadgetMenuDriver : IHudAreaDriver
{
    /// <summary>Selected line colour.</summary>
    public static readonly Color SelectedColour = new(1f, 1f, 1f, 1f);

    /// <summary>Unselected line colour (200,200,200).</summary>
    public static readonly Color NormalColour = new(200f / 255f, 200f / 255f, 200f / 255f, 1f);

    /// <summary>Unavailable line colour (128,128,128,128).</summary>
    public static readonly Color UnavailableColour = new(128f / 255f, 128f / 255f, 128f / 255f, 128f / 255f);

    private readonly string _area;
    private readonly string _linePrefix;
    private GadgetMenu? _menu;

    protected GadgetMenuDriver(string area, string linePrefix)
    {
        _area = area;
        _linePrefix = linePrefix;
        Areas = [area];
    }

    public abstract HudSide? Side { get; }

    public IReadOnlyCollection<string> Areas { get; }

    /// <summary>The menu shown last frame, or null.</summary>
    public GadgetMenu? Menu => _menu;

    public void Read(SPlayerController? pc, HudSnapshot s) => _menu = Build(pc);

    protected abstract GadgetMenu? Build(SPlayerController? pc);

    /// <summary>The area holding the lines below the page instance (the instance itself, or an inner instance).</summary>
    protected virtual MagmaNode? Lines(MagmaNode area) => area;

    public void Drive(HudPage page, HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(page);
        MagmaNode? area = page.Area(_area);
        if (area is null)
        {
            return;
        }

        if (_menu is null || !s.HasPawn)
        {
            HudPage.Hide(area);
            return;
        }

        if (!area.Visible)
        {
            area.Restart();
        }

        MagmaNode lines = Lines(area) ?? area;
        lines.Widget("A_button").Hidden = true;
        for (int i = 0; i < _menu.Lines.Count; i++)
        {
            string widget = _linePrefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (lines.Area.WidgetIndex(widget) < 0)
            {
                continue;
            }

            GadgetMenuLine line = _menu.Lines[i];
            MagmaOverride o = lines.Widget(widget);
            o.Text = line.Text;
            switch (line.Look)
            {
                case GadgetMenuLineLook.Untouched:
                    continue;
                case GadgetMenuLineLook.Unavailable:
                    o.Color = UnavailableColour;
                    break;
                case GadgetMenuLineLook.Selected:
                    o.Color = SelectedColour;
                    MoveArrow(lines, widget);
                    break;
                default:
                    o.Color = NormalColour;
                    break;
            }
        }
    }

    /// <summary>The arrow keeps its horizontal place and takes the selected line's top.</summary>
    private static void MoveArrow(MagmaNode lines, string widget)
    {
        int arrow = lines.Area.WidgetIndex("PC_button");
        int line = lines.Area.WidgetIndex(widget);
        if (arrow < 0 || line < 0 || lines.Area.Widgets[arrow].Keys.Count == 0 || lines.Area.Widgets[line].Keys.Count == 0)
        {
            return;
        }

        Rect4 home = lines.Area.Widgets[arrow].Keys[0].Rect;
        Rect4 to = lines.Area.Widgets[line].Keys[0].Rect;
        lines.Widget("PC_button").Pos = new Vector2(home.L, to.T);
    }
}

/// <summary>The sticky cam menu, StickyCamInteraction on P_HudATT (rules 14 and 15).</summary>
public sealed class StickyCamMenuDriver : GadgetMenuDriver
{
    public StickyCamMenuDriver()
        : base("StickyCamInteraction", "textSticky")
    {
    }

    public override HudSide? Side => HudSide.Spy;

    protected override GadgetMenu? Build(SPlayerController? pc) => GadgetHudRules.StickyCamMenu(pc);
}

/// <summary>The mine menu, MineInteraction on P_HudDEF (rules 16 and 17).</summary>
public sealed class MineMenuDriver : GadgetMenuDriver
{
    public MineMenuDriver()
        : base("MineInteraction", "textMine")
    {
    }

    public override HudSide? Side => HudSide.Merc;

    protected override GadgetMenu? Build(SPlayerController? pc) => GadgetHudRules.MineMenu(pc);

    protected override MagmaNode? Lines(MagmaNode area) => area.Find("Element");
}

/// <summary>
/// The gas mask overlay, MaskGas on P_HudDEF (rules 18 to 21): the mask frame and air gauge while the mask is the
/// selected gadget, the breathing loops while the air flows.
/// </summary>
public sealed class GasMaskDriver : IHudAreaDriver
{
    private GasMaskView _view;
    private bool _lowAirLatch;

    public HudSide? Side => HudSide.Merc;

    public IReadOnlyCollection<string> Areas { get; } = ["MaskGas"];

    /// <summary>The overlay's inputs last frame.</summary>
    public GasMaskView View => _view;

    public void Read(SPlayerController? pc, HudSnapshot s) => _view = GadgetHudRules.GasMask(pc);

    public void Drive(HudPage page, HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(page);
        MagmaNode? area = page.Area("MaskGas");
        if (area is null)
        {
            return;
        }

        MagmaNode? mask = area.Find("Mask");
        MagmaNode? gauge = area.Find("Jauge");
        if (!_view.Shown || !s.HasPawn)
        {
            HudPage.Hide(area);
            return;
        }

        if (!area.Visible || mask is not { Visible: true })
        {
            area.SetVisible(true, children: false);
            area.SetFrame(0);
            area.Playing = false;
            mask?.Restart();
            gauge?.SetVisible(true);
            _lowAirLatch = false;
        }

        MagmaNode? normal = mask?.Find("RespirationMask");
        MagmaNode? fast = mask?.Find("RespirationMask2");
        if (_view.Worn)
        {
            if (_view.LowAir && !_lowAirLatch)
            {
                _lowAirLatch = true;
                if (fast is { Visible: false })
                {
                    HudPage.Hide(normal);
                    fast.Restart();
                }
            }
            else if (normal is { Visible: false })
            {
                HudPage.Hide(fast);
                normal.Restart();
            }
        }
        else
        {
            HudPage.Hide(normal);
            HudPage.Hide(fast);
        }

        if (gauge is not null)
        {
            gauge.SetFrame(_view.GaugeFrame);
            gauge.Playing = false;
        }
    }
}

/// <summary>
/// The heart sensor cone in the spy radar (rules 22 to 28), and the merc blips the sensor marks: Radar/Heart while
/// scanning, Radar/Heart1 while something is in the cone; a loop that changes over carries on from the other's frame.
/// </summary>
public sealed class HeartSensorDriver : IHudAreaDriver
{
    private HeartCone _cone;
    private SGadHeartBeatSensor? _sensor;
    private SBasePawn? _radarOwner;
    private bool _detectedLastFrame;
    private float _now;
    private float _lastDetectedSound;
    private int _lastFrame;
    private MagmaNode? _shown;

    public HudSide? Side => HudSide.Spy;

    /// <summary>The cone areas sit inside the radar, which the spy page drives; nothing top-level is owned.</summary>
    public IReadOnlyCollection<string> Areas { get; } = [];

    /// <summary>The cone shown last frame.</summary>
    public HeartCone Cone => _cone;

    /// <summary>Asked for when a sound is due (the detected heartbeat, the scan ping).</summary>
    public Action<GadgetHudSound>? PlaySound { get; set; }

    /// <summary>Scan pings and heartbeats asked for so far.</summary>
    public int CheckSounds { get; private set; }

    public int DetectedSounds { get; private set; }

    public void Read(SPlayerController? pc, HudSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _cone = GadgetHudRules.Heart(pc);
        _sensor = pc?.SBPawn?.SelectedGadget as SGadHeartBeatSensor;
        _now = pc?.Level.TimeSeconds ?? 0f;
        if (!ReferenceEquals(_radarOwner, pc?.SBPawn))
        {
            _radarOwner = pc?.SBPawn;
            _detectedLastFrame = false;
        }

        if (!_detectedLastFrame || pc?.XLevel is null || pc.SBPawn is not { SPawnType: 0 } own)
        {
            return;
        }

        // Native radar 0x10b2d410 runs before the cone page and uses its previous detection latch. Keep the
        // simulation's marks between rendered frames: consuming them here would flicker above the 60 Hz tick.
        // The spy-bullet tag already adds its own blips.
        foreach (SBasePawn q in pc.XLevel.AllActors<SBasePawn>())
        {
            if (!ReferenceEquals(q, own) && q.SPawnType != 0 && q.bDetectedByHBS && !(q.bSpyBulletOn && q.iTeamSpying == 0f))
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Enemy, q.Location));
            }
        }
    }

    public void Drive(HudPage page, HudSnapshot s, float dt)
    {
        ArgumentNullException.ThrowIfNull(page);
        MagmaNode? idle = page.Area("Radar/Heart");
        MagmaNode? hit = page.Area("Radar/Heart1");
        if (idle is null || hit is null)
        {
            _detectedLastFrame = false;
            return;
        }

        _detectedLastFrame = _cone == HeartCone.Detected;
        // Original runtime captures show both independent cone clocks advancing while invisible. Their phase
        // is read again when drawing/reselecting, so freezing the hidden cone changes the resumed wave and ping.
        idle.TickWhenHidden = true;
        hit.TickWhenHidden = true;
        // A_Heart1 contains an unpositioned spot at (0, 0). Detected enemies are
        // already placed by HudRadar; this embedded marker must not draw as well.
        hit.Widget("spot").Hidden = true;

        // The radar hides both cones every frame before this driver runs; showing without a restart keeps the
        // clocks, and which one was up last frame is remembered here.
        switch (_cone)
        {
            case HeartCone.Hidden:
                // Deselection stops Heart only if it was the visible cone. A hidden Heart keeps its phase
                // when switching away directly from detection, just like the independent Heart1 clock.
                if (ReferenceEquals(_shown, idle))
                {
                    idle.Playing = false;
                }

                idle.SetVisible(false);
                hit.SetVisible(false);
                _shown = null;
                return;
            case HeartCone.Idle:
                hit.SetVisible(false);
                idle.SetVisible(true);
                idle.SetFrame(0);
                idle.Playing = false;
                _shown = idle;
                // State 0 resets both clocks and the wrap latch. Holstering resets only Heart; the native
                // keeps Heart1's frame and the latch so the next drawn scan resumes from the detected loop.
                if (_sensor?.OwnerPawn is { WeaponLoaded: not 0 })
                {
                    hit.SetFrame(0);
                    hit.Playing = false;
                    _lastFrame = 0;
                }

                return;
            case HeartCone.Detected:
                ShowPlaying(hit, idle);
                if (GadgetHudRules.HeartDetectedSoundDue(_now, _lastDetectedSound))
                {
                    _lastDetectedSound = _now;
                    DetectedSounds++;
                    Play(_sensor?.Snd_DetectedPawn);
                }

                break;
            default:
                ShowPlaying(idle, hit);
                break;
        }

        int frame = HeartFrame(_shown!);
        if (frame < _lastFrame)
        {
            CheckSounds++;
            Play(_sensor?.Snd_Check);
        }

        _lastFrame = frame;
    }

    /// <summary>Shows <paramref name="to"/> playing; when it was not up last frame it starts at <paramref name="other"/>'s frame.</summary>
    private void ShowPlaying(MagmaNode to, MagmaNode other)
    {
        if (!ReferenceEquals(_shown, to) || !to.Playing)
        {
            // The native copies GetFrame to SetFrame, and retains the previous frame for its wrap check.
            // Resetting that latch here loses the scan ping when detection changes on the loop's wrap.
            to.SetFrame(HeartFrame(other));
        }

        other.SetVisible(false);
        to.SetVisible(true);
        to.Playing = true;
        _shown = to;
    }

    // Native GetFrame 0x10ba48a0 rounds halfway up. MagmaNode.Frame instead floors for keyframe action traversal.
    private static int HeartFrame(MagmaNode node) => (int)MathF.Floor(node.FrameF + .5f);

    private void Play(AssetRef? sound)
    {
        if (_sensor is not null && sound is { IsNone: false } s)
        {
            PlaySound?.Invoke(new GadgetHudSound(_sensor, s));
        }
    }
}
