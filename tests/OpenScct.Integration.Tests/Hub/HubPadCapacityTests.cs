using System;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

[Collection(BotMatchCollection.Name)]
public sealed class HubPadCapacityTests
{
    // Exercise the same Hub and TestBox fixtures in CI and on machines with converted game content installed.
    private static readonly MapCatalog Maps = new(Path.Combine(
        MapCatalog.FindRepoRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repository fixtures are required."),
        "fixtures"));

    [Theory]
    [InlineData(EGameMode.GM_Multi, 12, 12, 24)]
    [InlineData(EGameMode.GM_Multi, 20, 12, 24)]
    [InlineData(EGameMode.GM_MercDeathMatch, 24, 24, 24)]
    [InlineData(EGameMode.GM_MercDeathMatch, 32, 24, 24)]
    public void ConfigAcceptsTwentyFourPlacesAndClampsLargerMatches(EGameMode mode, int requested, int teamSize, int slots)
    {
        var config = HubConfig.Default with
        {
            Pads = [new HubPadConfig { Id = "Pad1", Map = MapCatalog.TestBox, Mode = mode, TeamSize = requested }],
        };

        HubPadConfig pad = Assert.Single(config.Checked(Maps).Pads);
        Assert.True(pad.Playable);
        Assert.Equal(mode, pad.Mode);
        Assert.Equal(teamSize, pad.TeamSize);
        Assert.Equal(slots, pad.Slots);
        Assert.Equal(slots, pad.Needed);
    }

    [Theory]
    [InlineData(EGameMode.GM_Multi, 12, 12)]
    [InlineData(EGameMode.GM_MercDeathMatch, 0, 24)]
    public void LaptopAppliesTwentyFourPlacesAndPublishesThemToTheClient(EGameMode mode, int spySlots, int mercSlots)
    {
        var clock = new VirtualClock();
        var network = new LoopbackNetwork();
        using var server = new HubServer(network.Bind("server"), new HubSettings
        {
            Maps = Maps,
            Classes = WalkerRig.Classes,
            Config = HubConfig.Default with
            {
                Pads = [new HubPadConfig { Id = "Pad1", Map = MapCatalog.TestBox }],
            },
            MatchLauncher = new UnusedLauncher(),
        });
        using var client = new HubClient(network.Bind("client"), "server", clock.NowMicroseconds,
            new HubClientSettings { Maps = Maps, Classes = WalkerRig.Classes, PlayerName = "Host" });
        long frame = 0;
        void Step()
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
            server.Tick(clock.NowMicroseconds);
            client.Tick(clock.NowMicroseconds);
        }

        for (int i = 0; i < 600 && server.Players.FirstOrDefault()?.Slot.Controller?.Pawn is null; i++) Step();
        HubPlayer player = Assert.Single(server.Players);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(player.Slot.Controller?.Pawn);
        HubPads pads = Assert.IsType<HubPads>(server.Pads);
        HubPad pad = Assert.IsType<HubPad>(pads.Find("Pad1"));
        pawn.Location = pad.Area.Laptop + new FVector(0, -100, 86);

        var setup = LobbySetup.ForMap(MapCatalog.TestBox, mode, Maps.GetMapSettings(MapCatalog.TestBox), 24);
        Assert.Null(pads.ApplySetup(player, "Pad1", setup));
        Assert.Equal((spySlots, mercSlots, 24), (pad.Config.SpySlots, pad.Config.MercSlots, pad.Config.Slots));
        Assert.Equal(24, pads.SetupOf(pad).MaxPlayers);
        Assert.Equal(mode == EGameMode.GM_MercDeathMatch ? "Merc Deathmatch 24 players" : "Story 12v12", pad.Config.Format);

        for (int i = 0; i < 30; i++) Step();
        HubPadInfo visible = client.Pads["Pad1"];
        Assert.Equal((spySlots, mercSlots), (visible.SpySlots, visible.MercSlots));
        Assert.Equal(24, client.PadSetups["Pad1"].Setup.MaxPlayers);
    }

    private sealed class UnusedLauncher : IHubMatchLauncher
    {
        public IHubMatch Start(HubMatchRequest request) => throw new InvalidOperationException("No match should start in this test.");
    }
}
