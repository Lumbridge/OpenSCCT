using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefenseDummy_Shotgun: the third-person shotgun (spec/equipment/merc-guns.md). The server's pellet
/// trace stores the near and far hit ranges here (replicated); each machine that sees a new shot from it plays the
/// muzzle effects and throws the pellet marks, except the shooter's own client, which drew its marks when it fired.
/// </summary>
public partial class SWeaponDefenseDummy_Shotgun
{
    private static readonly Name BoneFx = new("FX");

    /// <summary>
    /// The muzzle step of the tick: nothing until the near range changes (a new shot whose near range equals the
    /// last one shows nothing, as in the original), then one count: the muzzle effects where nobody sees the
    /// first-person gun, and the pellet marks on the server and on the other players' machines.
    /// </summary>
    [Uc("SBase.SWeaponDefenseDummy_Shotgun.Tick", Sha1 = "9f2810e3a0a4b05793909359b831f96b351787e8")]
    [PortOf("uc:SBase.SWeaponDefenseDummy_Shotgun:Tick", Spec = "spec/equipment/merc-guns.md#rules")]
    private protected override void TickMuzzle()
    {
        if (LocalMinDistanceCheck == MinProjDistance)
        {
            return;
        }

        if (MuzzleCount > MyMuzzleCount)
        {
            MyMuzzleCount += 1;
            bool othersShot = !ReferenceEquals(Level.GetLocalPlayerController(), Instigator?.Controller);
            if ((Instigator is SBasePawn { MyWeapon: null } || (Role == ENetRole.ROLE_Authority && othersShot)) &&
                Level.NetMode != ENetMode.NM_DedicatedServer)
            {
                ClientFireMuzzle();
            }

            if (Role == ENetRole.ROLE_Authority || othersShot)
            {
                SpawnImpacts(false);
            }
        }

        LocalMinDistanceCheck = MinProjDistance;
    }

    /// <summary>
    /// The pellet marks of a shot seen from outside: thrown from the first-person gun's FX bone when this machine has
    /// one, else from this gun's, over the replicated ranges.
    /// </summary>
    [Uc("SBase.SWeaponDefenseDummy_Shotgun.SpawnImpacts", Sha1 = "01711685c4060aa1d3e6f558efa1482a7cb1f990")]
    [PortOf("uc:SBase.SWeaponDefenseDummy_Shotgun:SpawnImpacts", Spec = "spec/equipment/merc-guns.md#rules")]
    public void SpawnImpacts(bool bTrace)
    {
        if (Instigator is not SBasePawn pawn)
        {
            return;
        }

        Actor from = pawn.MyWeapon is { } gun ? gun : this;
        FCoords fx = CombatServices.Ports(this).BoneCoords(from, BoneFx);
        SWeaponDefense_Shotgun.SpawnPelletMarks(this, fx, MinProjDistance, MaxProjDistance);
    }
}
