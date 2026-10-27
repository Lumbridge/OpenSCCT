using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadGrenLauncher: the grenade gadgets (smoke, chaff, frag). An aimed throw uses the charge strength; the
/// spy's quick launch throws at the feet. Written from spec/equipment/grenade-throw.md.
/// </summary>
public partial class SGadGrenLauncher
{
    private static readonly Name CNAME_s_FullReloading = new("s_FullReloading");
    private static readonly Name CNAME_s_Reloading = new("s_Reloading");
    private static readonly Name CNAME_s_FireGrenade = new("s_FireGrenade");
    private static readonly Name CNAME_s_ElbowShocking = new("s_ElbowShocking");
    private static readonly Name CNAME_ThrowGren = new("ThrowGren");

    private SProjGrenade? _projectileDefaults;

    /// <summary>The class defaults of ProjectileClass (its ProjectileSpeed sets the throw force).</summary>
    public SProjGrenade? ProjectileDefaults =>
        _projectileDefaults ??= PawnInventory.IndexOf(this).Find(ProjectileClass)?.Create() as SProjGrenade;

    /// <summary>
    /// The aimed throw (rule 3): refused while the weapon reloads, throws or elbows; the force grows with the charge
    /// value; a merc's rifle plays the throw clip.
    /// </summary>
    [Uc("SBase.SGadGrenLauncher.Use", Sha1 = "b66192a150a031e7ad2bc1790385c0b053623384")]
    [PortOf("uc:SBase.SGadGrenLauncher:Use", Spec = "spec/equipment/grenade-throw.md#rules")]
    public override void Use(float Value)
    {
        SBasePawn? pawn = OwnerPawn;
        if (pawn?.MyWeapon is { } weapon && (weapon.IsInState(CNAME_s_FullReloading) || weapon.IsInState(CNAME_s_Reloading) ||
            weapon.IsInState(CNAME_s_FireGrenade) || weapon.IsInState(CNAME_s_ElbowShocking)))
        {
            return;
        }

        float speed = ProjectileDefaults?.ProjectileSpeed ?? 0f;
        UseGren(WeaponRules.ThrowForce(Value, speed));
        if (pawn?.Controller is SPlayerController pc)
        {
            pc.Vibration_Gadget(K.GadgetRumble);
        }

        if (pawn?.MyWeapon is SWeaponDefense rifle)
        {
            rifle.GotoState(CNAME_s_FireGrenade);
        }
    }

    /// <summary>
    /// UseGren (native 1312, decoded game 0x10aa03e0): the launch origin and rotation from the weapon's FX bone and
    /// the aim, then ThrowGren.
    /// </summary>
    [UcNative("ASGadGrenLauncher::execUseGren", Exe = 0x10aa03e0)]
    [Uc("SBase.SGadGrenLauncher.UseGren", Sha1 = "d3377d08be628f09813bfb5c77d1e045ac92db93")]
    [PortOf("nat:SBase.SGadGrenLauncher.UseGren", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void UseGren(int Force)
    {
        // Decoded at game 0x10aa03e0: nothing without a drawn weapon; the throw starts at the weapon's FX bone, a merc's
        // 40 uu along his aim (MyDesiredRotation, which is also the throw's rotation), a spy's 10 uu along the FX bone.
        if (OwnerPawn is not { WeaponLoaded: not 0 } pawn || pawn.MyWeapon is not { } weapon)
        {
            return;
        }

        // A player merc's FX bone at the release, 80 uu behind the aimed rifle's (measured, grenade-throw.md rule 4a).
        FCoords fx = pawn.SPawnType == PawnType.SPAWNDEFENSE && pawn.Controller is SPlayerController thrower
            ? CombatNatives.MercThrowFxCoords(pawn, thrower.MyDesiredRotation)
            : CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone);
        FRotator rotation;
        FVector origin;
        if (pawn.SPawnType == PawnType.SPAWNDEFENSE)
        {
            rotation = pawn.Controller is SPlayerController pc ? pc.MyDesiredRotation : pawn.Rotation;
            origin = fx.Origin + (rotation.Vector() * K.MercThrowForward);
        }
        else
        {
            rotation = fx.XAxis.Rotation();
            origin = fx.Origin + (rotation.Vector() * K.SpyThrowForward);
        }

        ThrowGren(Force, origin, rotation, false);
    }

    /// <summary>
    /// QuickUse (native 1313, decoded game 0x10aa0660): the spy's quick launch, dropped from under his right hand.
    /// Refused for a gadget that cannot be quick launched, inside the refire time, and without charges (unless
    /// unlimited or on a tutorial map); the refire clock starts here. A spy's launch starts 10 uu under B R Hand when
    /// a 15 uu box fits between the pawn and that point, else 5 uu above the weapon's FX bone (nothing is thrown
    /// without a weapon); its rotation is pitched -15000 with the bone's yaw; its force is -200 while the quick throw
    /// clip runs (bQuickGadgetHasBegun), else 20. A merc's (no merc grenade has CanQuickLaunch) starts 5 uu above the
    /// FX bone with force 1 and his desired yaw. Then ThrowGren with QuickLaunch, which uses the charge.
    /// </summary>
    [UcNative("ASGadGrenLauncher::execQuickUse", Exe = 0x10aa0660)]
    [Uc("SBase.SGadGrenLauncher.QuickUse", Sha1 = "067d5096f219c64b53bb1c7d5e3754285b565a47")]
    [PortOf("nat:SBase.SGadGrenLauncher.QuickUse", Spec = "spec/equipment/grenade-throw.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execQuickUse game 0x10aa0660; the B R Hand placement is estimated (K.QuickThrowHand*), the simulation has no skeleton")]
    public override void QuickUse()
    {
        if (!CanQuickLaunch || Level.TimeSeconds - LastUsed < RefireRate)
        {
            return;
        }

        if (Charge < 1 && Charge != K.UnlimitedCharge && !GadgetWorld.CurLevelIsTutorial(this))
        {
            return;
        }

        LastUsed = Level.TimeSeconds;
        if (OwnerPawn is not { } pawn)
        {
            return;
        }

        if (pawn.SPawnType != PawnType.SPAWNOFFENSE)
        {
            if (pawn.MyWeapon is not { } rifle)
            {
                return;
            }

            FRotator desired = pawn.Controller is SPlayerController pc ? pc.MyDesiredRotation : pawn.Rotation;
            FVector fxOrigin = CombatServices.Ports(this).BoneCoords(rifle, SWeaponDefense.FxBone).Origin;
            ThrowGren(K.QuickThrowForceMerc, fxOrigin + new FVector(0f, 0f, K.QuickThrowFxRaise),
                new FRotator(K.QuickThrowPitch, desired.Yaw, desired.Roll), true);
            return;
        }

        FCoords yawFrame = FCoords.FromRotator(new FRotator(0, pawn.Rotation.Yaw, 0));
        FVector hand = pawn.Location + (yawFrame.XAxis * K.QuickThrowHandForward) + (yawFrame.YAxis * K.QuickThrowHandRight) +
            (yawFrame.ZAxis * K.QuickThrowHandUp);
        FVector origin = new(hand.X, hand.Y, hand.Z - K.QuickThrowHandDrop);
        if (QuickThrowBlocked(pawn, origin))
        {
            if (pawn.MyWeapon is not { } weapon)
            {
                return;
            }

            origin = CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone).Origin + new FVector(0f, 0f, K.QuickThrowFxRaise);
        }

        int force = pawn.bQuickGadgetHasBegun ? K.QuickThrowForce : K.QuickThrowForceNoClip;
        ThrowGren(force, origin, new FRotator(K.QuickThrowPitch, pawn.Rotation.Yaw, 0), true);
    }

