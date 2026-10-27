using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The animation side of ExecuteAction (spec/native/execute-action.md) that the action ports leave out: the prologue
/// (BlendChangeTime and the blend into channel 0 reset), the walk blender sets AnimateWalking writes, the blend
/// params and rate scales of the overlay channels (the draw channel 14, the roll and landing channel 15, the no-run
/// overlay on 1, the elbow, interact and palm overlays), the blend of the roll into channel 0 and the set refresh after a
/// roll. The action ports (PawnActions, TraversalActions, MovesetActions, the gadgets) play the clips;
/// <see cref="Begin"/> runs before them and <see cref="End"/> after. The cases no port plays are
/// <see cref="CombatActionAnimation"/>'s.
/// </summary>
public static class ActionAnimation
{
    private static readonly Name GrabStSpRt0 = new("GrabStSpRt0");
    private static readonly Name GrabStAlFd0 = new("GrabStAlFd0");
    private static readonly Name GrbdStAlFd0 = new("GrbdStAlFd0");
    private static readonly Name GrbdStAlRt0 = new("GrbdStAlRt0");
    private static readonly Name GrbdStAlLt0 = new("GrbdStAlLt0");
    private static readonly Name WalkCrSpLt2 = new("WalkCrSpLt2");
    private static readonly Name JoggStAlRt2 = new("JoggStAlRt2");
    private static readonly Name JoggStAlLt2 = new("JoggStAlLt2");
    private static readonly Name JoggStAlFd2 = new("JoggStAlFd2");
    private static readonly Name JoggStRpFd0 = new("JoggStRpFd0");
    private static readonly Name JumpStAlFL2 = new("JumpStAlFL2");
    private static readonly Name JumpStAlFR2 = new("JumpStAlFR2");
    private static readonly Name RollStAlFdR = new("RollStAlFdR");
    private static readonly Name RollStAlFdL = new("RollStAlFdL");
    private static readonly Name RollStJpFdR = new("RollStJpFdR");
    private static readonly Name RollStJpFdL = new("RollStJpFdL");
    private static readonly Name BoneSpine1 = new("B Spine1");
    private static readonly Name BackCrAlFdL = new("BackCrAlFdL");
    private static readonly Name BackCrAlFdR = new("BackCrAlFdR");
    private static readonly Name BackStAlFdL = new("BackStAlFdL");
    private static readonly Name BackStAlFdR = new("BackStAlFdR");

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 112: BlendChangeTime = 0.15 on the ledge")]
    [Provenance(ProvenanceKind.Measured, "golden traversal.spy-ledge-shimmy: the shimmy channels step dt / 0.15")]
    public const float LedgeBlendChangeTime = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 180: BlendChangeTime = 0.2")]
    public const float BtwCornerOffBlendChangeTime = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimateBTW: crouched back-to-wall coefficient 75")]
    public const float BtwCrouchCoeff = 75f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimateBTW: standing back-to-wall coefficient 130")]
    public const float BtwStandCoeff = 130f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimateBTW: the stand and crouch holds tween over 0.2 s after another BTW pose")]
    public const float BtwHoldTween = 0.2f;

