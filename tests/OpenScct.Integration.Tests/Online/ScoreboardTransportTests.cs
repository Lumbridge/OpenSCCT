using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Scoreboards;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class ScoreboardTransportTests
{
    [Fact]
    public void MaximumRetainedRosterFitsBoundedControlMessages()
    {
        var board = new RoundScoreboard
        {
            RoundId = Guid.NewGuid().ToString("N"), Sequence = 1,
            Rows = Enumerable.Range(1, 256).Select(i => new ScoreboardPlayer
            {
                ParticipantId = i, Name = "Participant " + i, Team = i % 2, LeftEarly = i > 24,
                Stats = Enumerable.Range(0, 100).ToDictionary(k => "career.metric_" + k, k => i * 1000 + k),
            }).ToArray(),
        };
        var lines = ScoreboardWire.Encode(board, 1);
        Assert.All(lines, line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) < 512));
        var receiver = new ScoreboardWire.Receiver();
        RoundScoreboard? result = null;
        foreach (var line in lines) receiver.Receive(line, out result, out _, out _);
        Assert.Equal(256, result!.Rows.Count);
        Assert.Equal(256099, result.Rows[^1].Stats!["career.metric_99"]);
    }

    [Fact]
    public void BothTeamsAndALateSpectatorReceiveOnlyDelayedStatsThenFinalBeforeResultsAndHistoricalLobby()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture unavailable.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with { AutoLaunch = false, EndScreenSeconds = 10f };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 943, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        int joined = 0;
        var received = new List<(string Name, long When, RoundScoreboard Board)>();
        OnlineClient Join(string name, int team)
        {
            var client = rig.Add(new OnlineClient(JoinTarget.Custom("Scoreboards", () => network.Bind(name + ++joined), "server"),
                OnlineRig.ClientSettings(name, team), rig.Now));
            client.SessionStarted += session => session.ScoreboardReceived += b => received.Add((name, rig.Now, b));
            return client;
        }
        var spy = Join("Spy", 0); var merc = Join("Merc", 1);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 2), 10));
        spy.SetReady(true); merc.SetReady(true);
        Assert.True(rig.RunUntil(() => spy.Lobby.Players.All(p => p.Ready), 10));
        spy.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.Scoreboard is not null &&
            c.Session.LocalPlayer?.Pawn is not null), 20));
        rig.RunUntil(() => false, 5); // Let spawn protection finish before the authoritative combat interaction.
        var spyController = (SPlayerController)rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == "Spy").Controller!;
        var mercController = (SPlayerController)rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == "Merc").Controller!;
        var pri = spyController.PlayerReplicationInfo!;
        mercController.PawnTakeDamage(0, spyController.Pawn, mercController.Pawn!.Location, default, SDamageType.SDTStickyShocker, 1);
        Assert.Equal(1, RoundStatCounters.Read(pri)[FeatKeys.SpyShockerStuns]);
        long changed = rig.Now;
        rig.RunUntil(() => false, 10);
        var watcher = Join("Watcher", 255);
        Assert.True(rig.RunUntil(() => watcher.State == OnlineClientState.Match && watcher.Session?.Scoreboard is not null, 10));
        while (rig.Now - changed < 29_000_000) rig.Step();
        Assert.DoesNotContain(received, r => r.Board.Rows.Any(p => p.Stats?.GetValueOrDefault(FeatKeys.SpyShockerStuns) == 1));
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.Session?.Scoreboard?.Rows.Any(p =>
            p.Name == "Spy" && p.Stats?.GetValueOrDefault(FeatKeys.SpyShockerStuns) == 1) == true), 34));
        Assert.All(received.Where(r => r.Board.Rows.Any(p => p.Stats?.GetValueOrDefault(FeatKeys.SpyShockerStuns) == 1)),
            r => Assert.True(r.When - changed >= 30_000_000));
        Assert.Equal(-1, watcher.Session!.ScoreboardLocalParticipantId);
        Assert.NotEqual(-1, spy.Session!.ScoreboardLocalParticipantId);
        mercController.PawnTakeDamage(0, spyController.Pawn, mercController.Pawn!.Location, default, SDamageType.SDTStickyShocker, 2);
        Assert.Equal(2, RoundStatCounters.Read(pri)[FeatKeys.SpyShockerStuns]);
        var game = rig.Server.Host.Game!;
        game.bGameEnded = true; game.GameReplicationInfo!.WinningTeam = 0; game.GameReplicationInfo.WinningReason = 0;
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.Session?.Scoreboard?.Completed == true), 2));
        Assert.All(rig.Clients, c => Assert.Equal(OnlineClientState.Match, c.State));
        Assert.All(rig.Clients, c => Assert.Equal(2, c.Session!.Scoreboard!.Rows.Single(p => p.Name == "Spy").Stats![FeatKeys.SpyShockerStuns]));
        string roundId = spy.Session!.Scoreboard!.RoundId;
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Results), 12));
        int activeCount = received.Count;
        spy.BackToLobby();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Session?.HistoricalScoreboard is not null), 10));
        Assert.Equal(activeCount, received.Count);
        Assert.All(rig.Clients, c => { Assert.Null(c.Session!.Scoreboard); Assert.Equal(roundId, c.LastScoreboard!.RoundId); });
        Assert.NotEqual(-1, spy.LastScoreboardLocalParticipantId);
    }

    [Fact]
    public void PoisonCreditedAfterDepartureIsReleasedAndSurvivesAReplacementPri()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture unavailable.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 944, SimLinkProfile.A);
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with { AutoLaunch = false };
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        int joined = 0;
        OnlineClient Join(string name, int team) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Poison scoreboard", () => network.Bind(name + ++joined), "server"),
            OnlineRig.ClientSettings(name, team), rig.Now));
        var spy = Join("Spy", 0); var returning = Join("ReturningMerc", 1); _ = Join("OtherMerc", 1);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 3), 10));
        foreach (var client in rig.Clients) client.SetReady(true);
        Assert.True(rig.RunUntil(() => spy.Lobby.Players.All(p => p.Ready), 10));
        spy.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
        rig.RunUntil(() => false, 5);
        SPlayerController Player(string name) => (SPlayerController)rig.Server.Host.Players.Single(p =>
            p.Controller?.PlayerReplicationInfo?.PlayerName == name).Controller!;
        var victim = Player("Spy"); var attacker = Player("ReturningMerc");
        var original = attacker.PlayerReplicationInfo!;
        int participantId = rig.Server.Host.Scoreboards.ParticipantId(original);
        var pawn = (SPawnAttaque)victim.Pawn!;
        victim.PawnTakeDamage(0, attacker.Pawn, pawn.Location, default, SDamageType.SDamage_Poison);
        Assert.True(pawn.bPoisonned);
        returning.Leave();
        Assert.True(rig.RunUntil(() => rig.Server.Players.All(p => p.Name != "ReturningMerc"), 10));
        Assert.True(original.bDeleteMe);
        Assert.Same(attacker, victim.PoisonInstigator);
        int atDeparture = original.Stats!.DamageGiven;
        pawn.NativeTickHealth(2f);
        victim.CountPoisonDrain();
        int credited = original.Stats.DamageGiven;
        Assert.True(credited > atDeparture);
        pawn.bPoisonned = false;
        victim.CountPoisonDrain();
        // A credit must age 30 seconds, then wait for the next 30-second combat release.
        Assert.True(rig.RunUntil(() => spy.Session?.Scoreboard?.Rows.Any(p => p.ParticipantId == participantId &&
            p.LeftEarly && p.Stats?.GetValueOrDefault("career.damagegiven") == credited) == true, 63));
        var rejoined = Join("ReturningMerc", 0);
        Assert.True(rig.RunUntil(() => rejoined.State == OnlineClientState.Match && rig.Server.Host.Players.Any(p =>
            p.Controller?.PlayerReplicationInfo?.PlayerName == "ReturningMerc"), 10));
        var restored = Player("ReturningMerc").PlayerReplicationInfo!;
        Assert.NotSame(original, restored);
        Assert.Same(original.Stats, restored.Stats);
        var final = rig.Server.Host.Scoreboards.Finish()!;
        var row = final.Rows.Single(p => p.Name == "ReturningMerc");
        Assert.Equal(participantId, row.ParticipantId);
        Assert.False(row.LeftEarly);
        Assert.Equal(credited, row.Stats!["career.damagegiven"]);
    }

    [Fact]
    public void TransfersCommitAtomicallyAndRejectMissingOrOversizedChunks()
    {
        var board = new RoundScoreboard { RoundId = Guid.NewGuid().ToString("N"), Sequence = 3,
            Rows = [new() { ParticipantId = 1, Name = "Spy", Stats = new Dictionary<string, int> { [FeatKeys.SpyShockerStuns] = 7 } }] };
        var lines = ScoreboardWire.Encode(board, 1);
        Assert.All(lines, line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) < 512));
        var receiver = new ScoreboardWire.Receiver();
        foreach (string line in lines.SkipLast(1))
        {
            Assert.True(receiver.Receive(line, out var pending, out _, out _));
            Assert.Null(pending);
        }
        receiver.Receive(lines[^1], out var result, out int local, out bool historical);
        Assert.Equal(board.RoundId, result!.RoundId);
        Assert.Equal(7, result.Rows[0].Stats![FeatKeys.SpyShockerStuns]);
        Assert.Equal(1, local); Assert.False(historical);
        receiver.Receive(lines[0], out _, out _, out _);
        receiver.Receive(lines[^1], out result, out _, out _);
        Assert.Null(result);
        receiver.Receive("#svm boardbegin " + new string('a', 32) + " 9999999 1 0", out result, out _, out _);
        Assert.Null(result);
        foreach (string line in ScoreboardWire.Encode(board, -1, historical: true))
            receiver.Receive(line, out result, out local, out historical);
        Assert.NotNull(result); Assert.True(historical); Assert.Equal(-1, local);
        string invalidId = new string('b', 32);
        receiver.Receive($"#svm boardbegin {invalidId} 4 1 0", out _, out _, out _);
        receiver.Receive($"#svm boarddata {invalidId} 0 /////w==", out _, out _, out _);
        receiver.Receive($"#svm boardend {invalidId}", out result, out _, out _);
        Assert.Null(result);
    }

    [Fact]
    public void OfflineRulesUseHostThreadSamplesAtLeastThirtySecondsOldThenRevealFinal()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture unavailable.");
        var network = new LoopbackNetwork();
        using var host = new ServerHost(network.Bind("host"), new ServerSettings { Map = MapCatalog.TestBox, Maps = WalkerRig.Maps });
        string error = "";
        var game = GameInfo.SpawnGameInfo<SGameInfo>(host.World.Level, "?GameMode=5", ref error);
        Assert.Null(host.Game); // Offline PlayMapLoader also owns rules through Level.Info.Game.
        game.GameReplicationInfo!.bMatchHasBegun = true;
        var pri = host.World.Level.SpawnActor(new PlayerReplicationInfo { PlayerName = "Spy", PlayerID = 1,
            Team = new TeamInfo { TeamIndex = 0 }, Stats = new USPlayerStats() }, default, default)!;
        host.Scoreboards.Tick(0);
        Assert.Null(host.Scoreboards.Current!.Rows.Single().Stats);
        pri.Stats!.AddFeat(FeatKeys.SpyShockerStuns, 5);
        for (int second = 1; second < 30; second++) host.Scoreboards.Tick(second * 1_000_000L);
        Assert.Null(host.Scoreboards.Current!.Rows.Single().Stats);
        host.Scoreboards.Tick(30_000_000);
        Assert.Equal(0, host.Scoreboards.Current!.Rows.Single().Stats!.GetValueOrDefault(FeatKeys.SpyShockerStuns));
        Assert.Equal(0, host.Scoreboards.Current.AsOfSeconds);
        pri.Stats.AddFeat(FeatKeys.SpyShockerStuns, 2);
        host.Scoreboards.Tick(31_000_000);
        Assert.Equal(0, host.Scoreboards.Current!.Rows.Single().Stats!.GetValueOrDefault(FeatKeys.SpyShockerStuns));
        Assert.Equal(60, host.Scoreboards.Current.NextCombatUpdateSeconds);
        host.Scoreboards.Tick(59_000_000);
        Assert.Equal(0, host.Scoreboards.Current!.Rows.Single().Stats!.GetValueOrDefault(FeatKeys.SpyShockerStuns));
        host.Scoreboards.Tick(60_000_000);
        Assert.Equal(5, host.Scoreboards.Current!.Rows.Single().Stats![FeatKeys.SpyShockerStuns]);
        Assert.Equal(90, host.Scoreboards.Current.NextCombatUpdateSeconds);
        game.bGameEnded = true; game.GameReplicationInfo.WinningTeam = 255;
        host.Scoreboards.Tick(60_000_001);
        Assert.Null(host.Scoreboards.Final); // The end-state can precede its authoritative winning result.
        game.GameReplicationInfo.WinningTeam = 0;
        host.Scoreboards.Tick(60_000_002);
        Assert.Equal(7, host.Scoreboards.Final!.Rows.Single().Stats![FeatKeys.SpyShockerStuns]);
        Assert.True(host.Scoreboards.Final.Completed);
        Assert.NotNull(host.Scoreboards.Final.EndedUtc);
    }

    [Fact]
    public void DepartedRowsAndPrivateIdentitySurviveAndRejoinUsesTheSameParticipant()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture unavailable.");
        var network = new LoopbackNetwork();
        using var host = new ServerHost(network.Bind("host"), new ServerSettings { Map = MapCatalog.TestBox, Maps = WalkerRig.Maps });
        string error = "";
        var game = GameInfo.SpawnGameInfo<SGameInfo>(host.World.Level, "?GameMode=5", ref error);
        game.GameReplicationInfo!.bMatchHasBegun = true;
        int sessionId = 10;
        host.Scoreboards.IdentityOf = _ => new ScoreboardIdentity("account:42", sessionId, AccountId: 42);
        PlayerReplicationInfo Spawn(int playerId) => host.World.Level.SpawnActor(new PlayerReplicationInfo { PlayerName = "Spy", PlayerID = playerId,
            Team = new TeamInfo { TeamIndex = 0 }, Stats = new USPlayerStats() }, default, default)!;
        var pri = Spawn(1);
        pri.Stats!.DamageGiven = 73;
        host.Scoreboards.Tick(0);
        int id = host.Scoreboards.ParticipantId(pri);
        host.Scoreboards.Depart(pri); pri.Destroy();
        host.Scoreboards.Tick(1_000_000);
        Assert.True(host.Scoreboards.Current!.Rows.Single().LeftEarly);
        sessionId = 20;
        var replacement = Spawn(2); replacement.Stats = pri.Stats;
        host.Scoreboards.Tick(2_000_000);
        Assert.Equal(id, host.Scoreboards.ParticipantId(replacement));
        Assert.False(host.Scoreboards.Current!.Rows.Single().LeftEarly);
        host.Scoreboards.Depart(replacement); replacement.Destroy();
        var final = host.Scoreboards.Finish()!;
        Assert.True(final.Partial);
        Assert.Equal(73, final.Rows.Single().Stats!["career.damagegiven"]);
        Assert.True(final.Rows.Single().LeftEarly);
        Assert.Equal(42, host.Scoreboards.Identity(id)!.AccountId);
        Assert.Equal(20, host.Scoreboards.Identity(id)!.SessionId);
        Assert.Equal(final.ElapsedSeconds, final.AsOfSeconds); // The authority stopped this round; all totals are now safe.
    }
}