    /// <summary>The quick launch's clearance: a 15 uu box swept from the pawn to the start point hits something.</summary>
    [Provenance(ProvenanceKind.Decoded, "execQuickUse game 0x10aa07ae: SingleLineCheck(pawn, start, Location, 0x86, 15) and Time < 1")]
    private static bool QuickThrowBlocked(SBasePawn pawn, FVector start)
    {
        if (GadgetWorld.Collision(pawn) is not { } world)
        {
            return false;
        }

        var extent = new FVector(K.QuickThrowClearExtent, K.QuickThrowClearExtent, K.QuickThrowClearExtent);
        return world.LineCheck(out OpenScct.Physics.Collision.FCheckResult hit, pawn, start, pawn.Location, extent, (OpenScct.Physics.Collision.ETraceFlags)K.QuickThrowClearFlags) &&
            hit.bHit && hit.Time < 1f;
    }

    /// <summary>
    /// ThrowGren (native 1314, reliable owner to server): the server spawns the projectile, sets QuickLaunch,
    /// launches it, uses one charge and spawns the launch flash. A local throw is rounded the way a remote one
    /// arrives (whole units, rotation cut to its high byte), as Reloaded does.
    /// </summary>
    [UcNative("ASGadGrenLauncher::execThrowGren")]
    [Uc("SBase.SGadGrenLauncher.ThrowGren", Sha1 = "6f9846bba34405fe33069725f223579c183677d3")]
    [PortOf("nat:SBase.SGadGrenLauncher.ThrowGren", Spec = "spec/equipment/grenade-throw.md#rules")]
    [PortOf("rl:SBase.SGadGrenLauncher.ThrowGren", Spec = "spec/native/net-reloaded.md#rules")]
    public void ThrowGren(int Force, FVector Origin, FRotator Rotation, bool bQuickLaunch)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ThrowGren)
            {
                Target = this,
                Int1 = Force,
                Vector1 = Origin,
                Rotation = Rotation,
                Bool1 = bQuickLaunch,
            });
            return;
        }

        var origin = new FVector(SvmMath.Round(Origin.X), SvmMath.Round(Origin.Y), SvmMath.Round(Origin.Z));
        var rotation = new FRotator(Rotation.Pitch & 0xFF00, Rotation.Yaw & 0xFF00, Rotation.Roll & 0xFF00);
        if (DeviceClasses.Spawn(this, ProjectileClass, OwnerPawn, default, origin, rotation) is SProjGrenade grenade)
        {
            grenade.Instigator = OwnerPawn;
            grenade.QuickLaunch = bQuickLaunch;
            grenade.Launch(Force, bQuickLaunch);
            if (!grenade.LaunchEffect.IsNone)
            {
                DeviceClasses.Spawn(this, grenade.LaunchEffect, OwnerPawn, default, origin, rotation);
            }
        }

        DecCharge();
        LastUsed = Level.TimeSeconds;
    }
}
