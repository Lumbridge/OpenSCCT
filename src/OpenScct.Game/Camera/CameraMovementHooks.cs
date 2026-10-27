using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>
/// The movement port's camera calls answered by the camera port: SetCameraMode becomes the controller camera's
/// SetMode, CameraSmooth its ActivateSmooth (the script's m_camera.SetMode and m_camera.ActivateSmooth). Every other
/// call is passed on. Install it as (or under) the level's <see cref="MovementService.Hooks"/>.
/// </summary>
public sealed class CameraMovementHooks : IMovementHooks
{
    public CameraMovementHooks(IMovementHooks? inner = null)
    {
        Inner = inner ?? MovementHooksLocal.Instance;
    }

    public IMovementHooks Inner { get; }

    [PortOf("nat:SBase.SPlayerCam.SetMode", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    public void SetCameraMode(SPlayerController PC, ECamMode Mode)
    {
        PlayerCamera.Of(PC)?.SetMode(Mode);
        Inner.SetCameraMode(PC, Mode);
    }

    [PortOf("uc:SBase.SPlayerCam:ActivateSmooth", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void CameraSmooth(SPlayerController PC, float Smooth)
    {
        PlayerCamera.Of(PC)?.ActivateSmooth(Smooth);
        Inner.CameraSmooth(PC, Smooth);
    }

    public void PlayWeaponClip(SPlayerController PC, MovementWeaponClip Clip) => Inner.PlayWeaponClip(PC, Clip);

    public void SetWeaponLoaded(SBasePawn Pawn, int Num) => Inner.SetWeaponLoaded(Pawn, Num);

    public void SwitchLaser(SPlayerController PC, bool On) => Inner.SwitchLaser(PC, On);

    public bool UseSearch(SPlayerController PC, int Step, bool bCantRollCuzColl) => Inner.UseSearch(PC, Step, bCantRollCuzColl);

    public void DiscardPickup(SPlayerController PC) => Inner.DiscardPickup(PC);

    public void PlayAnimDeath(SPlayerController PC) => Inner.PlayAnimDeath(PC);

    public void FootPrint(SBasePawn Pawn, bool bTrueIfLeftFoot) => Inner.FootPrint(Pawn, bTrueIfLeftFoot);

    public void LaunchQuickGadget(SPlayerController PC) => Inner.LaunchQuickGadget(PC);

    public void PerformDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => Inner.PerformDropAttack(PC, PawnToAttack);

    public void TellServerForDropAttack(SPlayerController PC, SBasePawn PawnToAttack) => Inner.TellServerForDropAttack(PC, PawnToAttack);

    [PortOf("uc:SBase.SPlayerCam:AttachCamToBone", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void AttachCameraToBone(SPlayerController PC, bool YesNo, Name BoneName, FVector Offset)
    {
        PlayerCamera.Of(PC)?.AttachCamToBone(YesNo, BoneName, Offset);
        Inner.AttachCameraToBone(PC, YesNo, BoneName, Offset);
    }
}
