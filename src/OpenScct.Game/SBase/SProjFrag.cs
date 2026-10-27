using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjFrag: the merc frag grenade (spec/equipment/frag-grenade.md). Two seconds after the throw the server
/// explodes it once: the explosion visual and flash, the sound, the camera shake within ShakeRange and the damage
/// within Range. Players take damage through their controller's PawnTakeDamage (their own TakeDamage is empty).
/// </summary>
public partial class SProjFrag
{
    /// <summary>The launch sound; the trail emitter is a view effect.</summary>
    [Uc("SBase.SProjFrag.PostBeginPlay", Sha1 = "91b7047a3fea44a33f4f28b61ee84ed41f95e96d")]
    [PortOf("uc:SBase.SProjFrag:PostBeginPlay", Spec = "spec/equipment/frag-grenade.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        PlaySound(SoundLaunch, ESoundSlot.SLOT_None, null, false, K.GrenadeSoundRadius);
    }

    /// <summary>
    /// Explode (native 0x10ab4780, decoded in spec/native/natives2-damage.md rule 4; once): the trail goes, the noise,
    /// the explosion sound for everyone (radius 500), the explosion visual, then the blast (damage within Range in
    /// sight, the spy hold rule, the shake within ShakeRange) and the grenade is gone.
    /// </summary>
    [UcNative("ASProjFrag::Explode", Exe = 0x10ab4780)]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/frag-grenade.md#rules")]
    [PortOf("eng:ASProjFrag::Explode", Spec = "spec/native/natives2-damage.md#rules")]
    public override void Explode(FVector HitLocation, FVector HitNormal)
    {
        if (bExploding)
        {
            return;
        }

        FragLaunchEmitter?.Destroy();
        FragLaunchEmitter = null;
        NotifyNoise(K.FragNoise);
        PlaySound(SoundExplode, ESoundSlot.SLOT_None, 1f, false, K.FragSoundRadius);
        XLevel.Spawn<SGrenadeExplosion>(HitLocation, HitLocation.Rotation(), null, Instigator);
        Pawn? instigator = Owner as SBasePawn;
        bExploding = true;
        GadgetBlast.Run(new GadgetBlast.Blast(this, Location, Location, Range, true, Damage, Range, fDistForBlast,
            SDamageType.SDamage_Explosion, SDamageType.SDTBase, SDamageType.SDamage_Explosion, PercentDamageRefillable, ShakeRange,
            instigator, false));
        Destroy();
    }

    /// <summary>The damage at distance <paramref name="d"/>: Damage falling linearly to 0 at Range.</summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 4: int((1 - dist / Range) Damage), Damage when Range is 0")]
    public int DamageAt(float d) => Range == 0 ? Damage : d >= Range ? 0 : SvmMath.Trunc(Damage * (1f - d / Range));
}
