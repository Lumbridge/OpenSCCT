using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn's walk blender: the post-tick update of the animation channels (spec/native/ANSWERS-anim.md A6),
// ported in OpenScct.Game.Animation.PawnMovementAnimation; the climb step of the vertical pipe and the ladder that decides
// where a climb may stop is one of its branches (spec/native/ANSWERS-fidelity.md, physOnPipe bCanStop).
public partial class SBasePawn
{
    /// <summary>The channel the climb clip (ForwardAnim) plays on going up, and the one going down.</summary>
    [Provenance(ProvenanceKind.Decoded, "editor 0x11200140: channels 5 and 6")]
    public const int ClimbChannel = 5;

    [Provenance(ProvenanceKind.Decoded, "editor 0x11200140: channels 5 and 6")]
    public const int ClimbDownChannel = 6;

    /// <summary>
    /// ASBasePawn::UpdateMovementAnimation, called by the pawn's post-tick after physics (editor 0x11205ef0 slot
    /// 0x160): the walk blender over channels 1 to 15 and the climb step (<see cref="PawnMovementAnimation"/>).
    /// </summary>
    [UcNative("ASBasePawn::UpdateMovementAnimation", Editor = 0x112008a0)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/ANSWERS-anim.md#a6-walk-blender-asbasepawnupdatemovementanimation-0x112008a0-complete")]
    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0 calls 0x11200140 for Physics 6 or 13 with bBeginSpecial12Anim")]
    public void UpdateMovementAnimation(float DeltaTime)
    {
        if (MovementService.Of(this) is null)
        {
            return;
        }

        PawnMovementAnimation.Update(this, DeltaTime);
    }
}
