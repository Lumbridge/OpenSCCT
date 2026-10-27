using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// Reads the local player's HUD state from the simulation: the controller, its pawn, its SHud, the GRI and PRIs,
/// following the native page drivers (docs/HUD.md, spec/hud/*.md), plus the HUD's own effect state
/// (<see cref="HudEffects"/>). Read only: nothing here writes simulation state.
/// </summary>
public sealed class HudSimReader : IHudSource
{
    /// <summary>Rows of the connected players list.</summary>
    public const int MaxConnectedPlayers = 4;

    private static readonly Name NAME_s_Hack_Use = new("s_Hack_Use");
    private static readonly Name NAME_s_Computer_Use = new("s_Computer_Use");
    private static readonly Name NAME_s_Binoculars_Att = new("s_Binoculars_Att");
    private static readonly Name NAME_s_Binoculars_Def = new("s_Binoculars_Def");

    private readonly Func<SPlayerController?> _controller;
    private readonly Func<string, string, string, string>? _localize;

    /// <param name="controller">The local player's controller, or null while there is none.</param>
    /// <param name="localize">MagmaDynamic lookups (section, key, fallback), or null for the English fallbacks.</param>
    /// <param name="effects">The HUD's own effect state, or null.</param>
    public HudSimReader(Func<SPlayerController?> controller, Func<string, string, string, string>? localize = null, HudEffects? effects = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _localize = localize;
        Effects = effects;
    }

    /// <summary>The HUD's own effect state the reader adds to the snapshot.</summary>
    public HudEffects? Effects { get; set; }

    /// <summary>
    /// Hides the connected players list whatever the HUD says (OpenSCCT's own: the hub sets it, where the four-row list
    /// cannot hold the hub's players and P opens the hub's own list).
    /// </summary>
    public bool HidePlayers { get; set; }

    public SPlayerController? Controller => _controller() is { bDeleteMe: false } pc ? pc : null;

