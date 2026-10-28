using System;
using System.Globalization;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// P_HudDEF, the merc's page (docs/HUD.md): health gauge, radar with the alarm blips of hacked terminals, objectives
/// count, countdown, alarm and alert boxes, gadget box, ammunition, posture icon, time limit, message areas, the
/// reticle with its sound arrows and message, the throw charge, the connected players, and the screen effects:
/// perturbation, shutdown, sticky shock, droplets, foam and the flash-bang white-out (spec/hud/screen-effects.md,
/// spec/hud/merc-reticle.md).
/// </summary>
public sealed class MercHudPage : HudPage
{
    public const string PageName = "P_HudDEF";
    public const string ReticleArea = "MERC_reticule";
    public const string UziReticleArea = "MERC_reticule_uzi";
    public const string ShotgunReticleArea = "MERC_reticule_shotgun";

    [Provenance(ProvenanceKind.Decoded, "merc perturbation handler 0x10b2c5f0: every top-level instance SetTime(60 frames) + Play")]
    public const int PerturbedFrame = 60;

    [Provenance(ProvenanceKind.Decoded, "merc shutdown handler 0x10b2c430: SetTime(frame 10) + Play")]
    public const int ShutDownFrame = 10;

    [Provenance(ProvenanceKind.Decoded, "merc shutdown handler 0x10b2c430: stopped areas with 10 < frame < 40 go to 40")]
    public const int RebootFrame = 40;

    [Provenance(ProvenanceKind.Decoded, "merc shutdown handler 0x10b2c430: t0 + 12 < now clears bShutDown")]
    public const float ShutDownSeconds = 12f;

    [Provenance(ProvenanceKind.Decoded, "merc shutdown handler 0x10b2c430: the reboot latch clears when t1 + 2 < now")]
    public const float RebootSeconds = 2f;

    [Provenance(ProvenanceKind.Decoded, "merc sticky handler 0x10b281c0: SetTime(60 frames)")]
    public const int StickyShockFrame = 60;

    [Provenance(ProvenanceKind.Decoded, "merc foam handler 0x10b282b0: hold frame 12, clean from 13")]
    public const int FoamHoldFrame = 12;

    [Provenance(ProvenanceKind.Decoded, "merc droplet handler 0x10b28100: restart past 50, outro from 45 to 50")]
    public const int DropletOutroFrom = 45;

    [Provenance(ProvenanceKind.Decoded, "merc droplet handler 0x10b28100")]
    public const int DropletOutro = 50;

    [Provenance(ProvenanceKind.Decoded, "posture handler 0x10b283f0: crouched from frame 10, standing from 0, crossfades end at 4 and 14")]
    public static readonly (int Stand, int Crouch, int CrouchEnd) PostureFrames = (0, 10, 14);

    private static readonly string[] Arrows = ["arrowSoundLeft", "arrowSoundRight", "", "", "arrowSoundForward", "arrowSoundBehind"];

    private readonly HudRadar? _radar;
    private bool _perturbed;
    private bool _shutLatch;
    private bool _rebooting;
    private float _t0;
    private float _t1;
    private int _shutDowns;
    private int _flashes;
    private bool _alert;

