using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// The HUD's own effect state, fed by the simulation's presentation notifications for the local player
/// (<see cref="CombatFx"/>, raised where the original called the SHud natives): the screen-effect slots
/// (<see cref="HudSfx"/>), the damage feedback of ClientDamageHudEffect, the merc's flash white-out, foam and
/// shutdown, the throw charge and the goggle messages (spec/hud/screen-effects.md). It keeps presentation state only.
/// </summary>
public sealed class HudEffects
{
    // ClientDamageHudEffect (spec/hud/screen-effects.md rule 7).
    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect 0x10ab0990: AddSfx(TextureColorRed, 120, 0, 0.5, Linear) for SPawnAttaque")]
    public const float DamageRedStart = 120f;

    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect 0x10ab0990: red wash 0.5 s")]
    public const float DamageRedTime = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect type 8: AddSfx(FlashEffect, 1, 0, 8, Linear)")]
    public const float FlashTime = 8f;

    [Provenance(ProvenanceKind.Decoded, "ClientDamageHudEffect type 7: SetHudProjection(1, 5, 10)")]
    public static readonly (int Splash, int Stay, int Clean) FoamProjection = (1, 5, 10);

    private const int DamageExtinguisher = (int)SDamageType.SDTExtincteurProjection;
    private const int DamageFlash = (int)SDamageType.SDTFlash;
    private const int DamageStickyShocker = (int)SDamageType.SDTStickyShocker;

    private static readonly Name NAME_s_Dead = new("s_Dead");

    private readonly List<SimEvent> _pending = new();
    private readonly object _gate = new();

    public HudSfx Sfx { get; } = new();

    /// <summary>
    /// Raised for each notification of the local player as <see cref="Update"/> applies it (the controller, the event,
    /// the HUD time): other presentation of the player's own events (the controller's rumble) listens here.
    /// </summary>
    public event Action<SPlayerController, SimEvent, float>? Applied;

    /// <summary>The last throw charge the simulation reported (SHud.SetBallisticValue).</summary>
    public float Ballistic { get; private set; }

    /// <summary>Damage perturbation the HUD runs itself (merc page), as an end time; 0 when none.</summary>
    public float PerturbedUntil { get; private set; }

    /// <summary>Time of the last flash-bang white-out on the merc page, or -1.</summary>
    public float FlashTime0 { get; private set; } = -1f;

    /// <summary>Time of the last merc HUD shutdown, or -1.</summary>
    public float ShutDownTime { get; private set; } = -1f;

    /// <summary>Counts of shutdowns seen (the page driver starts one on each change).</summary>
    public int ShutDowns { get; private set; }

    /// <summary>Counts of flashes seen (the page driver restarts the white-out on each change).</summary>
    public int Flashes { get; private set; }

    /// <summary>Counts of after-image captures asked for (bCaptureRemanence; the view captures a frame on each change).</summary>
    public int RemanenceCaptures { get; private set; }

    // The foam (SHud.DrawProjection, rule 15): kept here because the simulation does not run it.
    public bool Projection { get; private set; }

    public bool NeigeDisp { get; private set; }

    public int TimeStopProj { get; private set; }

    public int TimeSnowToStay { get; private set; }

    private int _timeToSplash;
    private int _timeToClean;
    private int _apparition;
    private int _disparition;
    private int _timeBeginClean;

