using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The SBasePawn inventory functions (spec/equipment/inventory-selection.md, loadouts.md): the gadget list
/// (MyGadget, then each gadget's MyGadget link), the selected gadget, the weapon, adding, removing, selecting and
/// cycling. They are SBasePawn units kept with the equipment port.
/// </summary>
public static class PawnInventory
{
    private static readonly ClassRef GadBombClass = new("SBase.SGadBomb");
    private static readonly ClassRef GadTorcheClass = new("SBase.SGadTorche");
    private static readonly ClassRef GadTorcheLaserClass = new("SBase.SGadTorcheLaser");
    private static readonly Name NAME_s_FallDropAttacked = new("s_FallDropAttacked");
    private static readonly Name NAME_s_LyingOnGround = new("s_LyingOnGround");
    private static readonly Name NAME_s_FallDown = new("s_FallDown");
    private static readonly Name NAME_s_StickyShock = new("s_StickyShock");
    private static readonly Name NAME_s_InteractWithObject = new("s_InteractWithObject");

    /// <summary>The class index used to resolve gadget and weapon classes.</summary>
    public static UcClassIndex IndexOf(Actor actor) => actor.XLevel?.GetService<UcClassIndex>() ?? DeviceClasses.Index;

    /// <summary>True when <paramref name="item"/>'s class is <paramref name="cls"/> or a subclass of it.</summary>
    public static bool IsA(Actor item, ClassRef cls)
    {
        if (cls.IsNone)
        {
            return false;
        }

        for (UcClassInfo? c = item.GetUcClass(); c is not null; c = c.Super)
        {
            if (string.Equals(c.Path, cls.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The selectable test: activatable, not a pad-up gadget, not a bomb (inventory rule 3).</summary>
    public static bool IsSelectable(SGadget g) => g.bActivatable && !g.IsAPadUpGadget && !IsA(g, GadBombClass);

    /// <summary>Appends a gadget to the pawn's list (refusing None and duplicates), owned by the pawn.</summary>
    [Uc("SBase.SBasePawn.AddGadget", Sha1 = "fe705c3f543561b459b1593f0b59d20dc9219da0")]
    [PortOf("uc:SBase.SBasePawn:AddGadget", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void AddGadget(SBasePawn Pawn, SGadget? NewGadget)
    {
        if (NewGadget is null)
        {
            return;
        }

        SGadget? last = null;
        for (SGadget? g = Pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (ReferenceEquals(g, NewGadget))
            {
                return;
            }

            last = g;
        }

        NewGadget.SetOwner(Pawn);
        NewGadget.MyGadget = null;
        if (last is null)
        {
            Pawn.MyGadget = NewGadget;
        }
        else
        {
            last.MyGadget = NewGadget;
        }

        Pawn.replicateGadgets = true;
    }

    /// <summary>Unlinks a gadget (clearing the selection when it was selected), drops its owner and destroys it.</summary>
    [Uc("SBase.SBasePawn.DeleteGadget", Sha1 = "f4a783bdccb57d202347765c2f5d57b5e9cd1f90")]
    [PortOf("uc:SBase.SBasePawn:DeleteGadget", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void DeleteGadget(SBasePawn Pawn, SGadget? Gadget)
    {
        if (Gadget is null)
        {
            return;
        }

        Unlink(Pawn, Gadget);
        Gadget.SetOwner(null);
        Gadget.Destroy();
    }

    /// <summary>Removes a gadget from the pawn's list without destroying it.</summary>
    public static void Unlink(SBasePawn Pawn, SGadget Gadget)
    {
        if (ReferenceEquals(Pawn.SelectedGadget, Gadget))
        {
            Pawn.SelectedGadget = null;
        }

        if (ReferenceEquals(Pawn.PadUpGadget, Gadget))
        {
            Pawn.PadUpGadget = null;
        }

        if (ReferenceEquals(Pawn.MyGadget, Gadget))
        {
            Pawn.MyGadget = Gadget.MyGadget;
            Gadget.MyGadget = null;
            return;
        }

        for (SGadget? g = Pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (ReferenceEquals(g.MyGadget, Gadget))
            {
                g.MyGadget = Gadget.MyGadget;
                Gadget.MyGadget = null;
                return;
            }
        }
    }

    /// <summary>Destroys the pawn's weapon.</summary>
    [Uc("SBase.SBasePawn.DeleteWeapon", Sha1 = "f7a7d1dbd0f292a6064ed6b387c0f5c10b2e1d89")]
    [PortOf("uc:SBase.SBasePawn:DeleteWeapon", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void DeleteWeapon(SBasePawn Pawn)
    {
        if (Pawn.MyWeapon is { } weapon)
        {
            Pawn.MyWeapon = null;
            weapon.Destroy();
        }
    }

    /// <summary>Deletes up to 16 gadgets, the weapon, and a merc's third-person weapon dummy (inventory rule 12).</summary>
    [Uc("SBase.SBasePawn.DeleteAllInventory", Sha1 = "ecb62d6f6fe3001b6dd06e189c1b8c160c568ae4")]
    [PortOf("uc:SBase.SBasePawn:DeleteAllInventory", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void DeleteAllInventory(SBasePawn Pawn)
    {
        for (int i = 0; i < K.GadgetDeleteLimit && Pawn.MyGadget is { } g; i++)
        {
            DeleteGadget(Pawn, g);
        }

        SWeapon? active = Pawn.MyWeapon;
        (Pawn.PrimaryWeapon as SWeaponDefense)?.Dummy?.Destroy();
        Pawn.SidearmWeapon?.Dummy?.Destroy();
        Pawn.PrimaryWeapon?.Destroy();
        if (!ReferenceEquals(Pawn.SidearmWeapon, Pawn.PrimaryWeapon)) Pawn.SidearmWeapon?.Destroy();
        Pawn.PrimaryWeapon = null;
        Pawn.SidearmWeapon = null;
        Pawn.ActiveWeaponSlot = 0;
        Pawn.WeaponSwitchUntil = 0;
        Pawn.WeaponFireLatch = false;
        Pawn.WeaponSwitching = false;
        Pawn.MyWeapon = active;
        DeleteWeapon(Pawn);
        if (Pawn is SPAWNDEFENSE merc)
        {
            merc.WeaponDummy?.Destroy();
            merc.WeaponDummy = null;
            merc.Torche = null;
            merc.TorcheLaser = null;
        }
    }

    /// <summary>The first selectable gadget of the list (MyGadget.SelectNext()).</summary>
    [Uc("SBase.SBasePawn.GetFirstGagdet", Sha1 = "05f9e7ba418fe30681752f904ef6de2e35113fc4")]
    [PortOf("uc:SBase.SBasePawn:GetFirstGagdet", Spec = "spec/equipment/inventory-selection.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.uc GetFirstGagdet: return MyGadget.SelectNext()")]
    public static SGadget? GetFirstGagdet(SBasePawn Pawn) => Pawn.MyGadget?.SelectNext();

    /// <summary>
    /// FindGadgetType (native): the pawn's first gadget of <paramref name="GadgetClass"/> or a subclass, or None.
    /// </summary>
    [UcNative("ASBasePawn::execFindGadgetType")]
    [PortOf("nat:SBase.SBasePawn.FindGadgetType", Spec = "spec/equipment/inventory-selection.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Native contract: first gadget of the class in list order")]
    public static SGadget? FindGadgetType(SBasePawn Pawn, ClassRef GadgetClass)
    {
        for (SGadget? g = Pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (IsA(g, GadgetClass))
            {
                return g;
            }
        }

        return null;
    }

    /// <summary>True in the states where gadget selection is refused (inventory rules 4 and 5).</summary>
    internal static bool SelectionRefused(SBasePawn Pawn, bool bMineWall)
    {
        if (Pawn.Controller is not SPlayerController pc)
        {
            return false;
        }

        return pc.IsInState(NAME_s_FallDropAttacked) || pc.IsInState(NAME_s_LyingOnGround) || pc.IsInState(NAME_s_FallDown) ||
            pc.IsInState(NAME_s_StickyShock) || (bMineWall && GadgetStates.IsPuttingMineOnWall(pc));
    }

    /// <summary>
    /// The next selectable gadget after the selection, wrapping to the first; refused when dead or knocked down
    /// (inventory rule 5). It does not Load the gadget.
    /// </summary>
    [Uc("SBase.SBasePawn.NextGadget", Sha1 = "01b8fe64fa1ab372b2de209d712352c249375b83")]
    [PortOf("uc:SBase.SBasePawn:NextGadget", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void NextGadget(SBasePawn Pawn)
    {
        if (Pawn.Health <= 0 || SelectionRefused(Pawn, true))
        {
            return;
        }

        if (Pawn.SelectedGadget is not { } current)
        {
            Pawn.SelectedGadget = Pawn.MyGadget?.SelectNext();
            return;
        }

        current.Unload();
        SGadget? next = current.MyGadget?.SelectNext() ?? Pawn.MyGadget?.SelectNext();
        Pawn.SelectedGadget = next;
        if (Pawn.Controller is PlayerController { myHUD: { } hud })
        {
            hud.bGadgetJustSelected = true;
            hud.LastSwitchGadget = hud.LastSwitchGadget + 1 > K.GadgetSlots - 1 ? 0 : hud.LastSwitchGadget + 1;
        }
    }

    /// <summary>
    /// The closest selectable gadget before the selection, else the last one after it (wrap), each scan at most 10
    /// items; the result is loaded (inventory rule 6).
    /// </summary>
    [Uc("SBase.SBasePawn.PrevGadget", Sha1 = "4edcb44b00884af214507a1345b70758bc2f3827")]
    [PortOf("uc:SBase.SBasePawn:PrevGadget", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void PrevGadget(SBasePawn Pawn)
    {
        if (Pawn.Controller is null || Pawn.Health <= 0 || SelectionRefused(Pawn, true))
        {
            return;
        }

        SGadget? current = Pawn.SelectedGadget ?? Pawn.MyGadget;
        SGadget? before = null;
        int n = 0;
        for (SGadget? g = Pawn.MyGadget; g is not null && !ReferenceEquals(g, current) && n < K.GadgetScanLimit; g = g.MyGadget, n++)
        {
            if (IsSelectable(g))
            {
                before = g;
            }
        }

        if (before is null)
        {
            n = 0;
            for (SGadget? g = current?.MyGadget; g is not null && n < K.GadgetScanLimit; g = g.MyGadget, n++)
            {
                if (IsSelectable(g))
                {
                    before = g;
                }
            }
        }

        if (before is null)
        {
            return;
        }

        Pawn.SelectedGadget?.Unload();
        Pawn.SelectedGadget = before;
        before.Load();
        if (Pawn.Controller is PlayerController { myHUD: { } hud })
        {
            hud.bGadgetJustSelected = true;
            hud.LastSwitchGadget = hud.LastSwitchGadget - 1 < 0 ? K.GadgetSlots - 1 : hud.LastSwitchGadget - 1;
        }
    }

    /// <summary>
    /// SelectGagdet(num) (an event used by code): the first selectable gadget, then num steps of NextGadget, then
    /// Load; only when num is below the team's slot count.
    /// </summary>
    [Uc("SBase.SBasePawn.SelectGagdet", Sha1 = "56781050c9d42e9252e9f0d4b7ee9537c1871e55")]
    [PortOf("uc:SBase.SBasePawn:SelectGagdet", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void SelectGagdet(SBasePawn Pawn, int num)
    {
        if (num < 0 || num >= TeamSlots(Pawn))
        {
            return;
        }

        SelectSlot(Pawn, num);
    }

    /// <summary>The selection steps shared by SelectGagdet and SPlayerController.SelectGadget.</summary>
    internal static void SelectSlot(SBasePawn Pawn, int num)
    {
        SGadget? slotGadget = null;
        bool hasCarry = false;
        int slotIndex = 0;
        for (SGadget? gadget = Pawn.MyGadget; gadget is not null; gadget = gadget.MyGadget)
        {
            hasCarry |= gadget is SidearmCarry;
            if (!IsSelectable(gadget)) continue;
            if (slotIndex++ == num) slotGadget = gadget;
        }
        if (hasCarry && (slotGadget is null || !IsSelectable(slotGadget))) return;
        SGadget? before = Pawn.SelectedGadget;
        before?.Unload();
        Pawn.SelectedGadget = hasCarry ? slotGadget : Pawn.MyGadget?.SelectNext();
        for (int i = 0; !hasCarry && i < num; i++)
        {
            NextGadget(Pawn);
        }

        Pawn.SelectedGadget?.Load();
        if (Pawn.Controller is PlayerController { myHUD: { } hud })
        {
            if (!ReferenceEquals(before, Pawn.SelectedGadget))
            {
                hud.bGadgetJustSelected = true;
            }

            hud.LastSwitchGadget = num;
        }
    }

    /// <summary>The team's gadget slot count (STeamInfo.iNBGadgets), 4 without a team.</summary>
    public static int TeamSlots(SBasePawn Pawn) =>
        (Pawn.PlayerReplicationInfo?.Team as STeamInfo ?? Pawn.Controller?.PlayerReplicationInfo?.Team as STeamInfo)?.iNBGadgets ??
        K.GadgetSlots;

    /// <summary>
    /// The generic pad-up switch (spies; inventory rule 10): the same class again unloads it, another class is found,
    /// loaded and used; refused when scoped with the laser class, while interacting, or for the torch in a vision mode.
    /// </summary>
    [Uc("SBase.SBasePawn.PadUpGadgetSwitch", Sha1 = "82d80e2118b635ecab59c767d0fc0b2c1062ea6d")]
    [PortOf("uc:SBase.SBasePawn:PadUpGadgetSwitch", Spec = "spec/equipment/inventory-selection.md#rules")]
    public static void PadUpGadgetSwitch(SBasePawn Pawn, ClassRef GadgetClass)
    {
        if (Pawn is SPAWNDEFENSE merc)
        {
            PawnWeapons.PadUpGadgetSwitch(merc, GadgetClass);
            return;
        }

        if (Pawn.Controller is SPlayerController pc)
        {
            if ((pc.bBinocularVision && GadgetClass == GadTorcheLaserClass) || pc.IsInState(NAME_s_InteractWithObject) ||
                (GadgetClass == GadTorcheClass && pc.Goggle is { CurrentMode: not SGoggle.REN_DynLight }))
            {
                return;
            }
        }

        if (Pawn.PadUpGadget is { } current && IsA(current, GadgetClass))
        {
            current.Unload();
            Pawn.PadUpGadget = null;
            return;
        }

        Pawn.PadUpGadget?.Unload();
        Pawn.PadUpGadget = FindGadgetType(Pawn, GadgetClass);
        if (Pawn.PadUpGadget is { } found)
        {
            found.Load();
            found.Use(0f);
        }
    }

    /// <summary>
    /// SGadget.SelectNext (native): this gadget or the first selectable one after it along the list, or None.
    /// </summary>
    [UcNative("ASGadget::execSelectNext", Exe = 0x10a9f970)]
    [PortOf("nat:SBase.SGadget.SelectNext", Spec = "spec/equipment/gadget-natives.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "gadget-natives.md rule 5: bActivatable, not IsAPadUpGadget, not an SGadBomb, along MyGadget")]
    public static SGadget? SelectNext(SGadget From)
    {
        for (SGadget? g = From; g is not null; g = g.MyGadget)
        {
            if (IsSelectable(g))
            {
                return g;
            }
        }

        return null;
    }
}
