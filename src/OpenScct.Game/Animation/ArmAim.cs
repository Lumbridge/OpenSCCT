using OpenScct.Core;
using OpenScct.Game.Camera;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The arm cases of the script event SBasePawn.TurnHead (spec/native/aim-ik.md rule 5): while the pawn fires round a
/// corner (back to wall), fires holding a grabbed body, fires hanging from a pipe or fires braced in a split jump,
/// the script turns an upper arm bone toward the view (or, drawing the corner weapon backward, toward a fixed pose
/// off the pawn's facing), at most a fixed step per axis per tick from where the bone is drawn. When no case holds a
/// bone any more the arms (and the head) are released. The rotations go to the mesh instance's bone direction
/// controllers (<see cref="MeshBoneDirections"/>), which the view applies.
/// </summary>
public static class ArmAim
{
    private static readonly Name RightUpperArm = new("B R UpperArm");
    private static readonly Name LeftUpperArm = new("B L UpperArm");
    private static readonly Name RightForeArm = new("B R ForeArm");
    private static readonly Name Head = new("B Head");

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeRightArmRot, ChangeLeftArmRot, ChangeRightArmRotGrab, ChangeRightArmRotPipH2, ChangeRightArmRotPipH4: iRotMax = int(100000 x Level.m_dT)")]
    private const float ArmSpeed = 100000f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.OrienteForVandamme: iRotSpeed = int(150000 x Level.m_dT)")]
    private const float SplitJumpArmSpeed = 150000f;

