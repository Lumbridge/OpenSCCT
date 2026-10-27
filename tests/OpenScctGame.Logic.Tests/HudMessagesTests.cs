using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The HUD's message routing (spec/hud/messages.md): stacks, the game queue, death messages.</summary>
public sealed class HudMessagesTests
{
    private sealed class Rig
    {
        public Rig()
        {
            World = new World(1, ENetMode.NM_Standalone);
            Level level = World.Level;
            level.Info.m_dT = Sim.DeltaTime;
            var gri = new GameReplicationInfo { GameMode = EGameMode.GM_Multi };
            gri.Teams[0] = new STeamInfo { TeamIndex = 0 };
            gri.Teams[1] = new STeamInfo { TeamIndex = 1 };
            level.Info.GRI = gri;
            Pc = level.SpawnActor(new SPlayerController(), default, default)!;
            Pc.PlayerReplicationInfo ??= new PlayerReplicationInfo();
            Pc.PlayerReplicationInfo.PlayerName = "Sam";
            Pc.PlayerReplicationInfo.Team = gri.Teams[0];
            Pc.GameReplicationInfo = gri;
            Hud = level.SpawnActor(new SHud(), default, default)!;
            Hud.PlayerOwner = Pc;
            Pc.myHUD = Hud;
            Mate = new PlayerReplicationInfo { PlayerName = "Ghost", Team = gri.Teams[0] };
            Merc = new PlayerReplicationInfo { PlayerName = "Shadownet", Team = gri.Teams[1] };
        }

        public World World { get; }

        public SPlayerController Pc { get; }

        public SHud Hud { get; }

        public PlayerReplicationInfo Mate { get; }

        public PlayerReplicationInfo Merc { get; }

        public void Seconds(float s)
        {
            for (int i = 0; i < (int)(s * Sim.Hz); i++)
            {
                World.Step();
            }
        }
    }

    [Fact]
    public void DeathMessagesSayWhoKilledWhom()
    {
        var killer = new PlayerReplicationInfo { PlayerName = "Shadownet" };
        var victim = new PlayerReplicationInfo { PlayerName = "Ghost" };
        Assert.Equal("Shadownet killed Ghost", HudMessages.DeathString(0, killer, victim));
        Assert.Equal("Shadownet sniped Ghost", HudMessages.DeathString(23, killer, victim));
        Assert.Equal("Ghost has resigned from the game.", HudMessages.DeathString(24, killer, victim));
        Assert.Equal("Shadownet has broken Ghost's neck", HudMessages.DeathString(19, killer, victim));
        Assert.StartsWith("Ghost ", HudMessages.DeathString(18, killer, victim), System.StringComparison.Ordinal);
        Assert.StartsWith("Ghost ", HudMessages.DeathString(4, victim, victim), System.StringComparison.Ordinal);
        Assert.NotEqual("Ghost killed Ghost", HudMessages.DeathString(4, victim, victim));
        Assert.Equal("Someone killed Ghost", HudMessages.DeathString(30, null, victim));
        Assert.Equal("Sam entered the game.", HudMessages.EngineGameString(1, new PlayerReplicationInfo { PlayerName = "Sam" }));
        Assert.Equal("Sam has left the game.", HudMessages.EngineGameString(4, new PlayerReplicationInfo { PlayerName = "Sam" }));
        Assert.Equal("", HudMessages.EngineGameString(4, null));
    }

    [Fact]
    public void ATeammatesDeathIsPriorityTwoAndAnEnemysPriorityOne()
    {
        var rig = new Rig();
        HudMessages.ReceiveLocalizedMessage(rig.Pc, HudMessages.DeathMessageClass, 0, rig.Merc, rig.Mate, null);
        HudMessages.ReceiveLocalizedMessage(rig.Pc, HudMessages.DeathMessageClass, 0, rig.Mate, rig.Merc, null);
        Assert.Equal(["Ghost killed Shadownet", "Shadownet killed Ghost"], rig.Hud.GameMessageQueue.Select(m => m.Message));
        Assert.Equal([1, 2], rig.Hud.GameMessageQueue.Select(m => (int)m.prio));
    }

    [Fact]
    public void TheGameQueueKeepsAscendingPriorityWithTheScriptsQuirk()
    {
        var rig = new Rig();
        HudMessages.SetGameMessage(rig.Hud, "a1", 1);
        HudMessages.SetGameMessage(rig.Hud, "c3", 3);
        HudMessages.SetGameMessage(rig.Hud, "a1", 1);     // already queued
        HudMessages.SetGameMessage(rig.Hud, "z0", 0);     // below the first: the front
        HudMessages.SetGameMessage(rig.Hud, "b2", 2);     // in between: just after the first higher entry
        Assert.Equal(["z0", "a1", "c3", "b2"], rig.Hud.GameMessageQueue.Select(m => m.Message));
    }

    [Fact]
    public void TheQueueShowsItsHeadForFiveSecondsThenTheNext()
    {
        var rig = new Rig();
        HudMessages.SetGameMessage(rig.Hud, "first", 2);
        HudMessages.SetGameMessage(rig.Hud, "second", 2);
        HudMessages.Tick(rig.Hud);
        float start = rig.Hud.GameMessageStartTime;
        Assert.Equal(start + rig.Hud.GameMessageDisplayTime, rig.Hud.GameMessageEndTime);
        rig.Seconds(rig.Hud.GameMessageDisplayTime - 0.5f);
        HudMessages.Tick(rig.Hud);
        Assert.Equal("first", rig.Hud.GameMessageQueue[0].Message);
        rig.Seconds(1f);
        HudMessages.Tick(rig.Hud);
        Assert.Equal("second", rig.Hud.GameMessageQueue[0].Message);
        rig.Seconds(rig.Hud.GameMessageDisplayTime + 0.5f);
        HudMessages.Tick(rig.Hud);
        Assert.Empty(rig.Hud.GameMessageQueue);
        Assert.Equal(0f, rig.Hud.GameMessageEndTime);

        // The tutorial message holds the queue.
        HudMessages.SetGameMessage(rig.Hud, "held", 2);
        HudMessages.Tick(rig.Hud, tutorialShown: true);
        Assert.Equal(0f, rig.Hud.GameMessageEndTime);
    }

