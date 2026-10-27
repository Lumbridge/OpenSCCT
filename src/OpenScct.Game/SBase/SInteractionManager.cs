using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A player's Use menu, written from spec/objectives/interaction-system.md. Slots 0 to 4 hold the fixed entries,
// touched objects follow. The menu shows the usable entries ordered by iPriority (lowest first), ties in slot order.
public partial class SInteractionManager
{
    /// <summary>The player this menu belongs to (its owner).</summary>
    public SPlayerController? PlayerOwner => Owner as SPlayerController;

    [Uc("SBase.SInteractionManager.PostBeginPlay", Sha1 = "4cadbefce7da2d879ab186872e5849f07f1ce475")]
    [PortOf("uc:SBase.SInteractionManager:PostBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostBeginPlay()
    {
        Actor? controller = Owner;
        Interactions[4] = ObjectiveNatives.Spawn<SLeaveInteraction>(this, this, default, default);
        Interactions[3] = ObjectiveNatives.Spawn<SShareObjectInteraction>(this, controller, default, default);
        Interactions[2] = ObjectiveNatives.Spawn<SHealMateInteraction>(this, controller, default, default);
        Interactions[1] = ObjectiveNatives.Spawn<SRemoveSpyBulletInteraction>(this, controller, default, default);
        Interactions[0] = ObjectiveNatives.Spawn<SBackPackInteraction>(this, controller, default, default);
        NbObjects += K.FixedEntries;
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < Interactions.Length; i++)
        {
            Interactions[i] = Live(Interactions[i]);
        }
    }

    // ---- list upkeep ----------------------------------------------------------------------------------------

    /// <summary>Server: appends a touched object to the menu and mirrors it to the owning client.</summary>
    [UcNative("ASInteractionManager::execAddInteractionObj", Exe = 0x10ABF470, Editor = 0x112229F0)]
    [PortOf("nat:SBase.SInteractionManager.AddInteractionObj", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "List upkeep: no duplicates, appended after the last entry, at most MAXITEM")]
    public void AddInteractionObj(SInteractObject? Object)
    {
        if (Object is null || IndexOf(Object) >= 0 || NbObjects >= MAXITEM)
        {
            return;
        }

        int i = NbObjects;
        Interactions[i] = Object;
        NbObjects = i + 1;
        Ev_AddObjetOnClient(Object, i);
    }

    /// <summary>Server: removes an object from the menu and mirrors the removal to the owning client.</summary>
    [UcNative("ASInteractionManager::execRemoveInteractionObj", Exe = 0x10ABF970)]
    [PortOf("nat:SBase.SInteractionManager.RemoveInteractionObj", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "List upkeep: remove then mirror")]
    public void RemoveInteractionObj(SInteractObject? Object)
    {
        if (Object is null || IndexOf(Object) < 0)
        {
            return;
        }

        RemoveObject(Object);
        Ev_RemoveInteractionObjOnClient(Object);
    }

    /// <summary>Places an object at a slot (the client side of AddObjetOnClient).</summary>
    [UcNative("ASInteractionManager::execAddObjectAtIndex", Exe = 0x10ABF390)]
    [PortOf("nat:SBase.SInteractionManager.AddObjectAtIndex", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Slot write that grows NbObjects to cover the slot")]
    public void AddObjectAtIndex(SInteractObject? Object, int i)
    {
        if (i < 0 || i >= MAXITEM)
        {
            return;
        }

        Interactions[i] = Object;
        if (i >= NbObjects)
        {
            NbObjects = i + 1;
        }
    }

    /// <summary>Removes an object and closes the gap; the selection keeps pointing at the same entry when it can.</summary>
    [UcNative("ASInteractionManager::execRemoveObject", Exe = 0x10ABF5A0)]
    [PortOf("nat:SBase.SInteractionManager.RemoveObject", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Removal shifts the later slots down")]
    public void RemoveObject(SInteractObject? Object)
    {
        int index = Object is null ? -1 : IndexOf(Object);
        if (index < K.FixedEntries)
        {
            return;
        }

        for (int i = index; i < NbObjects - 1; i++)
        {
            Interactions[i] = Interactions[i + 1];
        }

        NbObjects--;
        Interactions[NbObjects] = null;
        if (SelectedInteractions > index)
        {
            SelectedInteractions--;
        }
        else if (SelectedInteractions >= NbObjects)
        {
            SelectedInteractions = NbObjects - 1;
        }
    }

    [Uc("SBase.SInteractionManager.Ev_AddObjetOnClient", Sha1 = "ea54c07d533d920682d1b12741a22792739ecd1d")]
    [PortOf("uc:SBase.SInteractionManager:Ev_AddObjetOnClient", Spec = "spec/objectives/interaction-system.md#rules")]
    public void Ev_AddObjetOnClient(SInteractObject? Obj, int i) => ObjectiveNatives.Net(this).AddObjetOnClient(this, Obj, i);

    /// <summary>Reliable client function: runs on the owning player's machine.</summary>
    [Uc("SBase.SInteractionManager.AddObjetOnClient", Sha1 = "67c62127d714fa92f187e32d6a669160a0319fae")]
    [PortOf("uc:SBase.SInteractionManager:AddObjetOnClient", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SInteractionManager.AddObjetOnClient", Spec = "spec/objectives/interaction-system.md#network")]
    public void AddObjetOnClient(SInteractObject? Obj, int i) => AddObjectAtIndex(Obj, i);

    [Uc("SBase.SInteractionManager.Ev_RemoveInteractionObjOnClient", Sha1 = "d899c61494efb88c65cd564f162d4d80de92fd4f")]
    [PortOf("uc:SBase.SInteractionManager:Ev_RemoveInteractionObjOnClient", Spec = "spec/objectives/interaction-system.md#rules")]
    public void Ev_RemoveInteractionObjOnClient(SInteractObject? Object) =>
        ObjectiveNatives.Net(this).RemoveInteractionObjOnClient(this, Object);

    /// <summary>Reliable client function: runs on the owning player's machine.</summary>
    [Uc("SBase.SInteractionManager.RemoveInteractionObjOnClient", Sha1 = "8fc0296a3ab92e1ec36a6f0a3458e51ac913af65")]
    [PortOf("uc:SBase.SInteractionManager:RemoveInteractionObjOnClient", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SInteractionManager.RemoveInteractionObjOnClient", Spec = "spec/objectives/interaction-system.md#network")]
    public void RemoveInteractionObjOnClient(SInteractObject? Object) => RemoveObject(Object);

    // ---- queries ----------------------------------------------------------------------------------------------

    [UcNative("ASInteractionManager::execGetNbInteractions", Exe = 0x10ABECF0)]
    [PortOf("nat:SBase.SInteractionManager.GetNbInteractions", Spec = "spec/objectives/interaction-system.md#rules")]
    public int GetNbInteractions() => NbObjects;

    /// <summary>True when an entry other than Exit is usable now, which is what opens the menu.</summary>
    [UcNative("ASInteractionManager::execHasUsableInteractions", Exe = 0x10ABED70, Editor = 0x11221790)]
    [PortOf("nat:SBase.SInteractionManager.HasUsableInteractions", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Any usable entry, Exit excluded (spec open question 5)")]
    public bool HasUsableInteractions()
    {
        SPlayerController? pc = PlayerOwner;
        for (int i = 0; i < NbObjects; i++)
        {
            if (Interactions[i] is { } e && e is not SLeaveInteraction && e.CanUse(pc))
            {
                return true;
            }
        }

        return false;
    }

    [UcNative("ASInteractionManager::execGetCurrentInteraction", Exe = 0x10ABED90)]
    [PortOf("nat:SBase.SInteractionManager.GetCurrentInteraction", Spec = "spec/objectives/interaction-system.md#rules")]
    public SInteractObject? GetCurrentInteraction() =>
        SelectedInteractions >= 0 && SelectedInteractions < NbObjects ? Live(Interactions[SelectedInteractions]) : null;

    [UcNative("ASInteractionManager::execGetCurrentSubInteraction", Exe = 0x10ABEDD0)]
    [PortOf("nat:SBase.SInteractionManager.GetCurrentSubInteraction", Spec = "spec/objectives/interaction-system.md#rules")]
    public int GetCurrentSubInteraction() => SelectedSubInteractions;

    /// <summary>Selects the first usable entry of the menu order. False when none is usable.</summary>
    [UcNative("ASInteractionManager::execSelectFirstViewAbleItem", Exe = 0x10ABEDF0, Editor = 0x11222970)]
    [PortOf("nat:SBase.SInteractionManager.SelectFirstViewAbleItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Lowest iPriority first (RE+ saw Use above Exit at a terminal); spec open question 1")]
    public bool SelectFirstViewAbleItem()
    {
        SelectedSubInteractions = 0;
        int first = FindAfter(int.MinValue, -1, forward: true);
        if (first < 0)
        {
            return false;
        }

        SelectedInteractions = first;
        return true;
    }

    /// <summary>Moves the selection to the next usable entry, wrapping at the end. False when it did not move.</summary>
    [UcNative("ASInteractionManager::execSelectNextItem", Exe = 0x10ABEEB0)]
    [PortOf("nat:SBase.SInteractionManager.SelectNextItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Menu order by iPriority, wrapping")]
    public bool SelectNextItem() => Step(forward: true);

    /// <summary>Moves the selection to the previous usable entry, wrapping at the start. False when it did not move.</summary>
    [UcNative("ASInteractionManager::execSelectPreviousItem", Exe = 0x10ABEFF0)]
    [PortOf("nat:SBase.SInteractionManager.SelectPreviousItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Menu order by iPriority, wrapping")]
    public bool SelectPreviousItem() => Step(forward: false);

    /// <summary>Positive when <paramref name="NewObj"/> comes before <paramref name="CurrentObj"/> in the menu.</summary>
    [UcNative("ASInteractionManager::execCheckPriority", Exe = 0x10ABF130, Editor = 0x11221D40)]
    [PortOf("nat:SBase.SInteractionManager.CheckPriority", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Compares iPriority, lower first")]
    public int CheckPriority(SInteractObject? CurrentObj, SInteractObject? NewObj)
    {
        if (CurrentObj is null || NewObj is null)
        {
            return NewObj is null ? 0 : 1;
        }

        return CurrentObj.iPriority.CompareTo(NewObj.iPriority);
    }

    /// <summary>Facing dot product and distance from the owner's pawn to <paramref name="Obj"/>.</summary>
    [UcNative("ASInteractionManager::execCalc", Exe = 0x10ABF1C0, Editor = 0x11221E10)]
    [PortOf("nat:SBase.SInteractionManager.Calc", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Pawn facing against the direction to the object, full 3D distance")]
    public void Calc(SInteractObject? Obj, out float Dot, out float dist)
    {
        Dot = 0f;
        dist = 0f;
        if (Obj is null || PlayerOwner?.Pawn is not { } pawn)
        {
            return;
        }

        FVector dir = Obj.Location - pawn.Location;
        dist = dir.Size();
        Dot = pawn.Rotation.Vector() | dir.SafeNormal();
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    /// <summary>The slot holding <paramref name="obj"/>, or -1.</summary>
    public int IndexOf(SInteractObject obj)
    {
        for (int i = 0; i < NbObjects; i++)
        {
            if (ReferenceEquals(Interactions[i], obj))
            {
                return i;
            }
        }

        return -1;
    }

    private bool Step(bool forward)
    {
        SInteractObject? current = GetCurrentInteraction();
        int next = current is null
            ? FindAfter(forward ? int.MinValue : int.MaxValue, forward ? -1 : int.MaxValue, forward)
            : FindAfter(current.iPriority, SelectedInteractions, forward);
        if (next < 0)
        {
            next = FindAfter(forward ? int.MinValue : int.MaxValue, forward ? -1 : int.MaxValue, forward);
        }

        if (next < 0 || next == SelectedInteractions)
        {
            return false;
        }

        SelectedInteractions = next;
        SelectedSubInteractions = 0;
        return true;
    }

    // The usable entry right after (forward) or before the (priority, slot) key in menu order, or -1.
    private int FindAfter(int priority, int slot, bool forward)
    {
        SPlayerController? pc = PlayerOwner;
        int best = -1;
        int bestPriority = 0;
        for (int i = 0; i < NbObjects; i++)
        {
            if (Interactions[i] is not { bDeleteMe: false } e || !e.CanUse(pc))
            {
                continue;
            }

            int p = e.iPriority;
            bool beyond = forward ? (p > priority || (p == priority && i > slot)) : (p < priority || (p == priority && i < slot));
            if (!beyond)
            {
                continue;
            }

            bool better = best < 0 ||
                (forward ? (p < bestPriority || (p == bestPriority && i < best)) : (p > bestPriority || (p == bestPriority && i > best)));
            if (better)
            {
                best = i;
                bestPriority = p;
            }
        }

        return best;
    }
}
