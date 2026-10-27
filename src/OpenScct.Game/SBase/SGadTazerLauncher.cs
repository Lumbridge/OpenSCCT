using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using CK = OpenScct.Game.Combat.K;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadTazerLauncher: the merc taser (spec/combat/shock-and-taser.md rules 27 and 28). The server picks the
/// target in front of the merc and shocks it with the taser pill (-10): 51 damage when it was not shocking, then
/// the freeze and, in a walking state, the knock-down.
/// </summary>
public partial class SGadTazerLauncher
{
    private static readonly Name CNAME_UseBulletSet = new("UseBulletSet");

    /// <summary>The use: the refire delay, then the server's shot.</summary>
    [Uc("SBase.SGadTazerLauncher.Use", Sha1 = "6eb04cac6f68583ec629f38a70fa91fc501762df")]
    [PortOf("uc:SBase.SGadTazerLauncher:Use", Spec = "spec/combat/shock-and-taser.md#taser-and-taser-like-hits")]
    public override void Use(float Value)
    {
        if (!CanUseGadget())
        {
            return;
        }

        LastUsed = Level.TimeSeconds;
        PlaySound(FireSound, ESoundSlot.SLOT_None, null, false, K.WeaponSoundRadius);
        UseBulletSet();
    }

    /// <summary>
    /// UseBulletSet (native 1301, reliable owner to server): the nearest pawn of the other side within fBulletRange
    /// and fBulletAngle degrees of the merc's facing, in sight, gets DamagePawn.
    /// </summary>
    [UcNative("ASGadTazerLauncher::execUseBulletSet")]
    [Uc("SBase.SGadTazerLauncher.UseBulletSet", Sha1 = "067d5096f219c64b53bb1c7d5e3754285b565a47")]
    [PortOf("nat:SBase.SGadTazerLauncher.UseBulletSet", Spec = "spec/combat/shock-and-taser.md#open-questions")]
    [Provenance(ProvenanceKind.Estimated, "Target selection not decoded: nearest other-side pawn within range and the angle as a half cone, in sight")]
    public void UseBulletSet()
    {
        if (OwnerPawn is not { } merc)
        {
            return;
        }

        if (Role < ENetRole.ROLE_Authority && merc.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_UseBulletSet) { Target = this });
            return;
        }

        FVector start = merc.Location + CombatServices.Ports(this).EyePosition(merc);
        FVector facing = (merc.Controller as SPlayerController)?.Rotation.Vector() ?? merc.Rotation.Vector();
        float cosHalf = SvmMath.Cos(SvmMath.RadiansToRotator(fBulletAngle * (3.1415927f / 180f)));
        IActorNatives? natives = XLevel.GetService<IActorNatives>();
        SBasePawn? best = null;
        float bestDist = float.MaxValue;
        foreach (SBasePawn p in XLevel.AllActors<SBasePawn>())
        {
            if (ReferenceEquals(p, merc) || p.Health <= 0 || p.SPawnType == merc.SPawnType)
            {
                continue;
            }

            FVector d = p.Location - start;
            float dist = d.Size();
            if (dist > fBulletRange + p.CollisionRadius || (d.SafeNormal() | facing) < cosHalf ||
                (natives is not null && !natives.FastTrace(p.Location, start)))
            {
                continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                best = p;
            }
        }

        if (best is not null)
        {
            DamagePawn(best, merc, best.Location, start);
        }
    }

    /// <summary>
    /// The taser hit (rule 27): no damage argument, the taser pill, no refill share; an enemy (or anyone in Merc
    /// Deathmatch) counts a taser hit for the merc.
    /// </summary>
    [Uc("SBase.SGadTazerLauncher.DamagePawn", Sha1 = "149c94624a75b9e95a936d6a2da8f71b1dfeac00")]
    [PortOf("uc:SBase.SGadTazerLauncher:DamagePawn", Spec = "spec/combat/shock-and-taser.md#taser-and-taser-like-hits")]
    public void DamagePawn(Pawn? Touched, Pawn? PawnDef, FVector HitLocation, FVector StartTrace)
    {
        if (Touched?.Controller is not SPlayerController victim)
        {
            return;
        }

        victim.PawnTakeDamage(0, PawnDef, HitLocation, (HitLocation - StartTrace) * K.BulletMomentumScale, MyDamageType, CK.TaserPill, 0);
        if (PawnDef?.Controller is SPlayerController merc &&
            (SGameInfo.TeamIndexOf(merc) != SGameInfo.TeamIndexOf(victim) || victim.IsMercDeathMatch))
        {
            SPlayerController.IncStat(merc, static s => s.Merc_TaserHits++);
        }
    }
}
