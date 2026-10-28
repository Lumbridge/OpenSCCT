using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A player's Use menu, written from spec/objectives/interaction-system.md. PostBeginPlay puts the five built-in entries
// (priority 999999) in slots 0 to 4; touched objects are inserted ahead of them in iPriority order (lowest first, ties
// in touch order), so the slot order is the menu order: the HUD lists the usable slots top to bottom and the selection
// walks them.
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

    /// <summary>
    /// Server: inserts a touched object into the menu, kept in iPriority order (game 0x10ABF4C0). The object goes
    /// before the first of the first ten entries whose priority is strictly higher, so equal priorities keep touch
    /// order and the five built-in entries (priority 999999) stay at the end, Exit last. An object already listed
    /// before that point, or with no higher entry among the first ten, is not added. The owning client is told the
    /// slot, then the selection goes back to the first usable entry.
    /// </summary>
    [UcNative("ASInteractionManager::execAddInteractionObj", Exe = 0x10ABF470, Editor = 0x112229F0)]
    [PortOf("nat:SBase.SInteractionManager.AddInteractionObj", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABF4C0: scan slots 0..9 while < NbObjects; same object: stop; new iPriority < slot iPriority: insert there (0x10ABF400), Ev_AddObjetOnClient unless the owner is local, SelectFirstViewAbleItem (0x10ABEE10)")]
    public void AddInteractionObj(SInteractObject? Object)
    {
        if (Object is null)
        {
            return;
        }

        for (int i = 0; i < NbObjects && i < Interactions.Length; i++)
        {
            if (i > K.MenuInsertLastSlot)
            {
                return;
            }

            SInteractObject? slot = Interactions[i];
            if (ReferenceEquals(slot, Object))
            {
                return;
            }

            if (slot is null || Object.iPriority < slot.iPriority)
            {
                InsertAt(Object, i);
                Ev_AddObjetOnClient(Object, i);
                SelectFirstViewAbleItem();
                return;
            }
        }
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

    /// <summary>
    /// Inserts an object at a slot, moving the later entries down one (the client side of AddObjetOnClient). The
    /// server's own manager is skipped when the slot already holds the object: the original does not send the call
    /// to a local owner, while the port's net layer runs it locally.
    /// </summary>
    [UcNative("ASInteractionManager::execAddObjectAtIndex", Exe = 0x10ABF390)]
    [PortOf("nat:SBase.SInteractionManager.AddObjectAtIndex", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABF390 calls the insert at 0x10ABF400")]
    public void AddObjectAtIndex(SInteractObject? Object, int i)
    {
        if (i >= 0 && i < NbObjects && i < Interactions.Length && ReferenceEquals(Interactions[i], Object))
        {
            return;
        }

        InsertAt(Object, i);
    }

    /// <summary>
    /// Removes an object and closes the gap (game 0x10ABF5F0). A removal at or before the selection moves it up one,
    /// not below slot 0.
    /// </summary>
    [UcNative("ASInteractionManager::execRemoveObject", Exe = 0x10ABF5A0)]
    [PortOf("nat:SBase.SInteractionManager.RemoveObject", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABF5F0: shift later slots down, clear the last, index <= Selected: Selected-- (min 0), NbObjects--")]
    public void RemoveObject(SInteractObject? Object)
    {
        int index = Object is null ? -1 : IndexOf(Object);
        if (index < 0)
        {
            return;
        }

        int last = index;
        for (; last < NbObjects - 1; last++)
        {
            Interactions[last] = Interactions[last + 1];
        }

        Interactions[last] = null;
        if (index <= SelectedInteractions)
        {
            SelectedInteractions = Math.Max(SelectedInteractions - 1, 0);
        }

        NbObjects--;
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

    /// <summary>
    /// Selects slot 0 when it is usable, otherwise the next usable slot (game 0x10ABEE10); the selected entry gets
    /// the focus. Always true.
    /// </summary>
    [UcNative("ASInteractionManager::execSelectFirstViewAbleItem", Exe = 0x10ABEDF0, Editor = 0x11222970)]
    [PortOf("nat:SBase.SInteractionManager.SelectFirstViewAbleItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABEE10: Selected = Sub = 0; slot 0 CanUse or SelectNextItem; SetFocus(true) on the selection unless it has bHasFocus")]
    public bool SelectFirstViewAbleItem()
    {
        SelectedInteractions = 0;
        SelectedSubInteractions = 0;
        if (Slot(0) is not { } first || !first.CanUse(PlayerOwner))
        {
            SelectNextItem();
        }

        if (Slot(SelectedInteractions) is { bHasFocus: false } selected)
        {
            selected.bHasFocus = true;
            selected.SetFocus(true);
        }

        return true;
    }

    /// <summary>
    /// Moves the selection to the next usable slot, wrapping at the end (game 0x10ABEED0); the old entry loses the
    /// focus and the new one gets it. Always true.
    /// </summary>
    [UcNative("ASInteractionManager::execSelectNextItem", Exe = 0x10ABEEB0)]
    [PortOf("nat:SBase.SInteractionManager.SelectNextItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABEED0: slot order, wrapping, at most NbObjects steps")]
    public bool SelectNextItem() => Step(forward: true);

    /// <summary>Moves the selection to the previous usable slot, wrapping at the start (game 0x10ABEFF0). Always true.</summary>
    [UcNative("ASInteractionManager::execSelectPreviousItem", Exe = 0x10ABEFF0)]
    [PortOf("nat:SBase.SInteractionManager.SelectPreviousItem", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ABEFF0: slot order, wrapping, at most NbObjects steps")]
    public bool SelectPreviousItem() => Step(forward: false);

    /// <summary>0 for the same object, 3 when <paramref name="CurrentObj"/>'s priority is not above <paramref name="NewObj"/>'s, else 1.</summary>
    [UcNative("ASInteractionManager::execCheckPriority", Exe = 0x10ABF130, Editor = 0x11221D40)]
    [PortOf("nat:SBase.SInteractionManager.CheckPriority", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x11221D40: same object 0, else (Current.iPriority <= New.iPriority) * 2 + 1; no script caller")]
    public int CheckPriority(SInteractObject? CurrentObj, SInteractObject? NewObj)
    {
        if (ReferenceEquals(CurrentObj, NewObj))
        {
            return 0;
        }

        return (CurrentObj?.iPriority ?? 0) <= (NewObj?.iPriority ?? 0) ? 3 : 1;
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

    private SInteractObject? Slot(int i) => i >= 0 && i < Interactions.Length ? Interactions[i] : null;

    // Game 0x10ABF400: insert at slot i moving the later entries down. A full list drops the entry in slot 13 and
    // keeps the last one (Exit); slot 14 itself takes no insert.
    private void InsertAt(SInteractObject? Object, int i)
    {
        int max = Interactions.Length;
        if (i < 0 || i >= max - 1)
        {
            return;
        }

        int last = NbObjects >= max ? max - 3 : NbObjects - 1;
        for (int k = last; k >= i; k--)
        {
            Interactions[k + 1] = Interactions[k];
        }

        NbObjects = Math.Clamp(NbObjects + 1, 0, max);
        Interactions[i] = Object;
    }

    // Game 0x10ABEED0 / 0x10ABEFF0: unfocus the selection, then walk the slots (wrapping) to the first usable one
    // and focus it; after NbObjects steps without one the selection stays where the walk ended.
    private bool Step(bool forward)
    {
        if (Slot(SelectedInteractions) is { } current)
        {
            current.SetFocus(false);
            current.bHasFocus = false;
        }

        SPlayerController? pc = PlayerOwner;
        for (int n = 0; n < NbObjects;)
        {
            n++;
            if (forward)
            {
                SelectedInteractions = SelectedInteractions < NbObjects - 1 ? SelectedInteractions + 1 : 0;
            }
            else
            {
                SelectedInteractions = SelectedInteractions < 1 ? NbObjects - 1 : SelectedInteractions - 1;
            }

            if (Slot(SelectedInteractions) is { bDeleteMe: false } e && e.CanUse(pc))
            {
                e.SetFocus(true);
                e.bHasFocus = true;
                return true;
            }
        }

        return true;
    }
}