    public MercHudPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options)
    {
        if (Area("Radar") is { } radar)
        {
            _radar = new HudRadar(radar, merc: true);
        }

        // The merc init hides the three weapon reticles; the driver shows the one of the weapon class.
        foreach (MagmaNode r in Area("Reticule/Reticule")?.Children ?? [])
        {
            r.SetVisible(false);
        }
    }

    public override HudSide Side => HudSide.Merc;

    public HudRadar? Radar => _radar;

    /// <summary>A HUD shutdown or its reboot runs (the reticle is frozen).</summary>
    public bool ShuttingDown => _shutLatch;

    /// <summary>The page is perturbed.</summary>
    public bool Perturbed => _perturbed;

    protected override void Drive(HudSnapshot s, float dt)
    {
        DriveHealth(s, dt);
        DriveObjectives(s);
        DriveChrono(s);
        bool alert = DriveCountdowns(s);
        DriveGadget(s);
        DriveMessages(s);
        DriveTutorial(s);
        DrivePlayers(s);
        DriveBallistic(Area("Ballistic/Ballistic"), s.Ballistic);
        SetShown(Area("Ballistic"), s.Ballistic > 0f, restart: false);
        _radar?.Update(s);

        // The merc update's effect handlers (spec/hud/screen-effects.md rule 9).
        SetShown(Area("Perturb_Global"), true, restart: false);
        bool shut = DriveShutDown(s);
        if (!shut && !DriveFoam(s))
        {
            DrivePerturbation(s);
        }

        DriveDroplets(s);
        DriveStickyShock(s);
        DrivePosture(s);
        DriveFlash(s);

        if (!shut)
        {
            DriveReticle(s);
        }

        DriveAmmo(s);
        DriveAlert(alert);
        HideAll("MaskGas", "MineInteraction", "Speak", "Backpack", "GameInvite");
    }

    // ---- effects --------------------------------------------------------------------------------------------

    /// <summary>Every top-level instance to <paramref name="frame"/>, playing or stopped.</summary>
    private void AllAreas(int frame, bool play)
    {
        foreach (MagmaNode c in Root.Children)
        {
            c.SetFrame(frame);
            c.Playing = play;
        }
    }

    /// <summary>Perturbation (rule 11): frame 60 and play on the rising edge, frame 0 stopped on the falling edge.</summary>
    private void DrivePerturbation(HudSnapshot s)
    {
        if (s.Merc.Perturbed && !_perturbed)
        {
            _perturbed = true;
            AllAreas(PerturbedFrame, true);
        }
        else if (!s.Merc.Perturbed && _perturbed)
        {
            _perturbed = false;
            AllAreas(0, false);
        }
    }

    /// <summary>Shutdown (rule 12): frame 10 for 12 s, then the stopped areas reboot from frame 40 for 2 s.</summary>
    private bool DriveShutDown(HudSnapshot s)
    {
        float now = s.TimeSeconds;
        if (s.Merc.ShutDowns != _shutDowns)
        {
            _shutDowns = s.Merc.ShutDowns;
            _shutLatch = true;
            _rebooting = false;
            _t0 = MathF.Truncate(now);
            AllAreas(ShutDownFrame, true);
        }

        if (!_shutLatch)
        {
            return false;
        }

        if (!_rebooting)
        {
            if (_t0 + ShutDownSeconds < now)
            {
                _rebooting = true;
                _t1 = MathF.Truncate(now);
            }

            return true;
        }

        foreach (MagmaNode c in Root.Children)
        {
            if (!c.Playing && c.Frame > ShutDownFrame && c.Frame < RebootFrame)
            {
                c.SetFrame(RebootFrame);
                c.Playing = true;
            }
        }

        if (_t1 + RebootSeconds < now)
        {
            _shutLatch = false;
            _rebooting = false;
        }

        return true;
    }

    /// <summary>Foam (rule 15): shown while projected, held at 12 during the stay, cleaned from 13. True while active.</summary>
    private bool DriveFoam(HudSnapshot s)
    {
        MagmaNode? tampon = Area("Mousse");
        MagmaNode? f = tampon?.Find("Element");
        if (f is null)
        {
            return false;
        }

        HudMercEffects m = s.Merc;
        if (m.Projection)
        {
            if (!f.Visible)
            {
                tampon!.SetVisible(true, children: false);
                f.Restart();
            }

            return true;
        }

        if (m.NeigeDisp)
        {
            if (m.TimeStopProj + m.TimeSnowToStay >= s.TimeSeconds)
            {
                tampon!.SetVisible(true, children: false);
                f.SetVisible(true);
                f.SetFrame(FoamHoldFrame);
            }
            else if (!f.Playing && f.Frame <= FoamHoldFrame)
            {
                f.SetFrame(FoamHoldFrame + 1);
                f.Playing = true;
            }

            return true;
        }

        Hide(f);
        f.Playing = false;
        Hide(tampon);
        return false;
    }

    /// <summary>Droplets (rule 16): loop while wet; leaving the water plays the outro from frame 50, then hides.</summary>
    private void DriveDroplets(HudSnapshot s)
    {
        MagmaNode? tampon = Area("gouttes");
        MagmaNode? a = tampon?.Find("Gouttes");
        if (a is null)
        {
            return;
        }

        if (s.Merc.Wet)
        {
            if (!a.Visible || a.Frame > DropletOutro)
            {
                tampon!.SetVisible(true, children: false);
                a.Restart();
            }
        }
        else if (a.Playing && a.Frame < DropletOutro)
        {
            if (a.Visible && a.Frame >= DropletOutroFrom)
            {
                a.SetFrame(DropletOutro);
                a.Playing = true;
            }
        }
        else if (!a.Playing && a.Visible)
        {
            Hide(a);
            Hide(tampon);
        }
    }

    /// <summary>Sticky shock (rule 13): the head shot area or the body shot area from frame 60 while shocked.</summary>
    private void DriveStickyShock(HudSnapshot s)
    {
        MagmaNode? head = Area("A_StickyShock");
        MagmaNode? body = Area("A_StickyShock2");
        if (!s.Merc.StickyShocking)
        {
            Hide(head);
            Hide(body);
            return;
        }

        MagmaNode? area = s.Merc.StickyShockHead ? head : body;
        if (area is { Visible: false })
        {
            area.SetVisible(true);
            area.SetFrame(StickyShockFrame);
            area.Playing = true;
        }
    }

    /// <summary>Posture icon (merc-reticle.md rule 12): crossfade to crouched from 10, to standing from 0.</summary>
    private void DrivePosture(HudSnapshot s)
    {
        MagmaNode? tampon = Area("Pos_Merc");
        SetShown(tampon, s.HasPawn, restart: false);
        MagmaNode? m = tampon?.Find("Element");
        if (m is null || !s.HasPawn)
        {
            return;
        }

        if (s.Merc.Crouched)
        {
            if (!m.Playing && m.Frame <= PostureFrames.Crouch)
            {
                m.SetFrame(PostureFrames.Crouch);
                m.Playing = true;
            }
        }
        else if (!m.Playing && (m.Frame >= PostureFrames.CrouchEnd || m.Frame == PostureFrames.Stand))
        {
            m.SetFrame(PostureFrames.Stand);
            m.Playing = true;
        }
    }

    /// <summary>Flash-bang white-out (rule 14): from frame 0 on each flash, hidden once it has stopped.</summary>
    private void DriveFlash(HudSnapshot s)
    {
        MagmaNode? tampon = Area("Flash");
        MagmaNode? flash = tampon?.Find("Flash");
        if (flash is null)
        {
            return;
        }

        if (s.Merc.Flashes != _flashes)
        {
            _flashes = s.Merc.Flashes;
            tampon!.SetVisible(true, children: false);
            flash.Restart();
        }
        else if (flash.Visible && !flash.Playing)
        {
            Hide(flash);
            Hide(tampon);
        }
    }

    /// <summary>Alert box (merc-reticle.md rule 13): plays from frame 0 when a countdown starts, hides when none runs.</summary>
    private void DriveAlert(bool alert)
    {
        MagmaNode? box = Area("AlertMessage");
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

    // ---- ammunition and reticle ------------------------------------------------------------------------------

    /// <summary>Ammo: "ClipAmmo/Ammo" while the merc has his weapon and the ammo display is on.</summary>
    private void DriveAmmo(HudSnapshot s)
    {
        MagmaNode? ammo = Area("Ammo");
        bool shown = s.HasWeapon && !s.AmmoOff;
        SetShown(ammo, shown, restart: false);
        if (shown)
        {
            SetText(ammo, "TextAmmoStock", string.Create(CultureInfo.InvariantCulture, $"{s.ClipAmmo}/{s.Ammo}"));
        }
    }

    /// <summary>
    /// The reticle (spec/hud/merc-reticle.md rules 1 to 6 and 11): the weapon class's reticle while the weapon is
    /// out, the laser off and bNoReticule clear, on its spread frame, red for 0.4 s after a hit, centred on the aim
    /// point; the sound arrows; the reticle message.
    /// </summary>
    private void DriveReticle(HudSnapshot s)
    {
        DriveReticleMessage(s);
        if (!s.HasPawn)
        {
            return;
        }

        MagmaNode? reticule = Area("Reticule");
        if (reticule is null || !s.ShowReticle)
        {
            Hide(reticule);
            return;
        }

        MagmaNode? inner = reticule.Find("Reticule");
        string name = s.WeaponKind switch
        {
            HudWeaponKind.Uzi => UziReticleArea,
            HudWeaponKind.Shotgun => ShotgunReticleArea,
            _ => ReticleArea,
        };
        MagmaNode? area = inner?.FindArea(name);
        if (area is { Visible: false })
        {
            area.SetVisible(true);
        }

        if (!reticule.Visible)
        {
            reticule.SetVisible(true);
            // The weapon reticles the init hid stay hidden when the instance's children follow it.
            foreach (MagmaNode r in inner?.Children ?? [])
            {
                r.SetVisible(ReferenceEquals(r, area));
            }

            DriveArrowsHidden(reticule);
        }

        if (area is not null)
        {
            area.SetFrame(Math.Max(0, s.ReticleFrame));
            area.Playing = false;
        }

        if (inner is not null)
        {
            inner.StateOverride = s.ReticleHit ? new Color(1f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
        }

        DriveArrows(reticule, s);

        // Rule 3 as Reloaded patches it (rl:fix.crosshair_shift): the page instance sits at a fixed spot, centred, or
        // Reloaded's classic_crosshair spot; it no longer follows SpotReticule, so an aim kick does not move it sideways.
        reticule.PositionOverride = ReticlePosition(Options.ClassicCrosshair);
    }

    /// <summary>
    /// Where the reticle's page instance goes (page units): Reloaded's fixed spot, centred (257, 179) or classic
    /// (254, 182), drawn one unit up and left so the centre dot lands where the reference game draws it.
    /// </summary>
    [PortOf("rl:fix.crosshair_shift", Spec = "spec/hud/merc-reticle.md#rules")]
    [PortOf("rl:qol.crosshair", Spec = "spec/hud/merc-reticle.md#rules")]
    public static Vector2 ReticlePosition(bool classic) =>
        (classic ? HudK.ReticleClassicSpot : HudK.ReticleCentredSpot) + HudK.ReticleDrawCalibration;

    /// <summary>
    /// The aim point in page units: SHud.SpotReticule (640 x 480 canvas units, stretched over the screen) brought to
    /// the reticle's centre anchoring, or the page centre while it is not set.
    /// </summary>
    private Vector2 AimPoint(HudSnapshot s)
    {
        if (s.SpotReticule is not { } spot || Renderer.S <= 0f)
        {
            return HudK.ReticleCentre;
        }

        if (Renderer.StretchAll)
        {
            // A stretched page is the canvas itself.
            return spot;
        }

        Vector2 screen = Renderer.Screen;
        float x = MagmaRenderer.PageWidth / 2f + (spot.X * screen.X / MagmaRenderer.PageWidth - screen.X / 2f) / Renderer.S;
        float y = spot.Y * screen.Y / MagmaRenderer.PageHeight / Renderer.S;
        return new Vector2(x, y);
    }

    private static void DriveArrowsHidden(MagmaNode reticule)
    {
        foreach (string arc in Arrows)
        {
            if (arc.Length > 0)
            {
                reticule.Find(arc)?.SetVisible(false);
            }
        }
    }

    /// <summary>The sound arrows (rule 6): each shows while trunc(now) is below its arrow time.</summary>
    private static void DriveArrows(MagmaNode reticule, HudSnapshot s)
    {
        float t = MathF.Truncate(s.TimeSeconds);
        for (int i = 0; i < Arrows.Length; i++)
        {
            bool on = t < s.ArrowTimes[i];
            if (Arrows[i].Length == 0)
            {
                reticule.Widget(i == 2 ? "arrowSoundUp" : "arrowSoundDown").Hidden = !on;
                continue;
            }

            MagmaNode? arc = reticule.Find(Arrows[i]);
            if (arc is null)
            {
                continue;
            }

            if (on && !arc.Visible)
            {
                arc.Restart();
            }
            else if (!on)
            {
                arc.SetVisible(false);
            }
        }
    }

    /// <summary>The reticle message (rule 11): the text while it lasts, following the aim point.</summary>
    private void DriveReticleMessage(HudSnapshot s)
    {
        MagmaNode? message = Area("ReticuleMessage");
        if (message is null)
        {
            return;
        }

        if (s.ReticleMessage.Length == 0)
        {
            Hide(message);
            return;
        }

        Show(message);
        SetText(message, "messageReticule", s.ReticleMessage);
        Vector2 aim = AimPoint(s);
        Vector2 home = InstanceOrigin(message);
        message.PositionOverride = home + new Vector2(aim.X - HudK.ReticleCentre.X, aim.Y - HudK.ReticleCentre.Y);
    }

    protected override void DrawExtras(HudSnapshot s)
    {
        // Under the ammunition count (94, 343, 76 x 20) and above the gadget box (49, 374), left anchored like them.
        if (s.HasWeapon && !s.AmmoOff)
        {
            ExtraText("Extras/FireMode", s.FireModeName, new Rect4(94f, 361f, 170f, 373f), MagmaAnchor.Left, MagmaAlign.Center,
                HudK.MercColour);
        }

        ExtraText("Extras/Vision", s.VisionName, new Rect4(49f, 326f, 178f, 340f), MagmaAnchor.Left, MagmaAlign.Center,
            HudK.MercColour);
    }
}