    [Provenance(ProvenanceKind.Decoded, "SBasePawn.AnimateBTW: the peek and fire holds tween over 0.3 s")]
    public const float BtwPoseTween = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "ExecuteAction prologue 0x11215980: BlendChangeTime = 0.25")]
    public const float DefaultBlendChangeTime = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360: fWalkSpeedLimit 200 and fRunSpeedLimit 250 before the set")]
    public const float DefaultWalkLimit = 200f;

    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360")]
    public const float DefaultRunLimit = 250f;

    [Provenance(ProvenanceKind.Decoded, "PlayRoll 0x111fe260, PlayRollUp 0x111fe360: PlayAnimWithBlend over 0.1 s on channel 15")]
    public const float RollBlendTime = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "run cycle phase (execute-action.md rule 5.9): channel 4 frame in [0.1, 0.6] is the left foot")]
    public const float LeftFootFrom = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "run cycle phase (execute-action.md rule 5.9)")]
    public const float LeftFootTo = 0.6f;

    /// <summary>What <see cref="End"/> needs from before the action ran.</summary>
    public readonly record struct Entry(bool WasRolling);

    /// <summary>The prologue of every ExecuteAction (rule 1): BlendChangeTime 0.25, no blend into channel 0.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#1-prologue-every-call")]
    [Provenance(ProvenanceKind.Estimated, "A pose build (tween cache) before the action's clips: fits the goldens' tween weights at landings (spy-jump-forward frame 109, spy-jump-in-place frame 79)")]
    public static Entry Begin(SBasePawn pawn, int action)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        var entry = new Entry(pawn.bIsRolling);
        pawn.PawnAnim.UpdateTweenCache();
        pawn.BlendChangeTime = DefaultBlendChangeTime;
        pawn.fBlendTimeWithC0 = 0f;
        return entry;
    }

    /// <summary>The channel work of <paramref name="action"/> beyond the clips the action ports play (execute-action.md section 4).</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "ExecuteAction cases 20 to 134: the blend params, rate scales, walk sets and roll blends")]
    public static void End(SBasePawn pawn, int action, Entry entry)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        switch (action)
        {
            case SBasePawn.SA_GRAB:
                WalkSet(pawn, SBasePawn.SA_GRAB);
                break;
            case SBasePawn.SA_GRAB_AVOIDED:
                WalkSet(pawn, SBasePawn.SA_WALK_ST_0H);
                break;
            case SBasePawn.SA_GRABBED_AVOID:
                WalkSet(pawn, SBasePawn.SA_WALK_ST_2H);
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 0f, 0.2f);
                break;
            case SBasePawn.SA_GRABBED_CATCH:
                a.SetBlendParams(SBasePawn.INTERACTCHANNEL, 0f, bone: BoneSpine1);
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 0f, 0.2f);
                break;
            case SBasePawn.SA_GRABBED:
                if (pawn.m_AttackedBy is not null)
                {
                    WalkSet(pawn, SBasePawn.SA_GRABBED);
                }

                break;

            // Crouch and stand walk tails (75, 76, 85 to 87): the draw and roll channels off, the set, the set after a roll.
            case SBasePawn.SA_WALK_CR_0H:
            case SBasePawn.SA_WALK_CR_2H:
            case SBasePawn.SA_WALK_ST_0H:
            case SBasePawn.SA_WALK_ST_2H:
            case SBasePawn.SA_RUN_ST_2H:
                DrawAndRollOff(a);
                if (action is SBasePawn.SA_WALK_ST_0H or SBasePawn.SA_WALK_ST_2H or SBasePawn.SA_RUN_ST_2H)
                {
                    // The stand walk tail (spec/native/execute-action.md): IK_Type = 0.
                    pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                }

                WalkSet(pawn, action);
                if (entry.WasRolling)
                {
                    pawn.bIsRolling = false;
                    SetAnimSetAfterRolling(pawn);
                }

                break;
            case SBasePawn.SA_WALK_CR_0Hto2H:
            case SBasePawn.SA_WALK_CR_2Hto0H:
            case SBasePawn.SA_WALK_ST_0Hto2H:
            case SBasePawn.SA_WALK_ST_2Hto0H:
                WalkSet(pawn, action);
                a.SetBlendParams(SBasePawn.DRAWCHANNEL, 1f, bone: BoneSpine1);
                break;
            case SBasePawn.SA_WAIT_CR2ST_2H:
            case SBasePawn.SA_WAIT_CR2ST_0H:
                a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                WalkSet(pawn, action);
                if (entry.WasRolling)
                {
                    pawn.bIsRolling = false;
                    SetAnimSetAfterRollingUp(pawn);
                }

                break;
            case SBasePawn.SA_WAIT_ST2CR_2H:
            case SBasePawn.SA_WAIT_ST2CR_0H:
                WalkSet(pawn, action);
                break;

            // Jumps (90, 91): the no-run overlay off, a running quick gadget cancelled (palm blend out, flag cleared,
            // NotifyQuickLaunch).
            case SBasePawn.SA_JUMP_ST_0H:
            case SBasePawn.SA_JUMP_ST_2H:
                a.SetBlendParams(1, 0f);
                pawn.CancelQuickGadget();

                break;

            // Landings: high tails (92, 93, 98, 99, 101) the roll channel off; low tails (94, 96, 100) the draw
            // channel too; 96 runs on with the no-run overlay.
            case SBasePawn.SA_LAND_DROPATTACK:
            case SBasePawn.SA_LAND_ST_0H_HIGH:
            case SBasePawn.SA_LAND_ST_0HTo2H_HIGH:
            case SBasePawn.SA_LAND_ST_2H_HIGH:
            case SBasePawn.SA_LAND_INERTE:
            case SBasePawn.SA_LAND_CR_0H_QUIET:
            case SBasePawn.SA_LAND_CR_2H_QUIET:
                a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                if (action is SBasePawn.SA_LAND_DROPATTACK or SBasePawn.SA_LAND_INERTE)
                {
                    // Their own cases (92, 101: walking physics, the stop, the landing clip) are the combat port's;
                    // this group caught them before the default reached it.
                    CombatActionAnimation.Execute(pawn, action);
                }

                break;
            case SBasePawn.SA_LAND_ST_0H_LOW:
            case SBasePawn.SA_LAND_ST_2H_LOW:
                LandLowTail(a);
                break;
            case SBasePawn.SA_WALK_ST_LAND_OH:
                LandLowTail(a);
                pawn.bNoRunAnimation = true;
                a.PlayAnim(1, PawnActions.Clip(pawn, JoggStRpFd0), 1f, 0f, loop: false);
                a.SetBlendParams(1, 1f);
                break;

            // Falls (103 to 106, 119, 125, 134): the fall reset.
            case SBasePawn.SA_FALL_ST_0H:
            case SBasePawn.SA_FALL_ST_2H:
            case SBasePawn.SA_LEDGEFALL:
            case SBasePawn.SA_VANDAMMEFALL:
            case SBasePawn.SA_ZIPFALL:
            case SBasePawn.SA_LONG_FALL_ST_0H:
            case SBasePawn.SA_FALL_INERTE:
                if (!pawn.bIsRolling)
                {
                    a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                }

                a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f, bone: BoneSpine1);
                a.SetChannelRateScale(SBasePawn.DRAWCHANNEL, 0f);
                if (action == SBasePawn.SA_FALL_INERTE)
                {
                    // Case 106 (falling physics, the inert fall clip, collision) is the combat port's: without it a
                    // pawn drop-attacked while hanging from a ledge stayed in PHYS_Ledge with no control.
                    CombatActionAnimation.Execute(pawn, action);
                }

                break;

            // Ledge (111, 112).
            case SBasePawn.SA_LEDGE_GRAB:
                a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                a.SetBlendParams(7, 0f);
                a.SetBlendParams(8, 0f);
                break;
            case SBasePawn.SA_ONLEDGE:
                a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                pawn.BlendChangeTime = LedgeBlendChangeTime;
                break;

            // Back to wall: the corner holds the shimmy blend (20), its exit blends over 0.2 s.
            case SBasePawn.SA_BTW_CORNER:
                pawn.BlendChangeTime = AnimationK.ShimmyCornerBlendTime;
                break;
            case SBasePawn.SA_BTW_CORNEROFF:
                pawn.BlendChangeTime = BtwCornerOffBlendChangeTime;
                break;
            case SBasePawn.SA_BTW_OUT:
                a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f);
                a.SetChannelRateScale(SBasePawn.DRAWCHANNEL, 0f);
                break;

            // Split jump (120 to 124).
            case SBasePawn.SA_VANDAMME_LT:
            case SBasePawn.SA_VANDAMME_RT:
            case SBasePawn.SA_VANDAMME_NT:
                a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
                break;
            case SBasePawn.SA_VANDAMME_2H:
            case SBasePawn.SA_VANDAMME_0H:
                a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f);
                break;

            // The overlays the ports play without their blend: the elbow (8 to 10) on the attack channel, the hand
            // interaction and the mine (13 to 16, 290) on the interact channel, the palm (38) from 0.1.
            case SBasePawn.SA_ELBOW_ATTACK:
            case SBasePawn.SA_ELBOW_ATTACK_MISSED:
                a.SetBlendParams(SBasePawn.ATTACKCHANNEL, 1f, bone: BoneSpine1);
                break;
            case SBasePawn.SA_ELBOW_ATTACK_GROUND:
                a.SetBlendParams(SBasePawn.ATTACKCHANNEL, 1f);
                break;
            case SBasePawn.SA_HANDINTERACT:
            case SBasePawn.SA_HANDINTERACT_BACK:
            case SBasePawn.SA_DROPMINE_GRAB:
            case SBasePawn.SA_DROPMINE:
            case SBasePawn.SA_DROPMINE_UNGRAB:
                a.SetBlendParams(SBasePawn.INTERACTCHANNEL, 1f, bone: BoneSpine1);
                break;
            case SBasePawn.SA_PALM_USING:
                a.SetBlendParams(SBasePawn.PALMCHANNEL, PalmStartAlpha, bone: BoneSpine1);
                a.BlendToAlpha(SBasePawn.PALMCHANNEL, 1f, PalmBlendIn);
                break;
            case SBasePawn.SA_HACK_ENTER:
                a.SetBlendParams(SBasePawn.INTERACTCHANNEL, 0f);
                break;

            default:
                CombatActionAnimation.Execute(pawn, action);
                break;
        }
    }

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 38: blend c22 0.1 @B Spine1 before the blend to 1")]
    private const float PalmStartAlpha = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 38: to c22 1/0.2")]
    private const float PalmBlendIn = 0.2f;

    /// <summary>
    /// The set part of ASBasePawn::AnimateWalking(Action) (execute-action.md rule 5.1): the speed limits 200 and 250,
    /// then the walk blender set and coefficients of the action and the state it clears. The wait loop on channel 0
    /// and the tween are the action ports' (PawnActions.AnimateWalking).
    /// </summary>
    [UcNative("ASBasePawn::AnimateWalking", Editor = 0x111fd360)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    [Provenance(ProvenanceKind.Decoded, "AnimateWalking 0x111fd360: the sets of 20, 32, 75 to 89")]
    public static void WalkSet(SBasePawn pawn, int action)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        pawn.fWalkSpeedLimit = DefaultWalkLimit;
        pawn.fRunSpeedLimit = DefaultRunLimit;
        bool merc = pawn.SPawnType != PawnType.SPAWNOFFENSE;
        switch (action)
        {
            case SBasePawn.SA_GRAB:
                pawn.RightAnim = GrabStSpRt0;
                pawn.TurnAnim = GrabStSpRt0;
                pawn.fWalkSpeedLimit = 150000f;
                pawn.fRunSpeedLimit = 150000f;
                pawn.fBlendCoeffWalkAnim = 57f;
                pawn.fBlendCoeffRunAnim = 57f;
                pawn.ForwardAnim = GrabStAlFd0;
                pawn.RunAnim = GrabStAlFd0;
                break;
            case SBasePawn.SA_GRABBED:
                pawn.fWalkSpeedLimit = 150000f;
                pawn.fRunSpeedLimit = 150000f;
                pawn.fBlendCoeffWalkAnim = 57f;
                pawn.fBlendCoeffRunAnim = 57f;
                pawn.ForwardAnim = GrbdStAlFd0;
                pawn.RightAnim = GrbdStAlRt0;
                pawn.LeftAnim = GrbdStAlLt0;
                break;
            case SBasePawn.SA_WALK_CR_0H:
            case SBasePawn.SA_WALK_CR_2Hto0H:
            case SBasePawn.SA_WAIT_ST2CR_0H:
                pawn.bStillGrabbed = false;
                pawn.SetCollision(true, true, true);
                pawn.bLyingOnGround = false;
                pawn.fRunSpeedLimit = pawn.HighLimitForFastCrouchOnly;
                pawn.fWalkSpeedLimit = pawn.LowLimitForSlowCrouchOnly;
                pawn.ForwardAnim = pawn.AWalkCrFd0;
                pawn.TurnAnim = pawn.AWalkCrTurnRt0;
                pawn.RightAnim = pawn.AWalkCrRt0;
                pawn.RunAnim = pawn.AJoggCrFd0;
                pawn.fBlendCoeffWalkAnim = 60f;
                pawn.fBlendCoeffRunAnim = 90f;
                break;
            case SBasePawn.SA_WALK_CR_2H:
            case SBasePawn.SA_WALK_CR_0Hto2H:
            case SBasePawn.SA_WAIT_ST2CR_2H:
                pawn.bStillGrabbed = false;
                pawn.SetCollision(true, true, true);
                pawn.RightAnim = pawn.AWalkCrRt2;
                pawn.bLyingOnGround = false;
                pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                pawn.ForwardAnim = pawn.AWalkCrFd2;
                if (merc)
                {
                    pawn.LeftAnim = WalkCrSpLt2;
                    pawn.fWalkSpeedLimit = 125f;
                    pawn.fRunSpeedLimit = 150f;
                    pawn.RunAnim = pawn.AJoggCrFd2;
                }
                else
                {
                    pawn.fWalkSpeedLimit = 200f;
                    pawn.fRunSpeedLimit = 800f;
                    pawn.RunAnim = pawn.AWalkCrFd2;
                }

                pawn.TurnAnim = pawn.AWalkCrTurnRt2;
                pawn.fBlendCoeffWalkAnim = 130f;
                pawn.fBlendCoeffRunAnim = 160f;
                break;
            case SBasePawn.SA_WAIT_CR2ST_2H:
            case SBasePawn.SA_WALK_ST_2H:
            case SBasePawn.SA_WALK_ST_0Hto2H:
                pawn.bStillGrabbed = false;
                pawn.SetCollision(true, true, true);
                pawn.bCollideWorld = true;
                pawn.bLyingOnGround = false;
                pawn.TurnAnim = pawn.AWalkStTurnRt2;
                pawn.fBlendCoeffWalkAnim = 120.6f;
                pawn.RightAnim = pawn.AWalkStRt2;
                pawn.ForwardAnim = pawn.AWalkStFd2;
                if (!merc)
                {
                    pawn.RunAnim = pawn.ARunStFd2;
                    pawn.fWalkSpeedLimit = 190f;
                    pawn.fRunSpeedLimit = 290f;
                    pawn.fBlendCoeffRunAnim = 192.5f;
                }
                else
                {
                    pawn.LeftAnim = pawn.AWalkStLt2;
                    pawn.fWalkSpeedLimit = 1000f;
                    pawn.fRunSpeedLimit = 2000f;
                }

                break;
            case SBasePawn.SA_WAIT_CR2ST_0H:
            case SBasePawn.SA_WALK_ST_0H:
            case SBasePawn.SA_WALK_ST_2Hto0H:
                pawn.bLyingOnGround = false;
                pawn.bStillGrabbed = false;
                pawn.RightAnim = pawn.AWalkStRt0;
                pawn.TurnAnim = pawn.AWalkStTurnRt0;
                pawn.fWalkSpeedLimit = pawn.LowLimitForWalkOnly;
                pawn.fRunSpeedLimit = pawn.HighLimitForRunOnly;
                pawn.ForwardAnim = pawn.AWalkStFd0;
                pawn.RunAnim = pawn.ARunStFd0;
                pawn.fBlendCoeffWalkAnim = 120.6f;
                pawn.fBlendCoeffRunAnim = 250f;
                break;
            case SBasePawn.SA_RUN_ST_2H:
                pawn.bStillGrabbed = false;
                pawn.TurnAnim = pawn.AWalkStTurnRt2;
                pawn.RightAnim = JoggStAlRt2;
                pawn.LeftAnim = JoggStAlLt2;
                pawn.ForwardAnim = JoggStAlFd2;
                pawn.fWalkSpeedLimit = 1000f;
                pawn.fRunSpeedLimit = 2000f;
                pawn.fBlendCoeffWalkAnim = 327f;
                break;
        }
    }

    /// <summary>
    /// PlayJumpStart (execute-action.md rule 5.7): LastZipP far away, BlendChangeTime 0.05, then above 10 uu/s the
    /// forward jump of the leading foot (bIsLeftPart follows it), at rest the standing jump unless the pawn wants to
    /// crouch. The armed jump (91) uses JumpStAlFL2 and JumpStAlFR2.
    /// </summary>
    [UcNative("ASBasePawn::PlayJumpStart", Editor = 0x111f97a0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    [Provenance(ProvenanceKind.Decoded, "PlayJumpStart 0x111f97a0: speed above 10, the run phase foot, BlendChangeTime 0.05")]
    public static void PlayJumpStart(SBasePawn pawn, int action)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        pawn.LastZipP = MovementK.FarZipPoint;
        pawn.BlendChangeTime = AnimationK.JumpStartBlendChangeTime;
        bool unarmed = action == SBasePawn.SA_JUMP_ST_0H;
        FVector v = pawn.Velocity;
        float speed = SvmMath.Sqrt((v.X * v.X) + (v.Y * v.Y));
        Name clip;
        if (speed > AnimationK.JumpForwardSpeed)
        {
            bool left = IsLeftFoot(pawn);
            pawn.bIsLeftPart = left;
            clip = left ? (unarmed ? pawn.AJumpForwardL : JumpStAlFL2) : (unarmed ? pawn.AJumpForwardR : JumpStAlFR2);
        }
        else if (!pawn.bWantsToCrouch)
        {
            clip = unarmed ? pawn.AJumpStart : JumpStAlFL2;
        }
        else
        {
            return;
        }

        pawn.PawnAnim.PlayAnim(0, PawnActions.Clip(pawn, clip), 1f, 0f, loop: false);
    }

    /// <summary>
    /// PlayRoll and PlayRollUp: bIsRolling, and the roll clip for the leading foot of the run cycle (channel 4's
    /// frame in [0.1, 0.6]: left) blended into channel 0 over 0.1 s on channel 15.
    /// </summary>
    [UcNative("ASBasePawn::PlayRoll", Editor = 0x111fe260)]
    [UcNative("ASBasePawn::PlayRollUp", Editor = 0x111fe360)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public static void PlayRoll(SBasePawn pawn, bool up)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        pawn.bIsRolling = true;
        bool left = IsLeftFoot(pawn);
        Name clip = up ? (left ? RollStJpFdL : RollStJpFdR) : (left ? RollStAlFdL : RollStAlFdR);
        PlayAnimWithBlend(pawn, clip, SBasePawn.DBLJUMPCHANNEL, RollBlendTime, 1f);
    }

    /// <summary>The run cycle's leading foot: channel 4's frame in [0.1, 0.6] is the left one.</summary>
    [Provenance(ProvenanceKind.Decoded, "PlayRoll, PlayRollUp, PlayJumpStart: left when the run channel's frame is not below 0.1 nor above 0.6")]
    public static bool IsLeftFoot(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        float f = pawn.PawnAnim.GetAnimFrame(4);
        return !(f < LeftFootFrom || LeftFootTo < f);
    }

    /// <summary>PlayAnimWithBlend: a one-shot on the channel (rate 0 plays at 1) that the walk blender fades in over the time.</summary>
    [UcNative("ASBasePawn::PlayAnimWithBlend", Editor = 0x111f9fd0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#notation-used-in-the-tables")]
    public static void PlayAnimWithBlend(SBasePawn pawn, Name sequence, int channel, float time, float rate)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (rate == 0f)
        {
            rate = 1f;
        }

        pawn.PawnAnim.PlayAnim(channel, PawnActions.Clip(pawn, sequence), rate, 0f, loop: false);
        pawn.ChannelForBlendWithC0 = channel;
        pawn.fBlendTimeWithC0 = time;
    }

    /// <summary>SetAnimSetAfterRolling: the walk set straight onto 5 to 8 and 4, channel 4 at alpha 1, frames 5 to 13 at 0.</summary>
    [UcNative("ASBasePawn::SetAnimSetAfterRolling", Editor = 0x111ff7e0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public static void SetAnimSetAfterRolling(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        SetSequence(pawn, 5, pawn.ForwardAnim);
        SetSequence(pawn, 6, pawn.ForwardAnim);
        SetSequence(pawn, 7, pawn.RightAnim);
        SetSequence(pawn, 8, pawn.RightAnim);
        SetSequence(pawn, 4, pawn.RunAnim);
        a.StepBlendAlpha(4, 1f, AnimationK.AtOnceFeetLedge);
        for (int ch = 5; ch < 14; ch++)
        {
            a.SetAnimFrame(ch, 0f, unitFlag: false);
        }
    }

    /// <summary>
    /// SetAnimSetAfterRollingUp: as <see cref="SetAnimSetAfterRolling"/>, plus the crouched copies on 9 to 13 (the Copy*
    /// fields), channel 9 at alpha 1 and 4 to 8 at 0.
    /// </summary>
    [UcNative("ASBasePawn::SetAnimSetAfterRollingUp", Editor = 0x111ff8f0)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#5-helper-natives-called-by-the-cases")]
    public static void SetAnimSetAfterRollingUp(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        PawnAnimation a = pawn.PawnAnim;
        SetSequence(pawn, 5, pawn.ForwardAnim);
        SetSequence(pawn, 6, pawn.ForwardAnim);
        SetSequence(pawn, 7, pawn.RightAnim);
        SetSequence(pawn, 8, pawn.RightAnim);
        SetSequence(pawn, 4, pawn.RunAnim);
        SetSequence(pawn, 10, pawn.CopyForwardAnim);
        SetSequence(pawn, 11, pawn.CopyForwardAnim);
        SetSequence(pawn, 12, pawn.CopyRightAnim);
        SetSequence(pawn, 13, pawn.CopyRightAnim);
        SetSequence(pawn, 9, pawn.CopyRunAnim);
        a.StepBlendAlpha(9, 1f, AnimationK.AtOnceFeetLedge);
        a.StepBlendAlpha(4, 1f, AnimationK.AtOnceFeetLedge);
        for (int ch = 4; ch < 9; ch++)
        {
            a.StepBlendAlpha(ch, 0f, AnimationK.AtOnceFeetLedge);
        }

        for (int ch = 5; ch < 14; ch++)
        {
            a.SetAnimFrame(ch, 0f, unitFlag: false);
        }
    }

    /// <summary>
    /// SBasePawn.AnimateBTW (ANSWERS-anim.md A8): the shimmy set by stance, then the hold loop of the BTW value on
    /// channel 0 (stand and crouch untweened when entering, else over 0.2 s; peeks and fire poses over 0.3 s), and
    /// OldBTW = BTW.
    /// </summary>
    [Uc("SBase.SBasePawn.AnimateBTW", Sha1 = "3a3298860753843298edc55e70b3966c68cff713")]
    [PortOf("uc:SBase.SBasePawn:AnimateBTW", Spec = "spec/native/ANSWERS-anim.md#a8-sbasepawnanimatebtw-script-event")]
    public static void AnimateBTW(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.bIsCrouched)
        {
            pawn.RightAnim = BackCrAlFdL;
            pawn.LeftAnim = BackCrAlFdR;
            pawn.fBlendCoeffWalkAnim = BtwCrouchCoeff;
        }
        else
        {
            pawn.RightAnim = BackStAlFdL;
            pawn.LeftAnim = BackStAlFdR;
            pawn.fBlendCoeffWalkAnim = BtwStandCoeff;
        }

        bool entering = pawn.OldBTW == BTW_.NOBTW;
        (string? clip, float tween) = pawn.BTW switch
        {
            BTW_.BTWStand => ("BackStAlNt0", entering ? 0f : BtwHoldTween),
            BTW_.BTWCrouch => ("BackCrAlNt0", entering ? 0f : BtwHoldTween),
            BTW_.BTWStandLeft => ("BackStPkNtL", BtwPoseTween),
            BTW_.BTWStandRight => ("BackStPkNtR", BtwPoseTween),
            BTW_.BTWCrouchLeft => ("BackCrPkNtL", BtwPoseTween),
            BTW_.BTWCrouchRight => ("BackCrPkNtR", BtwPoseTween),
            BTW_.BTWFireStandLeft => ("BackStSpNtL", BtwPoseTween),
            BTW_.BTWFireStandRight => ("BackStSpNtR", BtwPoseTween),
            BTW_.BTWFireCrouchLeft => ("BackCrSpNtL", BtwPoseTween),
            BTW_.BTWFireCrouchRight => ("BackCrSpNtR", BtwPoseTween),
            _ => ((string?)null, 0f),
        };
        if (clip is not null)
        {
            pawn.PawnAnim.PlayAnim(0, PawnActions.Clip(pawn, new Name(clip)), 1f, tween, loop: true);
        }

        pawn.OldBTW = pawn.BTW;
    }

    /// <summary>The walk tails' overlay reset: the draw channel off and frozen, the roll channel off.</summary>
    private static void DrawAndRollOff(PawnAnimation a)
    {
        a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f);
        a.SetChannelRateScale(SBasePawn.DRAWCHANNEL, 0f);
        a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
    }

    /// <summary>The low landing tail: the roll channel off, the draw channel off from B Spine1 and frozen.</summary>
    private static void LandLowTail(PawnAnimation a)
    {
        a.SetBlendParams(SBasePawn.DBLJUMPCHANNEL, 0f);
        a.SetBlendParams(SBasePawn.DRAWCHANNEL, 0f, bone: BoneSpine1);
        a.SetChannelRateScale(SBasePawn.DRAWCHANNEL, 0f);
    }

    private static void SetSequence(SBasePawn pawn, int channel, Name sequence)
    {
        if (sequence.IsNone)
        {
            pawn.PawnAnim.ClearSequence(channel);
        }
        else
        {
            pawn.PawnAnim.SetAnimSequence(channel, PawnActions.Clip(pawn, sequence));
        }
    }
}
