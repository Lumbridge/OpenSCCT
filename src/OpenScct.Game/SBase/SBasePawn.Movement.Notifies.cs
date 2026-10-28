using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn functions the movement clips call through their script notifies: the split jump's push, the
// Vandamme pistol draw and the hand-over-hand fire poses (spec/movement/wall-jump-split-vandamme.md,
// pipe-horizontal.md). The animation port's notify table routes the NotifyName of an AnimNotify_Script to these.
public partial class SBasePawn
{
    /// <summary>The split clip's push notify: the controller's ImpulsionSplit.</summary>
    [Uc("SBase.SBasePawn.ImpulsionSplit", Sha1 = "0421400580e697a4dc9fec07cba5b8b05310b3df")]
    [PortOf("uc:SBase.SBasePawn:ImpulsionSplit", Spec = "spec/movement/wall-jump-split-vandamme.md#split-jump")]
    public virtual void ImpulsionSplit()
    {
        if (Controller is SPlayerController pc)
        {
            pc.eventImpulsionSplit();
        }
    }

    /// <summary>
    /// The Vandamme draw clip's notify: drawing (the clip forward) gives the arm to the aim and the first-person split
    /// camera; holstering (backward) takes the arm back.
    /// </summary>
    [Uc("SBase.SBasePawn.NotifyTakeArmControlForSplitJump", Sha1 = "bb01bc9a3b916c4ed1cc7b25051c89100214632c")]
    [PortOf("uc:SBase.SBasePawn:NotifyTakeArmControlForSplitJump", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
    public virtual void NotifyTakeArmControlForSplitJump()
    {
        if (!PawnAnim[DRAWCHANNEL].bBackward)
        {
            bControlArmForVandamme = true;
            if (Controller is SPlayerController pc)
            {
                pc.MovementCamera(ECamMode.ECM_SplitJumpFP);
            }
        }
        else
        {
            bControlArmForVandamme = false;
        }
    }

    /// <summary>The Vandamme draw clip's notify: the brace loop and aim set of the pistol (forward) or of the bare hands (backward).</summary>
    [Uc("SBase.SBasePawn.ChangeAnimsForDrawUndrawSplitJump", Sha1 = "f62858ddf3329bf96939b6c4ca0e880b8f4e2159")]
    [PortOf("uc:SBase.SBasePawn:ChangeAnimsForDrawUndrawSplitJump", Spec = "spec/movement/wall-jump-split-vandamme.md#vandamme")]
    public virtual void ChangeAnimsForDrawUndrawSplitJump()
    {
        bool drawn = !PawnAnim[DRAWCHANNEL].bBackward;
        string set = drawn ? "1" : "0";
        PawnAnim.Play(0, Movement.PawnActions.Clip(this, new Name("SpltStAlNt" + set)), 1f, 0f, loop: true);
        RunAnim = new Name("SpltStAlUL" + set);
        ForwardAnim = new Name("SpltStAlUR" + set);
        LeftAnim = new Name("SpltStAlDL" + set);
        RightAnim = new Name("SpltStAlDR" + set);
    }

    /// <summary>The hand-over-hand draw clip's notify: the hanging fire pose and its camera.</summary>
    [Uc("SBase.SBasePawn.NotifyTakeArmControlForPipH2", Sha1 = "2a14c715ef69f9e2a0fc124f613127dfcefbf04c")]
    [PortOf("uc:SBase.SBasePawn:NotifyTakeArmControlForPipH2", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
    public virtual void NotifyTakeArmControlForPipH2()
    {
        PipHFire = PipHFire_.PipH2;
        if (Controller is SPlayerController pc)
        {
            pc.MovementCamera(ECamMode.ECM_HOHFP);
        }
    }

    /// <summary>The legs-over draw clip's notify: the upside-down fire pose and its camera.</summary>
    [Uc("SBase.SBasePawn.NotifyTakeArmControlForPipH4", Sha1 = "471d664becb50a570d15298b4f820ae11e4a2958")]
    [PortOf("uc:SBase.SBasePawn:NotifyTakeArmControlForPipH4", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
    public virtual void NotifyTakeArmControlForPipH4()
    {
        PipHFire = PipHFire_.PipH4;
        if (Controller is SPlayerController pc)
        {
            pc.MovementCamera(ECamMode.ECM_HOHFPHanged);
        }
    }

    /// <summary>
    /// A clip's collision notify during root motion: the move goes on with the extra physics off
    /// (bUseRootMotionAdditionnalPhysics with PHYS_None), so the pawn collides normally again.
    /// </summary>
    [Uc("SBase.SBasePawn.RMColOn", Sha1 = "e57bdf2a6a57d35f8e0efb095c58bb176deb25e6")]
    [PortOf("uc:SBase.SBasePawn:RMColOn", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public void RMColOn()
    {
        bUseRootMotionAdditionnalPhysics = true;
        RootMotionAdditionnalPhysics = EPhysics.PHYS_None;
    }

    /// <summary>A clip's noise notify: the local player hears this pawn (the merc's sound arrows), at full loudness.</summary>
    [Uc("SBase.SBasePawn.AnimNotifyNoise", Sha1 = "4c4a6166ec468b4fee86c16c9dcb95e871ab03db")]
    [PortOf("uc:SBase.SBasePawn:AnimNotifyNoise", Spec = "spec/movement/native-contracts.md#events-the-natives-raise-script-entry-points")]
    public void AnimNotifyNoise() => (Level.GetLocalPlayerController() as SPlayerController)?.ClientNotifyNoise(this, 1f);
}
