using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A group of objectives with its own completion rule, written from spec/objectives/missions.md.
public partial class SMission
{
    [Uc("SBase.SMission.PostNetBeginPlay", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SMission:PostNetBeginPlay", Spec = "spec/objectives/missions.md#rules")]
    protected override void PostNetBeginPlay()
    {
    }

    // ---- reset ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts the mission in play (rules 6 to 14, DiskHunt rule 34): announce it, count and activate its children
    /// (chained, a random subset, or all), clamp to MinimumObjectives, add to the GRI count, prepare the devices and
    /// fire AtResetEvent.
    /// </summary>
    [Uc("SBase.SMission.Reset", Sha1 = "d8edb92e076d8eb9f56670dace1fb3dbc479d811")]
    [PortOf("uc:SBase.SMission:Reset", Spec = "spec/objectives/missions.md#rules")]
    public override void Reset()
    {
        if (GameMode == EGameMode.GM_DiskHunt)
        {
            ResetDiskHunt();
            return;
        }

        if (GameMode != EGameMode.GM_Undefined)
        {
            ActivationTime = 0;
        }

        if (ActivationTime > 0 && !bDelayedActivation)
        {
            bDelayedActivation = true;
            ScheduleReset(ActivationTime);
            return;
        }

        ObjectiveMessages.BroadcastTeam(this, K.TeamSpies, K.MsgMissionSpy, this);
        ObjectiveMessages.BroadcastTeam(this, K.TeamMercs, K.MsgMissionMerc, this);
        foreach (SObjective? child in Objectives)
        {
            if (child is not null)
            {
                child.ParentObjective = this;
            }
        }

        ObjectiveTaken = false;
        activated = true;

        ObjectivesLeft = 0;
        foreach (SObjective? child in Objectives)
        {
            if (child is not SMission)
            {
                ObjectivesLeft++;
            }
        }

        foreach (SObjective? child in Objectives)
        {
            if (child is SMission sub)
            {
                sub.ComputeObjectivesLeft();
            }
        }

        int n = Objectives.Count;
        if (bChained)
        {
            if (n > 0)
            {
                Objectives[0]?.Reset();
            }

            for (int i = 1; i < n; i++)
            {
                Objectives[i]?.Desactivate();
            }

            CurrentObjectiveIndex = 0;
            if (n > 0 && Objectives[0] is { } first && first is not SMission && GameMode != EGameMode.GM_Undefined)
            {
                ObjectiveMessages.BroadcastTeam(this, K.TeamSpies, K.MsgMissionSpy, first);
                ObjectiveMessages.BroadcastTeam(this, K.TeamMercs, K.MsgMissionMerc, first);
            }
        }
        else if (RandomObjectives > 0 && RandomObjectives < n)
        {
            var remaining = new List<SObjective?>(Objectives);
            for (int k = 0; k < n - RandomObjectives; k++)
            {
                remaining.RemoveAt(Rand(remaining.Count));
                ObjectivesLeft--;
            }

            foreach (SObjective? child in remaining)
            {
                child?.Reset();
            }
        }
        else
        {
            foreach (SObjective? child in Objectives)
            {
                child?.Reset();
            }
        }

        if (!bChained && MinimumObjectives > 0 && MinimumObjectives < ObjectivesLeft)
        {
            ObjectivesLeft = MinimumObjectives;
        }

        TotalObjectivesLeft = GetTotalObjectivesLeft();
        if (GameMode != EGameMode.GM_Undefined)
        {
            if (Level.GRI is { } gri)
            {
                gri.NumObjectives += TotalObjectivesLeft;
            }

            PrepareObjectives();
        }

        TriggerEvent(AtResetEvent, this, null);
    }

    // DiskHunt reset (rule 34; not in the slice).
    private void ResetDiskHunt()
    {
        if (MinimumObjectives > 0)
        {
            ActivateDisks(MinimumObjectives);
        }
        else
        {
            foreach (SObjectiveTrigger? trigger in Triggers)
            {
                trigger?.DoReset();
            }
        }

        ComputeObjectivesLeft();
        TotalObjectivesLeft = GetTotalObjectivesLeft();
        if (Level.GRI is { } gri)
        {
            gri.NumObjectives += TotalObjectivesLeft;
        }

        ObjectiveTaken = false;
        TriggerEvent(AtResetEvent, this, null);
    }

    // ---- counters ---------------------------------------------------------------------------------------------

    /// <summary>The count of a nested mission before its own reset (rule 15; not the same formula as Reset's).</summary>
    [Uc("SBase.SMission.ComputeObjectivesLeft", Sha1 = "5a9901bb7319082072121dbafa9edc5ff33fe7fa")]
    [PortOf("uc:SBase.SMission:ComputeObjectivesLeft", Spec = "spec/objectives/missions.md#rules")]
    public void ComputeObjectivesLeft()
    {
        if (GameMode == EGameMode.GM_DiskHunt)
        {
            ObjectivesLeft = CountActiveTriggers();
            return;
        }

        int n = Objectives.Count;
        if (!bChained && RandomObjectives > 0 && RandomObjectives < n)
        {
            ObjectivesLeft = RandomObjectives;
        }
        else if (!bChained && MinimumObjectives > 0 && MinimumObjectives < n)
        {
            ObjectivesLeft = MinimumObjectives;
        }
        else
        {
            ObjectivesLeft = n;
        }

        foreach (SObjective? child in Objectives)
        {
            if (child is SMission sub)
            {
                ObjectivesLeft--;
                sub.ComputeObjectivesLeft();
            }
        }
    }

    /// <summary>Own count plus every nested mission's total (rule 16).</summary>
    [Uc("SBase.SMission.GetTotalObjectivesLeft", Sha1 = "fcc7eb9df9c99501c31b31253899e184d31cf607")]
    [PortOf("uc:SBase.SMission:GetTotalObjectivesLeft", Spec = "spec/objectives/missions.md#rules")]
    public int GetTotalObjectivesLeft()
    {
        if (GameMode == EGameMode.GM_DiskHunt)
        {
            return CountActiveTriggers();
        }

        int total = ObjectivesLeft;
        foreach (SObjective? child in Objectives)
        {
            if (child is SMission sub)
            {
                total += sub.GetTotalObjectivesLeft();
            }
        }

        return total;
    }

    /// <summary>Refreshes the total here and in every parent mission (rule 17; event raised by native code).</summary>
    [Uc("SBase.SMission.UpdateObjectivesLeft", Sha1 = "f2680d3f60d4c23e8f5b3e23f642c3651a29488f")]
    [PortOf("uc:SBase.SMission:UpdateObjectivesLeft", Spec = "spec/objectives/missions.md#rules")]
    public void UpdateObjectivesLeft()
    {
        TotalObjectivesLeft = GetTotalObjectivesLeft();
        if (ParentObjective is SMission parent)
        {
            parent.UpdateObjectivesLeft();
        }
    }

    /// <summary>Prepares the devices of every child's triggers, nested missions included (rule 18).</summary>
    [Uc("SBase.SMission.PrepareObjectives", Sha1 = "b970d4ba1c1d64ed4d355bd84a72311912ea98df")]
    [PortOf("uc:SBase.SMission:PrepareObjectives", Spec = "spec/objectives/missions.md#rules")]
    public void PrepareObjectives()
    {
        foreach (SObjective? child in Objectives)
        {
            if (child is SMission sub)
            {
                sub.PrepareObjectives();
            }
            else if (child is not null)
            {
                foreach (SObjectiveTrigger? trigger in child.Triggers)
                {
                    trigger?.Prepare();
                }
            }
        }
    }

    /// <summary>Announces child <paramref name="i"/> to both teams (event raised by native code, rule 37).</summary>
    [Uc("SBase.SMission.DoBroadcastLocalizedMessage", Sha1 = "1082e72fbf65aab5fa8f3674736bdc146462d417")]
    [PortOf("uc:SBase.SMission:DoBroadcastLocalizedMessage", Spec = "spec/objectives/missions.md#rules")]
    public void DoBroadcastLocalizedMessage(int i)
    {
        if (i >= 0 && i < Objectives.Count && Objectives[i] is { } child && child is not SMission &&
            GameMode != EGameMode.GM_Undefined)
        {
            ObjectiveMessages.BroadcastTeam(this, K.TeamSpies, K.MsgMissionSpy, child);
            ObjectiveMessages.BroadcastTeam(this, K.TeamMercs, K.MsgMissionMerc, child);
        }
    }

    // ---- completion -------------------------------------------------------------------------------------------

    /// <summary>
    /// A child was taken (rule 30). When the child at CurrentObjectiveIndex is not a mission, one fewer is left
    /// (never below 0; an unchained mission keeps index 0, so it is always the first child that is tested); the
    /// totals are refreshed. Chained: the next child is reset and announced, and the mission is taken after the
    /// last one. Unchained: the mission is taken once every child counts as taken, or once MinimumObjectives (when
    /// above 0) of them do. Taking the mission zeroes ObjectivesLeft. There is no DiskHunt branch: a DiskHunt
    /// mission has no children, so it is taken at once (and SMission.NotifyTaken checks the disks).
    /// </summary>
    [UcNative("ASMission::execNotifyChildTaken", Exe = 0x10ABFFB0, Editor = 0x112203F0)]
    [PortOf("nat:SBase.SMission.NotifyChildTaken", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABFFB0: ObjectivesLeft clamp, chained advance, unchained taken-children count against Objectives.Num and MinimumObjectives")]
    public override void NotifyChildTaken(Controller? Scorer)
    {
        if (ObjectiveTaken)
        {
            return;
        }

        // The native reads Objectives[CurrentObjectiveIndex] without a bounds check; an index past the end reads as
        // "not a mission" here.
        SObjective? current = CurrentObjectiveIndex >= 0 && CurrentObjectiveIndex < Objectives.Count ? Objectives[CurrentObjectiveIndex] : null;
        if (current is not SMission)
        {
            ObjectivesLeft = ObjectivesLeft - 1 >= 1 ? ObjectivesLeft - 1 : 0;
        }

        UpdateObjectivesLeft();
        if (bChained)
        {
            CurrentObjectiveIndex++;
            if (CurrentObjectiveIndex < Objectives.Count)
            {
                Objectives[CurrentObjectiveIndex]?.DoReset();
                DoBroadcastLocalizedMessage(CurrentObjectiveIndex);
                return;
            }

            NotifyTaken(Scorer, null);
            ObjectivesLeft = 0;
            return;
        }

        int taken = 0;
        foreach (SObjective? child in Objectives)
        {
            if (child is { ObjectiveTaken: true })
            {
                taken++;
            }
        }

        if (taken == Objectives.Count || (MinimumObjectives <= taken && MinimumObjectives > 0))
        {
            NotifyTaken(Scorer, null);
            ObjectivesLeft = 0;
        }
    }

    /// <summary>
    /// The mission is complete: its children are deactivated, then it is taken like an objective (no score and no
    /// team counters for a mission). DiskHunt first checks the disk counts (rule 34).
    /// </summary>
    [Uc("SBase.SMission.NotifyTaken", Sha1 = "00e093de328509fc1c5b5c50ff6c36e156d0d276")]
    [PortOf("uc:SBase.SMission:NotifyTaken", Spec = "spec/objectives/missions.md#rules")]
    public override void NotifyTaken(Controller? Scorer, SObjectiveTrigger? Trigger)
    {
        if (GameMode == EGameMode.GM_DiskHunt)
        {
            UpdateObjectivesLeft();
            int active = CountActiveTriggers();
            // SPersistentData(SGameInfo(Level.Game).GetPersistentData()).NumDisksToWin: the host's setting.
            int toWin = Level.Game is SGameInfo game ? game.PersistentData.NumDisksToWin : Level.GRI?.NumDisksToWin ?? 0;
            if (toWin > 0 ? MinimumObjectives - active < toWin : active > 0)
            {
                return;
            }
        }

        foreach (SObjective? child in Objectives)
        {
            child?.Desactivate();
        }

        base.NotifyTaken(Scorer, Trigger);
    }

    /// <summary>Takes the mission and every child out of play (rule 20).</summary>
    [UcNative("ASMission::execDesactivate", Exe = 0x10AE9C60, Editor = 0x11220290)]
    [PortOf("nat:SBase.SMission.Desactivate", Spec = "spec/objectives/missions.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AC0150 (ASMission vtable +0x138): the objective version, ObjectiveTaken, then every child's Desactivate")]
    public override void Desactivate()
    {
        base.Desactivate();
        foreach (SObjective? child in Objectives)
        {
            child?.Desactivate();
        }
    }

    /// <summary>
    /// DiskHunt (spec/objectives/disk-hunt.md rule 3): every disk leaves play, then <paramref name="number"/> disks
    /// are put back one group at a time, a random still inactive disk of each group in turn, so they spread evenly
    /// over the groups. The pick uses the game's random stream where the original uses the C runtime rand().
    /// </summary>
    [UcNative("ASMission::execActivateDisks", Exe = 0x10AC01A0, Editor = 0x112207B0)]
    [PortOf("nat:SBase.SMission.ActivateDisks", Spec = "spec/objectives/disk-hunt.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASMission::execActivateDisks: bActive cleared, groups by SDisk.Group (10 slots), round robin, rand() % count, DoReset")]
    [Provenance(ProvenanceKind.Estimated, "Random source: the sim stream instead of rand(); a non-disk trigger counts as group 0; a full round without a new activation stops the loop")]
    public void ActivateDisks(int number)
    {
        var groups = new List<int>(Modes.K.MaxDiskGroups);
        int present = 0;
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            if (trigger is null)
            {
                continue;
            }

            int group = DiskGroupOf(trigger);
            if (!groups.Contains(group) && groups.Count < Modes.K.MaxDiskGroups)
            {
                groups.Add(group);
            }

            present++;
            trigger.bActive = false;
        }

        number = SvmMath.Min(number, present);
        int activated = 0;
        int slot = 0;
        bool roundActivated = false;
        while (activated < number && groups.Count > 0)
        {
            int group = groups[slot];
            int inactive = 0;
            foreach (SObjectiveTrigger? trigger in Triggers)
            {
                if (trigger is { bActive: false } && DiskGroupOf(trigger) == group)
                {
                    inactive++;
                }
            }

            int pick = inactive != 0 ? Rand(inactive) : 0;
            int seen = 0;
            foreach (SObjectiveTrigger? trigger in Triggers)
            {
                if (trigger is not { bActive: false } || DiskGroupOf(trigger) != group)
                {
                    continue;
                }

                if (seen++ == pick)
                {
                    trigger.DoReset();
                    activated++;
                    roundActivated = true;
                    break;
                }
            }

            if (++slot >= groups.Count)
            {
                slot = 0;
                if (!roundActivated)
                {
                    break;
                }

                roundActivated = false;
            }
        }
    }

    private static int DiskGroupOf(SObjectiveTrigger trigger) => trigger is SDisk disk ? disk.Group : 0;

    private int CountActiveTriggers()
    {
        int count = 0;
        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            if (trigger is { bActive: true })
            {
                count++;
            }
        }

        return count;
    }
}
