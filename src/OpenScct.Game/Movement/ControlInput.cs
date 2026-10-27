using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The control variables SPlayerController.NativeComputeControlVariables writes each frame, from the input bytes
/// and axes the input layer stored on the controller (spec/native/input.md rules 1 to 3).
/// <para>
/// A held direction key gives a stick value of 6000 times the speed mode (GetSpeedMode: 1.0 fast or merc, 0.4 slow
/// spy), opposite keys cancel, and fNormePadMove is the sum of the two key axes times the speed mode, capped at 1.
/// iMove: on a ledge or a fence the raw stick against 2100 gives left, right, forward and back; otherwise the stick
/// up and down bits (16, 32) come from aForward against 3000 (walking, falling or the merc) or, for a spy climbing,
/// from the normalised stick's direction on the screen (any speed mode), and left, right,
/// forward and back from the move in the camera's flattened axes measured along the pawn's own axes, so the keys
/// are camera relative: with the pawn facing the camera, the left key moves it to its right. The mouse axes go to
/// fTurnValue and fLookUpValue unchanged.
/// </para>
/// </summary>
public static class ControlInput
{
    [UcNative("ASPlayerController::execNativeComputeControlVariables", Exe = 0x10aaf400, Editor = 0x111f37d0)]
    [PortOf("nat:SBase.SPlayerController.NativeComputeControlVariables", Spec = "spec/native/input.md#3-imove-bits")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111f37d0 (iMove bits), 0x111e6460 (input mapping): 6000 x k per key, 2100 on a ledge or fence, 3000 stick up and down, camera-relative 2100 sideways, 4500 forward (3000 held)")]
    [Provenance(ProvenanceKind.Estimated, "stick up and down for a spy climbing (pipes, ladders, zip lines): the screen direction of the climb is taken as the screen's up (ClimbStickUp); the mouse path")]
    public static void Compute(SPlayerController pc, float dt, ref bool lastUse)
    {
        float mode = pc.GetSpeedMode();
        float forward = Axis(pc.BT_Up, pc.BT_Down);
        float strafe = Axis(pc.BT_Right, pc.BT_Left);
        pc.aForward = forward * MovementK.StickFullScale * mode;
        pc.aStrafe = strafe * MovementK.StickFullScale * mode;
        float norm = (SvmMath.Abs(strafe) + SvmMath.Abs(forward)) * mode;
        pc.fNormePadMove = norm > 1f ? 1f : norm;

        pc.fTurnValue = pc.aTurn;
        pc.fLookUpValue = pc.aLookUp;
        float cam = SvmMath.Sqrt(pc.fTurnValue * pc.fTurnValue + pc.fLookUpValue * pc.fLookUpValue) / MovementK.StickFullScale;
        pc.fNormePadCam = cam > 1f ? 1f : cam;

        bool use = pc.bUsing != 0;
        pc.BT_UseJusteAppuye = use && !lastUse;
        lastUse = use;

        if (pc.iDontUpdatePadInput != 0)
        {
            Freeze(pc);
        }

        int last = pc.iMove;
        pc.iMove = MoveBits(pc, last);
    }

    /// <summary>
    /// The input freeze (a sticky shock): with iDontUpdatePadInput set the stick, the mouse and the action bytes
    /// read as zero, so the pawn neither walks nor turns (spec/native/input.md rule 2.2). fNormePadMove keeps its
    /// value, as in the native.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: iDontUpdatePadInput zeroes aForward, aStrafe, aLookUp, aTurn, fLookUpValue, fTurnValue, fNormePadCam, fNormePadCamSmoothee, bPressedJump, bHoldBreath, bFireMode, bUsing, bUseGadget, bUnCrouchAndUnDraw, bUseBackToWall")]
    private static void Freeze(SPlayerController pc)
    {
        pc.aForward = 0f;
        pc.aStrafe = 0f;
        pc.aLookUp = 0f;
        pc.aTurn = 0f;
        pc.fLookUpValue = 0f;
        pc.fTurnValue = 0f;
        pc.fNormePadCam = 0f;
        pc.fNormePadCamSmoothee = 0f;
        pc.bPressedJump = false;
        pc.bHoldBreath = 0;
        pc.bFireMode = 0;
        pc.bUsing = 0;
        pc.bUseGadget = 0;
        pc.bUnCrouchAndUnDraw = 0;
        pc.bUseBackToWall = 0;
    }

    /// <summary>The iMove mask (spec/native/input.md rule 3).</summary>
    private static int MoveBits(SPlayerController pc, int last)
    {
        if (pc.SBPawn is not { } pawn)
        {
            return SPlayerController.cMoveNone;
        }

        int move = SPlayerController.cMoveNone;
        if (pawn.Physics is EPhysics.PHYS_OnVerticalPlane or EPhysics.PHYS_Ledge)
        {
            if (pc.aForward > MovementK.LedgeStick)
            {
                move |= SPlayerController.cMoveForward;
            }
            else if (pc.aForward < -MovementK.LedgeStick)
            {
                move |= SPlayerController.cMoveBack;
            }

            if (pc.aStrafe > MovementK.LedgeStick)
            {
                move |= SPlayerController.cMoveRight;
            }
            else if (pc.aStrafe < -MovementK.LedgeStick)
            {
                move |= SPlayerController.cMoveLeft;
            }

            return move;
        }

        if (pawn.Physics is EPhysics.PHYS_Walking or EPhysics.PHYS_Falling || pc.SPawnType == PawnType.SPAWNDEFENSE)
        {
            if (pc.aForward > SPlayerController.cMinPadValue)
            {
                move |= SPlayerController.cMoveUp;
            }
            else if (pc.aForward < -SPlayerController.cMinPadValue)
            {
                move |= SPlayerController.cMoveDown;
            }
        }
        else
        {
            // A spy climbing (ladder, pipe, zip line...): the stick (aStrafe, -aForward, 0.01) normalised, against the
            // screen direction of the climb, so the speed mode does not matter.
            float up = ClimbStickUp(pc.aStrafe, pc.aForward);
            if (up > MovementK.ClimbStickCos)
            {
                move |= SPlayerController.cMoveUp;
            }
            else if (up < -MovementK.ClimbStickCos)
            {
                move |= SPlayerController.cMoveDown;
            }
        }

        // The move in the camera's flattened axes, measured along the pawn's facing and its left axis (F x Up).
        pc.Rotation.GetAxes(out FVector cx, out FVector cy, out _);
        FVector x = Flat(cx);
        FVector y = Flat(cy);
        FVector m = x * pc.aForward + y * pc.aStrafe;
        FVector f = pawn.Rotation.Vector();
        FVector left = f ^ new FVector(0f, 0f, 1f);
        float side = m | left;
        float along = (m.X * f.X + m.Y * f.Y) / SvmMath.Sqrt(f.X * f.X + f.Y * f.Y);
        if (side < -MovementK.SideStick)
        {
            move |= SPlayerController.cMoveRight;
        }

        if (side > MovementK.SideStick)
        {
            move |= SPlayerController.cMoveLeft;
        }

        float ahead = (last & SPlayerController.cMoveForward) != 0 ? MovementK.ForwardStickHeld : MovementK.ForwardStick;
        if (along > ahead)
        {
            move |= SPlayerController.cMoveForward;
        }

        if (along < -MovementK.ForwardStick)
        {
            move |= SPlayerController.cMoveBack;
        }

        return move;
    }

    /// <summary>
    /// The climbing modes' up test: the stick on the screen (aStrafe right, -aForward down, 0.01 out of the screen),
    /// normalised, dotted with the screen direction of the climb. That direction is the projection of the pawn's
    /// axes, which is not fully decoded (spec/native/input.md open question 2); with the camera behind a climbing
    /// pawn it is the screen's up, which is used here.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "NativeComputeControlVariables editor 0x111f37d0: stick (aStrafe, -aForward, 0.01) normalised, dot with a normalised screen direction, +-0.45")]
    [Provenance(ProvenanceKind.Estimated, "the screen direction is taken as the screen's up (0, -1, 0) (input.md open question 2)")]
    internal static float ClimbStickUp(float aStrafe, float aForward)
    {
        float sy = -aForward;
        float len = SvmMath.Sqrt((aStrafe * aStrafe) + (sy * sy) + (MovementK.ClimbStickDepth * MovementK.ClimbStickDepth));
        return -sy / len;
    }

    private static FVector Flat(FVector v)
    {
        float k = 1f / SvmMath.Sqrt(v.X * v.X + v.Y * v.Y);
        return new FVector(v.X * k, v.Y * k, 0f);
    }

    private static float Axis(byte positive, byte negative) => (positive != 0 ? 1f : 0f) - (negative != 0 ? 1f : 0f);
}
