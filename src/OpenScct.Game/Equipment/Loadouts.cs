using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The spawn loadout (spec/equipment/loadouts.md): the owner's profile equipment with the team's gadget restrictions,
/// spawned on the server through the pawn's spawn calls, the first gadget selected, and the merc's pad-up gadgets.
/// Gadgets beyond the slice spawn as their classes with the SGadget base behaviour.
/// </summary>
public static class Loadouts
{
    private static readonly Name CNAME_UpdateInventory = new("UpdateInventory");
    private static readonly Name CNAME_ServerSpawnInventory = new("ServerSpawnInventory");
    private static readonly Name CNAME_ServerSpawnWeapon = new("ServerSpawnWeapon");
    private static readonly ClassRef GadBomb = new("SBase.SGadBomb");

    /// <summary>The spy's fixed equipment without a profile (rule 2.1).</summary>
    [Provenance(ProvenanceKind.Decoded, "loadouts.md rule 2.1: smoke, chaff, sticky cam, heartbeat, Shocker")]
    public static readonly ClassRef[] SpyFixedGadgets =
    [
        new("SBase.SGadSmokeGrenade"), new("SBase.SGadChaffeGrenade"), new("SBase.SGadStickyCam"), new("SBase.SGadHeartBeatSensor"),
    ];

    /// <summary>The merc's fixed equipment without a profile (rule 2.1).</summary>
    [Provenance(ProvenanceKind.Decoded, "loadouts.md rule 2.1: frag, mines, cam network, gas mask, rifle")]
    public static readonly ClassRef[] MercFixedGadgets =
    [
        new("SBase.SGadFragGrenade"), new("SBase.SGadMultiMinesLauncher"), new("SBase.SGadCamNetwork"), new("SBase.SGadGasMask"),
    ];

    [Provenance(ProvenanceKind.Decoded, "loadouts.md: ATTWeapClass SStickyShocker")]
    public static readonly ClassRef SpyWeapon = new("SBase.SStickyShocker");

    [Provenance(ProvenanceKind.Decoded, "loadouts.md: DEFWeapClass SWeaponDefense")]
    public static readonly ClassRef MercWeapon = new("SBase.SWeaponDefense");

    /// <summary>The bomb modes: spies also carry a bomb in Sabotage and Multi.</summary>
    public static bool IsBombMode(EGameMode Mode) => Mode is EGameMode.GM_Sabotage or EGameMode.GM_Multi;

