using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// Numbers and small rules the HUD pages take from the original, each with its evidence. The rules that use them are
/// written down in docs/HUD.md; "Bridge" means the HUD research of the SCCT Versus UE5 Bridge project
/// (docs/HUD.md, docs/OVERLAYS.md and docs/overlays/*.md there), which decoded the native page drivers.
/// </summary>
public static class HudK
{
    // ---- gauges ---------------------------------------------------------------------------------------------

    /// <summary>Gauge fill: top = min(133, 137 - value / 100 x 133) in A_Jauge units (full = 4, empty = zero height).</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: native gauge 0x10b30090")]
    public const float GaugeBottom = 133f;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: native gauge 0x10b30090")]
    public const float GaugeTopOffset = 137f;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: native gauge 0x10b30090")]
    public const float GaugeSpan = 133f;

    /// <summary>The gauge triangles sit 3 units above the fill top.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: triangles top at top - 3")]
    public const float GaugeArrowLift = 3f;

    // ---- radar ----------------------------------------------------------------------------------------------

    /// <summary>World units per radar page unit.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: 1/44 at 0x10be0f98")]
    public const float RadarScale = 44f;

    /// <summary>Radar radius in page units (2156 uu).</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: 49 at 0x10be0f94")]
    public const float RadarRadius = 49f;

    /// <summary>The radar centre inside A_Radar.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: instance position (X + 75, Y + 11)")]
    public static readonly Vector2 RadarCentre = new(75f, 11f);

    /// <summary>A blip more than this many radar units below the player uses the Lower icon.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: dz < -2")]
    public const float RadarLower = -2f;

    /// <summary>A blip more than this many radar units above the player uses the Higher icon.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: dz > 7")]
    public const float RadarHigher = 7f;

    /// <summary>Teammate, enemy and alarm instances the radar drivers use.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3: 2 teammates, 3 enemies, 5 alarms (merc)")]
    public const int RadarTeammates = 2;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3")]
    public const int RadarEnemies = 3;

    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.3")]
    public const int RadarAlarms = 5;

    /// <summary>
    /// A disk carrier moving faster than this shows on the merc radar: Reloaded's 200 (stock 170), so a spy climbing a
    /// pole or a ladder with the disk no longer flashes on it (rl:fix.disk_radar).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.3: |Velocity| > 170 (merc update game 0x10b27bf9)")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1001ad50 replaces the 170.0 compare at game 0x10b27bf9 with 200.0 (0x1032b820)")]
    public const float FlagCarrierRadarSpeed = 200f;

    // ---- reticle --------------------------------------------------------------------------------------------

    /// <summary>The merc reticle turns red this long after the local merc's shot hit a pawn.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/reticle.md 1.2: fDateLastShootedPawn + 0.4 > TimeSeconds")]
    public const float ReticleHitTime = 0.4f;

    /// <summary>Where the reticle is centred without an aim point from the camera (SHud.SpotReticule): the page centre.</summary>
    public static readonly Vector2 ReticleCentre = new(320f, 240f);

    /// <summary>
    /// Reloaded's fixed spot of the reticle's page instance (rl:fix.crosshair_shift): it replaces the stock
    /// ftol(SpotReticule - half the A_Reticule box) of the reticle driver, so the reticle no longer moves with the aim
    /// point.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a9140 / 0x100a9160 at game 0x10b286d6 / 0x10b286fa: X 0x101, Y 0xb3 while classic_crosshair is off")]
    public static readonly Vector2 ReticleCentredSpot = new(257f, 179f);

    /// <summary>The same spot with Reloaded's classic_crosshair on: 3 units left and 3 down (rl:qol.crosshair).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a9140 / 0x100a9160: X 0xfe, Y 0xb6 while classic_crosshair (0x103ff835) is on")]
    public static readonly Vector2 ReticleClassicSpot = new(254f, 182f);

    /// <summary>
    /// Our page renderer draws the reticle's centre dot one unit right of and below the reference game's for the
    /// same instance spot (its elements' rects are not decoded), so the spot is moved back by one unit.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "sandbox capture at 1280 x 720 (Reloaded on, centred): rond02 drawn from pixel (637, 357), which our renderer draws with the instance at (256, 178)")]
    public static readonly Vector2 ReticleDrawCalibration = new(-1f, -1f);

    // ---- binoculars (spec/hud/binoculars.md) -------------------------------------------------------------------

    /// <summary>The merc's binocular sights turn red this long after his shot hit a pawn.</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: fDateLastShootedPawn + 0.4 <= TimeSeconds keeps the orange")]
    public const float BinocularHitTime = 0.4f;

    /// <summary>The merc's binocular aim point: this far out from the eye along MyDesiredRotation.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud s_H_Binoculars_Def.PostRender: EyePosition() + X * 1000.0")]
    public const float BinocularAimDistance = 1000f;

