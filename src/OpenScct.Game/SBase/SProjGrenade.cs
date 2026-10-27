using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjGrenade: a thrown grenade (spec/equipment/grenade-throw.md rules 8 to 10). It spins, bounces off walls
/// and actors by its rebound factors and explodes on the server when the fuse runs out; client copies just vanish.
/// An aimed grenade does not explode on impact (rl:net.grenades); a quick-launched spy grenade pops at its first
/// hit through its class's HitWall (<see cref="QuickLaunchPop"/>, grenade-throw.md rule 10a).
/// </summary>
public partial class SProjGrenade
{
    /// <summary>The fuse timer and the random spin, on every machine.</summary>
    [Uc("SBase.SProjGrenade.PostNetBeginPlay", Sha1 = "c0e7e03a62edd32a93f175e570c6ec2ba526e0fd")]
    [PortOf("uc:SBase.SProjGrenade:PostNetBeginPlay", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        SetTimer(TempsExplosion, false);
        float yaw = K.SpinYawBase + K.SpinYawRandom * FRand();
        if (FRand() < 0.5f)
        {
            yaw = -yaw;
        }

        float pitch = K.SpinPitchBase + K.SpinPitchRandom * FRand();
        fVitesseRotationYaw = yaw;
        fVitesseRotationPitch = pitch;
        RotationRate = new FRotator(SvmMath.Trunc(pitch), SvmMath.Trunc(yaw), 0);
    }

    /// <summary>The fuse: the server stops the grenade and explodes it; a client copy destroys itself.</summary>
    [Uc("SBase.SProjGrenade.Timer", Sha1 = "1c7acfda29fb01c44238fce87c141b85b20eb6f5")]
    [PortOf("uc:SBase.SProjGrenade:Timer", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void Timer()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            Destroy();
            return;
        }

        bBounce = false;
        SetPhysics(EPhysics.PHYS_None);
        Explode(Location, FVector.Zero);
    }

    /// <summary>
    /// Launch (native 1358, decoded game 0x10ab8a90). An aimed throw flies along the rotation at Force + 210 plus the
    /// thrower's own speed along that direction; nothing caps it at MaxSpeed. A quick launch keeps the spawn velocity
    /// (a tenth of a rising base's velocity when the thrower stands on one going up faster than 100 uu/s) and adds
    /// Force upward.
    /// </summary>
    [UcNative("ASProjGrenade::execLaunch", Exe = 0x10ab8a90)]
    [Uc("SBase.SProjGrenade.Launch", Sha1 = "8cb440a6c22e402c22911da23ca9c0304efad40e")]
    [PortOf("nat:SBase.SProjGrenade.Launch", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void Launch(int Force, bool bQuickLaunch)
    {
        if (!bQuickLaunch)
        {
            FVector dir = Rotation.Vector();
            FVector owner = Owner?.Velocity ?? FVector.Zero;
            float speed = owner.Size();
            FVector along = speed > 0f ? owner * (1f / speed) : owner;
            float launch = ((dir | along) * speed) + (Force + K.GrenadeLaunchBonus);
            Velocity = dir * launch;
        }
        else if (Owner?.Base is { } ground)
        {
            // The quick launch: the spawn velocity (none) is kept, or a tenth of a rising base's; Force goes on Z.
            // A thrower without a base (in the air) skips both, so the grenade just falls (game 0x10ab8bd3, 0x10ab8bdd).
            if (ground.Velocity.Z > K.QuickLaunchRisingBase)
            {
                Velocity = ground.Velocity * K.QuickLaunchBaseShare;
            }

            Velocity = new FVector(Velocity.X, Velocity.Y, Velocity.Z + Force);
        }

        QuickLaunch = bQuickLaunch;
    }

    /// <summary>
    /// The pop of a quick-launched grenade (the quick branch of the HitWall overrides, decoded game 0x10ab8760 and
    /// 0x10ac1c60): at its first hit, floors included, the server explodes it where it is (normal zero, physics and
    /// bounce left as they are) and a client copy is destroyed. False when the grenade was aimed (it bounces).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "HitWall overrides game 0x10ab8760 (chaff, flash-bang) and 0x10ac1c60 (smoke): bits_QuickLaunch (0x350) & 1, then Role == ROLE_Authority ? Explode(Location, 0) : DestroyActor")]
    protected bool QuickLaunchPop()
    {
        if (!QuickLaunch)
        {
            return false;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            Explode(Location, FVector.Zero);
        }
        else
        {
            Destroy();
        }

        return true;
    }

    /// <summary>
    /// The pawn branch of the smoke and flash-bang ProcessTouch overrides (decoded game 0x10ac1cb0 and 0x10ab87b0):
    /// touching anything but its owner marks the grenade as rebounded; a pawn other than the owner sets it off, on
    /// the server by stopping it (no bounce, PHYS_None) and exploding it at <paramref name="At"/> with a zero normal,
    /// on a client by destroying the copy. An ownerless grenade touches nothing. Touches of other actors keep the
    /// grenade flying (the stock actor rebound, game 0x10ab90f0, is not ported). Aimed and quick-launched grenades
    /// alike: the overrides do not read QuickLaunch.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ProcessTouch 0x10ac1cb0 / 0x10ab87b0: Owner != 0 && Owner != Other -> bits 0x350 |= 2; IsA(Pawn) -> Role == 4 ? (bBounce = 0, setPhysics(0), Explode) : DestroyActor")]
    protected bool PawnTouchPop(Actor Other, FVector At)
    {
        if (Owner is null || ReferenceEquals(Owner, Other))
        {
            return false;
        }

        bAlreadyRebounded = true;
        if (Other is not Pawn)
        {
            return false;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            bBounce = false;
            SetPhysics(EPhysics.PHYS_None);
            Explode(At, FVector.Zero);
        }
        else
        {
            Destroy();
        }

        return true;
    }

    /// <summary>
    /// A bounce (native 1359, body decoded at game 0x10ab8cf0). On world geometry the grenade first checks that the
    /// surface is still there one second of flight ahead (a zero-extent line back from Location + Velocity); when
    /// nothing is hit it stops and explodes on the server (a client copy just goes). Then the velocity is mirrored on
    /// the surface and scaled by WallReboundFactor (walls and actors alike), the impact sounds when the grenade leaves
    /// faster than 20 uu/s, an upward bounce above 400 uu/s is halved toward 400, a grenade without a fuse that leaves
    /// slower than 20 uu/s stops and explodes, and a mover hitting it face on stops it dead.
    /// </summary>
    [UcNative("ASProjGrenade::execHitWall", Exe = 0x10ab8c80)]
    [Uc("SBase.SProjGrenade.HitWall", Sha1 = "856fc076767eeb1dade4ebc7461f57aa86cd8666")]
    [PortOf("nat:SBase.SProjGrenade.HitWall", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        if (Wall is LevelInfo || Wall is null)
        {
            FVector ahead = Location + Velocity;
            if (!Equipment.GadgetWorld.Line(this, ahead, Location, (OpenScct.Physics.Collision.ETraceFlags)K.GrenadeBounceTraceFlags, out _))
            {
                StopAndExplode();
                return;
            }
        }

        bAlreadyRebounded = true;
        float vn = (Velocity | HitNormal);
        Velocity = (Velocity - (HitNormal * (2f * vn))) * WallReboundFactor;
        float speed = Velocity.Size();
        ProjectileSpeed = speed;
        if (speed > K.GrenadeRestSpeed && Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            PlaySound(SoundImpact, ESoundSlot.SLOT_None, null, false, K.GrenadeSoundRadius);
        }

        if (Velocity.Z <= K.GrenadeUpBounceCap)
        {
            if (speed < K.GrenadeRestSpeed && TempsExplosion == 0)
            {
                StopAndExplode();
                return;
            }
        }
        else
        {
            Velocity = new FVector(Velocity.X, Velocity.Y, (Velocity.Z + K.GrenadeUpBounceCap) * 0.5f);
        }

        if (Wall is Mover { bCollideActors: true } mover && !bIgnoreEncroachers)
        {
            FVector mv = mover.Velocity;
            float size = mv.Size();
            if (size > 0f && SvmMath.Abs((mv * (1f / size) | HitNormal)) > K.GrenadeMoverStopCos)
            {
                Velocity = FVector.Zero;
                bBounce = false;
            }
        }
    }

    /// <summary>The stop of the bounce rules: no physics, then the explosion on the server or the copy's end.</summary>
    private void StopAndExplode()
    {
        SetPhysics(EPhysics.PHYS_None);
        if (Role == ENetRole.ROLE_Authority)
        {
            Explode(Location, FVector.Zero);
        }
        else
        {
            Destroy();
        }
    }

    /// <summary>Projectile.Explode (native 1172): the class's explosion; the base grenade only disappears.</summary>
    [UcNative("AProjectile::execExplode")]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/frag-grenade.md#rules")]
    public virtual void Explode(FVector HitLocation, FVector HitNormal) => Destroy();
}
