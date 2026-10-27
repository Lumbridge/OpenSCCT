using OpenScct.Core;

namespace OpenScct.Game.Movement;

// The state names of the ladder, hand-over-hand, fence, wall jump, split jump, Vandamme, barrier, low ledge and
// co-op moves.
public static partial class MovementStateNames
{
    public static readonly Name BackFromTopFence = new("BackFromTopFence");

    public static readonly Name OnFence = new("OnFence");

    public static readonly Name OnTopFence = new("OnTopFence");

    public static readonly Name s_Back_LadderTop = new("s_Back_LadderTop");

    public static readonly Name s_BarrierJump = new("s_BarrierJump");

    public static readonly Name s_ChangeSideLadder = new("s_ChangeSideLadder");

    public static readonly Name s_Coop_S_Base = new("s_Coop_S_Base");

    public static readonly Name s_Coop_S_Goto = new("s_Coop_S_Goto");

    public static readonly Name s_downTopLadder = new("s_downTopLadder");

    public static readonly Name s_EndLadderDrawWeapon = new("s_EndLadderDrawWeapon");

    public static readonly Name s_FallDropAttacked = new("s_FallDropAttacked");

    public static readonly Name s_FeetLedge = new("s_FeetLedge");

    public static readonly Name s_FirstWallJump = new("s_FirstWallJump");

    public static readonly Name s_FirstWallJumpPrepare = new("s_FirstWallJumpPrepare");

    public static readonly Name s_FirstWallJumpSalto = new("s_FirstWallJumpSalto");

    public static readonly Name s_Hoist_P_Base = new("s_Hoist_P_Base");

    public static readonly Name s_Hoist_P_WaitForHoist = new("s_Hoist_P_WaitForHoist");

    public static readonly Name s_Hoist_P_Waiting = new("s_Hoist_P_Waiting");

    public static readonly Name s_Hoist_S_Base = new("s_Hoist_S_Base");

    public static readonly Name s_Hoist_S_CheckIfOK = new("s_Hoist_S_CheckIfOK");

    public static readonly Name s_Hoist_S_GotoPos = new("s_Hoist_S_GotoPos");

    public static readonly Name s_Hoist_S_Jump = new("s_Hoist_S_Jump");

    public static readonly Name s_ItTarget_S_CheckIfOK = new("s_ItTarget_S_CheckIfOK");

    public static readonly Name s_LadderBase = new("s_LadderBase");

    public static readonly Name s_LadderDownFromTop = new("s_LadderDownFromTop");

    public static readonly Name s_LadderTop = new("s_LadderTop");

    public static readonly Name s_LadderTop_Cr = new("s_LadderTop_Cr");

    public static readonly Name s_LadderToTop = new("s_LadderToTop");

    public static readonly Name s_LadderToTopTr = new("s_LadderToTopTr");

    public static readonly Name s_LadderUp2 = new("s_LadderUp2");

    public static readonly Name s_OnLadder = new("s_OnLadder");

    public static readonly Name s_OnLadderTop = new("s_OnLadderTop");

    public static readonly Name s_OnPipeH_4 = new("s_OnPipeH_4");

    public static readonly Name s_OnStopLadder = new("s_OnStopLadder");

    public static readonly Name s_OrthoPipe_Tr = new("s_OrthoPipe_Tr");

    public static readonly Name s_PipeH_Base = new("s_PipeH_Base");

    public static readonly Name s_PipH_Fire2TR2 = new("s_PipH_Fire2TR2");

    public static readonly Name s_PipH_Fire2TR4 = new("s_PipH_Fire2TR4");

    public static readonly Name s_PipH_Return = new("s_PipH_Return");

    public static readonly Name s_PipH_TR2_2 = new("s_PipH_TR2_2");

    public static readonly Name s_PipH_TR2_4 = new("s_PipH_TR2_4");

    public static readonly Name s_PipH_TR2Fire2 = new("s_PipH_TR2Fire2");

    public static readonly Name s_PipH_TR2Fire4 = new("s_PipH_TR2Fire4");

    public static readonly Name s_ShortLadder_P_Base = new("s_ShortLadder_P_Base");

    public static readonly Name s_ShortLadder_P_GotoDest = new("s_ShortLadder_P_GotoDest");

    public static readonly Name s_ShortLadder_P_Out = new("s_ShortLadder_P_Out");

    public static readonly Name s_ShortLadder_P_WaitForShortLadder = new("s_ShortLadder_P_WaitForShortLadder");

    public static readonly Name s_ShortLadder_P_Waiting = new("s_ShortLadder_P_Waiting");

    public static readonly Name s_ShortLadder_S_Base = new("s_ShortLadder_S_Base");

    public static readonly Name s_ShortLadder_S_CheckIfOK = new("s_ShortLadder_S_CheckIfOK");

    public static readonly Name s_ShortLadder_S_GotoPos = new("s_ShortLadder_S_GotoPos");

    public static readonly Name s_ShortLadder_S_Jump = new("s_ShortLadder_S_Jump");

    public static readonly Name s_SpeedLadder = new("s_SpeedLadder");

    public static readonly Name s_SpeedLadderFromTop = new("s_SpeedLadderFromTop");

    public static readonly Name s_SplitJump = new("s_SplitJump");

    public static readonly Name s_Tr_PipeH2V = new("s_Tr_PipeH2V");

    public static readonly Name s_Tr_PipeV2H = new("s_Tr_PipeV2H");

    public static readonly Name s_WaistLedge = new("s_WaistLedge");

    public static readonly Name s_WaistLedge_Cr = new("s_WaistLedge_Cr");

    public static readonly Name s_WaitForChangeSideLadderL = new("s_WaitForChangeSideLadderL");

    public static readonly Name s_WaitForChangeSideLadderR = new("s_WaitForChangeSideLadderR");

    public static readonly Name s_WallJumpSalto = new("s_WallJumpSalto");

    public static readonly Name Vandamme = new("Vandamme");

    public static readonly Name VandammeBegin = new("VandammeBegin");

    public static readonly Name VandammeWaitForWeapon = new("VandammeWaitForWeapon");
}
