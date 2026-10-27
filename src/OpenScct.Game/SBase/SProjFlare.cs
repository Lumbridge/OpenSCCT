using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjFlare: the merc's flare (spec/equipment/flare.md). Thrown by SGadFlareLauncher (and by an SBallTrap left
/// at its default class), it flies with a burning trail and catches at the first wall it hits: the server leaves a
/// replicated SFlareEmitter there (the burning flare, its light and loop), riding what was hit; every machine that
/// sees its copy hit leaves the burn mark.
/// </summary>
public partial class SProjFlare
{
    /// <summary>The launch sound; off a dedicated server the flight trail (SFlareCourseEmitter) rides the flare.</summary>
    [Uc("SBase.SProjFlare.PostBeginPlay", Sha1 = "5df98f1268f6036edefdb9daeb8533250a1d14ed")]
    [PortOf("uc:SBase.SProjFlare:PostBeginPlay", Spec = "spec/equipment/flare.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        PlaySound(SoundLaunch, ESoundSlot.SLOT_None, 1f, false, K.FlareLaunchSoundRadius);
        if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            CourseEmitter = Spawn<SFlareCourseEmitter>(this);
        }
    }

    /// <summary>
    /// HitWall (native override, decoded game 0x10ae8be0): no bounce. The flare marks itself rebounded; on the server
    /// the wall takes the projectile's damage (int(Damage), the flight direction times MomentumTransfer) and makes a
    /// noise; the wall becomes BaseToFollow; then, on every machine, Explode at Location + ExploWallOut x HitNormal.
    /// </summary>
    [UcNative("ASProjFlare::HitWall", Exe = 0x10ae8be0)]
    [PortOf("nat:SBase.SProjGrenade.HitWall", Spec = "spec/equipment/flare.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "0x10ae8be0: bits 0x350 |= 2; Role == 4 -> Wall.TakeDamage (name 0x9e3) when Wall, Wall.MakeNoise(1.0) (0x3ca); Wall -> +0x358 BaseToFollow; Explode (0x3cb) at Location + ExploWallOut * HitNormal")]
    [Provenance(ProvenanceKind.Estimated, "The native calls MakeNoise on the wall without a null test; a wall-less hit makes no noise here")]
    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        bAlreadyRebounded = true;
        if (Role == ENetRole.ROLE_Authority && Wall is not null)
        {
            Wall.eventTakeDamage(SvmMath.Trunc(Damage), Instigator, Location, Velocity.SafeNormal() * MomentumTransfer, MyDamageType);
            Wall.MakeNoise(K.FlareHitNoise);
        }

        if (Wall is not null)
        {
            BaseToFollow = Wall;
        }

        Explode(Location + (HitNormal * ExploWallOut), HitNormal);
    }

    /// <summary>
    /// ProcessTouch (native override, decoded game 0x10ae8d90), reached for a projectile target or an actor blocking
    /// actors and players (NativeTouch, game 0x10a31e80): unless it touches its owner before any rebound, the flare's
    /// velocity is mirrored on the touch normal and scaled by ActorReboundFactor, then loses its horizontal
    /// part, so it drops off the actor it touched.
    /// </summary>
    [UcNative("ASProjFlare::ProcessTouch", Exe = 0x10ae8d90)]
    [PortOf("nat:Engine.Projectile.NativeTouch", Spec = "spec/equipment/flare.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "0x10ae8d90: skip when !(bits 0x350 & 2) && Owner != None && Owner == Other; V = (V - 2 (V.N) N) * ActorReboundFactor; V.X = V.Y = 0; ProjectileSpeed = |V|")]
    [Provenance(ProvenanceKind.Decoded, "execNativeTouch 0x10a31e80: ProcessTouch only when Other bits +0x2f0 & 0x20000 (bProjTarget) or & 0x4000 and 0x8000 (bBlockActors, bBlockPlayers)")]
    [Provenance(ProvenanceKind.Estimated, "The touch normal is not passed to Touch here: up off the top of the actor, else horizontal")]
    protected override void Touch(Actor Other)
    {
        if (!(Other.bProjTarget || (Other.bBlockActors && Other.bBlockPlayers)))
        {
            return;
        }

        if (!bAlreadyRebounded && Owner is not null && ReferenceEquals(Owner, Other))
        {
            return;
        }

        bool onTop = Location.Z >= Other.Location.Z + Other.CollisionHeight;
        FVector normal = onTop ? new FVector(0f, 0f, 1f) : new FVector(Location.X - Other.Location.X, Location.Y - Other.Location.Y, 0f).SafeNormal();
        FVector v = Velocity.MirrorByVector(normal) * ActorReboundFactor;
        Velocity = new FVector(0f, 0f, v.Z);
        ProjectileSpeed = Velocity.Size();
    }

    /// <summary>
    /// The flare catches: on the server the burning SFlareEmitter 15 uu out of the surface, riding BaseToFollow, and
    /// the catch sound; on every machine the burn mark (SFlareBurnProjector) facing into the surface; the trail goes,
    /// then the flare.
    /// </summary>
    [Uc("SBase.SProjFlare.Explode", Sha1 = "1560c2ee89a7f533f51e55fd8cb032842f03a3a6")]
    [PortOf("uc:SBase.SProjFlare:Explode", Spec = "spec/equipment/flare.md#rules")]
    public override void Explode(FVector HitLocation, FVector HitNormal)
    {
        FVector at = HitLocation + (HitNormal.SafeNormal() * K.FlareStandOff);
        if (Role == ENetRole.ROLE_Authority)
        {
            SFlareEmitter? flare = Spawn<SFlareEmitter>(null, default, at, HitLocation.Rotation());
            flare?.SetBase(BaseToFollow);
            PlaySound(SoundExplode, ESoundSlot.SLOT_Misc, 1f, false, K.FlareExplodeSoundRadius, null, true);
        }

        Spawn<SFlareBurnProjector>(null, default, at, (-HitNormal).Rotation());
        CourseEmitter?.Destroy();
        Destroy();
    }
}
