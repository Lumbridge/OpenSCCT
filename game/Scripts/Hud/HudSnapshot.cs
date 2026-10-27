using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScctGame.Hud;

public enum HudSide
{
    Spy,
    Merc,
}

/// <summary>
/// The selected gadget as the gadget box shows it. Unavailable (an OpenSCCT addition) greys the icon and crosses it
/// out: the gadget cannot work on this level (the camera network on a map without cameras).
/// </summary>
public sealed record HudGadget(string Name, int Charge, string MagmaIconName, int IconU, int IconV, bool Unavailable = false);

/// <summary>A countdown box (Hacking or Objective): whole seconds shown as 0:ss.</summary>
public readonly record struct HudCountdown(int Seconds);

/// <summary>
/// The objective trigger classes SHud.BuildObjectivesAndTriggersList gives an icon (game 0x10af80b0): each picks its
/// MN_boutons tile (<see cref="HudMarkers.IconTile"/>). Other is a trigger class the list leaves out (no marker).
/// </summary>
public enum HudObjectiveKind
{
    /// <summary>SComputerObjectiveTrigger (terminals and laptops): the computer tile.</summary>
    Terminal,
    /// <summary>SFlag (the Story disk): the disc tile.</summary>
    Disk,
    /// <summary>SFlagDropZone: the down arrows tile.</summary>
    DropZone,
    /// <summary>SBombTargetObjectiveTrigger: the explosion tile.</summary>
    Bomb,
    /// <summary>SDisk (Disk Hunt): the server rack tile.</summary>
    HuntDisk,
    Other,
}

/// <summary>Which pass of ASHud::ERDrawObjectives (game 0x10af8c00) draws an Enhanced Reality marker.</summary>
public enum HudErRole
{
    /// <summary>A trigger of an SObjective (terminal, disk, bomb spot): shown inside the objective's zones.</summary>
    Trigger,
    /// <summary>The SObjective itself: one marker per objective while the viewer is outside its zones.</summary>
    Objective,
    /// <summary>A free SFlagDropZone: shown while it is triggering.</summary>
    DropZone,
    /// <summary>A Disk Hunt SDisk: shown when active or locked within 1500 uu.</summary>
    HuntDisk,
}

/// <summary>An Enhanced Reality marker as the spy's HUD shows it.</summary>
public sealed record HudObjective(
    string Name,
    HudObjectiveKind Kind,
    FVector Location,
    bool Triggering,
    bool Locked,
    bool Taken,
    int Percent,
    HudErRole Role = HudErRole.Trigger,
    bool Alert = false);

public enum HudBlipKind
{
    Teammate,
    Enemy,
    Alarm,
}

public readonly record struct HudBlip(HudBlipKind Kind, FVector Location);

/// <summary>A row of the connected players list.</summary>
public sealed record HudPlayerRow(string Name, int Team, bool Spectator, int ObjOrKills, int Lives, int Ping = 0);

/// <summary>The merc reticle of the weapon class (SWeaponDefense, its Uzi and shotgun subclasses).</summary>
public enum HudWeaponKind
{
    Rifle,
    Uzi,
    Shotgun,
}

/// <summary>The objective counter of the page: Story count, deathmatch team lives or disks.</summary>
public enum HudCounterMode
{
    Story,
    DeathMatch,
    DiskHunt,
}

/// <summary>Which coop move icon the spy page shows (A_Coop, spec/hud/counters-and-prompts.md).</summary>
public enum HudCoopIcon
{
    None,
    OfferLadder,
    ClimbMate,
    LedgeHoist,
    BoostedUp,
}

/// <summary>The merc page's screen effects (spec/hud/screen-effects.md rules 9 to 16).</summary>
public sealed class HudMercEffects
{
    /// <summary>The page is perturbed (SHud.bPerturbation or the HUD's own damage perturbation).</summary>
    public bool Perturbed { get; set; }

    /// <summary>Shutdowns seen so far; a change starts one.</summary>
    public int ShutDowns { get; set; }

    /// <summary>SBasePawn.bStickyShocking and bStickyShockedInTheHead.</summary>
    public bool StickyShocking { get; set; }

    public bool StickyShockHead { get; set; }

    /// <summary>SBasePawn.CurrentRainVolume is set (droplets).</summary>
    public bool Wet { get; set; }

    /// <summary>The foam: bProjection, bNeigeDisp, TimeStopProj and TimeSnowToStay.</summary>
    public bool Projection { get; set; }

    public bool NeigeDisp { get; set; }

    public int TimeStopProj { get; set; }

    public int TimeSnowToStay { get; set; }

    /// <summary>Flash-bang white-outs seen so far; a change restarts it.</summary>
    public int Flashes { get; set; }

    /// <summary>Pawn.bIsCrouched (the posture icon).</summary>
    public bool Crouched { get; set; }

