using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The SPlayerController helpers and natives of the ladder states (spec/movement/ladder.md): the ladder view rules of
// both teams, the merc's head camera on a ladder and its reset, the rung arithmetic, and the ceiling and side-change
// room natives (spec/native/ANSWERS-moveset.md 1 and 2).
public partial class SPlayerController
{
    /// <summary>
    /// The merc's aim on a ladder: the view yaw's offset from the pawn's yaw (wrapped into -32768..32768) is
    /// exaggerated by 1.4 and sent as the desired rotation (UpdateDesiredRotation); the local copy follows.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_OnLadder.PlayerMove", Spec = "spec/movement/ladder.md#common-ladder-behaviour-s_ladderbase-and-every-state-that-extends-it")]
    internal void MercLadderAim()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        int yawDiff = Rotation.Yaw - pawn.Rotation.Yaw;
        if (yawDiff > 32768)
        {
            yawDiff -= 65536;
        }
        else if (yawDiff < -32768)
        {
            yawDiff += 65536;
        }

        FRotator desired = Rotation;
        desired.Yaw = MovementMath.Int(pawn.Rotation.Yaw + MovementK.MercLadderAimFactor * yawDiff);
        MyDesiredRotation = desired;
        UpdateDesiredRotation(MyDesiredRotation);
        LocalMyDesiredRotation = MyDesiredRotation;
    }

    /// <summary>
    /// The view of most ladder states: the spy's view limited by the camera window around the pawn; the merc's
    /// exaggerated aim.
    /// </summary>
    internal void LadderView(float DeltaTime)
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.SPawnType == PawnType.SPAWNOFFENSE)
        {
            LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
        }
        else
        {
            MercLadderAim();
        }
    }

    /// <summary>
    /// The frame of the ladder states that only look and hold: the merc's head view, the ladder view rules, the move
    /// <paramref name="accel"/>.
    /// </summary>
    internal void LadderViewMove(float DeltaTime, FVector accel)
    {
        MercLadderFirstPerson();
        LadderView(DeltaTime);
        SendMove(DeltaTime, accel);
    }

    /// <summary>The merc's view on a ladder is the pawn's head view (bFirstPersonWithPawn), set every frame.</summary>
    internal void MercLadderFirstPerson()
    {
        if (SPawnType == PawnType.SPAWNDEFENSE)
        {
            bFirstPersonWithPawn = true;
        }
    }

    /// <summary>
    /// The merc's ladder camera: the camera on the head bone 13 forward and 9 up, the weapon not drawn, the behind
    /// view, no reticule, the first-person-with-pawn view.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_GrabLadder.BeginState", Spec = "spec/movement/ladder.md#grab-s_grabladder")]
    internal void MercLadderCamera()
    {
        MovementService.HooksOf(this).AttachCameraToBone(this, true, MovementK.MercLadderCameraBone, MovementK.MercLadderCameraOffset);
        if (SBPawn?.MyWeapon is SWeaponDefense rifle)
        {
            rifle.bDrawWeapon = false;
        }

        bBehindView = true;
        bNoReticule = true;
        bFirstPersonWithPawn = true;
    }

    /// <summary>The merc's camera off the head bone, the reticule back unless the laser was on.</summary>
    internal void MercLadderCameraOff()
    {
        MovementService.HooksOf(this).AttachCameraToBone(this, false, default, default);
        bBehindView = false;
        if (!bLaserEtaitAllume)
        {
            bNoReticule = false;
        }

        bFirstPersonWithPawn = false;
    }

    /// <summary>
    /// The merc leaves the ladder camera: camera off the head, the view and the desired rotation back to the pawn's
    /// rotation.
    /// </summary>
    [Uc("SBase.SPlayerController.ResetMercCam", Sha1 = "f45698612e9aa74fa6510304e4fb1ffe3151899c")]
    [PortOf("uc:SBase.SPlayerController:ResetMercCam", Spec = "spec/movement/ladder.md#merc-weapon-at-the-bottom-s_endladderdrawweapon")]
    public void ResetMercCam()
    {
        MercLadderCameraOff();
        if (SBPawn is { } pawn)
        {
            Rotation = pawn.Rotation;
        }

        MyDesiredRotation = Rotation;
        UpdateDesiredRotation(MyDesiredRotation);
        LocalMyDesiredRotation = MyDesiredRotation;
    }

    /// <summary>ActivateSmooth() without a value: the camera's own default for the pawn's side.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerCam.ActivateSmooth: a value of 0 takes the side's default (camera.md rule 8.3)")]
    internal const float CameraSmoothDefault = 0f;

    /// <summary>Room to climb out at the ladder top (the physics driver's bTestTopLadder): true when both boxes are clear.</summary>
    [UcNative("ASPlayerController::execbTestTopLadder", Exe = 0x10ab1140, Editor = 0x111e7f10)]
    [PortOf("nat:SBase.SPlayerController.bTestTopLadder", Spec = "spec/native/physics-ge-queries.md")]
    public bool bTestTopLadder(out int iCanUncrouch)
    {
        iCanUncrouch = 0;
        return SBPawn is { } pawn && MovementNatives.Driver(pawn) is { } driver && driver.bTestTopLadder(pawn, out iCanUncrouch);
    }

    /// <summary>The ladder's lower and upper end heights.</summary>
    internal static void LadderEnds(SBasePawn pawn, out float bottom, out float top)
    {
        bottom = pawn.ZipP1.Z < pawn.ZipP2.Z ? pawn.ZipP1.Z : pawn.ZipP2.Z;
        top = pawn.ZipP1.Z > pawn.ZipP2.Z ? pawn.ZipP1.Z : pawn.ZipP2.Z;
    }

    /// <summary>
    /// Room above the head to climb one more rung: a (35, 35, 87.5) box from the pawn 64 uu up (flags 0x2df) is clear.
    /// </summary>
    [UcNative("ASPlayerController::execCheckCeilingForLadder", Exe = 0x10aa3a40, Editor = 0x111e44c0)]
    [PortOf("nat:SBase.SPlayerController.CheckCeilingForLadder", Spec = "spec/native/ANSWERS-moveset.md#1-checkceilingforladder")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 1: box (35, 35, 87.5) from Location to Location + 64 Z, flags 0x2df, true when clear")]
    public bool CheckCeilingForLadder()
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        FVector l = pawn.Location;
        return BoxClear(pawn, l, l + new FVector(0f, 0f, MovementK.LadderRungSpacing), MovementK.StandingBox);
    }

    /// <summary>
    /// Room to swing round to the far side of the ladder: from 2 uu behind the pawn a standing box goes 70 aside (left
    /// with <paramref name="bLeft"/>), then forward 2 m_NarrowLadderArmsZone.X + 4 past the ladder, then back in to
    /// that distance in front of the pawn; every leg must be clear (flags 0x2df).
    /// </summary>
    [UcNative("ASPlayerController::execCheckRoomToChangeSideLadder", Exe = 0x10aa3b00)]
    [PortOf("nat:SBase.SPlayerController.CheckRoomToChangeSideLadder", Spec = "spec/native/ANSWERS-moveset.md#2-checkroomtochangesideladder")]
    [Provenance(ProvenanceKind.Decoded, "ANSWERS-moveset.md 2: start L - 2 X, 70 along +-Y, then (2 zone.X + 4) X, then to L + (2 zone.X + 4) X; box (35, 35, 87.5), flags 0x2df")]
    public bool CheckRoomToChangeSideLadder(bool bLeft)
    {
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        pawn.Rotation.GetAxes(out FVector x, out FVector y, out _);
        if (bLeft)
        {
            y = -y;
        }

        FVector l = pawn.Location;
        float across = 2f * pawn.m_NarrowLadderArmsZone.X + MovementK.LadderSideChangeExtra;
        FVector a = l - x * MovementK.LadderSideChangeBack;
        FVector b = a + y * MovementK.LadderSideChangeAside;
        FVector c = b + x * across;
        FVector d = l + x * across;
        return BoxClear(pawn, a, b, MovementK.StandingBox) && BoxClear(pawn, b, c, MovementK.StandingBox) &&
               BoxClear(pawn, c, d, MovementK.StandingBox);
    }

    /// <summary>A box trace of the traversal natives (flags 0x2df by default): true when nothing blocks it.</summary>
    internal static bool BoxClear(Actor source, FVector start, FVector end, FVector extent, uint flags = MovementK.TraceClearance)
    {
        if (MovementNatives.Driver(source) is not { } driver)
        {
            return true;
        }

        return !driver.Collision.LineCheck(out _, source, end, start, extent, (OpenScct.Physics.Collision.ETraceFlags)flags);
    }
}
