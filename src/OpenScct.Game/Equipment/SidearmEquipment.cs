using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>OpenSCCT equipment additions; carry IDs are deliberately outside the retail restriction mask.</summary>
public static class SidearmEquipment
{
    public static bool Enabled(SBasePawn pawn) =>
        (pawn.Controller as PlayerController)?.GameReplicationInfo?.SidearmsEnabled == true;

    public static bool Unrestricted(SBasePawn pawn) => pawn.Level.NetMode == ENetMode.NM_Standalone ||
        pawn.PlayerReplicationInfo?.SvmSidearmsUnrestricted == true || Modes.PracticeRules.IsOn(pawn);

    public static bool Available(SBasePawn pawn, SidearmKind kind) => pawn is SPAWNDEFENSE && Enabled(pawn) &&
        Sidearms.Available(kind, (pawn.PlayerReplicationInfo?.SvmRank ?? 0) & 255, Unrestricted(pawn));

    public static Actor? Spawn(Actor source, ClassRef cls, Actor? owner, FVector at, FRotator rotation)
    {
        if (Sidearms.Find(cls.Path) is { } definition)
            return source.XLevel.SpawnActor(new SidearmCarry { Kind = (int)definition.Kind }, at, rotation, owner, source.Instigator);
        return null;
    }

    public static void Give(SBasePawn pawn, SidearmKind kind)
    {
        var pistol = pawn.XLevel.SpawnActor(new MercSidearm { Kind = (int)kind }, pawn.Location, pawn.Rotation, pawn, pawn);
        if (pistol is MercSidearm weapon)
        {
            weapon.GiveTo(pawn);
            // Both carried actors must follow the pawn's relevance for observers and late joiners.
            // First-person rendering still belongs to FirstPersonView; these inventory actors stay hidden.
            FollowPawnRelevance(pawn, weapon);
            if (pawn.PrimaryWeapon is { } primary) FollowPawnRelevance(pawn, primary);
        }
    }

    public static void FollowPawnRelevance(SBasePawn pawn, SWeapon weapon)
    {
        weapon.bOnlyOwnerSee = false;
        weapon.SetBase(pawn);
    }

    public static bool CanSwitch(SBasePawn pawn)
    {
        if (pawn is not SPAWNDEFENSE || pawn.Health <= 0 || pawn.SidearmWeapon is null || pawn.PrimaryWeapon is null ||
            pawn.bDeleteMe || pawn.bLyingOnGround || pawn.bStickyShocking || pawn.bWeaponTransit ||
            pawn.Level.TimeSeconds < pawn.WeaponSwitchUntil || pawn.Controller is not SPlayerController pc ||
            pc.bUsingNow || pc.bIsInInventoryMode || pc.iDontUpdatePadInput != 0) return false;
        if (pawn.MyWeapon is { } current && current.GetStateName().ToString() is not
            ("s_Selected" or "s_Firing" or "s_Reloading" or "s_FullReloading" or "s_Sniping" or "s_IdleInHolster")) return false;
        string state = pc.GetStateName().ToString();
        return state is "s_Binoculars_Def" or "s_PlayerWalking" or "s_PlayerWalkingCr0H" or "s_PlayerWalkingSt0H" ||
            (state.StartsWith("s_PlayerWalking", StringComparison.Ordinal) && !state.Contains("_T_", StringComparison.Ordinal));
    }

