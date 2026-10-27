using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>Every [Uc] attribute of the movement port names a known unit with the right bytecode hash.</summary>
public sealed class MovementManifestTests
{
    private static readonly string[] States =
    [
        "s_PlayerWalking", "s_PlayerWalkingMario", "s_PlayerWalkingWeapon", "s_PlayerWalkingSt0H", "s_PlayerWalkingCr0H",
        "s_PlayerWalkingSt2H", "s_PlayerWalkingCr2H", "s_PlayerWalkingMario_T_StToCr", "s_PlayerWalkingMario_T_CrToSt",
        "s_PlayerWalkingWeapon_T_StToCr", "s_PlayerWalkingWeapon_T_CrToSt", "s_PlayerWalkingMario_T_2HToOH",
        "s_PlayerWalkingMario_T_0HTo2H", "s_PlayerHit", "s_PlayerJump", "PlayerFalling", "PlayerFallToBackGrab",
        "PlayerFallingFromRoll", "PlayerLanding", "PlayerLandingSilent", "s_PlayerRolling", "s_PlayerRollingUp",
        "s_PlayerFalling_OutOfWorld", "GrabLedge", "OnLedge", "s_ChangeLedgeWait", "TopLedge", "TopLedgeCrouch",
        "TopLedgeBarrier", "s_Tr_Barrier_Above_Ledge", "s_TrPipe2Ledge", "s_GrabPipe", "s_ChangePipeV", "s_OnPipe",
        "s_SpeedPipe", "s_TopPipe", "s_TopPipeCr", "s_WaitAtTopPipe", "s_downTopPipe", "s_GrabZip", "s_ZipLine",
        "s_ZipLineSpeedTR", "s_ZipLineSpeed", "s_ZipLineSlowTR", "s_ZipLineSlow", "s_ZipLineTrToNormal", "s_ZipWall",
        "s_BTW_Base", "s_EnterBTW", "s_BTW", "s_BTW_up", "s_BTW_down", "s_BTWCorner", "s_SortieBTW", "s_BTW_StandLeft",
        "s_BTW_StandRight", "s_BTW_CrouchLeft", "s_BTW_CrouchRight",
    ];

    [Fact]
    public void EveryMovementPortNamesItsUnitWithTheRightHash()
    {
        var index = new UcClassIndex(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
        IReadOnlyList<UcManifestCheck.Problem> problems = UcManifestCheck.Check(index, typeof(SGameInfo).Assembly);
        var mine = problems.Where(p =>
            p.Member.StartsWith("OpenScct.Game.Movement.", StringComparison.Ordinal) ||
            States.Any(s => p.Member.StartsWith("OpenScct.Game.SBase.SPlayerController+" + s + ".", StringComparison.Ordinal) ||
                            p.Member == "OpenScct.Game.SBase.SPlayerController+" + s)).ToList();
        Assert.Empty(mine);
    }

    [Fact]
    public void EveryMovementStateIsInTheControllersStateTable()
    {
        var pc = new SPlayerController();
        foreach (string s in States)
        {
            Assert.True(pc.GetStateTable().Find(new Name(s)) is not null, s);
        }
    }
}
