using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The ExecuteAction cases of the rest of the moveset (spec/native/execute-action.md section 4): the ladder (153 to
/// 170, 300 to 302, 310), hand over hand (206 to 223, 307, 308), the fence (126 to 129), the split jump and Vandamme
/// (104, 108 to 110, 120 to 125), the wall jump (234 to 238), the barrier, feet and waist ledges (171 to 175, 203 to
/// 205), the co-op moves (252 to 259, 268, 276 to 282, 286 to 289) and the merc's charge, bumps and melee clips (2 to
/// 4, 8 to 10, 17, 315, 316). Reached from <see cref="TraversalActions"/>; false for any other action.
/// </summary>
public static class MovesetActions
{
    [Provenance(ProvenanceKind.Decoded, "execute-action.md notation: L1 locks the root translation")]
    private const int L1 = 1;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md notation: L3 translation and yaw")]
    private const int L3 = 3;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 17, 317, 325: L5")]
    private const int L5 = 5;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 2, 316: L7")]
    private const int L7 = 7;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 155: the ladder climb blend coefficient 128")]
    private const float LadderClimbCoeff = 128f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 207: the hand-over-hand blend coefficient 126")]
    private const float HohCoeff = 126f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 209: the legs-over blend coefficient 113")]
    private const float HohLegsCoeff = 113f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 128: the fence climb coefficient 120, speed limits 1000")]
    private const float FenceCoeff = 120f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 128: fWalkSpeedLimit and fRunSpeedLimit 1000")]
    private const float FenceSpeedLimit = 1000f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 4: fBlendCoeffWalkAnim 385 with AtakStAlCh2")]
    private const float ChargeCoeff = 385f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 171: the feet ledge flips the hand when Velocity.Z < -100")]
    private const float FeetLedgeFlipSpeed = -100f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 171: the right-hand feet ledge end 95.4 forward")]
    private const float FeetLedgeRightX = 95.4f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 203 and 205: Velocity 200 X with Z -162 after a barrier")]
    private const float AfterBarrierForward = 200f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 205")]
    private const float AfterBarrierDown = -162f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 235: vSpeedAfterRM = -370 X with Z -615")]
    private const float SaltoBack = -370f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 235")]
    private const float SaltoDown = -615f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 104: the long fall tween 0.35")]
    private const float LongFallTween = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 253: fPhysReposLinearSpeed 380")]
    public const float CoopReposSpeed = 380f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 316: tween 0.05")]
    private const float GoBackTween = 0.05f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 8 to 10: the unarmed elbow at rate 1.5")]
    private const float ElbowRate = 1.5f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 278, 279: tween 0.3")]
    private const float HoistWaitTween = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 259: tween 0.5 standing")]
    private const float ShortLadderBeginTween = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 220: end(-5.6 Z, +32768)")]
    private const float PipeVToHZ = -5.6f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 221: end(21.5 Z, 0)")]
    private const float PipeHToVZ = 21.5f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 222: end(86.35 X + 205 Z, +32768)")]
    private const float PipeHToVUpX = 86.35f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 222")]
    private const float PipeHToVUpZ = 205f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 223: end(97.9 X - 207.2 Z, 0)")]
    private const float PipeVToHDownX = 97.9f;

    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 223")]
    private const float PipeVToHDownZ = -207.2f;

    /// <summary>The crouched waist climb's end height: the native's literal, not the script constant C_WAIST_LEDGECR (109.2).</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 174: end(77 X + 136.7 Z)")]
    private const float WaistLedgeCrouchedZ = 136.7f;

    /// <summary>The run channel whose frame gives the leading foot.</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.9: channel 4")]
    private const int RunChannel = 4;

    private const float T01 = 0.1f;

    private const float T02 = 0.2f;

    /// <summary>Executes one of the moveset actions; false for any other action.</summary>
    [UcNative("ASBasePawn::ExecuteAction", Exe = 0x10ad6800, Editor = 0x11215980)]
    [PortOf("nat:SBase.SBasePawn.ExecuteAction", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md case table rows for these actions; the walk blender sets and channel blends are not modelled")]
    public static bool Execute(SBasePawn pawn, int action)
    {
        bool cr = pawn.bIsCrouched;
        bool spy = pawn.SPawnType == PawnType.SPAWNOFFENSE;
        switch (action)
        {
            // ---- ladder ----
            case SBasePawn.SA_LADDER_GRAB:
                QuickGadgetCancel(pawn);
                pawn.ClientSmoothFlags = 1;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlIOR", cr ? T02 : T01, 1.5f);
                return true;
            case SBasePawn.SA_LADDER_GRABW:
                pawn.ClientSmoothFlags = 1;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStSpIOL", 0f);
                return true;
            case SBasePawn.SA_ON_LADDER:
                pawn.bIsLeftPart = false;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                HoldSet(pawn, "LaddStAlUpLR", LadderClimbCoeff, "LaddStAlNtL", T02, mirror: true);
                return true;
            case SBasePawn.SA_ON_LADDERW:
                pawn.bIsLeftPart = true;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                HoldSet(pawn, "LaddStAlUpLR", LadderClimbCoeff, "LaddStAlNtL", T02, mirror: false);
                return true;
            case SBasePawn.SA_LADDER_TOTOP_R:
                pawn.bBeginSpecial12Anim = false;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlEuR", 0f);
                return true;
            case SBasePawn.SA_LADDER_TOTOP_L:
                pawn.bBeginSpecial12Anim = false;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlEuL", 0f);
                return true;
            case SBasePawn.SA_ON_LADDERTOP:
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Loop(pawn, 0, "LaddStAlNt0", 0f);
                return true;
            case SBasePawn.SA_LADDER_TOP:
                pawn.Acceleration = default;
                if (spy)
                {
                    RootMotion(pawn, L1, null, new FVector(SBasePawn.C_TOP_LADDER_X, 0f, SBasePawn.C_TOP_LADDER_Z), 0);
                    Once(pawn, 0, "LaddStAlEu0", 0f, 1.5f);
                }
                else
                {
                    RootMotion(pawn, L1, null, new FVector(SBasePawn.C_TOP_LADDERW_X, 0f, SBasePawn.C_TOP_LADDERW_Z), 0);
                    Once(pawn, 0, "LaddStAlEu2", 0f);
                }

                return true;
            case SBasePawn.SA_BACK_LADDER_TOP:
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlEuR", 0f, backward: true);
                return true;
            case SBasePawn.SA_LADDER_DOWN_FROM_TOP:
                pawn.ClientSmoothFlags = 1;
                pawn.Acceleration = default;
                RootMotion(pawn, L3, null, default, 32768);
                if (cr)
                {
                    pawn.UnCrouchNoCheck();
                    Once(pawn, 0, spy ? "LaddCrAlBu0" : "LaddCrSpBu2", 0f, spy ? 1.5f : 1f);
                }
                else
                {
                    Once(pawn, 0, spy ? "LaddStAlBu0" : "LaddStSpBu2", 0f, spy ? 1.5f : 1f);
                }

                return true;
            case SBasePawn.SA_SPEED_LADDER_FROM_TOP:
            case SBasePawn.SA_SPEEDLADDER:
                pawn.bBeginSpecial12Anim = false;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Loop(pawn, 0, "LaddStAlDn0", T02);
                return true;
            case SBasePawn.SA_LADDER_DOWNW:
                RootMotion(pawn, L3, null, default, 32768);
                Once(pawn, 0, pawn.bIsLeftPart ? "LaddStEdIOL" : "LaddStEdIOR", 0f);
                return true;
            case SBasePawn.SA_ON_STOPLADDER:
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Loop(pawn, 0, "LaddStAlNt0", T01);
                return true;
            case SBasePawn.SA_LADDER_UP2:
                pawn.bBeginSpecial12Anim = false;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlBgR", 0f);
                return true;
            case SBasePawn.SA_LADDER_GRABFALL:
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlBg0", T01);
                return true;
            case SBasePawn.SA_LADDER_CATCH:
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Once(pawn, 0, "LaddStAlCt0", T01);
                return true;
            case SBasePawn.SA_CHANGESIDELADDER_L:
                RootMotion(pawn, L3, null, new FVector(2f * pawn.m_NarrowLadderArmsZone.X, 0f, 0f), 32768);
                Once(pawn, 0, "LaddStAlTuR", 0f, mirror: true);
                return true;
            case SBasePawn.SA_CHANGESIDELADDER_R:
                RootMotion(pawn, L3, null, new FVector(2f * pawn.m_NarrowLadderArmsZone.X, 0f, 0f), 32768);
                Once(pawn, 0, "LaddStAlTuR", 0f);
                return true;
            case SBasePawn.SA_AFTER_CHANGESIDELADDER:
                pawn.bIsStopped = true;
                pawn.bCanStop = true;
                pawn.bBeginSpecial12Anim = true;
                pawn.fOldFrame = 0f;
                pawn.Velocity = default;
                Phys(pawn, EPhysics.PHYS_OnLadder);
                Loop(pawn, 0, "LaddStAlNt0", T01);
                return true;
            case SBasePawn.SA_LADDER_TOP_CR:
                pawn.Acceleration = default;
                if (spy)
                {
                    RootMotion(pawn, L1, null, new FVector(SBasePawn.C_TOP_LADDER_CR_X, 0f, SBasePawn.C_TOP_LADDER_CR_Z), 0);
                    Once(pawn, 0, "LaddCrAlEu0", 0f, 1.5f);
                }
                else
                {
                    RootMotion(pawn, L1, null, default, 0);
                    Once(pawn, 0, "LaddCrSpEu2", 0f);
                }

                return true;

            // ---- hand over hand ----
            case SBasePawn.SA_PIPH_GRAB:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                pawn.IK_Type = IK_Type_.IKT_PIPH;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHstAlBg0", T01);
                return true;
            case SBasePawn.SA_ON_PIPEH:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                pawn.ForwardAnim = new Name("PipHStAlFdRL");
                pawn.fBlendCoeffWalkAnim = HohCoeff;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Loop(pawn, 0, "PipHStAlNt0", 0f);
                return true;
            case SBasePawn.SA_PIPH_UP4:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                MovementNatives.Driver(pawn)?.SetColForPipe4(pawn);
                Once(pawn, 0, "PipHPrAlBg0", 0f);
                return true;
            case SBasePawn.SA_ON_PIPEH_4:
                pawn.PipHFire = PipHFire_.PipH4NoFire;
                pawn.ForwardAnim = new Name("PipHPrAlFd0");
                pawn.fBlendCoeffWalkAnim = HohLegsCoeff;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Loop(pawn, 0, "PipHPrAlNt0", 0f);
                return true;
            case SBasePawn.SA_PIPH_TURN:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHStAlTr0", 0f);
                return true;
            case SBasePawn.SA_PIPH_DOWN2:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                MovementNatives.Driver(pawn)?.SetColAfterPipe4(pawn, bTest: false);
                Once(pawn, 0, "PipHPrAlEd0", 0f);
                return true;
            case SBasePawn.SA_PIPH_TR_FIRE4:
                PipeAimSet(pawn, "PipHPrAlUL1", "PipHPrAlUR1", "PipHPrAlDL1", "PipHPrAlDR1");
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHStSpBg2", T01);
                return true;
            case SBasePawn.SA_PIPH_FIRE4:
                pawn.PipHFire = PipHFire_.PipH4;
                PipeAimSet(pawn, "PipHPrAlUL1", "PipHPrAlUR1", "PipHPrAlDL1", "PipHPrAlDR1");
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Loop(pawn, 0, "PipHStSpNt2", 0f);
                return true;
            case SBasePawn.SA_PIPH_FIRE_TR4:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHStSpEd2", 0f);
                return true;
            case SBasePawn.SA_PIPH_TR_FIRE2:
                pawn.bWeaponTransit = true;
                PipeAimSet(pawn, "PipHStAlUL1", "PipHStAlUR1", "PipHStAlDL1", "PipHStAlDR1");
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHStAlBg1", T01);
                return true;
            case SBasePawn.SA_PIPH_FIRE2:
                pawn.bWeaponTransit = false;
                pawn.PipHFire = PipHFire_.PipH2;
                PipeAimSet(pawn, "PipHStAlUL1", "PipHStAlUR1", "PipHStAlDL1", "PipHStAlDR1");
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Loop(pawn, 0, "PipHStSpNt1", 0f);
                return true;
            case SBasePawn.SA_PIPH_FIRE_TR2:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                Phys(pawn, EPhysics.PHYS_OnPipeH);
                Once(pawn, 0, "PipHStAlEd1", 0f);
                return true;
            case SBasePawn.SA_PIPH_JUMP4:
                MovementNatives.Driver(pawn)?.SetColAfterPipe4(pawn, bTest: false);
                pawn.PipHFire = PipHFire_.NOPipH;
                pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                PawnActions.Falling(pawn);
                Once(pawn, 0, "PipHStSpFl0", 0f);
                return true;
            case SBasePawn.SA_PIPH_JUMPFIRE4:
                pawn.PipHFire = PipHFire_.NoPipHFire;
                pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                pawn.bWeaponTransit = true;
                MovementNatives.Driver(pawn)?.SetColAfterPipe4(pawn, bTest: false);
                PawnActions.Falling(pawn);
                Once(pawn, 0, "PipHStSpFl2", 0f, 2f);
                return true;
            case SBasePawn.SA_TR_PIPEV_H:
                pawn.ClientSmoothFlags = 0;
                pawn.IK_Type = IK_Type_.IKT_PIPH;
                RootMotion(pawn, L3, null, new FVector(0f, 0f, PipeVToHZ), 32768);
                Once(pawn, 0, pawn.bIsLeftPart ? "PipVStAlBhL" : "PipVStAlBhR", T01);
                return true;
            case SBasePawn.SA_TR_PIPEH_V:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                RootMotion(pawn, L3, null, new FVector(0f, 0f, PipeHToVZ), 0);
                Once(pawn, 0, "PipHStAlBV0", T01);
                return true;
            case SBasePawn.SA_TR_PIPEH_V_UP:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                RootMotion(pawn, L3, null, new FVector(PipeHToVUpX, 0f, PipeHToVUpZ), 32768);
                Once(pawn, 0, "PipHStAlBu0", T01);
                return true;
            case SBasePawn.SA_TR_PIPEV_H_DOWN:
                pawn.IK_Type = IK_Type_.IKT_PIPH;
                RootMotion(pawn, L3, null, new FVector(PipeVToHDownX, 0f, PipeVToHDownZ), 0);
                Once(pawn, 0, pawn.bIsLeftPart ? "PipVStAlBDL" : "PipVStAlBDR", T01);
                return true;
            case SBasePawn.SA_TR_ORTHOPIPE_L:
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_TR_ORTHO_PIPE_L_X, SBasePawn.C_TR_ORTHO_PIPE_L_Y, 0f), -16384);
                Once(pawn, 0, "PipHStAlTuL", T01);
                return true;
            case SBasePawn.SA_TR_ORTHOPIPE_R:
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_TR_ORTHO_PIPE_R_X, SBasePawn.C_TR_ORTHO_PIPE_R_Y, 0f), 16384);
                Once(pawn, 0, "PipHStAlTuR", T01);
                return true;

            // ---- fence ----
            case SBasePawn.SA_FENCEGRAB:
                QuickGadgetCancel(pawn);
                pawn.Acceleration = default;
                pawn.Velocity = default;
                Phys(pawn, EPhysics.PHYS_OnVerticalPlane);
                Once(pawn, 0, "FencStAlIO0", T01);
                return true;
            case SBasePawn.SA_FENCEGRAB_JUMP:
                pawn.Acceleration = default;
                pawn.Velocity = default;
                Phys(pawn, EPhysics.PHYS_OnVerticalPlane);
                Once(pawn, 0, "FencStAlBg0", T01);
                return true;
            case SBasePawn.SA_ONFENCE:
                pawn.RightAnim = new Name("FencStAlRt0");
                pawn.ForwardAnim = new Name("FencStAlUp0");
                pawn.fBlendCoeffWalkAnim = FenceCoeff;
                pawn.fWalkSpeedLimit = FenceSpeedLimit;
                pawn.fRunSpeedLimit = FenceSpeedLimit;
                Phys(pawn, EPhysics.PHYS_OnVerticalPlane);
                Loop(pawn, 0, "FencStAlNt0", 0f);
                return true;
            case SBasePawn.SA_TOPFENCE:
                pawn.Acceleration = default;
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_TOPFENCE_X, SBasePawn.C_TOPFENCE_Y, SBasePawn.C_TOPFENCE_Z), 32768);
                Once(pawn, 0, "FencStAlCo0", T01, 1.5f);
                return true;

            // ---- split jump and Vandamme ----
            case SBasePawn.SA_LONG_FALL_ST_0H:
                if (!pawn.vSpeedAfterRM.IsZero())
                {
                    pawn.Velocity = pawn.vSpeedAfterRM;
                }

                FallReset(pawn);
                Once(pawn, 0, pawn.WeaponLoaded == 0 ? pawn.AFall : pawn.AFall2H, LongFallTween, loop: true);
                return true;
            case SBasePawn.SA_DBL_JUMP_R:
                pawn.Velocity = default;
                Phys(pawn, EPhysics.PHYS_None);
                Once(pawn, 0, "SpltStAlRt0", T01);
                return true;
            case SBasePawn.SA_DBL_JUMP_L:
                pawn.Velocity = default;
                Phys(pawn, EPhysics.PHYS_None);
                Once(pawn, 0, "SpltStAlLt0", T01);
                return true;
            case SBasePawn.SA_DBL_JUMP_IMP:
                PawnActions.Falling(pawn);
                MovementNatives.Driver(pawn)?.MyDoJump(pawn);
                return true;
            case SBasePawn.SA_VANDAMME_LT:
                pawn.ClientSmoothFlags = 1;
                Phys(pawn, EPhysics.PHYS_Vandamme);
                Once(pawn, 0, "SpltStAlBgL", 0f);
                return true;
            case SBasePawn.SA_VANDAMME_RT:
                pawn.ClientSmoothFlags = 1;
                Phys(pawn, EPhysics.PHYS_Vandamme);
                Once(pawn, 0, "SpltStAlBgR", 0f);
                return true;
            case SBasePawn.SA_VANDAMME_NT:
                pawn.bControlArmForVandamme = false;
                pawn.IK_Type = IK_Type_.IKT_JCVD;
                PipeAimSet(pawn, "SpltStAlUL0", "SpltStAlUR0", "SpltStAlDL0", "SpltStAlDR0");
                Phys(pawn, EPhysics.PHYS_Vandamme);
                Loop(pawn, 0, "SpltStAlNt0", 0f);
                return true;
            case SBasePawn.SA_VANDAMME_2H:
                pawn.bWeaponTransit = true;
                Phys(pawn, EPhysics.PHYS_Vandamme);
                Once(pawn, SBasePawn.DRAWCHANNEL, "SpltStSpBg1", 0f);
                return true;
            case SBasePawn.SA_VANDAMME_0H:
                pawn.bControlArmForVandamme = false;
                pawn.bWeaponTransit = true;
                Phys(pawn, EPhysics.PHYS_Vandamme);
                Once(pawn, SBasePawn.DRAWCHANNEL, "SpltStSpBg1", 0f, backward: true);
                return true;
            case SBasePawn.SA_VANDAMMEFALL:
                pawn.bBeginSpecial12Anim = false;
                pawn.IK_Type = IK_Type_.IKT_WEAPONLOADED;
                FallReset(pawn);
                Once(pawn, 0, pawn.WeaponLoaded == 0 ? pawn.AFall : pawn.AFall2H, PawnActions.FallTween, loop: true);
                return true;

            // ---- wall jump ----
            case SBasePawn.SA_WLJP_JUMP1:
                pawn.bIsLeftPart = LeftFootLeads(pawn);
                RootMotion(pawn, L1, EPhysics.PHYS_None, default, 0);
                Once(pawn, 0, pawn.bIsLeftPart ? "WljpStAlBgR" : "WljpStAlBgL", 0f);
                return true;
            case SBasePawn.SA_WLJP_SALTO:
            {
                pawn.Rotation.GetAxes(out FVector x, out _, out _);
                FVector v = x * SaltoBack;
                v.Z = SaltoDown;
                pawn.vSpeedAfterRM = v;
                RootMotion(pawn, L3, EPhysics.PHYS_None, default, 0, viaNone: true);
                Once(pawn, 0, pawn.bIsLeftPart ? "WljpStAlBkR" : "WljpStAlBkL", 0f);
                return true;
            }

            case SBasePawn.SA_WLJP_UP:
                RootMotion(pawn, L1, EPhysics.PHYS_None, default, 0);
                Once(pawn, 0, pawn.bIsLeftPart ? "WljpStAlUpR" : "WljpStAlUpL", 0f);
                return true;
            case SBasePawn.SA_WLJP_UP_WAIST:
                RootMotion(pawn, L1, EPhysics.PHYS_None, default, 0);
                Once(pawn, 0, "WljpStAlUwL", 0f, mirror: pawn.bIsLeftPart);
                return true;
            case SBasePawn.SA_WLJP_UPSMALL:
                RootMotion(pawn, L1, EPhysics.PHYS_None, default, 0);
                Once(pawn, 0, "WljpStAlUmL", 0f, mirror: pawn.bIsLeftPart);
                return true;

            // ---- barrier, feet and waist ledges ----
            case SBasePawn.SA_FEETLEDGE:
                if (pawn.Velocity.Z < FeetLedgeFlipSpeed)
                {
                    pawn.bIsLeftPart = !pawn.bIsLeftPart;
                }

                pawn.fTimeOfFeetLedge = pawn.Level.TimeSeconds;
                RootMotion(pawn, L3, null,
                    new FVector(pawn.bIsLeftPart ? SBasePawn.C_FEET_LEDGE_X : FeetLedgeRightX, 0f, SBasePawn.C_FEET_LEDGE_Z), 0);
                Once(pawn, 0, pawn.bIsLeftPart ? "LedgStAlFtL" : "LedgStAlFtR", T01);
                return true;
            case SBasePawn.SA_FEETLEDGE_DETECT:
                QuickGadgetCancel(pawn);
                pawn.TypeLedge = LedgeType.LEDGE_FEET;
                Phys(pawn, EPhysics.PHYS_Ledge);
                return true;
            case SBasePawn.SA_WAISTLEDGE:
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_WAIST_LEDGE_X, 0f, SBasePawn.C_WAIST_LEDGE_Z), 0);
                Once(pawn, 0, "LedgStAlQk0", 0f, 1.3f);
                return true;
            case SBasePawn.SA_WAISTLEDGE_CR:
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_WAIST_LEDGECR_X, 0f, WaistLedgeCrouchedZ), 0);
                Once(pawn, 0, "LedgCrAlQk0", 0f, 1.2f);
                return true;
            case SBasePawn.SA_WAISTLEDGE_DETECT:
                pawn.TypeLedge = LedgeType.LEDGE_WAIST;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Once(pawn, 0, "LedgStAlBQ0", 0.08f, 50f);
                return true;
            case SBasePawn.SA_BARRIER_JUMP:
                RootMotion(pawn, L3, null, new FVector(SBasePawn.C_BARRIER_LEDGE_X, SBasePawn.C_BARRIER_LEDGE_Y, SBasePawn.C_BARRIER_LEDGE_Z), 0);
                Once(pawn, 0, "JumpStAlBr0", T01);
                return true;
            case SBasePawn.SA_BARRIER_JUMP_DETECT:
                QuickGadgetCancel(pawn);
                pawn.TypeLedge = LedgeType.LEDGE_BARRIER;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Once(pawn, 0, "JumpStAlBr0", T01);
                return true;
            case SBasePawn.SA_FALL_AFTERBARRIER:
            {
                PawnActions.Falling(pawn);
                pawn.Rotation.GetAxes(out FVector x, out _, out _);
                FVector v = x * AfterBarrierForward;
                v.Z = AfterBarrierDown;
                pawn.Velocity = v;
                Once(pawn, 0, "JumpStAlBf0", 0f);
                return true;
            }

            // ---- co-op ----
            case SBasePawn.SA_SHL_S_JUMP:
                Partner(pawn, SBasePawn.SA_SHL_P_PERFORM);
                pawn.Acceleration = default;
                RootMotion(pawn, L1, null, default, 0);
                Once(pawn, 0, "CoopBtwAlHpEdS", T01);
                return true;
            case SBasePawn.SA_SHL_S_JUMPWALL:
                Partner(pawn, SBasePawn.SA_SHL_P_PERFORM);
                pawn.Acceleration = default;
                RootMotion(pawn, L1, null, default, 0);
                Once(pawn, 0, "CoopBckFlpToNtS", T01);
                return true;
            case SBasePawn.SA_SHL_S_REPOS:
                pawn.fPhysReposLinearSpeed = CoopReposSpeed;
                pawn.bUseAdditionnalPhysicsForRepos = true;
                pawn.bServerControlsRepos = false;
                Phys(pawn, EPhysics.PHYS_Repositionnal);
                return true;
            case SBasePawn.SA_SHL_P_PERFORM:
                pawn.bReadyForShortLadder = false;
                RootMotion(pawn, L3, EPhysics.PHYS_Walking, default, 0);
                Once(pawn, 0, "CoopCrAlHpEdP", T01);
                return true;
            case SBasePawn.SA_SHL_P_STOP:
                pawn.bReadyForShortLadder = false;
                PawnActions.Walking(pawn);
                return true;
            case SBasePawn.SA_SHL_P_WAITING:
                if (pawn.Physics != EPhysics.PHYS_RootMotion)
                {
                    pawn.bReadyForShortLadder = true;
                    Loop(pawn, 0, "CoopCrAlHpNtP", 0f);
                }

                return true;
            case SBasePawn.SA_SHL_P_FINISH:
                PawnActions.Walking(pawn);
                pawn.UnCrouch(false);
                return true;
            case SBasePawn.SA_SHL_P_LEAVE:
                pawn.bReadyForShortLadder = false;
                Once(pawn, 0, "CoopCrAlHpBgP", T01, backward: true);
                return true;
            case SBasePawn.SA_SHL_P_BEGIN:
                PawnActions.Walking(pawn);
                Once(pawn, 0, "CoopCrAlHpBgP", cr ? 0f : ShortLadderBeginTween);
                return true;
            case SBasePawn.SA_HOIST_P_PERFORM:
                pawn.bReadyForHoistClimb = false;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Once(pawn, 0, "CoopDynHstBP", T01);
                return true;
            case SBasePawn.SA_HOIST_P_PERFORM_NOWALL:
                pawn.bReadyForHoistClimb = false;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Once(pawn, 0, "CoopDynHstBPf", T01);
                return true;
            case SBasePawn.SA_HOIST_P_STOP:
                pawn.bReadyForHoistClimb = false;
                Phys(pawn, EPhysics.PHYS_Ledge);
                return true;
            case SBasePawn.SA_HOIST_P_WAITING:
                pawn.bReadyForHoistClimb = true;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Loop(pawn, 0, "CoopDynHstBPnt", HoistWaitTween);
                return true;
            case SBasePawn.SA_HOIST_P_LEAVE:
                pawn.bReadyForHoistClimb = false;
                Phys(pawn, EPhysics.PHYS_Ledge);
                Loop(pawn, 0, "LedgStAlNt0", HoistWaitTween);
                return true;
            case SBasePawn.SA_HOIST_S_JUMP:
                Partner(pawn, SBasePawn.SA_HOIST_P_PERFORM);
                pawn.Acceleration = default;
                RootMotion(pawn, L1, null, default, 0);
                Once(pawn, 0, "CoopDynHstBSBg", T01);
                return true;
            case SBasePawn.SA_HOIST_S_JUMP_NOWALL:
                Partner(pawn, SBasePawn.SA_HOIST_P_PERFORM_NOWALL);
                pawn.Acceleration = default;
                RootMotion(pawn, L1, null, default, 0);
                Once(pawn, 0, "CoopDynHstBSBgf", T01);
                return true;
            case SBasePawn.SA_HOIST_S_BARRIER:
                HoistSupporter(pawn, "CoopDynHstBSRailUp");
                return true;
            case SBasePawn.SA_HOIST_S_FINISH:
                HoistSupporter(pawn, "CoopDynHstBSGround");
                return true;
            case SBasePawn.SA_HOIST_S_BARRIER_NOWALL:
                HoistSupporter(pawn, "CoopDynHstBSRailUpf");
                return true;
            case SBasePawn.SA_HOIST_S_FINISH_NOWALL:
                HoistSupporter(pawn, "CoopDynHstBSGroundf");
                return true;

            // ---- the merc's charge, bumps and melee clips ----
            case SBasePawn.SA_BUMP_WALL:
                RootMotion(pawn, L7, EPhysics.PHYS_Walking, default, 0);
                Once(pawn, 0, "AtakChAlEd2", 0f);
                return true;
            case SBasePawn.SA_BUMP_ENEMY:
                Once(pawn, 0, "AtakStAlFd2", 0f);
                return true;
            case SBasePawn.SA_CHARGING:
                pawn.ForwardAnim = new Name("AtakStAlCh2");
                pawn.fBlendCoeffWalkAnim = ChargeCoeff;
                PawnActions.Walking(pawn);
                return true;
            case SBasePawn.SA_ELBOW_ATTACK:
            case SBasePawn.SA_ELBOW_ATTACK_MISSED:
                if (pawn.WeaponLoaded != 0)
                {
                    Once(pawn, SBasePawn.ATTACKCHANNEL, "AtakStAlFd2", 0f);
                }
                else
                {
                    Once(pawn, SBasePawn.ATTACKCHANNEL, "AtakStAlFd0", 0f, ElbowRate);
                }

                return true;
            case SBasePawn.SA_ELBOW_ATTACK_GROUND:
                Once(pawn, SBasePawn.ATTACKCHANNEL, "AtakStAlDn0", 0f, ElbowRate);
                return true;
            case SBasePawn.SA_ELBOW_ATTACKED_GOBACK:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                pawn.Acceleration = default;
                FaceAttacker(pawn);
                pawn.m_AttackedBy = null;
                RootMotion(pawn, L5, EPhysics.PHYS_Walking, default, 0);
                Once(pawn, 0, cr ? "StrkCrAlBk2" : "StrkStAlBk2", 0f);
                return true;
            case SBasePawn.SA_BERSERK_ATTACK:
                pawn.IK_Type = IK_Type_.IKT_NONE;
                RootMotion(pawn, L3, EPhysics.PHYS_Walking, default, 0);
                Once(pawn, 0, "AtakStAlTu", T01);
                return true;
            case SBasePawn.SA_CHARGE_ATTACKED_GOBACK:
                ChargeAttackedGoBack(pawn);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The physics mode through the engine's setPhysics.</summary>
    private static void Phys(SBasePawn pawn, EPhysics mode) => PawnActions.SetPhysics(pawn, mode);

    /// <summary>
    /// A root-motion move: the lock type, the additional physics (none: collision off), the end placement (offset in
    /// the pawn's axes, yaw change), then PHYS_RootMotion (through PHYS_None first with <paramref name="viaNone"/>).
    /// </summary>
    private static void RootMotion(SBasePawn pawn, int lockType, EPhysics? additional, FVector endLocal, int endYaw, bool viaNone = false)
    {
        pawn.iLockRootMotionType = lockType;
        pawn.bUseRootMotionAdditionnalPhysics = additional is not null;
        if (additional is { } extra)
        {
            pawn.RootMotionAdditionnalPhysics = extra;
        }

        pawn.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        pawn.vMoveAfterRM = x * endLocal.X + y * endLocal.Y + z * endLocal.Z;
        pawn.rRotAfterRM = endYaw;
        if (viaNone)
        {
            Phys(pawn, EPhysics.PHYS_None);
        }

        Phys(pawn, EPhysics.PHYS_RootMotion);
    }

    /// <summary>
    /// The hold set of the ladder: at rest on the ladder (bIsStopped, bCanStop and bBeginSpecial12Anim, fOldFrame 0,
    /// Velocity 0, so the climb step starts its next step from here), the climb clip as ForwardAnim with its
    /// coefficient and the hold loop on the base channel.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 155, 156: hold set (flags 0x7000000, fOldFrame 0, Velocity 0)")]
    private static void HoldSet(SBasePawn pawn, string forward, float coeff, string loop, float tween, bool mirror)
    {
        pawn.bIsStopped = true;
        pawn.bCanStop = true;
        pawn.bBeginSpecial12Anim = true;
        pawn.fOldFrame = 0f;
        pawn.ForwardAnim = new Name(forward);
        pawn.fBlendCoeffWalkAnim = coeff;
        Loop(pawn, 0, loop, tween, mirror: mirror);
        pawn.Velocity = default;
    }

    /// <summary>The aim set of the hand-over-hand fire poses and the Vandamme brace (RunAnim, ForwardAnim, LeftAnim, RightAnim).</summary>
    private static void PipeAimSet(SBasePawn pawn, string run, string forward, string left, string right)
    {
        pawn.RunAnim = new Name(run);
        pawn.ForwardAnim = new Name(forward);
        pawn.LeftAnim = new Name(left);
        pawn.RightAnim = new Name(right);
    }

    /// <summary>
    /// The "fall reset" of the fall actions (103 to 106): no pipe fire pose, no weapon transit, out of the zip speed
    /// pose, PHYS_Falling.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 103 to 106: fall reset")]
    private static void FallReset(SBasePawn pawn)
    {
        pawn.PipHFire = PipHFire_.NOPipH;
        pawn.bWeaponTransit = false;
        if (pawn.bIsZipSpeed)
        {
            MovementNatives.Driver(pawn)?.UnZipSpeed(pawn, bTest: false);
        }

        PawnActions.Falling(pawn);
    }

    /// <summary>
    /// The actions that grab a traversal element cancel a quick gadget in progress (<see cref="SBasePawn.CancelQuickGadget"/>):
    /// the palm channel blends out, the flag is cleared, then the pawn's NotifyQuickLaunch (the controlling side launches it).
    /// </summary>
    [PortOf("uc:SBase.SBasePawn:NotifyQuickLaunch", Spec = "spec/native/execute-action.md#4-cases")]
    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 251 note: the grab actions blend c22 out, clear bQuickGadgetHasBegun and raise NotifyQuickLaunch")]
    internal static void QuickGadgetCancel(SBasePawn pawn) => pawn.CancelQuickGadget();

    /// <summary>The run cycle phase: a frame of the run channel (4) in [0.1, 0.6] means the left foot leads.</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md rule 5.9: channel 4 frame in [0.1, 0.6]")]
    private static bool LeftFootLeads(SBasePawn pawn)
    {
        float f = pawn.PawnAnim[RunChannel].Frame;
        return f >= 0.1f && f <= 0.6f;
    }

    /// <summary>
    /// The partner pawn of a co-op move runs its half of the move first (X(m_InteractionTarget, n)): the full
    /// ExecuteAction of the mate, with its own prologue and epilogue, not written to its sAction and never sent.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 252, 268, 281, 287: the partner's ExecuteAction before the own fields (coop.md rule 22)")]
    private static void Partner(SBasePawn pawn, int action)
    {
        if (pawn.m_InteractionTarget is SBasePawn mate && !ReferenceEquals(mate, pawn))
        {
            mate.ExecuteAction(action);
        }
    }

    /// <summary>The hoist climber's barrier and finish clips: through Walking to root motion, no acceleration.</summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md cases 280 to 282, 287 to 289: Walking then RootMotion, L1 -add end0, Acceleration 0")]
    private static void HoistSupporter(SBasePawn pawn, string clip)
    {
        pawn.Acceleration = default;
        PawnActions.Walking(pawn);
        RootMotion(pawn, L1, null, default, 0);
        Once(pawn, 0, clip, 0f);
    }

    /// <summary>The pawn turns in place to face its attacker (the yaw of the flattened offset), as replicated.</summary>
    private static void FaceAttacker(SBasePawn pawn)
    {
        if (pawn.m_AttackedBy is not { } attacker)
        {
            return;
        }

        FVector d = attacker.Location - pawn.Location;
        d.Z = 0f;
        if (d.IsZero())
        {
            return;
        }

        var face = new FRotator(0, d.Rotation().Yaw, 0);
        if (MovementNatives.Driver(pawn) is { } driver)
        {
            driver.MoveActor(pawn, default, face, out _);
        }
        else
        {
            pawn.Rotation = face;
        }

        pawn.ReceivedRotation = pawn.Rotation;
    }

    /// <summary>
    /// SA_CHARGE_ATTACKED_GOBACK (316): knocked back by a charge: the stance recorded in StrkType, through None to root
    /// motion with walking, the facing (the pawn's own SpecialYaw on its owner, else toward the attacker) and the clip.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "execute-action.md case 316")]
    private static void ChargeAttackedGoBack(SBasePawn pawn)
    {
        pawn.IK_Type = IK_Type_.IKT_NONE;
        pawn.Acceleration = default;
        if (pawn.bIsRolling)
        {
            pawn.bIsRolling = false;
        }

        pawn.StrkType = (StrkType_)((pawn.bIsCrouched ? 0 : 1) + (pawn.WeaponLoaded != 0 ? 2 : 0));
        if (pawn.Controller is null || pawn.Controller.RemoteRole == ENetRole.ROLE_AutonomousProxy)
        {
            var face = new FRotator(0, pawn.SpecialYaw, 0);
            if (MovementNatives.Driver(pawn) is { } driver)
            {
                driver.MoveActor(pawn, default, face, out _);
            }
            else
            {
                pawn.Rotation = face;
            }
        }
        else if (pawn.m_AttackedBy is not null)
        {
            FaceAttacker(pawn);
            pawn.SpecialYaw = pawn.Rotation.Yaw;
        }

        RootMotion(pawn, L7, EPhysics.PHYS_Walking, default, 0, viaNone: true);
        Once(pawn, 0, "StrkCrAlBk0", GoBackTween);
    }

    private static void Once(SBasePawn pawn, int channel, string clip, float tween, float rate = 1f, bool mirror = false,
        bool backward = false, bool loop = false) =>
        pawn.PawnAnim.Play(channel, PawnActions.Clip(pawn, new Name(clip)), rate, tween, loop, mirror, backward);

    private static void Once(SBasePawn pawn, int channel, Name clip, float tween, float rate = 1f, bool loop = false) =>
        pawn.PawnAnim.Play(channel, PawnActions.Clip(pawn, clip), rate, tween, loop);

    private static void Loop(SBasePawn pawn, int channel, string clip, float tween, bool mirror = false) =>
        pawn.PawnAnim.Play(channel, PawnActions.Clip(pawn, new Name(clip)), 1f, tween, loop: true, mirror);
}