    /// <summary>The range finder's line length.</summary>
    [Provenance(ProvenanceKind.Decoded, "P_BinocularsDEF update 0x10b342c0: bone X axis * 15000.0")]
    public const float BinocularRange = 15000f;

    // ---- objectives -----------------------------------------------------------------------------------------

    /// <summary>A Disk Hunt disk's Enhanced Reality marker shows within this distance (uu) of the viewer.</summary>
    [Provenance(ProvenanceKind.Decoded, "ERDrawObjectives 0x10af8c00 SDisk branch: (bActive or bLocked) and distance < 1500")]
    public const float HuntDiskErRange = 1500f;

    /// <summary>
    /// A terminal's capture percent as the spy markers show it: the sim's percent (game 0x10ABDF80, from the
    /// replicated DominationCaptureTime, CaptureTime and DeltaCaptureTime) clamped to 0..99, so it never shows 100.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/objectives/terminal-hack.md rules 32 and 33: SHud markers 0x10AF8C00 clamp 0..99 (0x10AF8E9D)")]
    public static int CapturePercent(SComputerObjectiveTrigger t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return Math.Clamp(t.GetHackPercent(), 0, 99);
    }

    /// <summary>Marker distances when the HUD has no unit set: metres, 1 uu = 1 cm.</summary>
    [Provenance(ProvenanceKind.Estimated, "HUD.StrDistanceUnit / fDistanceUnit have no class default; the live values were read by the Bridge")]
    public const string DefaultDistanceUnit = "m";

    [Provenance(ProvenanceKind.Estimated, "1 uu = 1 cm")]
    public const float DefaultDistanceScale = 0.01f;

    // ---- vision ---------------------------------------------------------------------------------------------

    /// <summary>Names of the render modes (SGoggle.CurrentMode / PlayerController.RendMap).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/equipment/vision-goggles.md: REN_DynLight 5, REN_NightVision 34, REN_ThermalVision 36, REN_MotionTrackingVision 37, REN_EMFVision 38, REN_Binoculars 50")]
    public static string VisionName(int mode) => mode switch
    {
        34 => "Night vision",
        36 => "Thermal vision",
        37 => "Motion tracking",
        38 => "EMF vision",
        50 => "Binoculars",
        _ => "",
    };

    // ---- interaction list ------------------------------------------------------------------------------------