    /// <summary>
    /// UpdateInventory (rule 2, server to owner): the profile's equipment with the restrictions applied, the weapon,
    /// the first gadget selected (a spy's bomb in bomb modes), skipping a selection that cannot be used.
    /// </summary>
    [Uc("SBase.SBasePawn.UpdateInventory", Sha1 = "7c94280b39ea244a1615cc17cb6a2e829a11de5a")]
    [PortOf("uc:SBase.SBasePawn:UpdateInventory", Spec = "spec/equipment/loadouts.md#rules")]
    public static void UpdateInventory(SBasePawn Pawn, EGameMode Mode)
    {
        if (Pawn.Role == ENetRole.ROLE_Authority)
        {
            OpenGrant(Pawn, Mode);
        }

        if (Pawn.Controller is SPlayerController { IsRemotePlayer: true } remote)
        {
            CombatServices.Net(Pawn).ClientCall(remote, new CombatCall(CNAME_UpdateInventory) { Target = Pawn, Int1 = (int)Mode });
            return;
        }

        bool spy = Pawn is SPawnAttaque;
        USPlayerProfile? profile = Pawn.XLevel?.GetService<IDeviceHooks>()?.GetPlayerProfile();
        bool tutorial = (Pawn.Controller as PlayerController)?.GameReplicationInfo is { bTutorMode: true };
        ILoadoutSource? source = profile is null && !tutorial && IsLocalOwner(Pawn) ? Pawn.XLevel?.GetService<ILoadoutSource>() : null;
        if (profile is null && source is null)
        {
            foreach (ClassRef g in spy ? SpyFixedGadgets : MercFixedGadgets)
            {
                ServerSpawnInventory(Pawn, g);
            }

            ServerSpawnWeapon(Pawn, spy ? SpyWeapon : MercWeapon);
            if (spy && IsBombMode(Mode))
            {
                ServerSpawnInventory(Pawn, GadBomb);
            }

            return;
        }

        ClassRef[] equipment;
        ClassRef weapon;
        int slots = PawnInventory.TeamSlots(Pawn);
        if (profile is null)
        {
            // The player's chosen gear (an OpenSCCT addition, spec/additions/gear-change.md): a copy, so the
            // restriction pass does not edit the saved choice.
            equipment = (ClassRef[])source!.Gadgets(spy).Clone();
            weapon = source.Weapon(spy);
            if (Team(Pawn) is { } chosenTeam)
            {
                ApplyRestrictions(equipment, chosenTeam);
                if (chosenTeam.ClassWeapon.Count > 0 && IndexOfClass(chosenTeam.ClassWeapon, weapon) < 0)
                {
                    weapon = chosenTeam.ClassWeapon[0];
                }
            }
        }
        else
        {
            equipment = spy ? profile.ATTEquipment : profile.DEFEquipment;
            weapon = spy ? profile.ATTWeapClass : profile.DEFWeapClass;
            if (tutorial && profile.GetUcClass().Create() is USPlayerProfile defaults)
            {
                equipment = spy ? defaults.ATTEquipment : defaults.DEFEquipment;
                weapon = spy ? defaults.ATTWeapClass : defaults.DEFWeapClass;
            }
            else if (Team(Pawn) is { } team)
            {
                ApplyRestrictions(equipment, team);
            }
        }

        for (int i = 0; i < slots && i < equipment.Length; i++)
        {
            if (!equipment[i].IsNone)
            {
                ServerSpawnInventory(Pawn, equipment[i]);
            }
        }

        // Request the sidearm before the primary completes the initial kit grant.
        if (!spy && SidearmEquipment.Enabled(Pawn) && source is not null && !source.Sidearm().IsNone)
            ServerSpawnInventory(Pawn, source.Sidearm());
        ServerSpawnWeapon(Pawn, weapon);
        PawnInventory.SelectGagdet(Pawn, 0);
        if (spy && IsBombMode(Mode))
        {
            ServerSpawnInventory(Pawn, GadBomb);
        }

        if (Pawn.SelectedGadget is { } selected && !PawnInventory.IsSelectable(selected))
        {
            PawnInventory.NextGadget(Pawn);
        }
    }

    /// <summary>The pawn's team info.</summary>
    public static STeamInfo? Team(SBasePawn Pawn) =>
        Pawn.PlayerReplicationInfo?.Team as STeamInfo ?? Pawn.Controller?.PlayerReplicationInfo?.Team as STeamInfo;

    /// <summary>A team option allowed by the host (a set bit of bDisallowedInv allows it, despite the name).</summary>
    public static bool IsAllowed(STeamInfo Team, int Option) => ((Team.bDisallowedInv >> Option) & 1) != 0;

