using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// P_HudATT, the spy's page (docs/HUD.md): health and energy gauges, radar, objectives count, countdown and alarm
/// boxes, gadget box, time limit, message areas, the sneak icon, the throw charge, the Speak/Hack prompt, the coop
/// icons, the tutorial message and the connected players.
/// </summary>
public sealed class SpyHudPage : HudPage
{
    public const string PageName = "P_HudATT";

    private static readonly string[] CoopAreas = ["Coop1", "Coop2", "Coop3", "Coop4"];

    private readonly HudRadar? _radar;

    public SpyHudPage(HudAssets assets, HudOptions? options = null)
        : base(assets, PageName, options)
    {
        if (Area("Radar") is { } radar)
        {
            _radar = new HudRadar(radar, merc: false);
        }

        // Console-only button art of the prompt is hidden by the spy init.
        if (Area("Speak") is { } speak)
        {
            foreach (string element in new[] { "Element1", "Element2", "Element3", "Element4" })
            {
                speak.Widget(element).Hidden = true;
            }
        }
    }

    public override HudSide Side => HudSide.Spy;

    public HudRadar? Radar => _radar;

    protected override void Drive(HudSnapshot s, float dt)
    {
        DriveHealth(s, dt);
        DriveEnergy(s);
        DriveObjectives(s);
        DriveChrono(s);
        DriveCountdowns(s);
        DriveGadget(s);
        DriveMessages(s);
        DriveTutorial(s);
        DrivePlayers(s);
        DriveBallistic(Area("Ballistic"), s.Ballistic);
        _radar?.Update(s);
        DriveCoop(s);

        // The sneak icon fades in while not running fast; the game rewinds it to its invisible first frame otherwise.
        SetShown(Area("Spy_Speed"), s.HasPawn && !s.FastSpeed);

        MagmaNode? speak = Area("Speak");
        SetShown(speak, s.SpeakPrompt.Length > 0);
        SetText(speak, "_TextSpeakATT", s.SpeakPrompt);

        HideAll("StickyCamInteraction", "GameInvite");
    }

    /// <summary>Energie: shown while the energy is below its maximum.</summary>
    private void DriveEnergy(HudSnapshot s)
    {
        MagmaNode? energy = Area("Energie");
        bool shown = s.HasPawn && s.EnergyLevelMax > 0 && s.EnergyLevel < s.EnergyLevelMax;
        SetShown(energy, shown, restart: false);
        if (shown && energy is not null)
        {
            DriveGauge(energy, s.EnergyLevel * 100f / s.EnergyLevelMax);
        }
    }

    /// <summary>
    /// Coop icons (spec/hud/counters-and-prompts.md rules 4 to 6): one of Move_coop..4 plays from frame 0 when it
    /// appears, its console button art hidden; the others hide.
    /// </summary>
    private void DriveCoop(HudSnapshot s)
    {
        MagmaNode? coop = Area("Coop");
        SetShown(coop, true, restart: false);
        int shown = s.Coop switch
        {
            HudCoopIcon.OfferLadder => 0,
            HudCoopIcon.ClimbMate => 1,
            HudCoopIcon.LedgeHoist => 2,
            HudCoopIcon.BoostedUp => 3,
            _ => -1,
        };
        for (int i = 0; i < CoopAreas.Length; i++)
        {
            MagmaNode? icon = coop?.Find(CoopAreas[i]);
            if (i != shown)
            {
                Hide(icon);
                continue;
            }

            if (icon is { Visible: false })
            {
                icon.Restart();
                icon.Find("button_white")?.SetVisible(false);
            }
        }
    }

    protected override void DrawExtras(HudSnapshot s)
    {
        // Above the gadget box (462, 369), right anchored like it.
        ExtraText("Extras/Vision", s.VisionName, new Rect4(462f, 350f, 591f, 366f), MagmaAnchor.Right, MagmaAlign.Center,
            HudK.SpyColour);
    }
}
