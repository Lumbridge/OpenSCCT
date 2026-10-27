using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMinePoison: the merc's poison mine (spec/equipment/mine-launcher.md rule 23). Placed 1.5 uu further off the
/// wall than the spy trap it extends, it poisons the first spy who crosses its beam; damage releases its gas.
/// </summary>
public partial class SMinePoison
{
    [Provenance(ProvenanceKind.Decoded, "SMinePoison.PostBeginPlay SetLocation(Location + 1.5 X)")]
    private const float PoisonOffset = 1.5f;

    /// <summary>
    /// CheckPresence (native 1820, decoded 0x10ab43a0): the first spy within the beam's length plus his radius, close
    /// and in front of the mine or inside its 0.9 cone, whose cylinder the beam crosses, is poisoned: the gas and
    /// projectile sounds, poison damage 0 from the owner, the poison cloud, and the mine is gone.
    /// </summary>
    [UcNative("ASMinePoison::execCheckPresence", Exe = 0x10ab43a0, Editor = 0x111cdc70)]
    [PortOf("nat:SBase.SMinePoison.CheckPresence", Spec = "spec/equipment/mine-launcher.md#mines")]
    public override void CheckPresence()
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        float length = TrapBeamLength;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p is not SPawnAttaque spy || spy.bDeleteMe)
            {
                continue;
            }

            FVector d = spy.Location - Location;
            float dist = d.Size();
            if (!(dist < length + spy.CollisionRadius) || dist <= 0f)
            {
                continue;
            }

            float c = (d * (1f / dist)) | x;
            if (!((dist < K.LaserCloseRange && c >= 0f) || c > K.LaserConeCos))
            {
                continue;
            }

            if (!GadgetWorld.SegmentCylinderHit(Location, LaserEndLocation, spy.Location, spy.CollisionRadius, spy.CollisionHeight,
                out FVector hit))
            {
                continue;
            }

            Poison(spy, hit);
            return;
        }
    }

    /// <summary>The poisoning of <paramref name="Spy"/> hit at <paramref name="Hit"/> (CheckPresence's end).</summary>
    private void Poison(SPawnAttaque Spy, FVector Hit)
    {
        PlaySound(GasSnd, ESoundSlot.SLOT_None, 1f, false, K.PoisonSoundRadius);
        PlaySound(ProjectileSnd, ESoundSlot.SLOT_None, 1f, false, K.PoisonSoundRadius);
        if (PcOwner is null && (Owner as Pawn)?.Controller is SPlayerController owner)
        {
            PcOwner = owner;
        }

        Pawn? by = PcOwner?.Pawn;
        (Spy.Controller as SPlayerController)?.PawnTakeDamage(0, by, Hit, (Hit - Location) * K.PoisonMomentumScale, MyDamageType, 0, 0);
        Spy.PoisonnedBy = by as SBasePawn;
        XLevel.SpawnActor(new SPoisonEmitter(), Location, Rotation, null, Instigator);
        Destroy();
    }

    /// <summary>Damage releases the poison cloud, then removes the mine.</summary>
    [Uc("SBase.SMinePoison.TakeDamage", Sha1 = "b84a7c3d18304e898fc39751936acadfd859310a")]
    [PortOf("uc:SBase.SMinePoison:TakeDamage", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        XLevel.SpawnActor(new SMinePoisonEmitter(), Location, Rotation, this);
        base.TakeDamage(Damage, EventInstigator, HitLocation, Momentum, DamageType, PillTag);
    }

    /// <summary>The beam and (on the server) the interaction go, then the trap's clean-up.</summary>
    [Uc("SBase.SMinePoison.Destroyed", Sha1 = "86f1d3f721a83e0ed78c3fab2c1273129f93123e")]
    [PortOf("uc:SBase.SMinePoison:Destroyed", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Destroyed()
    {
        Laser?.Destroy();
        if (Role == ENetRole.ROLE_Authority)
        {
            InteractionObj?.Destroy();
        }

        base.Destroyed();
    }

    /// <summary>1.5 uu further off the wall, then the trap's start.</summary>
    [Uc("SBase.SMinePoison.PostBeginPlay", Sha1 = "8128b18065c979c662b484f2e1b392c27e7f1db6")]
    [PortOf("uc:SBase.SMinePoison:PostBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void PostBeginPlay()
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        SetLocation(Location + x * PoisonOffset);
        base.PostBeginPlay();
    }
}