    public void Clear()
    {
        Perturbed = StickyShocking = StickyShockHead = Wet = Projection = NeigeDisp = Crouched = false;
        ShutDowns = Flashes = TimeStopProj = TimeSnowToStay = 0;
    }
}

/// <summary>The spectator page (P_SpectatorMode, spec/hud/spectator-page.md).</summary>
public sealed class HudSpectator
{
    public bool Active { get; set; }

    /// <summary>SPlayerController.bGameEnded: the page's panel hides, the end banner shows.</summary>
    public bool GameEnded { get; set; }

    /// <summary>PRI.bOnlySpectator: a visitor rather than a dead player.</summary>
    public bool Visitor { get; set; }

    public string Objectives { get; set; } = "";

    public string RemainingTime { get; set; } = "";

    /// <summary>Own lives (dead player) or the watched player's (visitor); empty keeps the last value.</summary>
    public string Lives { get; set; } = "";

    public bool ShowTeamLives { get; set; }

    public string TeamLives { get; set; } = "";

    public string Watched { get; set; } = "";

    public bool ShowWatching { get; set; }

    public bool ShowSwitchPlayer { get; set; }

    public string Tip { get; set; } = "";

    /// <summary>Seconds to the respawn shown (1 to 49), 0 when not shown.</summary>
    public int RespawnIn { get; set; }

    /// <summary>No life left: "No respawn left."</summary>
    public bool NoRespawn { get; set; }

    public void Clear()
    {
        Active = GameEnded = Visitor = ShowTeamLives = ShowWatching = ShowSwitchPlayer = NoRespawn = false;
        Objectives = RemainingTime = Lives = TeamLives = Watched = Tip = "";
        RespawnIn = 0;
    }
}

/// <summary>Which binocular page replaces the player page (spec/hud/binoculars.md rule 1).</summary>
public enum HudBinocularMode
{
    None,

    /// <summary>The spy in s_Binoculars_Att: P_BinocularsATT.</summary>
    Spy,

    /// <summary>The merc in s_Binoculars_Def (or its reloading child state): P_BinocularsDEF.</summary>
    Merc,
}

/// <summary>An objective trigger the spy's binoculars mark (spec/hud/binoculars.md rules 12 to 17).</summary>
public sealed record HudBinocularTarget(
    string Name,
    HudObjectiveKind Kind,
    FVector Location,
    bool Triggering,
    bool Locked,
    int Percent,
    bool Marked);

/// <summary>The binocular pages and the spy's binocular markers (spec/hud/binoculars.md).</summary>
public sealed class HudBinoculars
{
    public HudBinocularMode Mode { get; set; }

    /// <summary>SPlayerController.fBinocularVisionFOV: the zoom step, 30, 20 or 10.</summary>
    public float Fov { get; set; } = 30f;

    /// <summary>SPlayerController.bHoldBreath (merc): the sights stay centred.</summary>
    public bool HoldBreath { get; set; }

    /// <summary>SPlayerController.BinocularHoldBreathTime: breath left, 5 s full.</summary>
    public float BreathTime { get; set; } = 5f;

    /// <summary>The merc's shot hit a pawn less than 0.4 s ago (fDateLastShootedPawn): red sights.</summary>
    public bool Hit { get; set; }

    /// <summary>SPlayerController.VoiceChatTarget is set: the Hack prompt.</summary>
    public string HackPrompt { get; set; } = "";

    /// <summary>The merc's range finder in uu, or null without a weapon to trace from.</summary>
    public float? Distance { get; set; }

    /// <summary>The merc's aim point 1000 uu out along MyDesiredRotation (SHud s_H_Binoculars_Def.PostRender).</summary>
    public FVector? AimLocation { get; set; }

    /// <summary>The spy's objective triggers (SHud.ObjectivesAndTriggersList without the objectives).</summary>
    public List<HudBinocularTarget> Targets { get; } = new();

    /// <summary>SHud.BinocularsAttLock, BinocularAttLocked and DropZone.</summary>
    public string LockText { get; set; } = "";

    public string LockedText { get; set; } = "";

    public string DropZoneText { get; set; } = "";

    public void Clear()
    {
        Mode = HudBinocularMode.None;
        Fov = 30f;
        HoldBreath = Hit = false;
        BreathTime = 5f;
        HackPrompt = LockText = LockedText = DropZoneText = "";
        Distance = null;
        AimLocation = null;
        Targets.Clear();
    }
}

/// <summary>A row of the in-match scoreboard (spec/hud/scoreboard.md).</summary>
public sealed record HudScoreRow(string Name, int Team, bool Spectator, int ObjOrKills, int Kills, int Deaths, int Lives, int Ping, bool Local);

/// <summary>An objective of the mission status list (View_GameStatus).</summary>
public sealed record HudObjectiveLine(string Text, bool Done);

