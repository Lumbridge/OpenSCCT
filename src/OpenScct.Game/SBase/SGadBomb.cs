using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

// The spy's "Modem" that arms bomb spots. Written from spec/objectives/bomb-target.md (rules 7 and 8).
public partial class SGadBomb
{
    private static readonly Name CNAME_ActivateBombTarget = new("ActivateBombTarget");

    /// <summary>On the spy's machine at the end of the plant animation: one charge used, then the server arms the spot.</summary>
    [Uc("SBase.SGadBomb.Use", Sha1 = "65e8398ec1e966535853c0fe0119229013456802")]
    [PortOf("uc:SBase.SGadBomb:Use", Spec = "spec/objectives/bomb-target.md#rules")]
    public override void Use(float Value)
    {
        LastUsed = Level.TimeSeconds;
        Charge--;
        ActivateBombTarget();
    }

    /// <summary>The server arms the spot chosen at the menu and credits the planting player.</summary>
    [Uc("SBase.SGadBomb.ActivateBombTarget", Sha1 = "5683018d8e6b9a467f01c2fe668d571c31fa2e36")]
    [PortOf("uc:SBase.SGadBomb:ActivateBombTarget", Spec = "spec/objectives/bomb-target.md#rules")]
    [PortOf("rep:SBase.SGadBomb.ActivateBombTarget", Spec = "spec/objectives/bomb-target.md#network")]
    public void ActivateBombTarget()
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController owner)
        {
            CombatServices.Net(this).ServerCall(owner, new CombatCall(CNAME_ActivateBombTarget) { Target = this });
            return;
        }

        if (BombTrigger is not { } spot)
        {
            return;
        }

        spot.eventTriggering(true);
        spot.ControllerWhoTakesIt = (Owner as Pawn)?.Controller as PlayerController;
    }

    /// <summary>
    /// Whether the plant can start (rule 8): the owner must walk and not roll (else "wrong position", 6); the refire
    /// delay must be over and a charge left, a tutorial map needing none (else "no modem left", 0).
    /// </summary>
    [UcNative("ASGadBomb::execCanUse", Exe = 0x10A9FBB0, Editor = 0x111DADE0)]
    [PortOf("nat:SBase.SGadBomb.CanUse", Spec = "spec/objectives/bomb-target.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "ASGadBomb::execCanUse: PHYS_Walking and sAction outside 130..132, then RefireRate since LastUsed and Charge or a tutorial level")]
    public override bool CanUseGadget()
    {
        if (Owner is { } owner &&
            (owner.Physics != EPhysics.PHYS_Walking ||
             (owner is SBasePawn pawn && pawn.sAction >= SBasePawn.SA_ROLLING && pawn.sAction <= SBasePawn.SA_ROLL_LAND)))
        {
            WhyCantUse = MK.WhyCantUseWrongPosition;
            return false;
        }

        if (RefireRate <= Level.TimeSeconds - LastUsed && (Charge >= 1 || IsTutorialLevel()))
        {
            return true;
        }

        WhyCantUse = MK.WhyCantUseNoModemLeft;
        return false;
    }

    /// <summary>SPersistentData.CurLevelIsTutorial for the current map: the TME and TSP tutorial maps.</summary>
    [Provenance(ProvenanceKind.Estimated, "Tutorial maps by name prefix (TME, TSP); the persistent data tutorial list is not ported")]
    private bool IsTutorialLevel()
    {
        string map = GameFlowServices.Host(this).GetURLMap() ?? string.Empty;
        return map.StartsWith("TME", System.StringComparison.OrdinalIgnoreCase) ||
               map.StartsWith("TSP", System.StringComparison.OrdinalIgnoreCase);
    }
}
