using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The ExecuteAction cases of the traversal moves (spec/native/execute-action.md): the ledge (111 to 118, 224, 225,
/// 311), the vertical pipe (143 to 152, 246 to 248, 303 to 306, 309), the zip line (133 to 142), the
/// repositioning actions (148, 161, 326) and back to wall (176 to 182, 193 to 195). Each sets the physics mode and
/// the root-motion end of the move, then plays its clip on the base channel. Unknown actions do nothing.
/// </summary>
public static class TraversalActions
{
    [Provenance(ProvenanceKind.Decoded, "execute-action.md notation: L1 locks the root translation, L3 translation and yaw")]
    private const int LockTranslation = 1;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md notation: L3")]
    private const int LockTranslationYaw = 3;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 113: bServerTestRootMotion(113)")]
    private const int ServerTestedPullUp = 113;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 148 and 19: fPhysReposLinearSpeed 180")]
    public const float ReposSpeed = 180f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 161: fPhysReposLinearSpeed 300")]
    public const float FastReposSpeed = 300f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 225: Velocity = 200 X with Z = -162")]
    private const float AfterLedgeBarrierForward = 200f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 225")]
    private const float AfterLedgeBarrierDown = -162f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 145: hold set coefficient 131; case 112: shimmy coefficient 80")]
    private const float PipeHoldCoeff = 131f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 112")]
    private const float LedgeShimmyCoeff = 80f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 246: end offset (2 m_PipeArmsZone.X + 16) forward")]
    private const float PipeChangeExtra = 16f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 115: end(0.01 Z, +16384)")]
    private const float InsideCornerLift = 0.01f;

    /// <summary>Executes a traversal action.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md case table rows for these actions; the walk blender sets and channel blends are not modelled")]
    public static void Execute(SBasePawn pawn, int action)
    {
        bool cr = pawn.bIsCrouched;
        switch (action)
        {
            // ---- ledge ----
            case SBasePawn.SA_LEDGE_GRAB:
                pawn.bBeginSpecial12Anim = false;
                pawn.TypeLedge = LedgeType.LEDGE_NORMAL;
                pawn.PawnAnim.Stop(MovementK.DoubleJumpChannel);
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Ledge);
                Once(pawn, "LedgStAlBg0", 0.08f, 2f);
                break;
            case SBasePawn.SA_ONLEDGE:
                pawn.bBeginSpecial12Anim = true;
                pawn.RightAnim = new Name("LedgStAlRt0");
                pawn.LeftAnim = new Name("LedgStAlLt0");
                pawn.fBlendCoeffWalkAnim = LedgeShimmyCoeff;
                pawn.PawnAnim.Stop(MovementK.DoubleJumpChannel);
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Ledge);
                Loop(pawn, "LedgStAlNt0", 0f);
                break;
            case SBasePawn.SA_TOPLEDGE:
                pawn.bBeginSpecial12Anim = false;
                pawn.Acceleration = default;
                if (RootMotion(pawn, LockTranslationYaw, new FVector(SBasePawn.C_LEDGE_UPST_X, 0f, SBasePawn.C_LEDGE_UPST_Z), 0, ServerTestedPullUp))
                {
                    Once(pawn, "LedgStAlUp0", 0.15f);
                }

                break;
            case SBasePawn.SA_TOPLEDGECROUCH:
                pawn.bBeginSpecial12Anim = false;
                pawn.Acceleration = default;
                RootMotion(pawn, LockTranslationYaw, new FVector(SBasePawn.C_LEDGE_UPCR_X, 0f, SBasePawn.C_LEDGE_UPCR_Z), 0);
                Once(pawn, "LedgCrAlUp0", 0.15f);
                break;
            case SBasePawn.SA_CHANGELEDGE_IR:
                Corner(pawn, new FVector(0f, 0f, InsideCornerLift), 16384, "LedgStAlTiL", mirror: true);
                break;
            case SBasePawn.SA_CHANGELEDGE_IL:
                Corner(pawn, new FVector(0f, 0f, InsideCornerLift), -16384, "LedgStAlTiL", mirror: false);
                break;
            case SBasePawn.SA_CHANGELEDGE_OR:
                Corner(pawn, new FVector(SBasePawn.C_LEDGE_CORNER_X, SBasePawn.C_LEDGE_CORNER_Y, 0f), -16384, "LedgStAlToL", mirror: true);
                break;
            case SBasePawn.SA_CHANGELEDGE_OL:
                Corner(pawn, new FVector(SBasePawn.C_LEDGE_CORNER_X, -SBasePawn.C_LEDGE_CORNER_Y, 0f), 16384, "LedgStAlToL", mirror: false);
                break;
            case SBasePawn.SA_LEDGE_BARRIER:
                RootMotion(pawn, LockTranslationYaw, new FVector(SBasePawn.C_LEDGE_BARRIER_X, 0f, SBasePawn.C_LEDGE_BARRIER_Z), 0);
                Once(pawn, "LedgStAlRb0", 0.1f);
                break;
            case SBasePawn.SA_FALL_AFTERLEDGEBARRIER:
            {
                PawnActions.Falling(pawn);
                pawn.Rotation.GetAxes(out FVector x, out _, out _);
                FVector v = x * AfterLedgeBarrierForward;
                v.Z = AfterLedgeBarrierDown;
                pawn.Velocity = v;
                PawnActions.Loop(pawn, 0, pawn.AFall, PawnActions.FallTween);
                break;
            }

            case SBasePawn.SA_BARRIER_ABOVE_LEDGE_CR:
                RootMotion(pawn, LockTranslation, default, 0);
                Once(pawn, "LedgCrNwUp0", 0.1f);
                break;

            // ---- vertical pipe ----
            case SBasePawn.SA_PIPE_GRAB:
                MovesetActions.QuickGadgetCancel(pawn);
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_OnPipe);
                Once(pawn, "PipVStAlIOR", 0.1f, 1.5f);
                break;
            case SBasePawn.SA_PIPE_GRABFALL:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_OnPipe);
                Once(pawn, "PipVStAlBg0", 0.1f, 1.8f);
                break;
            case SBasePawn.SA_ON_PIPE:
                pawn.bIsLeftPart = pawn.bInvertNextLeftPart;
                pawn.bInvertNextLeftPart = false;
                PipeHold(pawn, pawn.OldPhysics == EPhysics.PHYS_RootMotion ? 0f : 0.2f, mirror: !pawn.bIsLeftPart);
                break;

            case SBasePawn.SA_ON_PIPE2:
                pawn.bIsLeftPart = false;
                PipeHold(pawn, 0f, mirror: true);
                break;
            case SBasePawn.SA_ON_PIPE2L:
                pawn.bIsLeftPart = true;
                PipeHold(pawn, 0f, mirror: false);
                break;
            case SBasePawn.SA_SPEEDPIPE:
                pawn.bBeginSpecial12Anim = false;
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_OnPipe);
                Loop(pawn, "PipVStAlDn0", 0.2f);
                break;
            case SBasePawn.SA_TOPPIPE:
                pawn.Acceleration = default;
                RootMotion(pawn, LockTranslation, new FVector(79.4f, 45f, 191.5f), 0);
                Once(pawn, pawn.bIsLeftPart ? "PipVStAlEuL" : "PipVStAlEuR", 0.1f, 1.5f);
                break;
            case SBasePawn.SA_TOPPIPE_CR:
                pawn.Acceleration = default;
                RootMotion(pawn, LockTranslation, new FVector(92.7f, 45f, 143.15f), 0);
                Once(pawn, pawn.bIsLeftPart ? "PipVCrAlEuL" : "PipVCrAlEuR", 0.1f, 1.5f);
                break;
            case SBasePawn.SA_DOWN_TOPPIPE:
                DownTopPipe(pawn,
                    new FVector(SBasePawn.C_DOWNTOPPIPE_X, SBasePawn.C_DOWNTOPPIPE_Y, SBasePawn.C_DOWNTOPPIPE_Z),
                    new FVector(SBasePawn.C_DOWNTOPPIPE_CR_X, SBasePawn.C_DOWNTOPPIPE_CR_Y, SBasePawn.C_DOWNTOPPIPE_CR_Z),
                    "PipVStAlBuL", "PipVCrAlBuL");
                break;
            case SBasePawn.SA_DOWN_TOPPIPE2:
                DownTopPipe(pawn,
                    new FVector(SBasePawn.C_DOWNTOPPIPE2_X, SBasePawn.C_DOWNTOPPIPE2_Y, SBasePawn.C_DOWNTOPPIPE2_Z),
                    new FVector(SBasePawn.C_DOWNTOPPIPE2_CR_X, SBasePawn.C_DOWNTOPPIPE2_CR_Y, SBasePawn.C_DOWNTOPPIPE2_CR_Z),
                    "PipVStAlBuR", "PipVCrAlBuR");
                break;
            case SBasePawn.SA_CHANGE_PIPEV_L:
                RootMotion(pawn, LockTranslationYaw, new FVector(2f * pawn.m_PipeArmsZone.X + PipeChangeExtra, 0f, 0f), 32768);
                Once(pawn, pawn.bIsLeftPart ? "PipVStAlTrLL" : "PipVStAlTrRL", 0.1f);
                break;
            case SBasePawn.SA_CHANGE_PIPEV_R:
                RootMotion(pawn, LockTranslationYaw, new FVector(2f * pawn.m_PipeArmsZone.X + PipeChangeExtra, 0f, 0f), 32768);
                Once(pawn, pawn.bIsLeftPart ? "PipVStAlTrRL" : "PipVStAlTrLL", 0.1f, mirror: true);
                break;
            case SBasePawn.SA_AFTER_CHANGE_PIPEV:
                pawn.bIsStopped = true;
                pawn.bCanStop = true;
                pawn.bBeginSpecial12Anim = true;
                pawn.fOldFrame = 0f;
                pawn.Velocity = default;
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_OnPipe);
                Loop(pawn, "PipVStAlNtL", 0f, mirror: !pawn.bIsLeftPart);
                break;
            case SBasePawn.SA_TR_PIPELEDGE_R:
                pawn.TypeLedge = LedgeType.LEDGE_NORMAL;
                RootMotion(pawn, LockTranslation, new FVector(SBasePawn.C_TR_PIPE_LEDGE_X, 45f, SBasePawn.C_TR_PIPE_LEDGE_Z), 0);
                Once(pawn, "PipVStLgEuR", 0.1f);
                break;
            case SBasePawn.SA_TR_PIPELEDGE_L:
                pawn.TypeLedge = LedgeType.LEDGE_NORMAL;
                RootMotion(pawn, LockTranslation, new FVector(SBasePawn.C_TR_PIPE_LEDGE_X, SBasePawn.C_TR_PIPE_LEDGE_Y, SBasePawn.C_TR_PIPE_LEDGE_Z), 0);
                Once(pawn, "PipVStLgEuR", 0.1f, mirror: true);
                break;
            case SBasePawn.SA_TR_LEDGEPIPE_R:
                pawn.bInvertNextLeftPart = true;
                RootMotion(pawn, LockTranslation, new FVector(SBasePawn.C_TR_LEDGE_PIPE_X, 45f, SBasePawn.C_TR_LEDGE_PIPE_Z), 0);
                Once(pawn, "PipVStLgBuR", 0.1f, mirror: true);
                break;
            case SBasePawn.SA_TR_LEDGEPIPE_L:
                RootMotion(pawn, LockTranslation, new FVector(SBasePawn.C_TR_LEDGE_PIPE_X, SBasePawn.C_TR_LEDGE_PIPE_Y, SBasePawn.C_TR_LEDGE_PIPE_Z), 0);
                Once(pawn, "PipVStLgBuR", 0.1f);
                break;

            // ---- repositioning ----
            case SBasePawn.SA_REPOS:
                Repos(pawn, ReposSpeed);
                break;
            case SBasePawn.SA_FAST_REPOS:
                Repos(pawn, FastReposSpeed);
                break;
            case SBasePawn.SA_EXIT_REPOS:
                pawn.bNoReposAtEnd = true;
                break;

            // ---- zip line ----
            case SBasePawn.SA_ZIP_GRAB:
                if (pawn.Physics == EPhysics.PHYS_OnPipe)
                {
                    pawn.ClientSmoothFlags = 0;
                    pawn.IK_Type = IK_Type_.IKT_NONE;
                    pawn.RootMotionAdditionnalPhysics = EPhysics.PHYS_None;
                    RootMotion(pawn, LockTranslationYaw, new FVector(-14f, 6f, 4f), 32768);
                    Once(pawn, pawn.bIsLeftPart ? "PipVSZilBhL" : "PipVSZilBhR", 0.1f);
                }
                else
                {
                    pawn.Velocity.Z = 0f;
                    PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                    Once(pawn, "ZippStAlBg0", 0.1f);
                }

                break;
            case SBasePawn.SA_ZIPFALL:
                if (pawn.bIsZipSpeed)
                {
                    MovementNatives.Driver(pawn)?.UnZipSpeed(pawn, bTest: false);
                }

                pawn.bIsRolling = false;
                PawnActions.Falling(pawn);
                PawnActions.Loop(pawn, 0, pawn.WeaponLoaded == 0 ? pawn.AFall : pawn.AFall2H, PawnActions.FallTween);
                break;
            case SBasePawn.SA_ZIP_WALL:
                MovementNatives.Driver(pawn)?.UnZipSpeed(pawn, bTest: false);
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Once(pawn, "ZippCrAlBl0", 0.1f);
                break;
            case SBasePawn.SA_ON_ZIP:
            {
                bool fromRootMotion = pawn.Physics == EPhysics.PHYS_RootMotion;
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Loop(pawn, "ZippStAlDn0", fromRootMotion ? 0f : 0.2f);
                break;
            }

            case SBasePawn.SA_ON_ZIP_SPEED:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Once(pawn, "ZippCrAlBg0", 0f);
                MovementNatives.Driver(pawn)?.SetZipSpeed(pawn);
                break;
            case SBasePawn.SA_ON_ZIP_END_TRSPEED:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Loop(pawn, "ZippCrAlDn0", 0f);
                break;
            case SBasePawn.SA_ON_ZIP_NORMAL:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Once(pawn, "ZippCrAlEd0", 0f);
                MovementNatives.Driver(pawn)?.UnZipSpeed(pawn, bTest: false);
                break;
            case SBasePawn.SA_ON_ZIP_SLOW2NORMAL:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Once(pawn, "ZippStAlSN0", 0f, backward: true);
                break;
            case SBasePawn.SA_ON_ZIP_SLOW:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Once(pawn, "ZippStAlSN0", 0f);
                break;
            case SBasePawn.SA_ON_ZIP_END_TRSLOW:
                PawnActions.SetPhysics(pawn, EPhysics.PHYS_Linear);
                Loop(pawn, "ZippStAlSw0", 0f);
                break;

            // ---- back to wall ----
            case SBasePawn.SA_BTW_ENTER:
                pawn.ClientSmoothFlags = 0;
                PawnActions.Walking(pawn);
                Once(pawn, cr ? "BackCrAlBg0" : "BackStAlBg0", cr ? 0f : 0.05f);
                break;
            case SBasePawn.SA_BTW_ENTERBACK:
                pawn.ClientSmoothFlags = 0;
                PawnActions.Walking(pawn);
                Once(pawn, cr ? "BackCrAlEd0" : "BackStAlEd0", 0f, backward: true);
                break;
            case SBasePawn.SA_BTW_END:
                PawnActions.Walking(pawn);
                Once(pawn, cr ? "BackCrAlEd0" : "BackStAlEd0", 0.05f);
                break;
            case SBasePawn.SA_BTW_CORNER:
                PawnActions.Walking(pawn);
                Once(pawn, cr ? "BackCrAlTr0" : "BackStAlTr0", 0.4f);
                break;
            case SBasePawn.SA_BTW_CORNEROFF:
                PawnActions.Walking(pawn);
                AnimateBTW(pawn);
                break;
            case SBasePawn.SA_BTW_LEFT:
                pawn.BTW = cr ? BTW_.BTWCrouchLeft : BTW_.BTWStandLeft;
                PawnActions.Walking(pawn);
                AnimateBTW(pawn);
                break;
            case SBasePawn.SA_BTW_RIGHT:
                pawn.BTW = cr ? BTW_.BTWCrouchRight : BTW_.BTWStandRight;
                PawnActions.Walking(pawn);
                AnimateBTW(pawn);
                break;
            case SBasePawn.SA_BTW_CR:
                pawn.BTW = BTW_.BTWCrouch;
                PawnActions.Walking(pawn);
                AnimateBTW(pawn);
                break;
            case SBasePawn.SA_BTW_ST:
                pawn.BTW = BTW_.BTWStand;
                PawnActions.Walking(pawn);
                AnimateBTW(pawn);
                break;
            case SBasePawn.SA_BTW_OUT:
                OpenScct.Game.Animation.ArmAim.RemoveBTWArmControl(pawn);
                pawn.PawnAnim.Stop(SBasePawn.DRAWCHANNEL);
                pawn.OldBTW = pawn.BTW;
                pawn.BTW = BTW_.NOBTW;
                pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                pawn.bWeaponTransit = false;
                break;
            default:
                if (!MovesetActions.Execute(pawn, action))
                {
                    InteractionActions.Execute(pawn, action);
                }

                break;
        }
    }

    /// <summary>
    /// A root-motion move: the lock type, no additional physics, and the end placement (offset in the pawn's axes,
    /// yaw change), then PHYS_RootMotion. The authority checks the ledge pull-up (113) first; false when refused.
    /// </summary>
    private static bool RootMotion(SBasePawn pawn, int lockType, FVector endLocal, int endYaw, int serverTestAction = 0)
    {
        pawn.iLockRootMotionType = lockType;
        pawn.bUseRootMotionAdditionnalPhysics = false;
        pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector move = x * endLocal.X + y * endLocal.Y + z * endLocal.Z;
        if (serverTestAction != 0 && pawn.Role == ENetRole.ROLE_Authority && MovementNatives.Driver(pawn) is { } driver)
        {
            return driver.bServerTestRootMotion(pawn, serverTestAction, move, endYaw);
        }

        pawn.vMoveAfterRM = move;
        pawn.rRotAfterRM = endYaw;
        PawnActions.SetPhysics(pawn, EPhysics.PHYS_RootMotion);
        return true;
    }

    private static void Corner(SBasePawn pawn, FVector end, int yaw, string clip, bool mirror)
    {
        pawn.bBeginSpecial12Anim = false;
        pawn.Acceleration = default;
        RootMotion(pawn, LockTranslationYaw, end, yaw);
        Once(pawn, clip, 0.2f, mirror: mirror);
    }

    /// <summary>The pipe hold set (ForwardAnim PipVStAlUpRL, coefficient 131) and the hold loop.</summary>
    /// <summary>
    /// The pipe hold actions (145, 151, 152): PHYS_OnPipe, at rest on the pipe (bIsStopped, bCanStop and
    /// bBeginSpecial12Anim, fOldFrame 0, Velocity 0, so the climb step animation starts its next step from here), the
    /// climb clip PipVStAlUpRL as ForwardAnim and the hold loop on the base channel.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ExecuteAction cases 145, 151, 152 (editor 0x11215980): flags 0x7000000 on the pawn's bits, fOldFrame 0, Velocity 0; tween 0 for 145 when OldPhysics is RootMotion")]
    private static void PipeHold(SBasePawn pawn, float tween, bool mirror)
    {
        PawnActions.SetPhysics(pawn, EPhysics.PHYS_OnPipe);
        pawn.bIsStopped = true;
        pawn.bCanStop = true;
        pawn.bBeginSpecial12Anim = true;
        pawn.fOldFrame = 0f;
        pawn.ForwardAnim = new Name("PipVStAlUpRL");
        pawn.fBlendCoeffWalkAnim = PipeHoldCoeff;
        Loop(pawn, "PipVStAlNtL", tween, mirror: mirror);
        pawn.Velocity = default;
    }

    private static void DownTopPipe(SBasePawn pawn, FVector standing, FVector crouched, string standClip, string crouchClip)
    {
        pawn.Acceleration = default;
        bool cr = pawn.bIsCrouched;
        RootMotion(pawn, LockTranslationYaw, cr ? crouched : standing, 32768);
        if (cr)
        {
            pawn.UnCrouchNoCheck();
        }

        Once(pawn, cr ? crouchClip : standClip, 0.1f, 1.5f);
    }

    /// <summary>The repositioning actions: PHYS_Repositionnal at the given speed, client controlled, Z ignored.</summary>
    private static void Repos(SBasePawn pawn, float speed)
    {
        pawn.fPhysReposLinearSpeed = speed;
        pawn.bServerControlsRepos = false;
        pawn.bUseAdditionnalPhysicsForRepos = true;
        PawnActions.SetPhysics(pawn, EPhysics.PHYS_Repositionnal);
    }

    /// <summary>
    /// SBasePawn.AnimateBTW: the shimmy set and the back-to-wall hold loop for the current BTW value (standing,
    /// crouched, the four peeks and the four fire poses), ported in OpenScct.Game.Animation.ActionAnimation.
    /// </summary>
    public static void AnimateBTW(SBasePawn pawn) => Animation.ActionAnimation.AnimateBTW(pawn);

    private static void Once(SBasePawn pawn, string clip, float tween, float rate = 1f, bool mirror = false, bool backward = false) =>
        pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, ClipName(clip)), rate, tween, loop: false, mirror, backward);

    private static void Loop(SBasePawn pawn, string clip, float tween, bool mirror = false) =>
        pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, ClipName(clip)), 1f, tween, loop: true, mirror);

    /// <summary>Clip names as interned names (looked up once per name).</summary>
    private static Name ClipName(string clip) => new(clip);
}
