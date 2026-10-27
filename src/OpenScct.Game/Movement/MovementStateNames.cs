using OpenScct.Core;

namespace OpenScct.Game.Movement;

/// <summary>The names of the controller states and labels the movement port changes to or tests.</summary>
public static partial class MovementStateNames
{
    public static readonly Name DIE_InPlace = new("DIE_InPlace");

    public static readonly Name GameEnded = new("GameEnded");

    public static readonly Name GrabFence = new("GrabFence");

    public static readonly Name GrabLedge = new("GrabLedge");

    public static readonly Name LandStAlAk0 = new("LandStAlAk0");

    public static readonly Name OnLedge = new("OnLedge");

    public static readonly Name PlayerFalling = new("PlayerFalling");

    public static readonly Name PlayerFallingFromRoll = new("PlayerFallingFromRoll");

    public static readonly Name PlayerFallToBackGrab = new("PlayerFallToBackGrab");

    public static readonly Name PlayerLanding = new("PlayerLanding");

    public static readonly Name PlayerLandingSilent = new("PlayerLandingSilent");

    public static readonly Name s_BarrierJumpDetect = new("s_BarrierJumpDetect");

    public static readonly Name s_Blast = new("s_Blast");

    public static readonly Name s_BTW_CrouchLeft = new("s_BTW_CrouchLeft");

    public static readonly Name s_BTW_CrouchRight = new("s_BTW_CrouchRight");

    public static readonly Name s_BTW_down = new("s_BTW_down");

    public static readonly Name s_BTW_StandLeft = new("s_BTW_StandLeft");

    public static readonly Name s_BTW_StandRight = new("s_BTW_StandRight");

    public static readonly Name s_BTW_up = new("s_BTW_up");

    public static readonly Name s_BTWCorner = new("s_BTWCorner");

    public static readonly Name s_BTWFireCrouchLeft = new("s_BTWFireCrouchLeft");

    public static readonly Name s_BTWFireCrouchRight = new("s_BTWFireCrouchRight");

    public static readonly Name s_BTWFireStandLeft = new("s_BTWFireStandLeft");

    public static readonly Name s_BTWFireStandRight = new("s_BTWFireStandRight");

    public static readonly Name s_ChangeLedgeWait = new("s_ChangeLedgeWait");

    public static readonly Name s_ChangePipeV = new("s_ChangePipeV");

    public static readonly Name s_Dead = new("s_Dead");

    public static readonly Name s_downTopPipe = new("s_downTopPipe");

    public static readonly Name s_DropAttack_Landing = new("s_DropAttack_Landing");

    public static readonly Name s_EnterBTW = new("s_EnterBTW");

    public static readonly Name s_FallDown = new("s_FallDown");

    public static readonly Name s_FeetLedgeDetect = new("s_FeetLedgeDetect");

    public static readonly Name s_FirstWallJumpPrepareFromJump = new("s_FirstWallJumpPrepareFromJump");

    public static readonly Name s_FirstWallJumpSaltoPrepareFromJump = new("s_FirstWallJumpSaltoPrepareFromJump");

    public static readonly Name s_GrabbingFromLedge_Begin = new("s_GrabbingFromLedge_Begin");

    public static readonly Name s_GrabLadder = new("s_GrabLadder");

    public static readonly Name s_GrabPipe = new("s_GrabPipe");

    public static readonly Name s_GrabPipH = new("s_GrabPipH");

    public static readonly Name s_GrabZip = new("s_GrabZip");

    public static readonly Name s_LadderGrabFall = new("s_LadderGrabFall");

    public static readonly Name s_OnPipe = new("s_OnPipe");

    public static readonly Name s_OnPipeH = new("s_OnPipeH");

    public static readonly Name s_PipH_Fire2 = new("s_PipH_Fire2");

    public static readonly Name s_PipH_Fire4 = new("s_PipH_Fire4");

    public static readonly Name s_PlayerFalling_OutOfWorld = new("s_PlayerFalling_OutOfWorld");

    public static readonly Name s_PlayerJump = new("s_PlayerJump");

    public static readonly Name s_PlayerRolling = new("s_PlayerRolling");

    public static readonly Name s_PlayerRollingUp = new("s_PlayerRollingUp");

    public static readonly Name s_PlayerWalkingCr0H = new("s_PlayerWalkingCr0H");

    public static readonly Name s_PlayerWalkingCr2H = new("s_PlayerWalkingCr2H");

    public static readonly Name s_PlayerWalkingMario_T_0HTo2H = new("s_PlayerWalkingMario_T_0HTo2H");

    public static readonly Name s_PlayerWalkingMario_T_2HToOH = new("s_PlayerWalkingMario_T_2HToOH");

    public static readonly Name s_PlayerWalkingMario_T_CrToSt = new("s_PlayerWalkingMario_T_CrToSt");

    public static readonly Name s_PlayerWalkingMario_T_StToCr = new("s_PlayerWalkingMario_T_StToCr");

    public static readonly Name s_PlayerWalkingSt0H = new("s_PlayerWalkingSt0H");

    public static readonly Name s_PlayerWalkingSt2H = new("s_PlayerWalkingSt2H");

    public static readonly Name s_PlayerWalkingWeapon = new("s_PlayerWalkingWeapon");

    public static readonly Name s_PlayerWalkingWeapon_T_CrToSt = new("s_PlayerWalkingWeapon_T_CrToSt");

    public static readonly Name s_PlayerWalkingWeapon_T_StToCr = new("s_PlayerWalkingWeapon_T_StToCr");

    public static readonly Name s_PreSplitJump = new("s_PreSplitJump");

    public static readonly Name s_SortieBTW = new("s_SortieBTW");

    public static readonly Name s_SpeedPipe = new("s_SpeedPipe");

    public static readonly Name s_ThrowGoTo = new("s_ThrowGoTo");

    public static readonly Name s_TopPipe = new("s_TopPipe");

    public static readonly Name s_TopPipeCr = new("s_TopPipeCr");

    public static readonly Name s_Tr_Barrier_Above_Ledge = new("s_Tr_Barrier_Above_Ledge");

    public static readonly Name s_Tr_PipeV2H_Oriente = new("s_Tr_PipeV2H_Oriente");

    public static readonly Name s_Tr_PipeV2H_OrienteDown = new("s_Tr_PipeV2H_OrienteDown");

    public static readonly Name s_TrPipe2Ledge = new("s_TrPipe2Ledge");

    public static readonly Name s_WaistLedgeDetect = new("s_WaistLedgeDetect");

    public static readonly Name s_WaitAtTopLadder = new("s_WaitAtTopLadder");

    public static readonly Name s_WaitAtTopPipe = new("s_WaitAtTopPipe");

    public static readonly Name s_ZipLine = new("s_ZipLine");

    public static readonly Name s_ZipLineSlow = new("s_ZipLineSlow");

    public static readonly Name s_ZipLineSlowTR = new("s_ZipLineSlowTR");

    public static readonly Name s_ZipLineSpeed = new("s_ZipLineSpeed");

    public static readonly Name s_ZipLineSpeedTR = new("s_ZipLineSpeedTR");

    public static readonly Name s_ZipLineTrToNormal = new("s_ZipLineTrToNormal");

    public static readonly Name s_ZipWall = new("s_ZipWall");

    public static readonly Name TopLedge = new("TopLedge");

    public static readonly Name TopLedgeBarrier = new("TopLedgeBarrier");

    public static readonly Name TopLedgeCrouch = new("TopLedgeCrouch");

    public static readonly Name VandammeWeapon = new("VandammeWeapon");
}
