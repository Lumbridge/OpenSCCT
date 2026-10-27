using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn script notifies of the back-to-wall draw clips (BackStSpBgL/R, BackCrSpBgL/R on the draw channel)
// and of the throw clip (ThroStAlEdH, ThroCrAlEdH). Written from spec/movement/back-to-wall.md (Firing round the
// corner) and spec/objectives/disk-flag.md (Throwing a hand item). Registered by Movement/MovementNotifies.cs.
public partial class SBasePawn
{
    private static readonly Name NAME_BackStSpBgL = new("BackStSpBgL");
    private static readonly Name NAME_BackStSpBgR = new("BackStSpBgR");
    private static readonly Name NAME_BackCrSpBgL = new("BackCrSpBgL");
    private static readonly Name NAME_BackCrSpBgR = new("BackCrSpBgR");

    /// <summary>
    /// The left draw clip's notify: drawing (forward) puts the pawn in the fire pose of its stance with the arm on the
    /// aim; holstering (backward) back in the peek pose.
    /// </summary>
    [Uc("SBase.SBasePawn.NotifyTakeArmControlForBTWFireLeft", Sha1 = "59b63fcd8d3983e7f1e20d8844c05d50828dabba")]
    [PortOf("uc:SBase.SBasePawn:NotifyTakeArmControlForBTWFireLeft", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    public virtual void NotifyTakeArmControlForBTWFireLeft() => TakeArmControlForBTWFire(BTW_.BTWFireStandLeft, BTW_.BTWFireCrouchLeft,
        BTW_.BTWStandLeft, BTW_.BTWCrouchLeft);

    /// <summary>The right draw clip's notify (as the left one, right poses).</summary>
    [Uc("SBase.SBasePawn.NotifyTakeArmControlForBTWFireRight", Sha1 = "0a037d8850a25e568fb0d9dab879ca0a04e742e9")]
    [PortOf("uc:SBase.SBasePawn:NotifyTakeArmControlForBTWFireRight", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    public virtual void NotifyTakeArmControlForBTWFireRight() => TakeArmControlForBTWFire(BTW_.BTWFireStandRight, BTW_.BTWFireCrouchRight,
        BTW_.BTWStandRight, BTW_.BTWCrouchRight);

    private void TakeArmControlForBTWFire(BTW_ fireSt, BTW_ fireCr, BTW_ peekSt, BTW_ peekCr)
    {
        if (BTW == BTW_.NOBTW)
        {
            return;
        }

        if (!PawnAnim.IsAnimBackward(DRAWCHANNEL))
        {
            BTW = bIsCrouched ? fireCr : fireSt;
            IK_Type = IK_Type_.IKT_BTW;
        }
        else
        {
            BTW = bIsCrouched ? peekCr : peekSt;
        }
    }

    /// <summary>True while the draw channel plays one of the four back-to-wall draw clips.</summary>
    internal bool IsBtwDrawClip()
    {
        // The draw channel keeps its last sequence, so the notify must come from that channel too.
        if (PawnAnim.NotifyChannel != DRAWCHANNEL)
        {
            return false;
        }

        Name a = PawnAnim[DRAWCHANNEL].Sequence;
        return a == NAME_BackStSpBgL || a == NAME_BackStSpBgR || a == NAME_BackCrSpBgL || a == NAME_BackCrSpBgR;
    }

    /// <summary>
    /// NotifyDrawUndrawWeapon and NotifyChangeHandWeapon of the back-to-wall draw clips: the spy's quick draw (or
    /// holster when the clip plays backward) and, on the right clips, the pistol to the other hand. Other clips that
    /// carry these notifies are left to their own call sites (the traversal draws load the weapon at the action).
    /// </summary>
    [PortOf("uc:SBase.SPawnAttaque:NotifyDrawUndrawWeapon", Spec = "spec/movement/back-to-wall.md#firing-round-the-corner-s_btwfire-states")]
    internal void BtwDrawNotify(bool bChangeHand)
    {
        if (this is not SPawnAttaque spy || !IsBtwDrawClip())
        {
            return;
        }

        if (bChangeHand)
        {
            PawnWeapons.NotifyChangeHandWeapon(spy);
        }
        else
        {
            PawnWeapons.SpyNotifyDrawUndrawWeapon(spy);
        }
    }

    /// <summary>The throw clip's release frame: only the local player's own pawn throws (its controller sends it).</summary>
    [Uc("SBase.SBasePawn.AnimNotifyThrow", Sha1 = "a2c65b84bc112ff6ad11e11a97ff7992371803a8")]
    [PortOf("uc:SBase.SBasePawn:AnimNotifyThrow", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
    public virtual void AnimNotifyThrow()
    {
        if (!ReferenceEquals(Level?.GetLocalPlayerController()?.Pawn, this))
        {
            return;
        }

        (Controller as SPlayerController)?.ThrowObject();
    }
}
