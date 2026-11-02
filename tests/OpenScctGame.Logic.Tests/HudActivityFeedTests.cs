using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class HudActivityFeedTests
{
    private static PlayerReplicationInfo Player(string name, int id, int team) =>
        new() { PlayerName = name, PlayerID = id, Team = new STeamInfo { TeamIndex = team } };

    private static HudActivityEntry Event(float now, string actor = "Sam") =>
        new(0, HudActivityKind.Kill, new(1, actor, 0), new(2, "Ghost", 1), HudActivityCause.None, "KILL", "", now, false);

    private static HudActivityEntry Classify(string messageClass, int message, PlayerReplicationInfo? actor = null,
        PlayerReplicationInfo? victim = null, UObject? source = null, PlayerReplicationInfo? local = null, float now = 1f)
    {
        Assert.True(HudActivityClassifier.TryCreate(messageClass, message, actor, victim, source, local, now, "", out HudActivityEntry entry));
        return entry;
    }

    [Fact]
    public void BurstShowsFiveNewestImmediatelyAndKeepsIdenticalCombatOutcomes()
    {
        var feed = new HudActivityFeed();
        for (int i = 0; i < 8; i++) feed.Add(Event(1f));
        Assert.Equal(5, feed.Entries.Count);
        Assert.Equal(new long[] { 8, 7, 6, 5, 4 }, feed.Entries.Select(e => e.Id));
        Assert.All(feed.Entries, e => Assert.Equal("Sam", e.Actor!.Name));
        feed.Tick(6.99f);
        Assert.Equal(5, feed.Entries.Count);
        feed.Tick(7f);
        Assert.Empty(feed.Entries);
    }

    [Fact]
    public void ReceiptSnapshotsNamesAndTeamsAndMarksLocalParticipants()
    {
        var actor = Player("Sam\nFisher", 10, 0);
        var victim = Player("Ghost", 20, 1);
        HudActivityEntry entry = Classify(HudMessages.DeathMessageClass, 23, actor, victim, local: victim);
        actor.PlayerName = "Renamed";
        actor.Team!.TeamIndex = 1;
        victim.PlayerName = "Gone";
        Assert.Equal(new HudActivityParticipant(10, "Sam Fisher", 0), entry.Actor);
        Assert.Equal(new HudActivityParticipant(20, "Ghost", 1), entry.Victim);
        Assert.True(entry.InvolvesLocalPlayer);
        Assert.Equal(HudActivityCause.Headshot, entry.Cause);
        Assert.Equal("KILL", entry.Label);
    }

    [Theory]
    [InlineData(16, HudActivityKind.Knockout, HudActivityCause.DropAttack)]
    [InlineData(17, HudActivityKind.Knockout, HudActivityCause.Elbow)]
    [InlineData(20, HudActivityKind.Knockout, HudActivityCause.Grab)]
    [InlineData(62, HudActivityKind.Sleep, HudActivityCause.Smoke)]
    [InlineData(63, HudActivityKind.Sleep, HudActivityCause.StickyCamera)]
    public void CombatUsesNormalizedAttackerThenVictim(int message, HudActivityKind kind, HudActivityCause cause)
    {
        HudActivityEntry entry = Classify(HudMessages.GameMessagesClass, message, Player("Attacker", 1, 0), Player("Victim", 2, 1));
        Assert.Equal(kind, entry.Kind);
        Assert.Equal(cause, entry.Cause);
        Assert.Equal("Attacker", entry.Actor!.Name);
        Assert.Equal("Victim", entry.Victim!.Name);
    }

    [Fact]
    public void MissingPlayersSelfDeathsAndEnvironmentDoNotInventKillers()
    {
        var victim = Player("Ghost", 2, 1);
        HudActivityEntry unknown = Classify(HudMessages.DeathMessageClass, 5, victim: victim);
        Assert.Equal(HudActivityKind.Death, unknown.Kind);
        Assert.Null(unknown.Actor);
        HudActivityEntry self = Classify(HudMessages.DeathMessageClass, (int)SDamageType.Suicided, victim, victim);
        Assert.Equal(HudActivityKind.Suicide, self.Kind);
        Assert.Null(self.Actor);
        HudActivityEntry fell = Classify(HudMessages.DeathMessageClass, (int)SDamageType.SFell, victim, victim);
        Assert.Equal(HudActivityKind.Death, fell.Kind);
        Assert.Equal(HudActivityCause.Fall, fell.Cause);
        Assert.Null(fell.Actor);
        Assert.Equal(HudActivityKind.Resign, Classify(HudMessages.DeathMessageClass, 24, victim, victim).Kind);
        HudActivityEntry absent = Classify(HudMessages.DeathMessageClass, 0);
        Assert.Null(absent.Actor);
        Assert.Null(absent.Victim);
    }

    [Fact]
    public void ActivityClassificationDoesNotRequireExtractedGameText()
    {
        var trigger = new SObjectiveTrigger { TriggerName = "Server room", Objective = new SObjective { ObjectiveName = "Terminal" } };
        Assert.Equal("Server room: Data captured", Classify(HudMessages.GameMessagesClass, 22, source: trigger).Text);
        Assert.Equal("Terminal: Objective neutralized", Classify(HudMessages.GameMessagesClass, 37, source: trigger).Text);
        Assert.Equal("Victory", Classify(HudMessages.GameMessagesClass, 4).Text);
        Assert.Equal("Ghost joined the match", Classify(HudMessages.EngineGameMessageClass, 1, Player("Ghost", 2, 1)).Text);
        Assert.Equal("Ghost left the match", Classify(HudMessages.EngineGameMessageClass, 4, Player("Ghost", 2, 1)).Text);
        Assert.Equal("Ghost used their last life", Classify(HudMessages.GameMessagesClass, 56, Player("Ghost", 2, 1)).Text);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(19)]
    [InlineData(39)]
    [InlineData(41)]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(55)]
    [InlineData(57)]
    [InlineData(58)]
    [InlineData(60)]
    [InlineData(61)]
    public void GuidanceAndUrgentLocalPromptsRetainTheirExistingRouting(int message) =>
        Assert.False(HudActivityClassifier.TryCreate(HudMessages.GameMessagesClass, message, null, null, null, null, 1f, "", out _));

    [Fact]
    public void AlarmRefreshIsSourceSpecificAndDoesNotExtendExpiry()
    {
        var feed = new HudActivityFeed();
        var first = new SAlarm { AlarmDescription = "Laboratory" };
        var second = new SAlarm { AlarmDescription = "Laboratory" };
        feed.Add(Classify(HudMessages.GameMessagesClass, 1, source: first, now: 1f));
        first.bAlarmOn = false;
        feed.Add(Classify(HudMessages.GameMessagesClass, 40, source: first, now: 4f));
        Assert.Single(feed.Entries);
        Assert.Equal(1f, feed.Entries[0].ReceivedAt);
        Assert.Equal("Alarm notice: Laboratory", feed.Entries[0].Text);
        feed.Add(Classify(HudMessages.GameMessagesClass, 1, source: second, now: 5f));
        Assert.Equal(2, feed.Entries.Count);
        feed.Tick(7f);
        Assert.Single(feed.Entries);
        Assert.Equal(5f, feed.Entries[0].ReceivedAt);
        feed.Add(Classify(HudMessages.GameMessagesClass, 1, source: first, now: 7f));
        Assert.Equal(2, feed.Entries.Count);
    }

    [Fact]
    public void BindPreservesEarlyArrivalAndRespawnButResetsAReplacementController()
    {
        var feed = new HudActivityFeed();
        var owner = new SPlayerController();
        feed.Add(Event(1f));
        feed.Bind(owner, 1f);
        feed.Bind(owner, 2f);
        Assert.Single(feed.Entries);
        feed.Bind(new SPlayerController(), 2f);
        Assert.Empty(feed.Entries);
        feed.Add(Event(3f));
        feed.Bind(null, 3f);
        Assert.Empty(feed.Entries);
    }

    [Fact]
    public void RollbackAndConnectionResetDiscardOldEvents()
    {
        var feed = new HudActivityFeed();
        feed.Add(Event(10f));
        feed.Tick(11f);
        feed.Tick(0f);
        Assert.Empty(feed.Entries);
        feed.Add(Event(0f));
        feed.Reset();
        Assert.Empty(feed.Entries);
        feed.Add(Event(1f));
        feed.Bind(new SPlayerController(), 1f);
        Assert.Single(feed.Entries);
    }

    [Fact]
    public void FeedRoutingAvoidsLegacyDuplicatesPreservesPromptsAndClearsOnRestart()
    {
        var world = new World(1, ENetMode.NM_Standalone);
        SPlayerController pc = world.Level.SpawnActor(new SPlayerController(), default, default)!;
        SHud hud = world.Level.SpawnActor(new SHud(), default, default)!;
        pc.myHUD = hud;
        hud.PlayerOwner = pc;
        pc.PlayerReplicationInfo = Player("Sam", 1, 0);
        var feed = new HudActivityFeed();
        HudMessages.ReceiveLocalizedMessage(pc, HudMessages.DeathMessageClass, 0, pc.PlayerReplicationInfo, Player("Ghost", 2, 1), null, feed);
        Assert.Single(feed.Entries);
        Assert.Empty(hud.GameMessageQueue);
        HudMessages.LocalizedMessage(hud, HudMessages.GameMessagesClass, 53, null, null, null, "Return to extraction", feed);
        Assert.Equal("Return to extraction", Assert.Single(hud.GameMessageQueue).Message);
        var alarm = new SAlarm { AlarmDescription = "Warehouse", bAlarmOn = true, Duration = 10, MessagePriority = 2 };
        HudMessages.LocalizedMessage(hud, HudMessages.GameMessagesClass, 1, null, null, alarm, "Warehouse intruder", feed);
        Assert.Equal(2, feed.Entries.Count);
        Assert.Contains(hud.MessageStack, m => m.Message == "Warehouse intruder");
        HudMessages.ReceiveLocalizedMessage(pc, HudMessages.GameMessagesClass, 2, null, null, null, feed);
        Assert.Equal("Match restarted", Assert.Single(feed.Entries).Text);
    }

    [Fact]
    public void SpectatorHighlightBelongsToTheLocalOwnerRatherThanTheWatchedPlayer()
    {
        var world = new World(1, ENetMode.NM_Standalone);
        SPlayerController owner = world.Level.SpawnActor(new SPlayerController(), default, default)!;
        SPlayerController watched = world.Level.SpawnActor(new SPlayerController(), default, default)!;
        SHud hud = world.Level.SpawnActor(new SHud(), default, default)!;
        owner.myHUD = hud;
        owner.PlayerReplicationInfo = Player("Spectator", 1, 0);
        owner.PlayerReplicationInfo.bOnlySpectator = true;
        watched.PlayerReplicationInfo = Player("Watched", 2, 0);
        hud.PlayerOwner = owner;
        hud.P = new SPawnAttaque { Controller = watched, PlayerReplicationInfo = watched.PlayerReplicationInfo };
        var victim = Player("Other", 3, 1);
        var feed = new HudActivityFeed();

        HudMessages.ReceiveLocalizedMessage(owner, HudMessages.DeathMessageClass, 0, watched.PlayerReplicationInfo, victim, null, feed);
        Assert.False(Assert.Single(feed.Entries).InvolvesLocalPlayer);
        HudMessages.ReceiveLocalizedMessage(owner, HudMessages.DeathMessageClass, 0, owner.PlayerReplicationInfo, victim, null, feed);
        Assert.True(feed.Entries[0].InvolvesLocalPlayer);
    }
}