/// <summary>
/// Everything the HUD pages show, read from the simulation each frame (<see cref="HudSimReader"/>) or set by a test.
/// The page drivers read only this, so the HUD never touches simulation objects.
/// </summary>
public sealed class HudSnapshot
{
    /// <summary>False when there is no local player to show (the pages hide).</summary>
    public bool Valid { get; set; }

    public HudSide Side { get; set; }

    public float TimeSeconds { get; set; }

    // HUD.bHideHUD and the per-element switches of HUD.
    public bool HideHud { get; set; }

    public bool LifeOff { get; set; }

    public bool GadgetOff { get; set; }

    public bool TimeOff { get; set; }

    public bool AmmoOff { get; set; }

    // Pawn
    public bool HasPawn { get; set; }

    public int Health { get; set; }

    public bool Poisoned { get; set; }

    public FVector PawnLocation { get; set; }

    /// <summary>|Pawn.Velocity| in uu/s: the spy's objective markers fade while he moves.</summary>
    public float PawnSpeed { get; set; }

    /// <summary>Controller yaw, 65536 units a turn.</summary>
    public int Yaw { get; set; }

    // Spy energy
    public float EnergyLevel { get; set; }

    public int EnergyLevelMax { get; set; }

    /// <summary>SPlayerController.bFastSpeed: the sneak icon shows while it is false.</summary>
    public bool FastSpeed { get; set; } = true;

    // Merc weapon
    public bool HasWeapon { get; set; }

    public int ClipAmmo { get; set; }

    public int Ammo { get; set; }

    /// <summary>SWeaponDefense.ROFMode: 0 single, 1 burst, 2 auto.</summary>
    public int FireMode { get; set; }

    public string FireModeName { get; set; } = "";

    /// <summary>The merc reticle shows: weapon drawn and SPlayerController.bNoReticule clear.</summary>
    public bool ShowReticle { get; set; }

    /// <summary>Spread frame of the reticle area, trunc(10 x accuracy / (MaxDispersion - MinDispersion)).</summary>
    public int ReticleFrame { get; set; }

    /// <summary>The local merc's shot hit a pawn in the last 0.4 s (red reticle).</summary>
    public bool ReticleHit { get; set; }

    /// <summary>HUD.BallisticValue: the throw charge, 0 when not throwing.</summary>
    public float Ballistic { get; set; }

    // Gadget
    public int TeamGadgetSlots { get; set; }

    public HudGadget? Gadget { get; set; }

    // Vision
    /// <summary>PlayerController.RendMap (34 night, 36 thermal, 37 motion, 38 EMF...).</summary>
    public int VisionMode { get; set; }

    public string VisionName { get; set; } = "";

    // Game clock (GRI)
    public int TimeLimit { get; set; }

    public int ElapsedTime { get; set; }

    // Objectives
    public int ObjectivesLeft { get; set; }

    public int NumObjectives { get; set; }

    public HudCountdown? HackCountdown { get; set; }

    public List<HudCountdown> ObjectiveCountdowns { get; } = new();

    public List<HudObjective> Objectives { get; } = new();

    /// <summary>HUD.EnhancedRealityLevel != 0: the spy's objective markers are drawn.</summary>
    public bool EnhancedReality { get; set; }

    /// <summary>SHud.ObjectiveHackingSpy ("Under hacking") and ObjectiveSecuredSpy ("Access denied").</summary>
    public string HackingText { get; set; } = "";

    public string SecuredText { get; set; } = "";

    /// <summary>HUD.StrDistanceUnit and fDistanceUnit (metres per uu) for the marker distances.</summary>
    public string DistanceUnit { get; set; } = "";

    public float DistanceScale { get; set; }

    // Messages (SHud stacks, oldest first)
    public List<string> SystemMessages { get; } = new();

    public List<string> StateMessages { get; } = new();

    public List<string> InfoMessages { get; } = new();

    public List<string> GameMessages { get; } = new();

    // Radar
    public List<HudBlip> Blips { get; } = new();

    /// <summary>SHud.MalfunctionGamePlayObjects: the spy's orange GPO markers (and the merc's alarm blips).</summary>
    public List<FVector> Gpos { get; } = new();

    // Interaction list (spies)
    public bool ShowInteractions { get; set; }

    public List<string> Interactions { get; } = new();

    public int SelectedInteraction { get; set; } = -1;

    public bool InteractionFocus { get; set; }

    // Players, lives
    public bool ShowPlayers { get; set; }

    public List<HudPlayerRow> Players { get; } = new();

    public int Lives { get; set; }

    // Speak / Hack prompt (spies)
    public string SpeakPrompt { get; set; } = "";

    // ---- merc page effects, reticle, prompts -------------------------------------------------------------------

    public HudMercEffects Merc { get; } = new();