    /// <summary>Queues an event raised by the simulation (any thread; drained by <see cref="Update"/>).</summary>
    public void Post(in SimEvent e)
    {
        if (e.Kind != SimEventKind.Notify)
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.Count < 256)
            {
                _pending.Add(e);
            }
        }
    }

    /// <summary>
    /// Applies the queued notifications that concern the local player <paramref name="pc"/>, then runs the per-draw
    /// counters (foam, SFX slots) for the time <paramref name="now"/>. Messages go to <paramref name="pc"/>'s SHud.
    /// </summary>
    public void Update(SPlayerController? pc, float now)
    {
        SimEvent[] events;
        lock (_gate)
        {
            events = _pending.ToArray();
            _pending.Clear();
        }

        if (pc is not null)
        {
            int pawnId = pc.Pawn?.ActorId ?? -1;
            int goggleId = (pc.Goggle as Actor)?.ActorId ?? -1;
            foreach (SimEvent e in events)
            {
                if (e.ActorId != 0 && (e.ActorId == pc.ActorId || e.ActorId == pawnId || e.ActorId == goggleId))
                {
                    Apply(pc, e, now);
                }
            }

            if (pc.SPawnType == PawnType.SPAWNDEFENSE && !pc.IsInState(NAME_s_Dead))
            {
                DrawProjection(pc.RendMap, now);
            }

            if (pc.Pawn is not { Health: > 0 } || pc.bEndGame)
            {
                Sfx.EndSleep();
            }
        }

        Sfx.Process(now);
    }

    private void Apply(SPlayerController pc, in SimEvent e, float now)
    {
        Applied?.Invoke(pc, e, now);
        string what = e.What.ToString();
        bool merc = pc.SPawnType == PawnType.SPAWNDEFENSE;
        switch (what)
        {
            case "ClientDamageHudEffect":
                ClientDamageHudEffect(pc, e.Slot, now);
                break;
            case "SetBallisticValue":
                Ballistic = e.Location.X;
                break;
            case "HudShutDown" when merc:
                ShutDownTime = now;
                ShutDowns++;
                if (pc.myHUD is SHud hud)
                {
                    // SHud.HudShutDown: AddHudNumberMessageToStack(7, 3), "System malfunction".
                    HudMessages.AddHudNumberMessageToStack(hud, 7, 3, 0);
                }

                break;
            case "OverlayFade":
            {
                // AddVisualSfx(TextureColor or TextureColorSleep, start, end, time, Linear): the notification names the
                // type in its parameter when it has one; a fade to black without one is the knock-out screen.
                float start = e.Location.X, end = e.Location.Y, time = e.Location.Z;
                SFXType type = e.Slot is > 0 and <= (int)SFXType.SFXT_TextureColorSleep
                    ? (SFXType)e.Slot
                    : end > start ? SFXType.SFXT_TextureColorSleep : SFXType.SFXT_TextureColor;
                SFXAttenuation evolution = time > 0f ? SFXAttenuation.SFXA_Linear : SFXAttenuation.SFXA_Constant;
                Sfx.AddVisualSfx(type, start, end, time, evolution, now, dontKill: type == SFXType.SFXT_TextureColorSleep || end > start);
                break;
            }

            case "CrossEyeBlur":
                Sfx.AddVisualSfx(SFXType.SFXT_CrossEye, e.Location.X, e.Location.Y, e.Location.Z, SFXAttenuation.SFXA_Linear, now);
                break;
            case "SmokeBlur":
                // GoInSmoke: SfxForceSlot(0) then AddVisualSfx(SFXT_Smoke, blur, 0, time, Linear).
                Sfx.SfxForceSlot(0);
                Sfx.AddVisualSfx(SFXType.SFXT_Smoke, e.Location.X, 0f, e.Location.Y, SFXAttenuation.SFXA_Linear, now);
                break;
            case "SetHudPerturbationLength" when merc:
                // 0x10a92880: started or not, the end becomes now + L (a shorter call shortens it).
                PerturbedUntil = now + e.Location.X;
                break;
            case "GoggleMessage":
                GoggleMessage(pc, e.Slot, e.Location.X);
                break;
        }
    }

    /// <summary>The HUD part of SPlayerController.ClientDamageHudEffect (spec/hud/screen-effects.md rule 7).</summary>
    [UcNative("ASPlayerController::execClientDamageHudEffect", Exe = 0x10ab0ae0)]
    [PortOf("nat:SBase.SPlayerController.ClientDamageHudEffect", Spec = "spec/hud/screen-effects.md#rules")]
    public void ClientDamageHudEffect(SPlayerController pc, int damageType, float now)
    {
        ArgumentNullException.ThrowIfNull(pc);
        bool merc = pc.SPawnType == PawnType.SPAWNDEFENSE;
        switch (damageType)
        {
            case DamageFlash:
                // 0x10ab0990: the goggle's CurrentMode, not RendMap (they differ while the goggle clip plays).
                if ((pc.Goggle?.CurrentMode ?? pc.RendMap) is 36 or 37 or 38)
                {
                    return;
                }

                // The whistle (Snd_FlashBangSiffle) plays in the simulation's half of the native.
                RemanenceCaptures++;
                Sfx.AddVisualSfx(SFXType.SFXT_FlashEffect, 1f, 0f, FlashTime, SFXAttenuation.SFXA_Linear, now);
                if (merc)
                {
                    FlashTime0 = now;
                    Flashes++;
                }

                return;
            case DamageExtinguisher:
                // 0x10ab0a9a: the goggle switch-off runs in the simulation (SPlayerController.ClientDamageHudEffect);
                // the HUD's part is SetHudProjection(1, 5, 10), which does nothing for spies.
                if (merc)
                {
                    SetHudProjection(FoamProjection.Splash, FoamProjection.Stay, FoamProjection.Clean, now);
                }

                return;
            case DamageStickyShocker:
                return;
        }

        if (pc.Pawn is null)
        {
            return;
        }

        // The merc's 1.5 s perturbation is the simulation's SetHudPerturbationLength (its notification reaches the
        // page through the SetHudPerturbationLength case above); only the spy's red wash is drawn here.
        if (pc.Pawn is SPawnAttaque)
        {
            Sfx.AddVisualSfx(SFXType.SFXT_TextureColorRed, DamageRedStart, 0f, DamageRedTime, SFXAttenuation.SFXA_Linear, now);
        }
    }

    /// <summary>SHud.SetHudProjection (rule 15): the foam splash, its stay and its clean time.</summary>
    [UcNative("ASHud::execSetHudProjection", Exe = 0x10a92b30, Editor = 0x111bec60)]
    [PortOf("nat:SBase.SHud.SetHudProjection", Spec = "spec/hud/screen-effects.md#rules")]
    public void SetHudProjection(int splash, int stay, int clean, float now)
    {
        _timeToSplash = splash;
        TimeSnowToStay = stay;
        _timeToClean = clean;
        if (NeigeDisp)
        {
            _timeBeginClean = 0;
            _disparition = 0;
            TimeStopProj = (int)now;
        }
        else
        {
            Projection = true;
        }
    }

    /// <summary>SHud.DrawProjection's counters (rule 15), once per HUD draw on a merc that is not dead.</summary>
    [UcNative("ASHud::execDrawProjection", Exe = 0x10a92bd0, Editor = 0x111bf480)]
    [PortOf("nat:SBase.SHud.DrawProjection", Spec = "spec/hud/screen-effects.md#rules")]
    private void DrawProjection(int rendMap, float now)
    {
        if (Projection)
        {
            if (rendMap is 37 or 38)
            {
                return;
            }

            // TimeBeginProj stays 0, so every draw adds a step.
            _apparition++;
            if (_apparition >= _timeToSplash * 10)
            {
                TimeStopProj = (int)now;
                Projection = false;
                NeigeDisp = true;
            }
        }
        else if (NeigeDisp)
        {
            if (TimeStopProj + TimeSnowToStay < now)
            {
                if (_timeBeginClean == 0)
                {
                    _timeBeginClean = (int)now;
                }

                if (_timeBeginClean + _disparition * 0.1f < now)
                {
                    _disparition++;
                }
            }

            if (_disparition >= _timeToClean * 10)
            {
                _timeBeginClean = _apparition = _disparition = 0;
                Projection = NeigeDisp = false;
            }
        }
    }

    /// <summary>SGoggle's mode messages on the auxiliary stack: 1 MTOn, 2 MTOff, 3 EMFOn, 4 EMFOff.</summary>
    private static void GoggleMessage(SPlayerController pc, int which, float time)
    {
        if (pc.myHUD is not SHud hud || pc.Goggle is not SGoggle goggle)
        {
            return;
        }

        string text = which switch
        {
            1 => goggle.MTOn,
            2 => goggle.MTOff,
            3 => goggle.EMFOn,
            4 => goggle.EMFOff,
            _ => "",
        };
        HudMessages.AddMessageToAuxiliaryStack(hud, text, Math.Max(1, (int)time), 0);
    }

    /// <summary>Forgets every effect (a new local player).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
        }

        Sfx.Reset();
        Ballistic = 0f;
        PerturbedUntil = 0f;
        FlashTime0 = ShutDownTime = -1f;
        Projection = NeigeDisp = false;
        _apparition = _disparition = _timeBeginClean = 0;
    }
}