    /// <summary>
    /// The restriction replacement (rule 2.3): a slot holding a disallowed option takes the first allowed option not
    /// already equipped, or is cleared when none is left. The array is edited in place (the in-memory profile).
    /// </summary>
    [PortOf("uc:SBase.SBasePawn:UpdateInventory", Spec = "spec/equipment/loadouts.md#rules")]
    public static void ApplyRestrictions(ClassRef[] Equipment, STeamInfo Team)
    {
        int options = SvmMath.Min(Team.ClassInventory.Count, K.TeamOptions);
        for (int o = 0; o < options; o++)
        {
            if (IsAllowed(Team, o))
            {
                continue;
            }

            ClassRef banned = Team.ClassInventory[o];
            for (int s = 0; s < Equipment.Length; s++)
            {
                if (Equipment[s] != banned)
                {
                    continue;
                }

                Equipment[s] = default;
                for (int r = 0; r < options; r++)
                {
                    ClassRef candidate = Team.ClassInventory[r];
                    if (IsAllowed(Team, r) && System.Array.IndexOf(Equipment, candidate) < 0)
                    {
                        Equipment[s] = candidate;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>The server spawns an equipment item at the pawn and gives it (reliable owner to server).</summary>
    [Uc("SBase.SBasePawn.ServerSpawnInventory", Sha1 = "6c393d3ddedf9252e33a8e303f80cfe7ac2d9d0d")]
    [PortOf("uc:SBase.SBasePawn:ServerSpawnInventory", Spec = "spec/equipment/loadouts.md#rules")]
    public static void ServerSpawnInventory(SBasePawn Pawn, ClassRef Class)
    {
        if (Pawn.Role < ENetRole.ROLE_Authority && Pawn.Controller is SPlayerController pc)
        {
            CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_ServerSpawnInventory) { Target = Pawn, Name1 = new Name(Class.Path ?? "") });
            return;
        }

        if (!TakeGrant(Pawn, Class, false))
        {
            return;
        }

        switch (DeviceClasses.Spawn(Pawn, Class, Pawn, default, Pawn.Location, Pawn.Rotation))
        {
            case SGadget gadget:
                gadget.GiveTo(Pawn);
                break;
            case SWeapon weapon:
                weapon.GiveTo(Pawn);
                break;
        }
    }

    /// <summary>The server spawns the weapon (the controller may substitute it) and gives it (reliable owner to server).</summary>
    [Uc("SBase.SBasePawn.ServerSpawnWeapon", Sha1 = "e72d99e5bf85c59a3faa39cc982c2ca917a67f05")]
    [PortOf("uc:SBase.SBasePawn:ServerSpawnWeapon", Spec = "spec/equipment/loadouts.md#rules")]
    public static void ServerSpawnWeapon(SBasePawn Pawn, ClassRef Class)
    {
        if (Pawn.Role < ENetRole.ROLE_Authority && Pawn.Controller is SPlayerController pc)
        {
            CombatServices.Net(Pawn).ServerCall(pc, new CombatCall(CNAME_ServerSpawnWeapon) { Target = Pawn, Name1 = new Name(Class.Path ?? "") });
            return;
        }

        if (!TakeGrant(Pawn, Class, true))
        {
            return;
        }

        ClassRef cls = Pawn.Controller is SPlayerController owner ? SpawnWeaponFor(owner, Class) : Class;
        if (DeviceClasses.Spawn(Pawn, cls, Pawn, default, Pawn.Location, Pawn.Rotation) is SWeapon weapon)
        {
            weapon.GiveTo(Pawn);
            Pawn.GearStartAmmo = weapon.Ammo;
            Pawn.GearStartClipAmmo = weapon is SWeaponDefense rifle ? rifle.ClipAmmo : 0;
        }
    }

    /// <summary>
    /// SpawnWeaponFor (rule 3): the class unchanged outside Merc Deathmatch; there the player's chosen respawn weapon
    /// (<see cref="SPlayerController.SpawnWeaponFor"/>, which the Use key cycles while he is dead).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:SpawnWeaponFor", Spec = "spec/equipment/loadouts.md#rules")]
    public static ClassRef SpawnWeaponFor(SPlayerController PC, ClassRef Class)
    {
        ClassRef chosen = PC.SpawnWeaponFor(Class);
        return chosen.IsNone ? Class : chosen;
    }

    /// <summary>True when the pawn's controller is the player of this machine (not a remote player or a bot).</summary>
    private static bool IsLocalOwner(SBasePawn Pawn) =>
        Pawn.Controller is PlayerController { IsRemotePlayer: false } pc && ReferenceEquals(Pawn.Level?.GetLocalPlayerController(), pc);

    /// <summary>The index of <paramref name="Class"/> in <paramref name="List"/> (by class path), or -1.</summary>
    public static int IndexOfClass(IReadOnlyList<ClassRef> List, ClassRef Class)
    {
        for (int i = 0; i < List.Count; i++)
        {
            if (string.Equals(List[i].Path, Class.Path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The server opens the pawn's kit grant when it asks for a kit (an OpenSCCT addition,
    /// spec/additions/gear-change.md): for <see cref="GearK.GrantSeconds"/> it takes the team's gadget slots, one
    /// weapon, and a spy's bomb in bomb modes, and nothing else.
    /// </summary>
    public static void OpenGrant(SBasePawn Pawn, EGameMode Mode)
    {
        Pawn.GearGrantUntil = Pawn.Level.TimeSeconds + GearK.GrantSeconds;
        Pawn.GearGrantGadgets = Team(Pawn) is null ? K.TeamOptions : PawnInventory.TeamSlots(Pawn);
        Pawn.GearGrantWeapon = true;
        Pawn.GearGrantSidearm = Pawn is SPAWNDEFENSE && SidearmEquipment.Enabled(Pawn);
        Pawn.GearGrantBomb = Pawn is SPawnAttaque && IsBombMode(Mode);
    }

    /// <summary>
    /// Whether the server takes a spawn request under the pawn's open grant: a weapon from the team's list, or an
    /// allowed team option the pawn does not carry yet, within the grant's counts. A tutorial map takes any class.
    /// </summary>
    private static bool TakeGrant(SBasePawn Pawn, ClassRef Class, bool Weapon)
    {
        if (Class.IsNone)
        {
            return false;
        }
        if (Pawn.Level.TimeSeconds > Pawn.GearGrantUntil)
        {
            // Cold render/content loading can outlive the initial owner-to-server kit exchange. An unarmed
            // living pawn may finish that first grant; the one-weapon and gadget counts still apply.
            if (!Pawn.GearGrantWeapon || Pawn.MyWeapon is not null || Pawn.Health <= 0) return false;
            Pawn.GearGrantUntil = Pawn.Level.TimeSeconds + GearK.GrantSeconds;
        }

        STeamInfo? team = Team(Pawn);
        bool tutorial = (Pawn.Controller as PlayerController)?.GameReplicationInfo is { bTutorMode: true };
        if (Weapon)
        {
            if (!Pawn.GearGrantWeapon ||
                (!tutorial && team is { ClassWeapon.Count: > 0 } && IndexOfClass(team.ClassWeapon, Class) < 0))
            {
                return false;
            }

            Pawn.GearGrantWeapon = false;
            return true;
        }

        if (Sidearms.Find(Class.Path) is { } sidearm)
        {
            if (!Pawn.GearGrantSidearm || Pawn.SidearmWeapon is not null ||
                !SidearmEquipment.Available(Pawn, sidearm.Kind)) return false;
            Pawn.GearGrantSidearm = false;
            return true;
        }

        if (string.Equals(Class.Path, GadBomb.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (!Pawn.GearGrantBomb)
            {
                return false;
            }

            Pawn.GearGrantBomb = false;
            return true;
        }

        if (Pawn.GearGrantGadgets <= 0)
        {
            return false;
        }

        if (!tutorial && team is not null)
        {
            int option = IndexOfClass(team.ClassInventory, Class);
            if (option < 0 || option >= K.TeamOptions || !IsAllowed(team, option))
            {
                return false;
            }
        }

        for (SGadget? g = Pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (PawnInventory.IsA(g, Class))
            {
                return false;
            }
        }

        Pawn.GearGrantGadgets--;
        return true;
    }

    /// <summary>
    /// Whether anything of the pawn's kit was used since it spawned (an OpenSCCT addition,
    /// spec/additions/gear-change.md): a gadget with a charge spent or used at all, or the weapon's ammo changed.
    /// The merc's pad-up torch and laser and a spy's bomb do not count.
    /// </summary>
    public static bool HasUsedKit(SBasePawn Pawn)
    {
        for (SGadget? g = Pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            if (g is SidearmCarry || g.IsAPadUpGadget || PawnInventory.IsA(g, GadBomb))
            {
                continue;
            }

            if (g.Charge != g.ClassDefaults.Charge || g.LastUsed > Pawn.LastStartTime)
            {
                return true;
            }
        }

        if (Pawn.SidearmWeapon is { } sidearm && (sidearm.Ammo != Pawn.GearSidearmStartAmmo ||
            sidearm.ClipAmmo != Pawn.GearSidearmStartClip)) return true;
        return (Pawn.PrimaryWeapon ?? Pawn.MyWeapon) switch
        {
            SWeaponDefense rifle => rifle.Ammo != Pawn.GearStartAmmo || rifle.ClipAmmo != Pawn.GearStartClipAmmo,
            SWeapon weapon => weapon.Ammo != Pawn.GearStartAmmo,
            _ => false,
        };
    }

    /// <summary>AddDefaultGadgets (rule 4): the merc's torch and laser; nothing for a spy.</summary>
    [Uc("SBase.SBasePawn.AddDefaultGadgets", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SBasePawn:AddDefaultGadgets", Spec = "spec/equipment/loadouts.md#rules")]
    public static void AddDefaultGadgets(SBasePawn Pawn, EGameMode Mode)
    {
        if (Pawn is SPAWNDEFENSE merc)
        {
            PawnWeapons.AddDefaultGadgets(merc);
        }
    }
}