    [Fact]
    public void TheStackKeepsTwoAndARepeatKeepsItsStart()
    {
        var rig = new Rig();
        HudMessages.AddMessageToStack(rig.Hud, "one", 10, 0);
        rig.Seconds(1f);
        float started = rig.Hud.MessageStack[0].StartedTime;
        HudMessages.AddMessageToStack(rig.Hud, "one", 10, 0);
        Assert.Single(rig.Hud.MessageStack);
        Assert.Equal(started, rig.Hud.MessageStack[0].StartedTime);
        Assert.True(rig.Hud.MessageStack[0].StayInStackTime >= 10);
        HudMessages.AddMessageToStack(rig.Hud, "two", 10, 1);
        HudMessages.AddMessageToStack(rig.Hud, "three", 10, 1);
        Assert.Equal(["two", "three"], rig.Hud.MessageStack.Select(m => m.Message));   // the lowest priority went
        HudMessages.AddMessageToStack(rig.Hud, "", 10, 5);
        Assert.Equal(2, rig.Hud.MessageStack.Count);
        // Short times are floored at MessageMustStayTime (4 s).
        HudMessages.AddInfoMessageToStack(rig.Hud, "zone", 1, 0);
        Assert.Equal((int)rig.Hud.MessageMustStayTime, rig.Hud.InfoMessageStack[0].StayInStackTime);
        HudMessages.AddInfoMessageToStack(rig.Hud, "other zone", 1, 0);
        Assert.Single(rig.Hud.InfoMessageStack);
    }

    [Fact]
    public void TheStateAreaRestartsAnExpiredLine()
    {
        var rig = new Rig();
        HudMessages.AddMessageToAuxiliaryStack(rig.Hud, "Motion Tracking on", 3, 0);
        Assert.Equal(3, rig.Hud.AuxiliaryMessageStack[0].StayInStackTime);
        rig.Seconds(4f);
        HudMessages.AddMessageToAuxiliaryStack(rig.Hud, "Motion Tracking on", 3, 0);
        Assert.Equal(rig.World.Level.TimeSeconds, rig.Hud.AuxiliaryMessageStack[0].StartedTime);
        Assert.Equal(4, rig.Hud.AuxiliaryMessageStack[0].StayInStackTime);
        HudMessages.AddMessageToAuxiliaryStack(rig.Hud, "EMF on", 3, 1);
        Assert.Equal(["EMF on"], rig.Hud.AuxiliaryMessageStack.Select(m => m.Message));
        rig.Seconds(5f);
        HudMessages.Tick(rig.Hud);
        Assert.Empty(rig.Hud.AuxiliaryMessageStack);
    }

    [Fact]
    public void TheReticleMessageYieldsOnlyToHigherPriority()
    {
        var rig = new Rig();
        HudMessages.AddReticuleMessage(rig.Hud, "Reloading", 2, 1);
        HudMessages.AddReticuleMessage(rig.Hud, "Low ammo", 2, 0);
        Assert.Equal("Reloading", rig.Hud.ReticuleMessage.Message);
        HudMessages.AddReticuleMessage(rig.Hud, "Reloading", 5, 1);
        Assert.Equal(5, rig.Hud.ReticuleMessage.StayInStackTime);
        HudMessages.AddReticuleMessage(rig.Hud, "Jammed", 2, 3);
        Assert.Equal("Jammed", rig.Hud.ReticuleMessage.Message);
        HudMessages.AddReticuleMessage(rig.Hud, "", 2, 0);
        Assert.Equal("", rig.Hud.ReticuleMessage.Message);
    }

    [Fact]
    public void GameMessagesRouteByTheirSwitch()
    {
        var rig = new Rig();
        HudMessages.ReceiveLocalizedMessage(rig.Pc, HudMessages.GameMessagesClass, 56, rig.Mate, null, null);
        Assert.Contains(rig.Hud.MessageStack, m => m.Message == "Ghost used his last life");
        HudMessages.ReceiveLocalizedMessage(rig.Pc, HudMessages.GameMessagesClass, 4, null, null, null);
        HudMessages.ReceiveLocalizedMessage(rig.Pc, HudMessages.GameMessagesClass, 23, null, null, null);
        // 23 adds its line and a one-second blank; the stack keeps two, so the oldest went.
        Assert.Equal(2, rig.Hud.MessageStack.Count);
        Assert.Equal(" ", rig.Hud.MessageStack[1].Message);
        Assert.Contains(rig.Hud.GameMessageQueue, m => m.Message == "You Win" && m.prio == 2);
        Assert.Equal("\none\ntwo", HudMessages.StackText(["one", "two"]));
        Assert.Equal("", HudMessages.StackText([]));
        HudMessages.AddHudNumberMessageToStack(rig.Hud, 7, 3, 0);
        Assert.Contains(rig.Hud.MessageStack, m => m.Message == rig.Hud.TextMalfunction);
    }
}