    public void Fill(HudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Controller is { } pc)
        {
            Read(pc, snapshot, _localize, Effects);
            snapshot.ShowPlayers &= !HidePlayers;
        }
    }

    public static void Read(SPlayerController pc, HudSnapshot s, Func<string, string, string, string>? localize = null, HudEffects? effects = null)
    {
        ArgumentNullException.ThrowIfNull(pc);
        ArgumentNullException.ThrowIfNull(s);
        Level level = pc.XLevel;
        LevelInfo info = pc.Level;
        var pawn = pc.Pawn as SBasePawn;
        if (pawn is { bDeleteMe: true })
        {
            pawn = null;
        }

        s.Valid = true;
        s.TimeSeconds = info.TimeSeconds;
        PawnType type = pawn?.SPawnType ?? pc.SPawnType;
        s.Side = type == PawnType.SPAWNDEFENSE ? HudSide.Merc : HudSide.Spy;
        s.Yaw = pc.Rotation.Yaw;

        HUD? hud = pc.myHUD;
        var shud = hud as SHud;
        if (hud is not null)
        {
            s.HideHud = hud.bHideHUD;
            s.LifeOff = hud.bLifeOff;
            s.GadgetOff = hud.bGadgetOff;
            s.TimeOff = hud.bTimeOff;
            s.AmmoOff = hud.bAMMOOff;
            s.Ballistic = hud.BallisticValue;
            for (int i = 0; i < 6; i++)
            {
                s.ArrowTimes[i] = i switch
                {
                    0 => hud.LeftArrowTime,
                    1 => hud.RightArrowTime,
                    2 => hud.TopArrowTime,
                    3 => hud.BottomArrowTime,
                    4 => hud.ForwardArrowTime,
                    _ => hud.BackwardArrowTime,
                };
            }
        }

        if (effects is not null)
        {
            if (s.Ballistic == 0f)
            {
                s.Ballistic = effects.Ballistic;
            }

            s.HideHud |= effects.Sfx.Sleeping && effects.Sfx.ValueOf(effects.Sfx.Find(SFXType.SFXT_TextureColorSleep)) < 255f;
        }

        ReadPawn(pc, pawn, s);
        s.VisionMode = pc.RendMap;
        s.VisionName = HudK.VisionName(pc.RendMap);

        GameReplicationInfo? gri = pc.GameReplicationInfo ?? info.GRI;
        if (gri is not null)
        {
            s.TimeLimit = gri.TimeLimit;
            s.ElapsedTime = gri.ElapsedTime;
            s.NumObjectives = gri.NumObjectives;
            s.DeathMatch = gri.GameMode is EGameMode.GM_DeathMatch or EGameMode.GM_MercDeathMatch;
        }

        s.ObjectivesLeft = MissionRules.GetObjectivesLeft(level);
        ReadCounter(gri, s);
        if (pc.PlayerReplicationInfo is { } pri)
        {
            s.Lives = pri.NumLives;
            s.TeamGadgetSlots = (pri.Team as STeamInfo)?.iNBGadgets ?? 0;
        }

        if (hud is not null)
        {
            s.DistanceUnit = hud.StrDistanceUnit.Length > 0 ? hud.StrDistanceUnit : HudK.DefaultDistanceUnit;
            s.DistanceScale = hud.fDistanceUnit > 0f ? hud.fDistanceUnit : HudK.DefaultDistanceScale;
            s.EnhancedReality = hud.EnhancedRealityLevel != 0;
        }

        if (shud is not null)
        {
            s.HackingText = shud.ObjectiveHackingSpy;
            s.SecuredText = shud.ObjectiveSecuredSpy;
            ReadCountdowns(shud, gri?.ElapsedTime ?? 0, s);
            ReadMessages(shud, s);
            ReadAlarm(shud, gri?.ElapsedTime ?? 0, s);
            s.ShowPlayers = shud.bShowConnectedPlayers;
            s.Tutorial = shud.sTutoMap.Length > 4 ? shud.sTutoMap : "";
            s.SleepingText = shud.TextSleeping;
            if (shud.SpotReticule.X != 0f || shud.SpotReticule.Y != 0f)
            {
                s.SpotReticule = new Vector2(shud.SpotReticule.X, shud.SpotReticule.Y);
            }

            FStackedMessage rm = shud.ReticuleMessage;
            if (!string.IsNullOrEmpty(rm.Message) && rm.StartedTime + rm.StayInStackTime > info.TimeSeconds)
            {
                s.ReticleMessage = rm.Message;
            }
        }

        // ProcessSFX 0x10a9239f: %i of trunc(fLyingOnGroundTime + fLyingOnGroundDuration - now), not clamped.
        s.SleepSeconds = (int)(pc.fLyingOnGroundTime + pc.fLyingOnGroundDuration - info.TimeSeconds);
        s.RespawnWeapon = HudRespawnWeaponDrawer.Read(pc);
        ReadEffects(pc, pawn, shud, effects, s);
        ReadObjectives(pc, pawn, level, gri, s);
        ReadRadar(pc, pawn, shud, s);
        ReadInteractions(pc, pawn, s);
        if (gri is not null)
        {
            ReadPlayers(gri, s);
            ReadScore(pc, gri, s);
            ReadObjectiveLines(pc, level, gri, s);
            ReadSpectator(pc, gri, s, localize);
        }

        ReadPrompts(pc, pawn, s, localize);
        ReadBinoculars(pc, pawn, shud, gri, s, localize);
    }

    private static void ReadPawn(SPlayerController pc, SBasePawn? pawn, HudSnapshot s)
    {
        s.FastSpeed = pc.bFastSpeed;
        if (pawn is null)
        {
            return;
        }

        s.HasPawn = true;
        s.Health = pawn.Health;
        s.Poisoned = pawn.bPoisonned;
        s.PawnLocation = pawn.Location;
        s.PawnSpeed = pawn.Velocity.Size();
        if (pawn is SPawnAttaque spy)
        {
            s.EnergyLevel = spy.EnergyLevel;
            s.EnergyLevelMax = spy.EnergyLevelMax;
        }

        s.LaserOn = pawn is SPAWNDEFENSE { TorcheLaser: { bInUse: true } };
        if (pawn.MyWeapon is SWeaponDefense { bDeleteMe: false } weapon)
        {
            s.HasWeapon = true;
            s.ClipAmmo = weapon.ClipAmmo;
            s.Ammo = weapon.Ammo;
            s.FireMode = (int)weapon.ROFMode;
            s.FireModeName = weapon.GetROFName();
            s.WeaponKind = weapon switch
            {
                SWeaponDefense_Uzi => HudWeaponKind.Uzi,
                SWeaponDefense_Shotgun => HudWeaponKind.Shotgun,
                _ => HudWeaponKind.Rifle,
            };
            s.ShowReticle = pawn.WeaponLoaded != 0 && !pc.bNoReticule && !s.LaserOn;
            float range = weapon.MaxDispersion - weapon.MinDispersion;
            s.ReticleFrame = range > 0f ? (int)(10f * weapon.GetAccuracy() / range) : 0;
            s.ReticleHit = pc.fDateLastShootedPawn > 0f && pc.fDateLastShootedPawn + HudK.ReticleHitTime > pc.Level.TimeSeconds;
        }

        if (pawn.SelectedGadget is { bDeleteMe: false } gadget)
        {
            s.Gadget = new HudGadget(gadget.ItemName, gadget.Charge, gadget.MagmaIconName, gadget.IconU, gadget.IconV,
                Unavailable: gadget is SGadCamNetwork { HasNetwork: false });
        }
    }

    /// <summary>The merc page's effects (spec/hud/screen-effects.md rules 9 to 16).</summary>
    private static void ReadEffects(SPlayerController pc, SBasePawn? pawn, SHud? shud, HudEffects? effects, HudSnapshot s)
    {
        HudMercEffects m = s.Merc;
        float now = pc.Level.TimeSeconds;
        m.Perturbed = (shud?.bPerturbation ?? false) || (effects is not null && now < effects.PerturbedUntil);
        m.StickyShocking = pawn?.bStickyShocking ?? false;
        m.StickyShockHead = pawn?.bStickyShockedInTheHead ?? false;
        m.Wet = pawn?.CurrentRainVolume is not null;
        m.Crouched = pawn?.bIsCrouched ?? false;
        if (effects is not null)
        {
            m.ShutDowns = effects.ShutDowns;
            m.Flashes = effects.Flashes;
            m.Projection = effects.Projection || (shud?.bProjection ?? false);
            m.NeigeDisp = effects.NeigeDisp || (shud?.bNeigeDisp ?? false);
            m.TimeStopProj = effects.NeigeDisp ? effects.TimeStopProj : shud?.TimeStopProj ?? 0;
            m.TimeSnowToStay = effects.NeigeDisp ? effects.TimeSnowToStay : shud?.TimeSnowToStay ?? 0;
        }
        else if (shud is not null)
        {
            m.Projection = shud.bProjection;
            m.NeigeDisp = shud.bNeigeDisp;
            m.TimeStopProj = shud.TimeStopProj;
            m.TimeSnowToStay = shud.TimeSnowToStay;
        }
    }

    /// <summary>The objective counter variant (spec/hud/counters-and-prompts.md rule 2).</summary>
    private static void ReadCounter(GameReplicationInfo? gri, HudSnapshot s)
    {
        if (gri?.GameMode == EGameMode.GM_DeathMatch)
        {
            s.CounterMode = HudCounterMode.DeathMatch;
            s.CounterText = string.Create(CultureInfo.InvariantCulture, $"{Lives(gri.TotalTeamLives[0])}/{Lives(gri.TotalTeamLives[1])}");
        }
        else if (gri?.GameMode == EGameMode.GM_DiskHunt)
        {
            s.CounterMode = HudCounterMode.DiskHunt;
            s.CounterText = string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, gri.NumDisks - s.ObjectivesLeft)}/{gri.NumDisksToWin}");
        }
        else
        {
            s.CounterMode = HudCounterMode.Story;
            s.CounterText = s.ObjectivesLeft.ToString(CultureInfo.InvariantCulture);
        }

        static string Lives(int n) => n >= HudK.InfiniteTeamLives ? HudK.InfiniteShort : n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ChronoHack: the first STriggerInteraction in ActorToDraw with MinTriggerTime != 0, trunc(MTT - Elapsed) + 1
    /// (at least 0). ChronoObjectif(2): the first two SObjectives with MinTriggerTime != 0, trunc(MTT - Elapsed).
    /// </summary>
    private static void ReadCountdowns(SHud hud, int elapsed, HudSnapshot s)
    {
        foreach (Actor? a in hud.ActorToDraw)
        {
            if (a is STriggerInteraction { bDeleteMe: false } t && t.MinTriggerTime != 0f && s.HackCountdown is null)
            {
                s.HackCountdown = new HudCountdown(Math.Max(0, (int)(t.MinTriggerTime - elapsed) + 1));
            }
            else if (a is SObjective { bDeleteMe: false } o && o.MinTriggerTime != 0f && s.ObjectiveCountdowns.Count < 2)
            {
                s.ObjectiveCountdowns.Add(new HudCountdown(Math.Max(0, (int)(o.MinTriggerTime - elapsed))));
            }
        }
    }

    /// <summary>The alarm box (spec/hud/counters-and-prompts.md rule 1).</summary>
    private static void ReadAlarm(SHud hud, int elapsed, HudSnapshot s)
    {
        float best = 0f;
        foreach (SAlarm? a in hud.AlarmsList)
        {
            if (a is { bDeleteMe: false, MessagePriority: > HudK.AlarmBoxPriority })
            {
                best = Math.Max(best, a.Duration + a.StartTime - elapsed);
            }
        }

        if (best > 0f)
        {
            s.AlarmSeconds = (int)best;
        }
    }

    /// <summary>
    /// The four SHud stacks as the page's message driver shows them (spec/hud/messages.md rules 8 and 9): every live
    /// entry of the three stacks, and the head of the game queue while it is shown.
    /// </summary>
    private static void ReadMessages(SHud hud, HudSnapshot s)
    {
        float now = hud.Level.TimeSeconds;
        Copy(hud.MessageStack, s.SystemMessages, now);
        Copy(hud.AuxiliaryMessageStack, s.StateMessages, now);
        Copy(hud.InfoMessageStack, s.InfoMessages, now);
        if (hud.GameMessageQueue.Count > 0 && hud.GameMessageEndTime > 0f && !string.IsNullOrEmpty(hud.GameMessageQueue[0].Message))
        {
            s.GameMessages.Add(hud.GameMessageQueue[0].Message);
        }

        static void Copy(List<FStackedMessage> from, List<string> to, float now)
        {
            foreach (FStackedMessage m in from)
            {
                if (m.StartedTime + m.StayInStackTime >= now && !string.IsNullOrEmpty(m.Message))
                {
                    to.Add(m.Message);
                }
            }
        }
    }

    /// <summary>
    /// The class of an objective trigger as SHud.BuildObjectivesAndTriggersList (game 0x10af80b0) tests it, in its
    /// order: terminal, disk (SFlag), drop zone, bomb spot (IsA), then Disk Hunt's SDisk (a top-level entry).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af80b0 class tests: SComputerObjectiveTrigger, SFlag, SFlagDropZone, IsA SBombTargetObjectiveTrigger, SDisk")]
    public static HudObjectiveKind ObjectiveKind(SObjectiveTrigger t) => t switch
    {
        SComputerObjectiveTrigger => HudObjectiveKind.Terminal,
        SFlag => HudObjectiveKind.Disk,
        SFlagDropZone => HudObjectiveKind.DropZone,
        SBombTargetObjectiveTrigger => HudObjectiveKind.Bomb,
        SDisk => HudObjectiveKind.HuntDisk,
        _ => HudObjectiveKind.Other,
    };

    /// <summary>
    /// The Enhanced Reality markers (ASHud::ERDrawObjectives, game 0x10af8c00, over the list BuildObjectivesAndTriggersList
    /// builds): per SObjective (missions left out) one objective marker while the viewer is outside its zones and its
    /// triggers' markers inside them (SObjective.ERObjectiveTargetVisibility / ERTriggerTargetVisibility, zone-info.md
    /// rule 8); Disk Hunt disks near the viewer; free drop zones while they trigger. Every value read here is
    /// replicated (bTriggering, bLocked, bActive, ObjectiveTaken, CaptureTime, DeltaCaptureTime), so a client draws
    /// the server's state.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "viewer region = the pawn's Region (the native's region source is a stack value the decompile does not show); target alpha used as on / off, the native's ramp is not ported")]
    private static void ReadObjectives(SPlayerController pc, SBasePawn? pawn, Level level, GameReplicationInfo? gri, HudSnapshot s)
    {
        Actor? viewer = pawn ?? pc.ViewTarget ?? pc;
        FPointRegion region = viewer.Region;
        bool merc = pawn?.SPawnType == PawnType.SPAWNDEFENSE;
        bool diskHunt = gri?.GameMode == EGameMode.GM_DiskHunt;
        foreach (Actor a in level.AllActors<Actor>())
        {
            if (a.bDeleteMe)
            {
                continue;
            }

            if (a is SDisk disk)
            {
                // game 0x10afa4c0..: Disk Hunt only; shown when active or locked within 1500 uu.
                if (diskHunt && (disk.bActive || disk.bLocked) && (disk.Location - viewer.Location).Size() < HudK.HuntDiskErRange)
                {
                    s.Objectives.Add(new HudObjective(disk.TriggerName, HudObjectiveKind.HuntDisk, disk.Location,
                        disk.bTriggering, disk.bLocked, false, -1, HudErRole.HuntDisk));
                }
            }
            else if (a is SObjective objective and not SMission)
            {
                ReadObjective(objective, region, merc, s);
            }
            else if (a is SFlagDropZone zone)
            {
                // game 0x10afa230..: shown while triggering; red for a merc or a spy carrying a disk, else orange.
                if (zone.bTriggering)
                {
                    bool alert = merc || pawn?.CarriedFlag is not null;
                    s.Objectives.Add(new HudObjective(zone.TriggerName, HudObjectiveKind.DropZone, zone.Location, true,
                        zone.bLocked, false, -1, HudErRole.DropZone, alert));
                }
            }
        }
    }

    /// <summary>One SObjective's markers: its own (outside its zones) and its triggers' (inside).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af8c00 objective pass: objective marker red if any trigger triggers, yellow if any is locked; percent = highest trigger percent; a bomb spot that is not armed hides while another spot of the objective is armed (spies)")]
    private static void ReadObjective(SObjective objective, FPointRegion region, bool merc, HudSnapshot s)
    {
        bool anyTriggering = false, anyLocked = false;
        int best = -1;
        HudObjectiveKind icon = HudObjectiveKind.Other;
        foreach (SObjectiveTrigger? t in objective.Triggers)
        {
            if (t is not { bDeleteMe: false })
            {
                continue;
            }

            anyTriggering |= t.bTriggering;
            anyLocked |= t.bLocked;
            HudObjectiveKind kind = ObjectiveKind(t);
            if (kind is not (HudObjectiveKind.Other or HudObjectiveKind.HuntDisk))
            {
                icon = kind;
            }

            if (t is SComputerObjectiveTrigger c)
            {
                best = Math.Max(best, HudK.CapturePercent(c));
            }
        }

        if (icon == HudObjectiveKind.Other)
        {
            return;
        }

        foreach (SObjectiveTrigger? t in objective.Triggers)
        {
            if (t is not { bDeleteMe: false } || ObjectiveKind(t) is HudObjectiveKind.Other or HudObjectiveKind.HuntDisk ||
                objective.ERTriggerTargetVisibility(t, region) == 0)
            {
                continue;
            }

            HudObjectiveKind kind = ObjectiveKind(t);
            if (kind == HudObjectiveKind.Bomb && !t.bTriggering && anyTriggering && !merc)
            {
                continue;
            }

            int percent = t is SComputerObjectiveTrigger c ? HudK.CapturePercent(c) : -1;
            string name = t.TriggerName.Length > 0 ? t.TriggerName : objective.ObjectiveName;
            s.Objectives.Add(new HudObjective(name, kind, t.Location, t.bTriggering, t.bLocked, objective.ObjectiveTaken, percent));
        }

        if (objective.ERObjectiveTargetVisibility(region) != 0)
        {
            s.Objectives.Add(new HudObjective(objective.ObjectiveName, icon, objective.Location, anyTriggering, anyLocked,
                objective.ObjectiveTaken, icon == HudObjectiveKind.Disk ? -1 : best, HudErRole.Objective));
        }
    }

    /// <summary>
    /// Radar blips (Bridge radar notes 3.2 and 3.3): other pawns of the player's side are teammates; enemies show when
    /// spy-bulleted by the player's side (and, for mercs, a spy carrying the disk faster than 170 uu/s); mercs also
    /// get an alarm blip for every malfunctioning game object and every trigger in SHud.ObjectivesList (a terminal
    /// being hacked).
    /// </summary>
    private static void ReadRadar(SPlayerController pc, SBasePawn? own, SHud? hud, HudSnapshot s)
    {
        bool merc = s.Side == HudSide.Merc;
        // The pawns, not the controllers: a client only has its own controller, so the other players' pawns are the
        // only ones it can see (teammates, and enemies marked by the laser or the heartbeat sensor).
        foreach (SBasePawn q in pc.XLevel.AllActors<SBasePawn>())
        {
            if (q.bDeleteMe || ReferenceEquals(q, own) || q.Health <= 0)
            {
                continue;
            }

            bool sameSide = (q.SPawnType == PawnType.SPAWNDEFENSE) == merc;
            if (sameSide)
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Teammate, q.Location));
            }
            else if (q.bSpyBulletOn && q.iTeamSpying == (merc ? 1f : 0f))
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Enemy, q.Location));
            }
            else if (merc && q.CarriedFlag is not null && q.Velocity.Size() > HudK.FlagCarrierRadarSpeed)
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Enemy, q.Location));
            }
        }

        if (hud is null)
        {
            return;
        }

        // The GPO markers of the spy canvas and the merc alarm blips both come from MalfunctionGamePlayObjects.
        foreach (FVector v in hud.MalfunctionGamePlayObjects)
        {
            s.Gpos.Add(v);
            if (merc)
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Alarm, v));
            }
        }

        if (!merc)
        {
            return;
        }

        foreach (SObjectiveTrigger? t in hud.ObjectivesList)
        {
            if (t is { bDeleteMe: false })
            {
                s.Blips.Add(new HudBlip(HudBlipKind.Alarm, t.Location));
            }
        }
    }

    /// <summary>
    /// The spy's interaction list (Bridge spy page notes, section 6): usable entries of the menu in slot order, shown
    /// when at least two are usable, the pawn is awake and the controller can interact; the selection shows while Use
    /// is held.
    /// </summary>
    private static void ReadInteractions(SPlayerController pc, SBasePawn? pawn, HudSnapshot s)
    {
        if (pc.IManager is not { bDeleteMe: false } manager || pawn is null)
        {
            return;
        }

        for (int i = 0; i < manager.NbObjects && i < manager.Interactions.Length; i++)
        {
            if (manager.Interactions[i] is { bDeleteMe: false } entry && entry.CanUse(pc))
            {
                if (i == manager.SelectedInteractions)
                {
                    s.SelectedInteraction = s.Interactions.Count;
                }

                s.Interactions.Add(entry.Description);
            }
        }

        s.InteractionFocus = pc.bUsing != 0;
        s.ShowInteractions = s.Side == HudSide.Spy && s.Interactions.Count >= 2 && !pawn.bSleeping && HudK.CanInteract(pc);
    }

    /// <summary>Connected players: spies, then mercs, then spectators, up to four rows.</summary>
    private static void ReadPlayers(GameReplicationInfo gri, HudSnapshot s)
    {
        for (int pass = 0; pass < 3 && s.Players.Count < MaxConnectedPlayers; pass++)
        {
            foreach (PlayerReplicationInfo? pri in gri.PRIArray)
            {
                if (pri is not { bDeleteMe: false } || s.Players.Count >= MaxConnectedPlayers)
                {
                    continue;
                }

                int team = pri.Team?.TeamIndex ?? 0;
                bool spectator = pri.bOnlySpectator;
                if ((pass == 2) != spectator || (!spectator && team != pass))
                {
                    continue;
                }

                int objOrKills = team == 0 && !s.DeathMatch ? pri.iNbObjectivesTaken : pri.Kills;
                s.Players.Add(new HudPlayerRow(pri.PlayerName, team, spectator, objOrKills, pri.NumLives, pri.Ping, pri.SvmRank));
            }
        }
    }

    /// <summary>Every player for the mission status page and the scoreboard: spies, mercs, then spectators.</summary>
    private static void ReadScore(SPlayerController pc, GameReplicationInfo gri, HudSnapshot s)
    {
        PlayerReplicationInfo? own = pc.PlayerReplicationInfo;
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (PlayerReplicationInfo? pri in gri.PRIArray)
            {
                if (pri is not { bDeleteMe: false })
                {
                    continue;
                }

                int team = pri.Team?.TeamIndex ?? 0;
                bool spectator = pri.bOnlySpectator;
                if ((pass == 2) != spectator || (!spectator && team != pass))
                {
                    continue;
                }

                int objOrKills = team == 0 && !s.DeathMatch ? pri.iNbObjectivesTaken : pri.Kills;
                s.Score.Add(new HudScoreRow(pri.PlayerName, team, spectator, objOrKills, pri.Kills, pri.Deaths, pri.NumLives,
                    pri.Ping, ReferenceEquals(pri, own), pri.SvmRank));
            }
        }
    }

    /// <summary>The mission status page's objective list (spec/hud/scoreboard.md rule 5).</summary>
    private static void ReadObjectiveLines(SPlayerController pc, Level level, GameReplicationInfo gri, HudSnapshot s)
    {
        bool spy = (pc.PlayerReplicationInfo?.Team?.TeamIndex ?? 0) == 0;
        foreach (SMission mission in level.AllActors<SMission>())
        {
            if (mission.GameMode != gri.GameMode)
            {
                continue;
            }

            bool oneNotTaken = false;
            foreach (SObjective? o in mission.Objectives)
            {
                if (o is null)
                {
                    continue;
                }

                bool open = !o.ObjectiveTaken || (mission.bChained && oneNotTaken);
                oneNotTaken |= open;
                string text = (spy ? o.Description : o.DescriptionDEF).Replace('|', ' ');
                s.ObjectiveLines.Add(new HudObjectiveLine(text, !open));
            }
        }
    }

    /// <summary>The spectator page (spec/hud/spectator-page.md rules 1 to 8).</summary>
    private static void ReadSpectator(SPlayerController pc, GameReplicationInfo gri, HudSnapshot s, Func<string, string, string, string>? localize)
    {
        HudSpectator v = s.Spectator;
        v.Active = HudK.IsSpectating(pc);
        v.GameEnded = pc.bGameEnded;
        if (!v.Active || v.GameEnded)
        {
            return;
        }

        string inf = localize?.Invoke("Magma", "Inifite_Short", HudK.InfiniteShort) ?? HudK.InfiniteShort;
        int left = s.ObjectivesLeft;
        v.Objectives = gri.GameMode == EGameMode.GM_DiskHunt
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, gri.NumDisks - left)}/{gri.NumDisksToWin}")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, gri.NumObjectives - left)}/{gri.NumObjectives}");
        int rem = gri.TimeLimit - gri.ElapsedTime / 60 - 1;
        v.RemainingTime = rem < 0 ? inf : string.Create(CultureInfo.InvariantCulture, $"{rem}:{59 - gri.ElapsedTime % 60:00}");
        if (pc.PlayerReplicationInfo is not { } pri)
        {
            return;
        }

        SBasePawn? target = pc.SpecTarget is { bDeleteMe: false } t ? t : null;
        PlayerReplicationInfo? tp = target?.PlayerReplicationInfo;
        v.Visitor = pri.bOnlySpectator;
        string Lives(int n) => n < HudK.InfiniteLives ? n.ToString(CultureInfo.InvariantCulture) : inf;
        if (!v.Visitor)
        {
            v.Lives = Lives(pri.NumLives);
            if (target is not null)
            {
                v.ShowTeamLives = true;
                int side = pc.SPawnType == PawnType.SPAWNDEFENSE ? 1 : 0;
                int n = gri.TotalTeamLives[side];
                v.TeamLives = n < HudK.InfiniteLives ? (n - pri.NumLives).ToString(CultureInfo.InvariantCulture) : inf;
                if (tp is not null)
                {
                    v.Watched = tp.PlayerName;
                    v.ShowWatching = true;
                    v.ShowSwitchPlayer = (tp.Team?.Size ?? 0) >= HudK.SwitchPlayerTeamSize;
                }
            }
        }
        else
        {
            v.ShowSwitchPlayer = true;
            if (target is not null && tp is not null)
            {
                v.Lives = Lives(tp.NumLives);
                v.Watched = tp.PlayerName;
                v.ShowWatching = true;
            }
        }

        if (pri.Team is { } team && team.TeamIndex is >= 0 and < 2 && gri.Teams[team.TeamIndex] is STeamInfo info &&
            pri.iTipNumber >= 0 && pri.iTipNumber < info.aTipStrings.Count)
        {
            v.Tip = info.aTipStrings[pri.iTipNumber];
        }

        if (pri.NumLives > 0 && !pc.IsInState(HudK.NAME_s_Spectating_Visitor))
        {
            int sec = (int)(pc.fRespawnDate - pc.Level.TimeSeconds + 1f);
            v.RespawnIn = sec is > 0 and < HudK.RespawnShownBelow ? sec : 0;
        }
        else
        {
            v.NoRespawn = true;
        }
    }

    /// <summary>The spy page's prompts: Speak / Hack, coop icons.</summary>
    private static void ReadPrompts(SPlayerController pc, SBasePawn? pawn, HudSnapshot s, Func<string, string, string, string>? localize)
    {
        if (s.Side != HudSide.Spy)
        {
            return;
        }

        bool nearDowned = false;
        if (pawn is not null)
        {
            for (Controller? c = pc.Level.ControllerList; c is not null; c = c.nextController)
            {
                if (c.Pawn is not SBasePawn { bDeleteMe: false } q || ReferenceEquals(q, pawn))
                {
                    continue;
                }

                float d = (q.Location - pawn.Location).Size();
                if (q.SPawnType != pawn.SPawnType)
                {
                    nearDowned |= q.Health > 0 && q.bLyingOnGround && d < pc.fDistToTalkWhenOnGround;
                }
            }
        }

        // Speak / Hack prompt (Bridge spy page notes 5.1): grabbing a merc, listening to a spy-bulleted one, or a
        // downed enemy close by.
        if (pc.bCanTalkToGuy || pc.bCanListenSpyBulletedGuy || nearDowned)
        {
            s.SpeakPrompt = pc.bCanTalkToGuy || nearDowned
                ? localize?.Invoke("HUD", "TextChat1", "Speak") ?? "Speak"
                : localize?.Invoke("HUD", "TextChat2", "Hack") ?? "Hack";
        }

        // Coop icons (spec/objectives/team-interactions.md, HUD prompts: the decoded spy driver).
        s.Coop = InteractionPrompts.Coop(pc, pawn);
    }
    /// <summary>
    /// The binocular pages (spec/hud/binoculars.md): which page the controller's state picks, the zoom step, the
    /// merc's breath, hit flash, range finder and aim point, and the spy's objective triggers.
    /// </summary>
    private static void ReadBinoculars(SPlayerController pc, SBasePawn? pawn, SHud? shud, GameReplicationInfo? gri, HudSnapshot s,
        Func<string, string, string, string>? localize)
    {
        HudBinoculars b = s.Binoculars;
        if (pawn is null)
        {
            return;
        }

        if (s.Side == HudSide.Spy && pc.IsInState(NAME_s_Binoculars_Att))
        {
            b.Mode = HudBinocularMode.Spy;
        }
        else if (s.Side == HudSide.Merc && pc.IsInState(NAME_s_Binoculars_Def))
        {
            b.Mode = HudBinocularMode.Merc;
        }
        else
        {
            return;
        }

        float now = pc.Level.TimeSeconds;
        b.Fov = pc.fBinocularVisionFOV;
        b.HoldBreath = pc.bHoldBreath != 0;
        b.BreathTime = pc.BinocularHoldBreathTime;
        b.Hit = pc.fDateLastShootedPawn + HudK.BinocularHitTime > now;
        if (pc.VoiceChatTarget is { bDeleteMe: false })
        {
            b.HackPrompt = localize?.Invoke("HUD", "TextChat2", "Hack") ?? "Hack";
        }

        if (b.Mode == HudBinocularMode.Merc)
        {
            ReadRangeFinder(pawn, b);
            pc.MyDesiredRotation.GetAxes(out FVector x, out _, out _);
            b.AimLocation = pawn.Location + OpenScct.Game.Combat.CombatServices.Ports(pc).EyePosition(pawn) + x * HudK.BinocularAimDistance;
            return;
        }

        if (shud is not null)
        {
            b.LockText = shud.BinocularsAttLock;
            b.LockedText = shud.BinocularAttLocked;
            b.DropZoneText = shud.DropZone;
        }

        bool story = gri?.GameMode == EGameMode.GM_Multi;
        foreach (SObjectiveTrigger t in pc.XLevel.AllActors<SObjectiveTrigger>())
        {
            if (t.bDeleteMe || !(t.bActive || t.bLocked || (story && t is SFlagDropZone)))
            {
                continue;
            }

            HudObjectiveKind kind = ObjectiveKind(t);
            if (kind == HudObjectiveKind.Other)
            {
                continue;
            }

            int percent = t is SComputerObjectiveTrigger c ? HudK.CapturePercent(c) : 0;
            b.Targets.Add(new HudBinocularTarget(t.TriggerName, kind, t.Location, t.bTriggering, t.bLocked, percent,
                shud is not null && ReferenceEquals(shud.LockedObjective, t)));
        }
    }

    /// <summary>
    /// The merc's range finder (rule 9): a line from the rifle's FX bone along its X axis for 15000 uu; the distance
    /// to the first hit, or 0. A collision query only.
    /// </summary>
    private static void ReadRangeFinder(SBasePawn pawn, HudBinoculars b)
    {
        if (pawn.MyWeapon is not SWeaponDefense { bDeleteMe: false } weapon)
        {
            return;
        }

        FCoords fx = OpenScct.Game.Combat.CombatServices.Ports(weapon).BoneCoords(weapon, SWeaponDefense.FxBone);
        FVector start = fx.Origin;
        FVector end = start + fx.XAxis * HudK.BinocularRange;
        b.Distance = OpenScct.Game.Movement.MovementNatives.Trace(weapon, out FVector hit, out _, end, start, true) is null
            ? 0f
            : (hit - start).Size();
    }
}
