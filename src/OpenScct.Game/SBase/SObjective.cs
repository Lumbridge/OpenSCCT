using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// One Story goal, complete as soon as one of its triggers is taken. Written from spec/objectives/missions.md.
public partial class SObjective
{
    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        ParentObjective = Live(ParentObjective);
    }

    [Uc("SBase.SObjective.PreBeginPlay", Sha1 = "bf3de1f612a1a63f78bca269e79fbebad54e61eb")]
    [PortOf("uc:SBase.SObjective:PreBeginPlay", Spec = "spec/objectives/missions.md#rules")]
    protected override void PreBeginPlay()
    {
        RemainingTime = K.ObjectiveRemainingTime;
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            if (trigger is not null)
            {
                trigger.Objective = this;
            }
        }
    }

    [Uc("SBase.SObjective.PostBeginPlay", Sha1 = "d406bdc274bbe791c0e37bf0c162b4461ceb46d2")]
    [PortOf("uc:SBase.SObjective:PostBeginPlay", Spec = "spec/objectives/missions.md#rules")]
    protected override void PostBeginPlay() => RemainingTime = K.ObjectiveRemainingTime;

    // ---- reset and deactivation -------------------------------------------------------------------------------

    /// <summary>Event form of Reset for native callers.</summary>
    [Uc("SBase.SObjective.DoReset", Sha1 = "19e33bddd3d57172161c8d5f551a14974c3c5ead")]
    [PortOf("uc:SBase.SObjective:DoReset", Spec = "spec/objectives/missions.md#rules")]
    public void DoReset() => Reset();

    /// <summary>A delayed reset (ScheduleReset) completes when the timer fires.</summary>
    [Uc("SBase.SObjective.Timer", Sha1 = "19e33bddd3d57172161c8d5f551a14974c3c5ead")]
    [PortOf("uc:SBase.SObjective:Timer", Spec = "spec/objectives/missions.md#rules")]
    protected override void Timer() => Reset();

    /// <summary>
    /// Puts the objective in play (rule 19). With an ActivationTime and no delayed activation yet, it only arms the
    /// timer (Timer calls Reset again). Otherwise every trigger gets DoReset, the objective is not taken and activated,
    /// RandomTriggers (when above 0 and below the trigger count) of the triggers picked at random stay active and the
    /// others are deactivated, and an objective with bShowStatusOnHud is added to the HUDs.
    /// </summary>
    [UcNative("ASObjective::execReset", Exe = 0x10A9F2E0, Editor = 0x11220210)]
    [PortOf("nat:SBase.SObjective.Reset", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10A9F300 (body of thunk 0x10A9F2E0)")]
    public override void Reset()
    {
        if (ActivationTime > 0 && !bDelayedActivation)
        {
            bDelayedActivation = true;
            SetTimer(ActivationTime, false);
            return;
        }

        bDelayedActivation = false;
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            trigger?.DoReset();
        }

        ObjectiveTaken = false;
        activated = true;
        if (RandomTriggers > 0 && RandomTriggers < Triggers.Count)
        {
            var others = new System.Collections.Generic.List<SObjectiveTrigger?>(Triggers);
            for (int i = 0; i < RandomTriggers; i++)
            {
                others.RemoveAt(Rand(others.Count));
            }

            foreach (SObjectiveTrigger? trigger in others)
            {
                trigger?.Desactivate();
            }
        }

        if (bShowStatusOnHud)
        {
            Ev_AddRemoveObjectiveOnClientHud(true);
        }
    }

    /// <summary>
    /// Takes the objective out of play (rule 20): it counts as taken (so it never blocks the end check), a pending
    /// delayed activation and the timer stop, its triggers are deactivated and the HUDs drop it.
    /// </summary>
    [UcNative("ASObjective::execDesactivate", Exe = 0x10AE9C60, Editor = 0x1121F9A0)]
    [PortOf("nat:SBase.SObjective.Desactivate", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10A9F5B0 (ASObjective vtable +0x138, called by the shared thunk 0x10AE9C60)")]
    public virtual void Desactivate()
    {
        bDelayedActivation = false;
        ObjectiveTaken = true;
        SetTimer(0f, false);
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            trigger?.Desactivate();
        }

        Ev_AddRemoveObjectiveOnClientHud(false);
    }

    /// <summary>Arms the objective's timer so that Timer() runs Reset() after <paramref name="Time"/> seconds.</summary>
    [UcNative("ASObjective::execScheduleReset", Exe = 0x10A9F280, Editor = 0x1121FA80)]
    [PortOf("nat:SBase.SObjective.ScheduleReset", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10A9F280: TimerRate = Time, TimerCounter = 0, loop flag cleared")]
    public void ScheduleReset(int Time) => SetTimer(Time, false);

    /// <summary>
    /// Called by every trigger capture change (HUD fields only). Clears iSimulatedObjectiveIsBeingTookUntil and
    /// MinTriggerTime, then keeps in MinTriggerTime the earliest positive end time of a running capture
    /// (<see cref="SObjectiveTrigger.GetTriggerTime"/>). When one runs, RemainingTime becomes the whole seconds
    /// from the local player's GRI ElapsedTime to it (never below 0). Alpha goes back to 1.
    /// </summary>
    [UcNative("ASObjective::execNotifyTriggering", Exe = 0x10A9F690, Editor = 0x1121FB40)]
    [PortOf("nat:SBase.SObjective.NotifyTriggering", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10A9F690: MinTriggerTime, RemainingTime = max(trunc(MinTriggerTime - LocalPC.GRI.ElapsedTime), 0), Alpha = 1")]
    public void NotifyTriggering()
    {
        iSimulatedObjectiveIsBeingTookUntil = 0f;
        MinTriggerTime = 0f;
        bool none = true;
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            if (trigger is not { bTriggering: true })
            {
                continue;
            }

            float end = trigger.GetTriggerTime();
            if (end > 0f)
            {
                none = false;
                if (end < MinTriggerTime || MinTriggerTime == 0f)
                {
                    MinTriggerTime = end;
                }
            }
        }

        // The native reads the local player's GRI without a guard; without one the count is left as it was.
        if (!none && (Level.LocalPlayerController?.GameReplicationInfo ?? Level.GRI) is { } gri)
        {
            int left = SvmMath.Trunc(MinTriggerTime - gri.ElapsedTime);
            RemainingTime = left > 0 ? left : 0;
        }

        Alpha = 1f;
    }

    // ---- completion -------------------------------------------------------------------------------------------

    /// <summary>
    /// The objective is complete (rules 23 to 29): its triggers stop, the game is told (which may end the match),
    /// the team and player counters move, the parent mission counts it, and the objective's Event fires.
    /// </summary>
    [Uc("SBase.SObjective.NotifyTaken", Sha1 = "0288ca7c0e9dc008cf304bf96899d672256c0383")]
    [PortOf("uc:SBase.SObjective:NotifyTaken", Spec = "spec/objectives/missions.md#rules")]
    public virtual void NotifyTaken(Controller? Scorer, SObjectiveTrigger? Trigger)
    {
        if (ObjectiveTaken)
        {
            return;
        }

        ObjectiveTaken = true;
        foreach (SObjectiveTrigger? t in Triggers)
        {
            t?.Desactivate();
        }

        IObjectiveHost host = ObjectiveNatives.Host(this);
        if (bScore)
        {
            host.AddPoint(Scorer, K.AP_A_TakeObjective);
        }

        host.NotifyObjectiveTaken(this);
        if (this is not SMission && Scorer is not null)
        {
            if (Scorer is SPlayerController pc && Level.GRI is { } gri && (int)pc.SPawnType < gri.Teams.Length &&
                gri.Teams[(int)pc.SPawnType] is STeamInfo team)
            {
                team.iNbObjectivesTaken++;
            }

            if (Scorer.PlayerReplicationInfo is { } pri)
            {
                pri.iNbObjectivesTaken++;
            }
        }

        ParentObjective?.NotifyChildTaken(Scorer);
        Pawn? instigator = Scorer?.Pawn;
        if (bUntriggerEvent)
        {
            UntriggerEvent(Event, this, instigator);
        }
        else
        {
            TriggerEvent(Event, this, instigator);
        }

        MinTriggerTime = 0f;
    }

    /// <summary>A child of this objective was taken; nothing for a plain objective (SMission counts its children).</summary>
    [Uc("SBase.SObjective.NotifyChildTaken", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SObjective:NotifyChildTaken", Spec = "spec/objectives/missions.md#rules")]
    public virtual void NotifyChildTaken(Controller? Scorer)
    {
    }

    // ---- HUD helpers ------------------------------------------------------------------------------------------

    /// <summary>Shows the objective as being taken for the first trigger's capture time (no script caller).</summary>
    [Uc("SBase.SObjective.SimulateBeingTook", Sha1 = "2910172148846bcfeb7bd321b92d7f821f50ca82")]
    [PortOf("uc:SBase.SObjective:SimulateBeingTook", Spec = "spec/objectives/missions.md#rules")]
    public void SimulateBeingTook()
    {
        float duration = Triggers.Count > 0 && Triggers[0] is { } first ? first.GetTriggerTime() : K.SimulateBeingTookFallback;
        iSimulatedObjectiveIsBeingTookUntil = Level.TimeSeconds + duration;
        if (bShowStatusOnHud)
        {
            AddRemoveObjectiveOnClientHud(true);
        }
    }

    [Uc("SBase.SObjective.Ev_AddRemoveObjectiveOnClientHud", Sha1 = "e16be4addf48cd6a1c1e527525cdcc3cdad8cebc")]
    [PortOf("uc:SBase.SObjective:Ev_AddRemoveObjectiveOnClientHud", Spec = "spec/objectives/missions.md#rules")]
    public void Ev_AddRemoveObjectiveOnClientHud(bool Add) => AddRemoveObjectiveOnClientHud(Add);

    /// <summary>
    /// Multicast (flag 0x01000000): on every machine the local player's HUD adds this objective to its draw list
    /// once, or removes every copy.
    /// </summary>
    [Uc("SBase.SObjective.AddRemoveObjectiveOnClientHud", Sha1 = "b7bbcd2094041be408488b85643b21bc3d815128")]
    [PortOf("uc:SBase.SObjective:AddRemoveObjectiveOnClientHud", Spec = "spec/objectives/missions.md#rules")]
    [PortOf("rep:SBase.SObjective.AddRemoveObjectiveOnClientHud", Spec = "spec/objectives/messages.md#network")]
    public void AddRemoveObjectiveOnClientHud(bool Add)
    {
        if (Level.LocalPlayerController?.myHUD is SHud hud)
        {
            ObjectiveHud.AddRemoveActorOnHud(hud, this, Add);
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            ObjectiveNatives.Net(this).AddRemoveObjectiveOnClientHud(this, Add);
        }
    }

    // ---- end check ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The objective clause of <c>SGameInfo.sCheckEndGame</c> (rule 31): true when every SObjective in the level,
    /// missions included, counts as taken. The game rules give the spies END_STORY when it holds while the match
    /// runs (after the time-limit test).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "missions.md rule 31: any SObjective with ObjectiveTaken false blocks the Story win")]
    public static bool AllObjectivesTaken(Level level)
    {
        foreach (SObjective objective in level.AllActors<SObjective>())
        {
            if (!objective.ObjectiveTaken)
            {
                return false;
            }
        }

        return true;
    }
}
