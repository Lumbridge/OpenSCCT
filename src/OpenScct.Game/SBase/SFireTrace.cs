using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFireTrace: the rifle tracer (spec/equipment/merc-rifle.md rule 23), a 2 s projectile-physics effect that
/// flies at FTSpeed toward the replicated impact point. Cosmetic: nothing reads it back.
/// </summary>
public partial class SFireTrace
{
    private static readonly Name CNAME_s_Binoculars_Def = new("s_Binoculars_Def");

    /// <summary>
    /// The start point on each client: the third-person gun when the shooter has no controller here, 75 uu above the
    /// shooter when he is scoped, else his first-person gun; then the velocity toward the impact.
    /// </summary>
    [Uc("SBase.SFireTrace.PostNetBeginPlay", Sha1 = "db56ef5ecb3809320e74ad1c5f79060192a2aba5")]
    [PortOf("uc:SBase.SFireTrace:PostNetBeginPlay", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (Instigator is not SBasePawn shooter)
        {
            return;
        }

        ICombatPorts ports = CombatServices.Ports(this);
        FVector start;
        if (shooter.Controller is null)
        {
            start = (shooter as SPAWNDEFENSE)?.WeaponDummy is { } dummy
                ? ports.BoneCoords(dummy, SWeaponDefense.FxBone).Origin
                : shooter.Location;
        }
        else if (shooter.Controller.GetStateName() == CNAME_s_Binoculars_Def)
        {
            start = shooter.Location + new FVector(0f, 0f, K.FireTraceScopeRaise);
        }
        else
        {
            start = shooter.MyWeapon is { } weapon ? ports.BoneCoords(weapon, SWeaponDefense.FxBone).Origin : shooter.Location;
        }

        SetLocation(start);
        FVector dir = (ImpactLoc - start).SafeNormal();
        Rotation = dir.Rotation();
        InitVelocity = dir * FTSpeed;
        Velocity = InitVelocity;
    }
}
