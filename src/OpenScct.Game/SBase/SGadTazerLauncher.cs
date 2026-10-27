using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using CK = OpenScct.Game.Combat.K;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadTazerLauncher: the merc taser (spec/equipment/tazer.md, spec/combat/shock-and-taser.md rules 27 and 28).
/// The owner's machine asks the server; the server shocks every pawn in the cone in front of the drawn weapon's FX bone,
/// spawns one STazerEmitter beam per pawn hit (or one short beam when none is) and turns the merc toward the last one.
/// </summary>
public partial class SGadTazerLauncher
{
    private static readonly Name CNAME_UseBulletSet = new("UseBulletSet");

    /// <summary>Alt-fire: only the refire delay is tested (the taser has no charges), then the shot and the rumble.</summary>
    [Uc("SBase.SGadTazerLauncher.Use", Sha1 = "6eb04cac6f68583ec629f38a70fa91fc501762df")]
    [PortOf("uc:SBase.SGadTazerLauncher:Use", Spec = "spec/equipment/tazer.md#rules")]
    public override void Use(float Value)
    {
        if (Level.TimeSeconds - LastUsed < RefireRate)
        {
            return;
        }

        LastUsed = Level.TimeSeconds;
        UseBulletSet();
        (OwnerPawn?.Controller as SPlayerController)?.Vibration_Gadget(K.GadgetRumble);
        base.Use(Value);
    }

    /// <summary>
    /// UseBulletSet (native 1301, game 0x10aa0b50; reliable owner to server). Every player hears the shot at the merc.
    /// With a weapon drawn, every living pawn other than the merc, not attacked and not shocking, whose aim point is
    /// within fBulletRange of a point 50 uu behind the FX bone, inside the distance-dependent cone and in a clear 15 uu
    /// box line, gets DamagePawn and its own beam. Then the merc's controller turns toward the last pawn hit
    /// (ForceRotateTo); with none hit, one beam along the FX bone's X axis.
    /// </summary>
    [UcNative("ASGadTazerLauncher::execUseBulletSet", Exe = 0x10aa0b50)]
    [Uc("SBase.SGadTazerLauncher.UseBulletSet", Sha1 = "067d5096f219c64b53bb1c7d5e3754285b565a47")]
    [PortOf("nat:SBase.SGadTazerLauncher.UseBulletSet", Spec = "spec/equipment/tazer.md#rules")]
    [PortOf("rep:SBase.SGadTazerLauncher.UseBulletSet", Spec = "spec/equipment/tazer.md#network")]
    [Provenance(ProvenanceKind.Decoded, "tazer.md rules 2 to 5: sound, FX bone, pawn list filters, range, cone, 0x86 box line, DamagePawn, STazerEmitter, ForceRotateTo")]
    [Provenance(ProvenanceKind.Estimated, "The FX bone is the drawn weapon's estimated pose (CombatPorts.BoneCoords) until skeletal poses run")]
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

        merc.PlaySound(FireSound, ESoundSlot.SLOT_None, K.TazerSoundVolume, true, K.TazerSoundRadius);
        if (merc.MyWeapon is not { } weapon)
        {
            return;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone);
        FVector aimFrom = fx.Origin - fx.XAxis * K.TazerAimPullBack;
        FVector traceFrom = fx.Origin - fx.XAxis * K.TazerTracePullBack;
        STazerEmitter? last = null;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            FVector at = p.Location;
            if (p.bLyingOnGround)
            {
                at.Z -= p.CollisionHeight * K.TazerLyingAimHeight;
            }
            else if (!p.bIsCrouched)
            {
                at.Z += K.TazerStandingAimRaise;
            }

            if (p.Health <= 0 || ReferenceEquals(p, merc) || p.m_AttackedBy is not null || p.bStickyShocking)
            {
                continue;
            }

            float dist = (at - aimFrom).Size();
            if (!(dist < fBulletRange) ||
                !(SvmMath.Cos((fBulletRange - dist) * K.TazerConeScale + K.TazerConeMin) < (fx.XAxis | (at - aimFrom).SafeNormal())))
            {
                continue;
            }