    /// <summary>The weapon class of the merc's reticle.</summary>
    public HudWeaponKind WeaponKind { get; set; }

    /// <summary>SPAWNDEFENSE.TorcheLaser.bInUse: the laser sight hides the reticle.</summary>
    public bool LaserOn { get; set; }

    /// <summary>SHud.SpotReticule (640 x 480 canvas units), or null for the page centre.</summary>
    public Godot.Vector2? SpotReticule { get; set; }

    /// <summary>
    /// Sound arrows (HUD.Left/Right/Top/Bottom/Forward/BackwardArrowTime): shown while trunc(TimeSeconds) is below
    /// each. Order: left, right, top, bottom, forward, backward.
    /// </summary>
    public float[] ArrowTimes { get; } = new float[6];

    /// <summary>SHud.ReticuleMessage while it lasts (merc page).</summary>
    public string ReticleMessage { get; set; } = "";

    /// <summary>Seconds of the most urgent alarm (ChronoAlarm), or null.</summary>
    public int? AlarmSeconds { get; set; }

    /// <summary>The objective counter mode and its text.</summary>
    public HudCounterMode CounterMode { get; set; }

    public string CounterText { get; set; } = "";

    /// <summary>The coop move icon (spies).</summary>
    public HudCoopIcon Coop { get; set; }

    /// <summary>SHud.sTutoMap: the tutorial message (tutorial maps only).</summary>
    public string Tutorial { get; set; } = "";

    // ---- spectator, scoreboard, status ---------------------------------------------------------------------

    public HudSpectator Spectator { get; } = new();

    /// <summary>Every player for the scoreboard: spies, mercs, then spectators.</summary>
    public List<HudScoreRow> Score { get; } = new();

    /// <summary>The mission's objectives as the mission status page lists them.</summary>
    public List<HudObjectiveLine> ObjectiveLines { get; } = new();

    /// <summary>GRI.GameMode is a deathmatch (kills instead of objectives).</summary>
    public bool DeathMatch { get; set; }

    /// <summary>The deathmatch respawn weapon on the canvas, or null.</summary>
    public HudRespawnWeapon? RespawnWeapon { get; set; }

    /// <summary>The knock-out screen's text and the seconds under it.</summary>
    public string SleepingText { get; set; } = "";

    public int SleepSeconds { get; set; }

    /// <summary>The binocular pages (spec/hud/binoculars.md).</summary>
    public HudBinoculars Binoculars { get; } = new();

    public void Clear()
    {
        Valid = false;
        HideHud = LifeOff = GadgetOff = TimeOff = AmmoOff = false;
        HasPawn = false;
        Health = 0;
        Poisoned = false;
        PawnLocation = default;
        PawnSpeed = 0f;
        HackingText = SecuredText = DistanceUnit = "";
        EnhancedReality = false;
        DistanceScale = 0f;
        Yaw = 0;
        EnergyLevel = 0f;
        EnergyLevelMax = 0;
        FastSpeed = true;
        HasWeapon = false;
        ClipAmmo = Ammo = FireMode = 0;
        FireModeName = "";
        ShowReticle = ReticleHit = false;
        ReticleFrame = 0;
        Ballistic = 0f;
        TeamGadgetSlots = 0;
        Gadget = null;
        VisionMode = 0;
        VisionName = "";
        TimeLimit = ElapsedTime = 0;
        ObjectivesLeft = NumObjectives = 0;
        HackCountdown = null;
        ObjectiveCountdowns.Clear();
        Objectives.Clear();
        SystemMessages.Clear();
        StateMessages.Clear();
        InfoMessages.Clear();
        GameMessages.Clear();
        Blips.Clear();
        Gpos.Clear();
        ShowInteractions = false;
        Interactions.Clear();
        SelectedInteraction = -1;
        InteractionFocus = false;
        ShowPlayers = false;
        Players.Clear();
        Lives = 0;
        SpeakPrompt = "";
        Merc.Clear();
        WeaponKind = HudWeaponKind.Rifle;
        LaserOn = false;
        SpotReticule = null;
        System.Array.Clear(ArrowTimes);
        ReticleMessage = "";
        AlarmSeconds = null;
        CounterMode = HudCounterMode.Story;
        CounterText = "";
        Coop = HudCoopIcon.None;
        Tutorial = "";
        Spectator.Clear();
        Score.Clear();
        ObjectiveLines.Clear();
        DeathMatch = false;
        RespawnWeapon = null;
        SleepingText = "";
        SleepSeconds = 0;
        Binoculars.Clear();
    }
}

/// <summary>Where the HUD gets its snapshot each frame.</summary>
public interface IHudSource
{
    /// <summary>Fills <paramref name="snapshot"/> (already cleared). Leaves <see cref="HudSnapshot.Valid"/> false
    /// when there is no local player.</summary>
    void Fill(HudSnapshot snapshot);
}