/// <summary>
/// Hands the simulation's presentation events to the HUD without taking the world's event sink from its owner:
/// the tap wraps whatever sink was installed and passes every event on to it.
/// </summary>
public sealed class HudEventTap : ISimEventSink
{
    private readonly HudEffects _effects;

    private HudEventTap(HudEffects effects, ISimEventSink? next)
    {
        _effects = effects;
        Next = next;
    }

    public ISimEventSink? Next { get; }

    public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
    {
        foreach (ref readonly SimEvent e in events)
        {
            _effects.Post(e);
        }

        Next?.OnSimEvents(tick, events);
    }

    /// <summary>
    /// Hands <paramref name="effects"/> the events <paramref name="world"/> raised since its last tick: a call that
    /// arrived from the server before the step (a ClientDamage's HUD effect on a client) raises them outside any tick,
    /// and the step drops them before the sink sees them. Call it once before each step of a client world.
    /// </summary>
    public static void TakeLate(World world, HudEffects effects)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(effects);
        foreach (ref readonly SimEvent e in world.Events.Late)
        {
            effects.Post(e);
        }
    }

    /// <summary>Makes sure <paramref name="world"/>'s events reach <paramref name="effects"/> (re-wraps a replaced sink).</summary>
    public static void Ensure(World world, HudEffects effects)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(effects);
        for (ISimEventSink? s = world.EventSink; s is HudEventTap tap; s = tap.Next)
        {
            if (ReferenceEquals(tap._effects, effects))
            {
                return;
            }
        }

        world.EventSink = new HudEventTap(effects, world.EventSink);
    }
}