            if (p.bLyingOnGround)
            {
                at.Z += K.TazerLyingAimRaise;
            }

            if (LineBlocked(this, at, traceFrom))
            {
                continue;
            }

            DamagePawn(p, merc, at, merc.Location, CK.TaserPill);
            last = SpawnBeam(merc, fx.Origin, (at - merc.Location).Rotation(), p) ?? last;
        }

        if (last?.PawnTarget is { } target)
        {
            FVector d = target.Location - merc.Location;
            int yaw = (short)TruncRotator(SvmMath.Atan2(d.Y, d.X));
            int pitch = (short)TruncRotator(SvmMath.Atan2(d.Z, SvmMath.Sqrt(d.X * d.X + d.Y * d.Y)));
            FVector n = d.SafeNormal();
            int side = fx.XAxis.Y * n.X - fx.XAxis.X * n.Y >= 0f ? -1 : 1;
            (merc.Controller as SPlayerController)?.ForceRotateTo(new FRotator(pitch, yaw, 0), side, K.TazerForceRotateStep);
            return;
        }

        FVector x = fx.XAxis;
        var along = new FRotator(TruncRotator(SvmMath.Atan2(x.Z, SvmMath.Sqrt(x.X * x.X + x.Y * x.Y))), TruncRotator(SvmMath.Atan2(x.Y, x.X)), 0);
        SpawnBeam(merc, fx.Origin, along, merc);
    }

    /// <summary>A beam at the FX bone from <paramref name="Merc"/> to <paramref name="Target"/> (the merc himself: a miss).</summary>
    private STazerEmitter? SpawnBeam(SBasePawn Merc, FVector At, FRotator Rotation, SBasePawn Target)
    {
        STazerEmitter? beam = Spawn<STazerEmitter>(Owner, Name.None, At, Rotation);
        if (beam is not null)
        {
            beam.PawnOwner = Merc as SPAWNDEFENSE;
            beam.PawnTarget = Target;
        }

        return beam;
    }

    /// <summary>The taser's box line (15 uu, flags 0x86) hits something before <paramref name="End"/>.</summary>
    internal static bool LineBlocked(Actor Source, FVector End, FVector Start) => LineHit(Source, End, Start, out _);

    /// <summary>The taser's box line (15 uu, flags 0x86); true and the hit when it stops before <paramref name="End"/>.</summary>
    internal static bool LineHit(Actor Source, FVector End, FVector Start, out FCheckResult Hit)
    {
        Hit = FCheckResult.NoHit(End);
        if (GadgetWorld.Collision(Source) is not { } world)
        {
            return false;
        }

        var extent = new FVector(K.TazerTraceExtent, K.TazerTraceExtent, K.TazerTraceExtent);
        return world.LineCheck(out Hit, Source, End, Start, extent, (ETraceFlags)K.TazerTraceFlags) && Hit.bHit && Hit.Time < 1f;
    }

    /// <summary>Radians to rotator units as the native does it: x 10430.21875, truncated toward zero.</summary>
    private static int TruncRotator(float Radians) => (int)(Radians * K.TazerRadToRotator);

    /// <summary>
    /// The taser hit (rule 27): no damage argument, the taser pill whatever <paramref name="PillTag"/> says, no refill
    /// share; an enemy (or anyone in Merc Deathmatch) counts a taser hit for the merc.
    /// </summary>
    [Uc("SBase.SGadTazerLauncher.DamagePawn", Sha1 = "149c94624a75b9e95a936d6a2da8f71b1dfeac00")]
    [PortOf("uc:SBase.SGadTazerLauncher:DamagePawn", Spec = "spec/combat/shock-and-taser.md#taser-and-taser-like-hits")]
    public void DamagePawn(Pawn? Touched, Pawn? PawnDef, FVector HitLocation, FVector StartTrace, int PillTag)
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
