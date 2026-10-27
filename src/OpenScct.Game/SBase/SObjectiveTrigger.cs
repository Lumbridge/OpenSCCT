using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The base of everything that can complete an objective, written from spec/objectives/objective-trigger.md.
public partial class SObjectiveTrigger
{
    public static new readonly StateTable ClassStates = new(SGamePlayObject.ClassStates,
        s_Initial.Instance, s_Inactive.Instance, s_TemporaryInactive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    // ---- state dispatch of the functions this class declares ----------------------------------------------------

    /// <summary>State version of <see cref="Reset"/> (Actor.Reset is the class-level version).</summary>
    public interface IReset
    {
        void Reset(SObjectiveTrigger self);
    }

    public interface ITriggering
    {
        void Triggering(SObjectiveTrigger self, bool Status);
    }

    public interface ITakeObjective
    {
        void TakeObjective(SObjectiveTrigger self);
    }

    public interface ILock
    {
        void Lock(SObjectiveTrigger self);
    }

    public interface IUnLock
    {
        void UnLock(SObjectiveTrigger self);
    }

    /// <summary>Resets the trigger for a new round (current state first). Missions call it through DoReset.</summary>
    public void eventReset()
    {
        if (StateNode is IReset s)
        {
            s.Reset(this);
        }
        else
        {
            Reset();
        }
    }

    /// <summary>Starts or stops a capture (current state first).</summary>
    public void eventTriggering(bool Status)
    {
        if (StateNode is ITriggering s)
        {
            s.Triggering(this, Status);
        }
        else
        {
            Triggering(Status);
        }
    }

    /// <summary>Completes the trigger for <see cref="SObjectiveTrigger.ControllerWhoTakesIt"/> (current state first).</summary>
    public void eventTakeObjective()
    {
        if (StateNode is ITakeObjective s)
        {
            s.TakeObjective(this);
        }
        else
        {
            TakeObjective();
        }
    }

    /// <summary>Locks the trigger (alarm), current state first.</summary>
    public void eventLock()
    {
        if (StateNode is ILock s)
        {
            s.Lock(this);
        }
        else
        {
            Lock();
        }
    }

    /// <summary>Unlocks the trigger (alarm), current state first.</summary>
    public void eventUnLock()
    {
        if (StateNode is IUnLock s)
        {
            s.UnLock(this);
        }
        else
        {
            UnLock();
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Objective = Live(Objective);
        ControllerWhoTakesIt = Live(ControllerWhoTakesIt);
    }

    // ---- setup ------------------------------------------------------------------------------------------------

    [Uc("SBase.SObjectiveTrigger.BeginPlay", Sha1 = "37ebfc29a20d224c10b8a703140dd32830677c8c")]
    [PortOf("uc:SBase.SObjectiveTrigger:BeginPlay", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected override void BeginPlay()
    {
        base.BeginPlay();
        bWorldGeometry = false;
    }

    [Uc("SBase.SObjectiveTrigger.PostBeginPlay", Sha1 = "33a9d70fcddb992fe3cee1ffeccaddd6b4541dbc")]
    [PortOf("uc:SBase.SObjectiveTrigger:PostBeginPlay", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (Objective is not null)
        {
            Objective.RemainingTime = SvmMath.Max(Objective.RemainingTime, CaptureTime);
        }
    }

    /// <summary>Sticky gadgets do not attach to objective devices.</summary>
    [Uc("SBase.SObjectiveTrigger.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SObjectiveTrigger:AcceptStickableGPO", Spec = "spec/objectives/objective-trigger.md#rules")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>Event raised by the native objective reset.</summary>
    [Uc("SBase.SObjectiveTrigger.DoReset", Sha1 = "19e33bddd3d57172161c8d5f551a14974c3c5ead")]
    [PortOf("uc:SBase.SObjectiveTrigger:DoReset", Spec = "spec/objectives/objective-trigger.md#rules")]
    public void DoReset() => eventReset();

    /// <summary>Event raised by the native Desactivate.</summary>
    [Uc("SBase.SObjectiveTrigger.GotoState_Inactive", Sha1 = "993d55c241053069baaecb7dcbaa09b92d88c41c")]
    [PortOf("uc:SBase.SObjectiveTrigger:GotoState_Inactive", Spec = "spec/objectives/objective-trigger.md#rules")]
    public void GotoState_Inactive() => GotoState(K.s_Inactive);

    /// <summary>Makes the device visible and usable when its mission starts; nothing in the base class.</summary>
    [Uc("SBase.SObjectiveTrigger.Prepare", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SObjectiveTrigger:Prepare", Spec = "spec/objectives/objective-trigger.md#rules")]
    public virtual void Prepare()
    {
    }

    [Uc("SBase.SObjectiveTrigger.Reset", Sha1 = "f0f2a4ea86f18a76abf1e26beea592fbf1de16ef")]
    [PortOf("uc:SBase.SObjectiveTrigger:Reset", Spec = "spec/objectives/objective-trigger.md#rules")]
    public override void Reset()
    {
        SetStaticMesh(InitialStaticMesh);
        bActive = true;
        bLocked = false;
        eventTriggering(false);
        bEMFIsOn = true;
        complete = false;
        takenTime = 0;
        GotoState(K.s_Initial);
    }

    // ---- capture ----------------------------------------------------------------------------------------------

    [Uc("SBase.SObjectiveTrigger.Triggering", Sha1 = "435a31c1392e57d9925db0b41942c1690fe701df")]
    [PortOf("uc:SBase.SObjectiveTrigger:Triggering", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected internal virtual void Triggering(bool Status)
    {
        bTriggering = Status;
        bool radar = this is not SBombTargetObjectiveTrigger;
        if (Status)
        {
            if (Objective is { bShowStatusOnHud: true })
            {
                Objective.AddRemoveObjectiveOnClientHud(true);
            }

            TriggerTime = ObjectiveNatives.PreciseElapsedTime(Level);
            if (radar)
            {
                AddRemoveObjectiveForRadar(true);
            }
        }
        else
        {
            TriggerTime = 0f;
            if (radar)
            {
                AddRemoveObjectiveForRadar(false);
            }
        }

        Objective?.NotifyTriggering();
    }

    /// <summary>
    /// Multicast (flag 0x01000000): on each machine whose local player is a merc with a pawn, the trigger joins or
    /// leaves that HUD's radar list.
    /// </summary>
    [Uc("SBase.SObjectiveTrigger.AddRemoveObjectiveForRadar", Sha1 = "eee174f1902e854c62ec37440c28fd1ddabc9ac8")]
    [PortOf("uc:SBase.SObjectiveTrigger:AddRemoveObjectiveForRadar", Spec = "spec/objectives/objective-trigger.md#rules")]
    [PortOf("rep:SBase.SObjectiveTrigger.AddRemoveObjectiveForRadar", Spec = "spec/objectives/messages.md#network")]
    public void AddRemoveObjectiveForRadar(bool bAdd)
    {
        if (Level.LocalPlayerController is { Pawn: SPAWNDEFENSE, myHUD: SHud hud })
        {
            if (bAdd)
            {
                ObjectiveHud.AddObjective(hud, this);
            }
            else
            {
                ObjectiveHud.RemoveObjective(hud, this);
            }
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            ObjectiveNatives.Net(this).AddRemoveObjectiveForRadar(this, bAdd);
        }
    }

    // ---- completion -------------------------------------------------------------------------------------------

    [Uc("SBase.SObjectiveTrigger.TakeObjective", Sha1 = "d43aeb7a08dc2c22f0f886ef93d2faa17b025528")]
    [PortOf("uc:SBase.SObjectiveTrigger:TakeObjective", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected internal virtual void TakeObjective()
    {
        PlayerController? taker = ControllerWhoTakesIt;
        if (taker is null)
        {
            return;
        }

        complete = true;
        GameReplicationInfo? gri = Level.GRI;
        takenTime = gri?.ElapsedTime ?? 0;
        if (gri is not null)
        {
            gri.LastTimeObjectif = takenTime;
        }

        if (bUntriggerEvent)
        {
            UntriggerEvent(Event, this, taker.Pawn);
        }
        else
        {
            TriggerEvent(Event, this, taker.Pawn);
        }

        if (this is not SDisk && taker is SPlayerController { PlayerReplicationInfo.Stats: { } stats })
        {
            stats.Spy_ObjectivesTaken.Add(ObjectiveNatives.ObjectName(this));
            stats.Spy_ObjectivesTakenTime.Add(takenTime);
        }

        Objective?.NotifyTaken(taker, this);
    }

    // ---- alarm locks -----------------------------------------------------------------------------------------

    [Uc("SBase.SObjectiveTrigger.Lock", Sha1 = "531c9d55d0a5749995a8a2fda630ae43f576cc0f")]
    [PortOf("uc:SBase.SObjectiveTrigger:Lock", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected internal virtual void Lock()
    {
        if (Objective is { RemainingTime: > 0 })
        {
            bTriggering = false;
            GotoState(K.s_TemporaryInactive);
        }
    }

    [Uc("SBase.SObjectiveTrigger.UnLock", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SObjectiveTrigger:UnLock", Spec = "spec/objectives/objective-trigger.md#rules")]
    protected internal virtual void UnLock()
    {
    }

    // ---- natives ---------------------------------------------------------------------------------------------

    /// <summary>
    /// While triggering, the game time a running capture completes (TriggerTime, the start, plus CaptureTime);
    /// 0 otherwise.
    /// </summary>
    [UcNative("ASObjectiveTrigger::execGetTriggerTime", Exe = 0x10AE8AF0, Editor = 0x111CE1A0)]
    [PortOf("nat:SBase.SObjectiveTrigger.GetTriggerTime", Spec = "spec/objectives/objective-trigger.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AE8AF0: bTriggering ? (float)CaptureTime + TriggerTime : 0")]
    public virtual float GetTriggerTime() => bTriggering ? CaptureTime + TriggerTime : 0f;

    /// <summary>
    /// True when the trigger is NOT active: the native returns the inverse of bActive (and ignores bLocked). No
    /// script of this build calls it.
    /// </summary>
    [UcNative("ASObjectiveTrigger::execIsActive", Exe = 0x10AE8B30, Editor = 0x111CE0D0)]
    [PortOf("nat:SBase.SObjectiveTrigger.IsActive", Spec = "spec/objectives/objective-trigger.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AE8B30: ~(flags >> 1) & 1, bActive is mask 2 of +0x3E4")]
    public bool IsActive() => !bActive;

    /// <summary>
    /// Takes the trigger out of play (mission done or not selected): neither active nor locked, the Triggering(false)
    /// event (always, triggering or not), the EMF glow off, then the GotoState_Inactive event unless the trigger is
    /// in s_TemporaryInactive (or a state below it), which it keeps.
    /// </summary>
    [UcNative("ASObjectiveTrigger::execDesactivate", Exe = 0x10AE9C60, Editor = 0x111CE0F0)]
    [PortOf("nat:SBase.SObjectiveTrigger.Desactivate", Spec = "spec/objectives/objective-trigger.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AE8B50 (editor ASObjectiveTrigger::Desactivate 0x111CE1E0): flags &= ~(bActive|bLocked), eventTriggering(false), bEMFIsOn = false, GotoState_Inactive unless in state 0x3C3 (s_TemporaryInactive, natives2-events.md rule 4)")]
    public virtual void Desactivate() => DesactivateTrigger();

    /// <summary>The ASObjectiveTrigger::Desactivate body that the subclasses' natives call after their own part.</summary>
    protected void DesactivateTrigger()
    {
        DesactivateFlags();
        if (!IsInState(K.s_TemporaryInactive))
        {
            GotoState_Inactive();
        }
    }

    /// <summary>The flag part of Desactivate: not active, not locked, Triggering(false), EMF off.</summary>
    protected void DesactivateFlags()
    {
        bActive = false;
        bLocked = false;
        eventTriggering(false);
        bEMFIsOn = false;
    }
}