    /// <summary>
    /// The switch of TurnHead past the weapon aim: the arm case of the pawn's IK type, then the release of every
    /// controlled bone on the first tick no case holds one (bDidWeTurnABone).
    /// </summary>
    [Uc("SBase.SBasePawn.TurnHead", Sha1 = "fa2992fa34ca759dc05b2b534fbda9b5c59bef60")]
    [PortOf("uc:SBase.SBasePawn:TurnHead", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.TurnHead cases IKT_JCVD, IKT_BTW, IKT_GRAB_FIREMODE, IKT_PIPH and the bDidWeTurnABone release")]
    public static void Control(SBasePawn pawn, float dt)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        bool controlled = false;
        int yaw = pawn.iYawPitchFromView & 0xffff;
        int pitch = (pawn.iYawPitchFromView >> 16) & 0xffff;
        switch (pawn.IK_Type)
        {
            case IK_Type_.IKT_JCVD:
                if (pawn.bControlArmForVandamme)
                {
                    controlled = true;
                    OrienteForVandamme(pawn, yaw, pitch, dt);
                }

                break;
            case IK_Type_.IKT_BTW:
                if (pawn.BTW >= BTW_.BTWFireStandLeft && pawn.BTW <= BTW_.BTWFireCrouchRight)
                {
                    controlled = true;
                    Btw(pawn, yaw, pitch, dt);
                }

                break;
            case IK_Type_.IKT_GRAB_FIREMODE:
                controlled = true;
                ChangeRightArmRotGrab(pawn, yaw, pitch, dt);
                break;
            case IK_Type_.IKT_PIPH:
                if (pawn.PipHFire == PipHFire_.PipH4)
                {
                    ChangeRightArmRotPipH4(pawn, yaw, pitch, dt);
                    controlled = true;
                }
                else if (pawn.PipHFire == PipHFire_.PipH2)
                {
                    ChangeRightArmRotPipH2(pawn, yaw, pitch, dt);
                    controlled = true;
                }

                break;
            default:
                break;
        }

        if (pawn.bDidWeTurnABone)
        {
            if (!controlled)
            {
                // The script also releases a B Spine1 rotation; nothing in the port sets one.
                MeshBoneDirections d = pawn.PawnAnim.Directions;
                d.SetBoneDirection(RightUpperArm, default, 0f);
                d.SetBoneDirection(LeftUpperArm, default, 0f);
                d.SetBoneDirection(Head, default, 0f);
                pawn.bDidWeTurnABone = false;
            }
        }
        else
        {
            pawn.bDidWeTurnABone = controlled;
        }
    }

    /// <summary>
    /// The back-to-wall fire case: drawing the corner weapon backward (holstering) the arm on the corner side turns to
    /// a fixed pose off the pawn's facing; otherwise it aims at the view: the right arm round a left corner, the left
    /// arm round a right one.
    /// </summary>
    private static void Btw(SBasePawn pawn, int yaw, int pitch, float dt)
    {
        int facing = pawn.Rotation.Yaw;
        if (pawn.PawnAnim.IsAnimBackward(SBasePawn.DRAWCHANNEL))
        {
            switch (pawn.BTW)
            {
                case BTW_.BTWFireStandLeft:
                    ChangeRightArmRot(pawn, 25000 + facing, -4510, 22300, useRoll: true, dt: dt);
                    break;
                case BTW_.BTWFireStandRight:
                    ChangeLeftArmRot(pawn, -11322 + facing, -7751, -1077, useRoll: true, dt: dt);
                    break;
                case BTW_.BTWFireCrouchLeft:
                    ChangeRightArmRot(pawn, 24600 + facing, -5800, 23000, useRoll: true, dt: dt);
                    break;
                case BTW_.BTWFireCrouchRight:
                    ChangeLeftArmRot(pawn, -7700 + facing, -5500, -4160, useRoll: true, dt: dt);
                    break;
                default:
                    break;
            }
        }
        else if (pawn.BTW == BTW_.BTWFireStandLeft || pawn.BTW == BTW_.BTWFireCrouchLeft)
        {
            ChangeRightArmRot(pawn, yaw, pitch, dt: dt);
        }
        else
        {
            ChangeLeftArmRot(pawn, yaw, pitch, dt: dt);
        }
    }

    /// <summary>The right upper arm toward the view (offset pitch 19, yaw -586, roll -28243), or the given rotation with <paramref name="useRoll"/>.</summary>
    [Uc("SBase.SBasePawn.ChangeRightArmRot", Sha1 = "6dddba56e75c41738391a1dfa930d23fa0d5e307")]
    [PortOf("uc:SBase.SBasePawn:ChangeRightArmRot", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeRightArmRot: rot(19 + pitch, -586 + yaw, -28243) or (pitch, yaw, roll) with UseRoll, stepped per axis, SetBoneDirection('B R UpperArm', alpha 1, space 1)")]
    public static void ChangeRightArmRot(SBasePawn pawn, int yaw, int pitch, int roll = 0, bool useRoll = false, float dt = 0f)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        FRotator target = useRoll ? new FRotator(pitch, yaw, roll) : new FRotator(19 + pitch, -586 + yaw, -28243);
        Turn(pawn, RightUpperArm, target, ArmSpeed, dt);
    }

    /// <summary>The left upper arm toward the view (offset pitch -470, yaw 588, roll -3564), or the given rotation with <paramref name="useRoll"/>.</summary>
    [Uc("SBase.SBasePawn.ChangeLeftArmRot", Sha1 = "d1e652e5b09a4dfb30592a0c3e8225bc5b1418ae")]
    [PortOf("uc:SBase.SBasePawn:ChangeLeftArmRot", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeLeftArmRot: rot(-470 + pitch, 588 + yaw, -3564) or (pitch, yaw, roll) with UseRoll, stepped per axis, SetBoneDirection('B L UpperArm', alpha 1, space 1)")]
    public static void ChangeLeftArmRot(SBasePawn pawn, int yaw, int pitch, int roll = 0, bool useRoll = false, float dt = 0f)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        FRotator target = useRoll ? new FRotator(pitch, yaw, roll) : new FRotator(-470 + pitch, 588 + yaw, -3564);
        Turn(pawn, LeftUpperArm, target, ArmSpeed, dt);
    }

    /// <summary>Firing with a grabbed body: the right upper arm toward the view (pitch -2503, yaw 2770, roll -27380); released during a weapon transition.</summary>
    [Uc("SBase.SBasePawn.ChangeRightArmRotGrab", Sha1 = "71ba8cf7155ec4712c182b9d490d675e9b693669")]
    [PortOf("uc:SBase.SBasePawn:ChangeRightArmRotGrab", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeRightArmRotGrab: bWeaponTransit sets the controller's alpha to 0, else rot(-2503 + pitch, 2770 + yaw, -27380) stepped")]
    public static void ChangeRightArmRotGrab(SBasePawn pawn, int yaw, int pitch, float dt)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.bWeaponTransit)
        {
            pawn.PawnAnim.Directions.SetBoneDirection(RightUpperArm, default, 0f);
            return;
        }

        Turn(pawn, RightUpperArm, new FRotator(-2503 + pitch, 2770 + yaw, -27380), ArmSpeed, dt);
    }

    /// <summary>Hanging from a pipe by the legs (upside down): the right upper arm toward the view (pitch -1074, yaw 880, roll -400).</summary>
    [Uc("SBase.SBasePawn.ChangeRightArmRotPipH4", Sha1 = "aee0f2a130d19933e9f4742600f97fa6ee416032")]
    [PortOf("uc:SBase.SBasePawn:ChangeRightArmRotPipH4", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeRightArmRotPipH4: rot(-1074 + pitch, 880 + yaw, -400) stepped")]
    public static void ChangeRightArmRotPipH4(SBasePawn pawn, int yaw, int pitch, float dt)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        Turn(pawn, RightUpperArm, new FRotator(-1074 + pitch, 880 + yaw, -400), ArmSpeed, dt);
    }

    /// <summary>Hanging from a pipe by one hand: the right upper arm toward the view (pitch -1750, yaw -2040, roll -24250).</summary>
    [Uc("SBase.SBasePawn.ChangeRightArmRotPipH2", Sha1 = "d18b95aacbb20e489f9fb6cba2745f0014dec249")]
    [PortOf("uc:SBase.SBasePawn:ChangeRightArmRotPipH2", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.ChangeRightArmRotPipH2: rot(-1750 + pitch, -2040 + yaw, -24250) stepped")]
    public static void ChangeRightArmRotPipH2(SBasePawn pawn, int yaw, int pitch, float dt)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        Turn(pawn, RightUpperArm, new FRotator(-1750 + pitch, -2040 + yaw, -24250), ArmSpeed, dt);
    }

    /// <summary>Braced in a split jump: the right upper arm toward the view (pitch -1190, yaw -240, roll -28075), at the faster step.</summary>
    [Uc("SBase.SBasePawn.OrienteForVandamme", Sha1 = "c5f1f58979c8b294118fd5fb2d704c84d29d5fe8")]
    [PortOf("uc:SBase.SBasePawn:OrienteForVandamme", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.OrienteForVandamme: rot(-1190 + pitch, -240 + yaw, -28075) stepped at int(150000 x m_dT)")]
    public static void OrienteForVandamme(SBasePawn pawn, int yaw, int pitch, float dt)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        Turn(pawn, RightUpperArm, new FRotator(-1190 + pitch, -240 + yaw, -28075), SplitJumpArmSpeed, dt);
    }

    /// <summary>The corner fire's exit (SA_BTW_OUT): the right upper arm and forearm go back to their animation.</summary>
    [Uc("SBase.SBasePawn.RemoveBTWArmControl", Sha1 = "d87a6c43d56ab9837915d88dbda1e6c111f199b4")]
    [PortOf("uc:SBase.SBasePawn:RemoveBTWArmControl", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.RemoveBTWArmControl: SetBoneDirection('B R UpperArm' and 'B R ForeArm', rot(0,0,0), alpha 0)")]
    public static void RemoveBTWArmControl(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        pawn.PawnAnim.Directions.SetBoneDirection(RightUpperArm, default, 0f);
        pawn.PawnAnim.Directions.SetBoneDirection(RightForeArm, default, 0f);
    }

    /// <summary>
    /// The step all the arm functions share: from the bone's world rotation now (GetBoneRotation), each axis moves
    /// toward the target by at most int(speed x Level.m_dT) (the tick's time), the shorter way round; the result is the bone's new
    /// direction controller (alpha 1, world space).
    /// </summary>
    private static void Turn(SBasePawn pawn, Name bone, FRotator target, float speed, float dt)
    {
        int max = (int)(speed * dt);
        FRotator current = BoneRotation(pawn, bone, target);
        var turned = new FRotator(Step(current.Pitch, target.Pitch, max), Step(current.Yaw, target.Yaw, max), Step(current.Roll, target.Roll, max));
        pawn.PawnAnim.Directions.SetBoneDirection(bone, turned, 1f);
    }

    /// <summary>
    /// GetBoneRotation of a bone: the rotation its controller holds it at (the pose build draws it there), else where
    /// this machine last drew it (the level's bone service), else the target (no pose here: a server, the arm turns at
    /// once).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "GetBoneRotation reads the last built pose; a held bone is taken at its controller's rotation, a free one from the drawn pose, and with no pose (server) the target is used")]
    private static FRotator BoneRotation(SBasePawn pawn, Name bone, FRotator target)
    {
        if (pawn.PawnAnim.Directions.TryGetRotation(bone, out FRotator held))
        {
            return held;
        }

        return CameraBones.TryGet(pawn, bone, out _, out FRotator drawn) ? drawn : target;
    }

    /// <summary>One axis: the signed 16-bit difference current - target, clamped to +-max, taken off current (kept in 0 to 65535).</summary>
    private static int Step(int current, int target, int max)
    {
        int dif = (current - target) & 0xffff;
        if (dif > 32767)
        {
            dif -= 65536;
        }

        if (dif > max)
        {
            dif = max;
        }

        if (dif < -max)
        {
            dif = -max;
        }

        return (current - dif) & 0xffff;
    }
}
