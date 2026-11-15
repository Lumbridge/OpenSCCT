using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class Ps2TerminalTests
{
    private static (ObjWorld World, SComputerObjectiveTrigger Terminal) Make(EGameMode mode, int handicap = 0)
    {
        var world = new ObjWorld();
        world.Level.Info.Game = new SGameInfo { MatchInfo = new USMatchInfo { GameMode = mode, Handicap = handicap } };
        SComputerObjectiveTrigger? terminal = null;
        SObjective? objective = null;
        world.Load(w =>
        {
            terminal = w.Place(new SComputerObjectiveTrigger
            {
                bPS2Objective = true, StaticMesh = AssetRef.None, CaptureTime = 12, DominationCaptureTime = 12,
                Ps2SkinActive = new AssetRef("PS2.Active"), Ps2SkinInactive = new AssetRef("PS2.Inactive"),
            }, default);
            objective = new SObjective();
            objective.Triggers.Add(terminal);
            w.Place(objective, default);
        });
        objective!.Reset();
        world.Run(2.1f); // Source reset first reverses the ten-second clip at5x.
        return (world, terminal!);
    }

    [Fact]
    public void Neutralization_keeps_source_progress_and_finishes_entire_alarm_restore_timeline_without_renderer()
    {
        var (world, terminal) = Make(EGameMode.GM_Neutralization, 2);
        Assert.Equal(14, terminal.CaptureTime);
        terminal.eventTriggering(true);
        world.Run(4f);
        world.Gri.ElapsedTime = 4;
        terminal.eventTriggering(false);
        Assert.Equal(10, terminal.CaptureTime);
        Assert.Equal(0f, terminal.Ps2AnimationRate);
        terminal.eventLock();
        Assert.True(terminal.bLocked);
        world.Run(.8f); // Finish reverse hack, then close the alarm lid.
        Assert.True(terminal.IsInState("s_TemporaryInactive"));
        Assert.False(terminal.bActive);
        terminal.eventUnLock();
        Assert.Equal((byte)4, terminal.Ps2VisualState);
        world.Run(2.4f);
        Assert.True(terminal.bLocked);
        world.Run(.2f);
        Assert.Equal((byte)0, terminal.Ps2VisualState);
        Assert.True(terminal.bLocked); // Restore the saved four seconds at4x before enabling use.
        world.Run(.8f);
        Assert.False(terminal.bLocked);
        Assert.True(terminal.bActive);
        Assert.True(terminal.IsInState("s_Initial"));
        Assert.Equal(10, terminal.CaptureTime);
        Assert.Equal(0f, terminal.Ps2AnimationRate);
    }

    [Fact]
    public void Renderer_completion_cannot_shortcut_source_gameplay_unlock_delay()
    {
        var (world, terminal) = Make(EGameMode.GM_Multi);
        terminal.eventLock();
        terminal.eventUnLock();
        int serial = terminal.Ps2AnimationSerial;
        terminal.Ps2AnimationFinished(serial);
        terminal.Ps2AnimationFinished(serial);
        terminal.Ps2AnimationFinished(serial - 1);
        Assert.True(terminal.bLocked);
        Assert.False(terminal.bActive);
        world.Run(2.4f);
        Assert.True(terminal.bLocked);
        world.Run(.2f);
        Assert.False(terminal.bLocked);
        Assert.True(terminal.bActive);
    }

    [Fact]
    public void Story_cancel_keeps_full_capture_time_and_reverses_source_hack_animation()
    {
        var (world, terminal) = Make(EGameMode.GM_Multi);
        terminal.eventTriggering(true);
        world.Run(3f);
        terminal.eventTriggering(false);
        Assert.Equal(12, terminal.CaptureTime);
        Assert.Equal((byte)2, terminal.Ps2VisualState);
        Assert.True(terminal.Ps2AnimationBackward);
        Assert.True(terminal.Ps2AnimationContinue);
    }
}
