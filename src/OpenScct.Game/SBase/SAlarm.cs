using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SAlarm: a map alarm raised by lasers, cameras, sensors and scripted events (spec/devices/alarm.md). While
/// active it fires its Events, locks its objective triggers and movers, tells the mercs (and the spies for low
/// priorities), marks the device that raised it, and after Duration seconds goes back to listening and undoes all
/// of it. Every machine adds the alarm to its own HUD when <see cref="bAlarmOn"/> changes.
/// </summary>
public partial class SAlarm
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_Listening.Instance, s_Active.Instance,
        s_ActiveDefPresent.Instance, s_InitialyActive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly ClassRef GameMessages = new("SBase.SGameMessages");

    /// <summary>The merc side of SGameInfo.BroadcastTeamLocalizedMessage.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Active.BeginState BroadcastTeamLocalizedMessage(1, ...)")]
    public const byte MercTeam = 1;

    /// <summary>The spy side of SGameInfo.BroadcastTeamLocalizedMessage.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Active.BeginState BroadcastTeamLocalizedMessage(0, ...)")]
    public const byte SpyTeam = 0;

    /// <summary>SGameMessages switch of the merc alarm line ("[description]: intruder detected").</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Active.BeginState / Trigger switch 1")]
    public const int MsgAlarm = 1;

    /// <summary>SGameMessages switch of the merc alarm line while a merc stands in the alarm zone.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_ActiveDefPresent.BeginState / Trigger switch 40")]
    public const int MsgAlarmDefPresent = 40;

    /// <summary>SGameMessages switch of the spy line (AlarmDescriptionSpy), sent for priorities 0 and 1.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Active.BeginState switch 24 when MessagePriority < 2")]
    public const int MsgAlarmSpy = 24;

    /// <summary>Priorities from this one up tell the spy who set the alarm off (AddMessageOnHud).</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Listening.Trigger: MessagePriority >= 2")]
    public const byte InstigatorMessagePriority = 2;

    /// <summary>Priorities from this one up say "area isolated" to the spy unless a merc is in the alarm zone.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Listening.Trigger: MessagePriority < 4 or a defender in the zone gives message 1, else 2")]
    public const byte IsolatedMessagePriority = 4;

    /// <summary>AddMessageOnHud number: "You've been detected".</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Listening.Trigger AddMessageOnHud(1)")]
    public const int HudDetected = 1;

    /// <summary>AddMessageOnHud number: "Area isolated" (versus) or detected.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_Listening.Trigger AddMessageOnHud(2)")]
    public const int HudAreaIsolated = 2;

    /// <summary>Period of s_InitialyActive's attempts to raise the alarm.</summary>
    [Provenance(ProvenanceKind.Decoded, "SAlarm.s_InitialyActive.BeginState SetTimer(2.0, true)")]
    public const float InitialyActivePeriod = 2f;

    // ---- events dispatched through the state ------------------------------------------------------------------

    /// <summary>A state that handles <see cref="SimulateActivated"/>.</summary>
    public interface ISimulateActivated
    {
        void SimulateActivated(SAlarm self, Actor? Other, Pawn? EventInstigator);
    }

    /// <summary>SimulateActivated through the current state (a noise emitter faking a detection).</summary>
    public void eventSimulateActivated(Actor? Other, Pawn? EventInstigator)
    {
        if (StateNode is ISimulateActivated s)
        {
            s.SimulateActivated(this, Other, EventInstigator);
        }
        else
        {
            SimulateActivated(Other, EventInstigator);
        }
    }

    [Uc("SBase.SAlarm.SimulateActivated", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SAlarm:SimulateActivated", Spec = "spec/devices/alarm.md#rules")]
    public void SimulateActivated(Actor? Other, Pawn? EventInstigator)
    {
    }

    // ---- native tick -----------------------------------------------------------------------------------------

    /// <summary>
    /// After the actor tick, on every machine: when <see cref="bAlarmOn"/> differs from the last value seen
    /// (<see cref="bLocalAlarmOn"/>), raise AlarmStateChange. On a client this is how a replicated change reaches the
    /// HUD.
    /// </summary>
    [UcNative("ASAlarm::Tick", Exe = 0x10a8ec90)]
    [PortOf("eng:ASAlarm::Tick", Spec = "spec/devices/alarm.md#network")]
    [Provenance(ProvenanceKind.Decoded, "vtable 0x10bf66e8 slot 29 = 0x10a8ec90: AActor::Tick, then (bAlarmOn ^ bLocalAlarmOn) -> eventAlarmStateChange")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (!bDeleteMe && bAlarmOn != bLocalAlarmOn)
        {
            AlarmStateChange();
        }
    }

    /// <summary>
    /// The alarm switched on or off: every pawn's controller's HUD adds (or removes) it. Off also clears the device
    /// marked on the HUD. Only HUDs of this machine exist, so each machine updates its own players.
    /// </summary>
    [Uc("SBase.SAlarm.AlarmStateChange", Sha1 = "1fbbf5260cb1acea9585ffcf5a02d7dc0a38a7ce")]
    [PortOf("uc:SBase.SAlarm:AlarmStateChange", Spec = "spec/devices/alarm.md#rules")]
    public void AlarmStateChange()
    {
        bLocalAlarmOn = bAlarmOn;
        foreach (SBasePawn p in XLevel.DynamicActors<SBasePawn>())
        {
            if ((p.Controller as SPlayerController)?.myHUD is SHud hud)
            {
                if (bAlarmOn)
                {
                    hud.AddAlarm(this);
                }
                else
                {
                    hud.RemoveAlarm(this);
                }
            }
        }

        if (!bAlarmOn)
        {
            ClearTriggerActor();
        }
    }

    // ---- script helpers --------------------------------------------------------------------------------------

    /// <summary>A merc entered this alarm's zone (SAlarmZoneInfo with this Tag as its Event): stand down.</summary>
    [Uc("SBase.SAlarm.PawnDefenseEntered", Sha1 = "d623a9dc051102b73a2edbe32c91d40427065e49")]
    [PortOf("uc:SBase.SAlarm:PawnDefenseEntered", Spec = "spec/devices/alarm.md#rules")]
    public void PawnDefenseEntered() => SetAlarmListening();

    /// <summary>A controlled merc stands in an SAlarmZoneInfo whose Event is this alarm's Tag.</summary>
    [Uc("SBase.SAlarm.IsDefenseInAlarmZone", Sha1 = "61056f37baa257699383bbef70f9dd213d18e459")]
    [PortOf("uc:SBase.SAlarm:IsDefenseInAlarmZone", Spec = "spec/devices/alarm.md#rules")]
    public bool IsDefenseInAlarmZone()
    {
        foreach (SPAWNDEFENSE p in XLevel.DynamicActors<SPAWNDEFENSE>())
        {
            if (p.Controller is not null && p.Region.Zone is SAlarmZoneInfo zone && zone.Event == Tag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Untriggers the Events, unlocks the triggers and movers, and listens again.</summary>
    [Uc("SBase.SAlarm.SetAlarmListening", Sha1 = "94edccbb0183b34625bbf1b47aeb64d47415fbe5")]
    [PortOf("uc:SBase.SAlarm:SetAlarmListening", Spec = "spec/devices/alarm.md#rules")]
    public void SetAlarmListening()
    {
        for (i = 0; i < Events.Count; i++)
        {
            UntriggerEvent(Events[i], this, null);
        }

        for (i = 0; i < ObjectiveTriggersToLock.Count; i++)
        {
            ObjectiveTriggersToLock[i]?.eventUnLock();
        }

        for (i = 0; i < MoversToLock.Count; i++)
        {
            MoversToLock[i].MoverToLock?.eventUnLock();
        }

        GotoState(nameof(s_Listening));
    }

    /// <summary>The countdown starts again: StartTime is now (game seconds), the timer fires after Duration.</summary>
    [Uc("SBase.SAlarm.RestartCountdown", Sha1 = "4f217d223337a7b819e2967365f08abd48bf1613")]
    [PortOf("uc:SBase.SAlarm:RestartCountdown", Spec = "spec/devices/alarm.md#rules")]
    public void RestartCountdown()
    {
        if (Level.Game?.GameReplicationInfo is { } gri)
        {
            StartTime = gri.PreciseElapsedTime();
        }

        SetTimer(Duration, false);
    }

    /// <summary>
    /// Without a merc in the alarm zone: triggers the Events, locks the triggers, triggers or untriggers each mover
    /// as asked and locks it, goes active and starts the countdown. With one: starts the countdown and goes to
    /// s_ActiveDefPresent (no events, no locks).
    /// </summary>
    [Uc("SBase.SAlarm.SetAlarmActive", Sha1 = "0e04468f1f82b7797d1d12a236e933cfe2d6101b")]
    [PortOf("uc:SBase.SAlarm:SetAlarmActive", Spec = "spec/devices/alarm.md#rules")]
    public void SetAlarmActive()
    {
        if (!IsDefenseInAlarmZone())
        {
            for (i = 0; i < Events.Count; i++)
            {
                TriggerEvent(Events[i], this, null);
            }

            for (i = 0; i < ObjectiveTriggersToLock.Count; i++)
            {
                ObjectiveTriggersToLock[i]?.eventLock();
            }

            for (i = 0; i < MoversToLock.Count; i++)
            {
                FMoverLockInfo m = MoversToLock[i];
                if (m.MoverToLock is not { } mover)
                {
                    continue;
                }

                // A script call, not an engine event: the mover's current state runs it even where that state
                // disabled the Trigger probe.
                Mover.MoverState? ms = mover.StateNode as Mover.MoverState;
                if (m.ActionBeforeLock == EActionBeforeLock.ActionBeforeLock_Trigger)
                {
                    if (ms is not null)
                    {
                        ms.Trigger(mover, this, null);
                    }
                    else
                    {
                        mover.eventTrigger(this, null);
                    }
                }
                else if (m.ActionBeforeLock == EActionBeforeLock.ActionBeforeLock_Untrigger)
                {
                    if (ms is not null)
                    {
                        ms.UnTrigger(mover, this, null);
                    }
                    else
                    {
                        mover.eventUnTrigger(this, null);
                    }
                }

                mover.eventLock();
            }

            GotoState(nameof(s_Active));
            RestartCountdown();
        }
        else
        {
            RestartCountdown();
            GotoState(nameof(s_ActiveDefPresent));
        }
    }

    // ---- shared pieces of the state functions ------------------------------------------------------------------

    /// <summary>The device marked on the HUD leaves it and stops being always relevant.</summary>
    internal void ClearTriggerActor()
    {
        if (TriggerActor is { } t)
        {
            t.RemoveFromHud();
            t.bAlwaysRelevant = false;
            TriggerActor = null;
        }
    }

    /// <summary>A gameplay object that raises the alarm becomes the marked device (shown on HUDs, always relevant).</summary>
    internal void MarkTriggerActor(Actor? Other)
    {
        if (Other is SBasicGamePlayObject gpo && !ReferenceEquals(TriggerActor, gpo))
        {
            ClearTriggerActor();
            TriggerActor = gpo;
            gpo.AddOnHud();
            gpo.bAlwaysRelevant = true;
        }
    }

    /// <summary>
    /// For priority 2 and up the instigating player reads "detected" (priorities 2 and 3, or a merc in the zone) or
    /// "area isolated" (4 and 5).
    /// </summary>
    internal void TellInstigator(Pawn? EventInstigator)
    {
        if (MessagePriority < InstigatorMessagePriority || EventInstigator?.Controller is not SPlayerController pc)
        {
            return;
        }

        pc.AddMessageOnHud(MessagePriority < IsolatedMessagePriority || IsDefenseInAlarmZone() ? HudDetected : HudAreaIsolated);
    }

    /// <summary>
    /// Re-raises the merc message and restarts the countdown when at least a whole game second has passed since
    /// the last start (GRI.ElapsedTime is whole seconds). The script reads the local player's GRI; a host without a
    /// local player reads the game's.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Level.GetLocalPlayerController().GameReplicationInfo: the stock host always has a local player; a dedicated host falls back to Level.Game's GRI")]
    internal void RepeatAlarm(int Switch)
    {
        GameReplicationInfo? gri = Level.GetLocalPlayerController()?.GameReplicationInfo ?? Level.Game?.GameReplicationInfo;
        if (gri is not null && gri.ElapsedTime > StartTime)
        {
            Broadcast(MercTeam, Switch);
            RestartCountdown();
        }
    }

    internal void Broadcast(byte Team, int Switch) =>
        (Level.Game as SGameInfo)?.BroadcastTeamLocalizedMessage(Team, GameMessages, Switch, null, null, this);

    /// <summary>The spy's alarm-triggered count, for a spy player.</summary>
    internal static void CountSpyAlarm(Pawn? EventInstigator)
    {
        if (EventInstigator is SPawnAttaque && EventInstigator.Controller is SPlayerController pc &&
            pc.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.Spy_AlarmsTriggered++;
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        TriggerActor = Live(TriggerActor);
    }
}