    public static bool Select(SBasePawn pawn, int slot, bool force = false)
    {
        if (slot is < 0 or > 1 || (!force && !CanSwitch(pawn))) return false;
        SWeapon? weapon = slot == 0 ? pawn.PrimaryWeapon : pawn.SidearmWeapon;
        if (weapon is null || weapon.bDeleteMe) return false;
        if (pawn.Controller is SPlayerController { bBinocularVision: true } scoped) scoped.eventSwitchBinocularVision();
        if (pawn.MyWeapon is SWeaponDefense old)
        {
            if (old is MercSidearm pistol) pistol.CancelReload();
            old.CancelCarriedReload();
            old.BulletsToFire = 0;
            old.GotoState(new Name("s_IdleInHolster"));
            old.bDrawWeapon = false;
            for (int c = 0; c < ViewModelAnim.ChannelCount; c++)
            {
                old.ViewAnim.Stop(c);
                // Stop retains the last pose. Cancel its overlay too, since a
                // stopped pain/scope clip will never receive AnimEnd to clear it.
                if (c != SWeaponDefense.MVTCHANNEL) old.ViewAnim.SetAlpha(c, 0f);
            }
        }
        pawn.ActiveWeaponSlot = slot;
        pawn.MyWeapon = weapon;
        pawn.WeaponSwitchUntil = pawn.Level.TimeSeconds + Sidearms.SwitchSeconds;
        pawn.WeaponFireLatch = true;
        pawn.WeaponSwitching = true;
        if (weapon is SWeaponDefense drawn)
        {
            drawn.bDrawWeapon = true;
            drawn.GotoState(new Name("s_Selected"));
            var drawClip = ViewModelClips.DataOf(ViewModelClips.FirstDrawStEd2);
            drawn.ViewAnim.Play(0, ViewModelClips.FirstDrawStEd2, drawClip, drawClip.Seconds(1f) / Sidearms.SwitchSeconds, Backward: true);
            if (pawn is SPAWNDEFENSE merc) merc.WeaponDummy = drawn.Dummy;
        }
        return true;
    }

    public static bool Ready(SWeapon weapon)
    {
        if (weapon.Instigator is not SBasePawn pawn || pawn.SidearmWeapon is null) return true;
        if (pawn.Controller?.bFire == 0) pawn.WeaponFireLatch = false;
        return ReferenceEquals(pawn.MyWeapon, weapon) && pawn.Health > 0 && !pawn.bStickyShocking &&
            !pawn.bLyingOnGround && !pawn.bWeaponTransit && pawn.Level.TimeSeconds >= pawn.WeaponSwitchUntil && !pawn.WeaponFireLatch;
    }

    public static void Tick(SWeapon weapon)
    {
        if (weapon.Instigator is not SBasePawn pawn || pawn.SidearmWeapon is null) return;
        if (pawn.Controller?.bFire == 0) pawn.WeaponFireLatch = false;
        if (pawn.Role == ENetRole.ROLE_Authority) pawn.WeaponSwitching = pawn.Level.TimeSeconds < pawn.WeaponSwitchUntil;
        SWeapon? active = pawn.ActiveWeaponSlot == 0 ? pawn.PrimaryWeapon : pawn.SidearmWeapon;
        if (active is null) return;
        // A replicated slot change reveals the newly selected gun once. Its own
        // animation/state owns visibility afterwards (scope, ladder, holster).
        if (!ReferenceEquals(pawn.MyWeapon, active) && active is SWeaponDefense selected)
            selected.bDrawWeapon = true;
        pawn.MyWeapon = active;
        if (weapon is SWeaponDefense gun && !ReferenceEquals(active, weapon)) gun.bDrawWeapon = false;
        if (pawn is SPAWNDEFENSE merc && active is SWeaponDefense drawn) merc.WeaponDummy = drawn.Dummy;
    }
}

/// <summary>A non-activatable carry item in the dedicated sidearm slot; it has no charges and cannot be shared.</summary>
public sealed class SidearmCarry : SGadget
{
    public int Kind;
    public SidearmCarry() { bActivatable = false; Charge = 0; }
    public override void GiveTo(Pawn other)
    {
        base.GiveTo(other);
        if (other is SBasePawn pawn && pawn.Role == ENetRole.ROLE_Authority && pawn.SidearmWeapon is null)
            SidearmEquipment.Give(pawn, (SidearmKind)Kind);
    }
    protected override void Destroyed()
    {
        if (OwnerPawn?.SidearmWeapon is { } weapon && weapon.Kind == Kind) weapon.Destroy();
        base.Destroyed();
    }
    public override void Use(float value) { }
    public override bool CanUseGadget() => false;
}
