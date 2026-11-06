using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenScct.Bots;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BotPreparationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BotFreeRoundsStartWithoutPreparingNavigationOrCreatingARunner(bool practice)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0;
        int factories = 0;
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, _) => { preparations++; return pending.Task; },
            Bots = (_, _, _) => { factories++; throw new InvalidOperationException("No bots were requested."); },
        };
        settings = settings with { Setup = settings.Setup with { Practice = practice, Bots = BotFill.Off } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 619, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 2);

        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match &&
            c.Session?.LocalPlayer?.Pawn is not null), 20),
            $"{rig.Server.Status()} | preparing={host.Lobby.PreparingBots}, preparation calls={preparations}");
        Assert.Equal(0, preparations);
        Assert.Equal(0, factories);
        Assert.False(host.Lobby.PreparingBots);
        Assert.Null(rig.Server.RoundBots);
        Assert.DoesNotContain(rig.Server.Players, p => p.IsBot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedBotsStillWaitForPreparationBeforeTheRoundStarts(bool practice)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0;
        int factories = 0;
        OnlineBotFactory factory = OnlineServerBots.Factory(new AiNavCache(string.Empty));
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, _) => { preparations++; return pending.Task; },
            Bots = (host, maps, bots) => { factories++; return factory(host, maps, bots); },
        };
        settings = settings with { Setup = settings.Setup with { Practice = practice, Bots = BotFill.PerSide, BotsPerSide = 1 } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 620, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 4);

        Assert.True(rig.RunUntil(() => host.Lobby.PreparingBots && guest.Lobby.PreparingBots, 5));
        rig.RunUntil(() => false, 1);
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
        Assert.All(rig.Clients, client => Assert.Equal(OnlineClientState.Lobby, client.State));
        Assert.Equal(1, preparations);
        Assert.Equal(0, factories);

        pending.SetResult();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match &&
            c.Session?.LocalPlayer?.Pawn is not null), 20), rig.Server.Status());
        Assert.Equal(1, preparations);
        Assert.Equal(1, factories);
        Assert.False(host.Lobby.PreparingBots);
        Assert.NotNull(rig.Server.RoundBots);
        Assert.Equal(2, rig.Server.Players.Count(p => p.IsBot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingBotsAfterCancelStartsWithoutWaitingForTheOldPreparation(bool practice)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0;
        int factories = 0;
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, _) => { preparations++; return pending.Task; },
            Bots = (_, _, _) => { factories++; throw new InvalidOperationException("Preparation has not completed."); },
        };
        settings = settings with { Setup = settings.Setup with { Practice = practice, Bots = BotFill.PerSide, BotsPerSide = 1 } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 621, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 4);
        Assert.True(rig.RunUntil(() => host.Lobby.PreparingBots && guest.Lobby.PreparingBots, 5));

        host.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => !c.Lobby.PreparingBots && c.Lobby.Countdown == 0), 5));
        host.SetSetup(host.Lobby.Setup with { Bots = BotFill.Off });
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.Lobby.Setup.Bots == BotFill.Off &&
            c.Lobby.Players.Count == 2), 5));
        ReadyAndLaunch(rig, host, guest, 2);

        bool showedPreparation = false;
        Assert.True(rig.RunUntil(() =>
        {
            showedPreparation |= rig.Clients.Any(c => c.Lobby.PreparingBots);
            return rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null);
        }, 20), rig.Server.Status());
        Assert.False(showedPreparation);
        Assert.False(pending.Task.IsCompleted);
        Assert.Equal(1, preparations);
        Assert.Equal(0, factories);
        Assert.Null(rig.Server.RoundBots);
    }

    [Theory]
    [InlineData("addbot merc")]
    [InlineData("placebot merc")]
    public void FirstPracticeBotPreparesWithoutBlockingTheLiveRound(string command)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0;
        int factories = 0;
        OnlineBotFactory factory = OnlineServerBots.Factory(new AiNavCache(string.Empty));
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, _) => { preparations++; return pending.Task; },
            Bots = (server, maps, bots) => { factories++; return factory(server, maps, bots); },
        };
        settings = settings with { Setup = settings.Setup with { Practice = true, Bots = BotFill.Off } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 622, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 2);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match &&
            c.Session?.LocalPlayer?.Pawn is not null), 20));
        Assert.Equal(0, preparations);
        Assert.Equal(0, factories);
        var messages = new List<string>();
        host.MessageReceived += message => { if (message.IsSystem) messages.Add(message.Text); };
        host.Chat("!botstop");
        Assert.True(rig.RunUntil(() => messages.Any(message => message.Contains("stopped", StringComparison.Ordinal)), 5));
        messages.Clear();
        host.Chat("!" + command);
        Assert.True(rig.RunUntil(() => preparations == 1 &&
            messages.Any(message => message.StartsWith("Preparing bots", StringComparison.Ordinal)), 5));
        float worldTime = rig.Server.Host.World.Level.TimeSeconds;

        // Exceed the transport's idle timeout: the server must still tick and exchange client packets.
        rig.RunUntil(() => false, 20);
        Assert.All(rig.Clients, client => Assert.Equal(OnlineClientState.Match, client.State));
        Assert.True(rig.Server.Host.World.Level.TimeSeconds > worldTime + 15);
        Assert.Equal(1, preparations);
        Assert.Equal(0, factories);
        Assert.Null(rig.Server.RoundBots);
        Assert.DoesNotContain(rig.Server.Players, player => player.IsBot);

        pending.SetResult();
        Assert.True(rig.RunUntil(() => rig.Server.Players.Count(player => player.IsBot) == 1 &&
            guest.Lobby.Players.Count(player => player.IsBot) == 1, 10));
        Assert.Equal(1, preparations);
        Assert.Equal(1, factories);
        Assert.True(rig.Server.RoundBots!.Stopped);
        var bot = rig.Server.Players.Single(player => player.IsBot);
        Assert.NotNull(rig.Server.RoundBots.ControllerOf(bot.Id)?.Pawn);
        Assert.All(rig.Clients, client => Assert.Equal(OnlineClientState.Match, client.State));
    }

    [Fact]
    public void FailedLiveBotPreparationReportsTheErrorAndCanBeRetried()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int preparations = 0;
        int factories = 0;
        OnlineBotFactory factory = OnlineServerBots.Factory(new AiNavCache(string.Empty));
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, _) => { preparations++; return pending.Task; },
            Bots = (server, maps, bots) => { factories++; return factory(server, maps, bots); },
        };
        settings = settings with { Setup = settings.Setup with { Practice = true, Bots = BotFill.Off } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 623, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 2);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match &&
            c.Session?.LocalPlayer?.Pawn is not null), 20));
        var messages = new List<string>();
        host.MessageReceived += message => { if (message.IsSystem) messages.Add(message.Text); };
        host.Chat("!addbot merc");
        Assert.True(rig.RunUntil(() => preparations == 1, 5));

        pending.SetException(new IOException("navigation test failure"));
        Assert.True(rig.RunUntil(() => messages.Any(message =>
            message.StartsWith("Could not prepare bots:", StringComparison.Ordinal) &&
            message.Contains("navigation test failure", StringComparison.Ordinal)), 5));
        Assert.Equal(0, factories);
        Assert.Null(rig.Server.RoundBots);
        Assert.All(rig.Clients, client => Assert.Equal(OnlineClientState.Match, client.State));

        pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Chat("!addbot merc");
        Assert.True(rig.RunUntil(() => preparations == 2, 5));
        pending.SetResult();
        Assert.True(rig.RunUntil(() => rig.Server.Players.Count(player => player.IsBot) == 1 &&
            guest.Lobby.Players.Count(player => player.IsBot) == 1, 10));
        Assert.Equal(2, preparations);
        Assert.Equal(1, factories);
        Assert.NotNull(rig.Server.RoundBots);
        Assert.All(rig.Clients, client => Assert.Equal(OnlineClientState.Match, client.State));
    }

    [Fact]
    public void LiveBotPreparationTracksChangedActivationMasksAndReusesThePreparedGraphs()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var pending = new List<TaskCompletionSource>();
        var masks = new List<byte?>();
        var nav = new AiNavCache(string.Empty);
        int factories = 0;
        int builtBeforeFactory = -1;
        int builtAfterFactory = -1;
        OnlineBotFactory factory = OnlineServerBots.Factory(nav);
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            PrepareBots = (_, _, mask) =>
            {
                masks.Add(mask);
                var task = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add(task);
                return task.Task;
            },
            Bots = (server, maps, bots) =>
            {
                factories++;
                builtBeforeFactory = nav.Built;
                var runner = factory(server, maps, bots);
                builtAfterFactory = nav.Built;
                return runner;
            },
        };
        settings = settings with { Setup = settings.Setup with { Practice = true, Bots = BotFill.Off } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 624, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        var host = Join(rig, network, "Host", 0, true);
        var guest = Join(rig, network, "Guest", 1, false);
        ReadyAndLaunch(rig, host, guest, 2);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match &&
            c.Session?.LocalPlayer?.Pawn is not null), 20));
        Assert.Empty(pending);

        rig.Server.Host.World.Level.Info.GEActivationMask = 5;
        host.Chat("!addbot merc");
        Assert.True(rig.RunUntil(() => pending.Count == 1, 5));
        Assert.Equal((byte)5, masks[0]);
        nav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, activationMask: masks[0]);
        Assert.Equal(2, nav.Built);

        // A door or other gameplay element can change while the worker prepares its original graph.
        rig.Server.Host.World.Level.Info.GEActivationMask = 9;
        pending[0].SetResult();
        Assert.True(rig.RunUntil(() => pending.Count == 2, 5));
        Assert.Equal((byte)9, masks[1]);
        Assert.Equal(0, factories);
        Assert.Null(rig.Server.RoundBots);
        Assert.DoesNotContain(rig.Server.Players, player => player.IsBot);

        nav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, activationMask: masks[1]);
        Assert.Equal(4, nav.Built);
        pending[1].SetResult();
        Assert.True(rig.RunUntil(() => rig.Server.Players.Count(player => player.IsBot) == 1 &&
            guest.Lobby.Players.Count(player => player.IsBot) == 1, 10));
        Assert.Equal(2, pending.Count);
        Assert.Equal(1, factories);
        Assert.Equal(4, builtBeforeFactory);
        Assert.Equal(builtBeforeFactory, builtAfterFactory);
        Assert.Equal(4, nav.Built);
        Assert.NotNull(rig.Server.RoundBots);
    }

    private static OnlineClient Join(OnlineRig rig, SimLinkNetwork network, string name, int team, bool host) =>
        rig.Add(new OnlineClient(JoinTarget.Custom("Bot preparation", () => network.Bind(name), "server"),
            OnlineRig.ClientSettings(name, team) with { HostKey = host ? "k" : null }, rig.Now));

    private static void ReadyAndLaunch(OnlineRig rig, OnlineClient host, OnlineClient guest, int players)
    {
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby &&
            c.Lobby.Players.Count == players), 10));
        guest.SetReady(true);
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5));
        host.Launch();
    }
}