    /// <summary>
    /// SPlayerController.CanInteract as far as the HUD needs it: true in the plain walking states and in the menu,
    /// false elsewhere. The stance-transition children of the walking states that turn it off are not separated.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "spec/objectives/interaction-system.md rule 11: CanInteract is true in s_PlayerWalking and its children and in s_MenuInteraction")]
    public static bool CanInteract(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        string state = pc.GetStateName().ToString();
        return state.StartsWith("s_PlayerWalking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "s_MenuInteraction", StringComparison.OrdinalIgnoreCase);
    }

    // ---- page colours ---------------------------------------------------------------------------------------

    /// <summary>Gauge fill colour of the spy energy gauge (alpha 128).</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md: Energie fill (45,149,210) alpha 128")]
    public static readonly Color EnergyFill = new(45f / 255f, 149f / 255f, 210f / 255f, 128f / 255f);

    /// <summary>The spy page's main area colour (Energie, Gadget, Chrono...).</summary>
    [Provenance(ProvenanceKind.Decoded, "Hud.mgm P_HudATT instance STATECOLOR (200,247,184)")]
    public static readonly Color SpyColour = new(200f / 255f, 247f / 255f, 184f / 255f, 1f);

    /// <summary>The merc page's main area colour (Vie, Gadget, Ammo, Chrono...).</summary>
    [Provenance(ProvenanceKind.Decoded, "Hud.mgm P_HudDEF instance STATECOLOR (255,182,68)")]
    public static readonly Color MercColour = new(1f, 182f / 255f, 68f / 255f, 1f);

    /// <summary>Height of the slice's extra text lines, as the ammunition count (h11).</summary>
    public const float ExtraTextHeight = 11f;

    /// <summary>Text shown for a gadget charge below zero (unlimited gadgets show no number).</summary>
    public const string NoCharge = "";

    /// <summary>Lives of 100 or more read as infinite (Localize Inifite_Short).</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/spyhud.md 8: NumLives >= 100 -> Inifite_Short")]
    public const int InfiniteLives = 100;

    /// <summary>The remaining whole seconds on the same replicated clock used by the match timeout.</summary>
    // Intentional display correction: the original page used limit * 60 - elapsed - 1. The gameplay
    // timeout is elapsed >= limit * 60, so subtracting that extra second showed zero while play remained.
    public static string? ChronoText(int timeLimit, int elapsed)
    {
        if (timeLimit <= 0)
        {
            return null;
        }

        return CountdownText(Math.Max(0, timeLimit * 60 - elapsed));
    }

    /// <summary>Whole seconds still remaining: a positive final fraction is one second, never zero.</summary>
    public static int RemainingSeconds(float seconds) => (int)MathF.Ceiling(Math.Max(0f, seconds));

    /// <summary>The countdown boxes, with minutes carried for durations of a minute or more.</summary>
    public static string CountdownText(int seconds)
    {
        int remaining = Math.Max(0, seconds);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{remaining / 60}:{remaining % 60:00}");
    }

    // ---- counters, spectator, effects -----------------------------------------------------------------------

    /// <summary>Team lives of 1000 or more read as infinite on the deathmatch counter.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/spyhud.md 7: TotalTeamLives >= 1000 -> Inifite_Short")]
    public const int InfiniteTeamLives = 1000;

    /// <summary>MagmaDynamic.int [Magma] Inifite_Short (the English fallback).</summary>
    public const string InfiniteShort = "Inf.";

    /// <summary>The alarm box shows alarms of a message priority above this.</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/HUD.md ChronoAlarm: SAlarm entries with MessagePriority > 4")]
    public const int AlarmBoxPriority = 4;

    /// <summary>The spectator page shows the respawn countdown below this many seconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "spectator update 0x10b2eeb0: 0 < s < 50")]
    public const int RespawnShownBelow = 50;

    /// <summary>"Switch player" shows for a watched team of at least this size.</summary>
    [Provenance(ProvenanceKind.Decoded, "spectator update 0x10b2eeb0: TP.Team.Size >= 3")]
    public const int SwitchPlayerTeamSize = 3;

    /// <summary>How close a ready teammate must be for the coop icons (the native placement tests are not ported).</summary>
    [Provenance(ProvenanceKind.Estimated, "stands in for the coop natives 0x10ae6d40 / 0x10ae5b50")]
    public const float CoopRadius = 300f;

    /// <summary>Height of the knock-out screen's text in page units (canvas MedFont; not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "SHud draws TextSleeping in Canvas.MedFont; the HUD's h14 texts stand in")]
    public const float SleepTextHeight = 14f;

    /// <summary>Height of the ping column texts (as the connected players list, h8).</summary>
    [Provenance(ProvenanceKind.Decoded, "Hud.mgm A_PCConnectedPlayers texts h8")]
    public const float PingTextHeight = 8f;

    /// <summary>Gap between the lives column and the ping column, in page units.</summary>
    public const float PingGap = 15f;

    /// <summary>Milliseconds per unit of PlayerReplicationInfo.Ping (ServerUpdatePing stores the value it is given).</summary>
    [Provenance(ProvenanceKind.Estimated, "Engine.PlayerController.ServerUpdatePing: Ping = NewPing; the sender's unit is not decoded")]
    public const int PingUnit = 1;

    /// <summary>Height of the frame-rate readout in page units (Reloaded draws its own; not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "Reloaded's fps readout font size is not decoded")]
    public const float FpsTextHeight = 10f;

    /// <summary>
    /// A colour the SHud canvas (and the console) draws with: the canvas modulates twice, so each channel is doubled
    /// (capped at 255) and the alpha kept. SFXT_TextureColorWhite draws 0x808080 and the merc's Hud colour
    /// (128, 93, 34) doubles to the merc page colour, which is what the doubling explains.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Canvas 2x modulation inferred: ProcessSFX TextureColorWhite = 0x808080; SHud merc Hud_R/G/B (128,93,34) x2 = page (255,182,68); Bridge ER colours are native x2")]
    public static Color CanvasColour(int r, int g, int b, int a = 255) =>
        new(Math.Min(255, 2 * r) / 255f, Math.Min(255, 2 * g) / 255f, Math.Min(255, 2 * b) / 255f, a / 255f);

    /// <summary>The solid white material of the HUD package (HUDConfig 64,19 to 78,33).</summary>
    public const string WhiteMaterial = "Blanc_opak";

    public static readonly OpenScct.Core.Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");

    /// <summary>
    /// PlayerController.IsSpectating as the page manager calls it. The killed/visitor states copy the spectator
    /// implementation but do not have a metadata Super link; the controller query handles those states too.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "IsSpectating overrides: s_MultiSpectating, BaseSpectating, GameEnded return true (uc-index sha a40381cc...)")]
    public static bool IsSpectating(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return pc.IsSpectating();
    }

    /// <summary>Radar position of a world point relative to the player (forward is up, right is right).</summary>
    [Provenance(ProvenanceKind.Decoded, "Bridge docs/overlays/radar.md 3.1: th = (-yaw - 16384) pi / 32768, X = dx c - dy s, Y = dx s + dy c")]
    public static Vector2 RadarProject(FVector point, FVector pawn, int yaw)
    {
        float th = (-yaw - 16384) * MathF.PI / 32768f;
        float c = MathF.Cos(th), s = MathF.Sin(th);
        float dx = (point.X - pawn.X) / RadarScale;
        float dy = (point.Y - pawn.Y) / RadarScale;
        return new Vector2(dx * c - dy * s, dx * s + dy * c);
    }
}
