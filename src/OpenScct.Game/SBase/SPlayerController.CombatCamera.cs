using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The head camera of the combat states (grabbed, charged, knocked out, standing up, the finishing move), raised as
// CombatFx.HeadCamera and applied through the movement hooks' camera call, as the ladder's camera is.
public partial class SPlayerController
{
    /// <summary>
    /// m_camera.AttachCamToBone(<paramref name="On"/>, 'B Head', <paramref name="Offset"/>): the first-person view
    /// follows the head bone. Detaching also clears bFirstPersonWithPawn, which the original's HUD clears after every
    /// frame (SHud.PostRender) and the port only clears here and in the ladder's camera reset.
    /// </summary>
    [PortOf("uc:SBase.SPlayerCam:AttachCamToBone", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    [Provenance(ProvenanceKind.Estimated, "bFirstPersonWithPawn cleared on detach in place of SHud.PostRender's per-frame clear")]
    internal void CombatHeadCamera(bool On, FVector Offset)
    {
        MovementService.HooksOf(this).AttachCameraToBone(this, On, On ? MovementK.MercLadderCameraBone : default, On ? Offset : default);
        if (!On)
        {
            bFirstPersonWithPawn = false;
        }
    }
}
