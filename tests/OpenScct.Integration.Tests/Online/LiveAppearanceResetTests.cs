using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Master;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class LiveAppearanceResetTests
{
    [Fact]
    public void HubPawnReceivesCosmeticsAndTitleResetWhileConnected()
    {
        using var rig = new OpenScct.Integration.Tests.Hub.HubTests.Rig(0);
        using var client = new OpenScct.Server.Hub.HubClient(rig.Network.Bind("visitor"), "server", rig.Now,
            new OpenScct.Server.Hub.HubClientSettings { Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, PlayerName = "Visitor", Team = 1 });
        rig.Run(8, () => client.Tick(rig.Now));
        var player = Assert.Single(rig.Server.Players);
        typeof(OpenScct.Server.Hub.HubPlayer).GetProperty("AccountId")!.SetValue(player, (long?)42);
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("hub-reset").Id;
        store.SetRank(id, 1, 55);
        Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
        {
            ExpectedRevision = store.Appearance(id).Revision, Merc = ["merc.unsuppressed.cryo"], Title = "prestige_1",
        }).Success);
        void Apply() => typeof(OpenScct.Server.Hub.HubServer).GetMethod("ApplyAccountAppearances", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(rig.Server, [new Dictionary<long, LiveAccountAppearance> { [42] = store.LiveAppearance(id) }]);
        Apply();
        rig.Run(1, () => client.Tick(rig.Now));
        Assert.NotEqual(0, client.Session.LocalPlayer!.PlayerReplicationInfo!.SvmLook | client.Session.LocalPlayer.PlayerReplicationInfo.SvmLookExtra);
        store.SetRank(id, 1, 1);
        Apply();
        rig.Run(1, () => client.Tick(rig.Now));
        var pri = client.Session.LocalPlayer!.PlayerReplicationInfo!;
        Assert.Equal(0, pri.SvmLook);
        Assert.Equal(0, pri.SvmLookExtra);
        Assert.Equal(store.LiveAppearance(id).Rank, pri.SvmRank);
        Assert.NotNull(client.Session.LocalPlayer.Pawn);
        Assert.Same(player, Assert.Single(rig.Server.Players));
    }

    [Fact]
    public void ConnectedPlayerReceivesResetWithoutRejoiningAndStaleSnapshotsCannotRestoreIt()
    {
        using var store = AccountStore.Open(":memory:");
        long id = store.GetOrCreate("live-reset").Id;
        store.SetRank(id, 1, 55);
        Assert.True(store.UpdateAppearance(id, new AppearanceUpdateRequest
        {
            ExpectedRevision = store.Appearance(id).Revision, Title = "prestige_1", Merc = ["merc.unsuppressed.cryo"],
        }).Success);
        var original = store.LiveAppearance(id);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 7, SimLinkProfile.A);
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "host-key", AutoLaunch = false, MinPlayersToLaunch = 1,
            HostAccount = new HostAccount(id, "LiveReset"),
        };
        using var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        using var rig = new OnlineRig(server, clock);
        var target = JoinTarget.Custom("Test", () => network.Bind("client"), "server");
        var client = rig.Add(new OnlineClient(target, OnlineRig.ClientSettings("LiveReset", 1) with { HostKey = "host-key" }, rig.Now));
        Assert.True(rig.RunUntil(() => client.State == OnlineClientState.Lobby, 10));
        var player = Assert.Single(server.Players);
        Assert.Equal(id, player.AccountId);
        void Apply(LiveAccountAppearance snapshot) => typeof(OnlineServer).GetMethod("ApplyAccountAppearances", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(server, [new Dictionary<long, LiveAccountAppearance> { [id] = snapshot }]);
        Apply(original);
        Assert.False(player.MercLook.IsDefault);
        Assert.True(rig.RunUntil(() => client.Lobby.Local?.Map == MapState.Ok, 10));
        client.Launch();
        Assert.True(rig.RunUntil(() => client.State == OnlineClientState.Match && player.Slot?.Controller?.PlayerReplicationInfo is not null && client.Session?.LocalPlayer?.PlayerReplicationInfo is not null, 20));
        // Apply the same snapshot once the live pawn exists as the join path would.
        var pri = player.Slot!.Controller!.PlayerReplicationInfo!;
        Assert.NotNull(pri);
        Assert.NotEqual(0, pri.SvmLook | pri.SvmLookExtra);
        store.SetRank(id, 1, 1);
        var reset = store.LiveAppearance(id);
        Apply(reset);
        Assert.True(rig.RunUntil(() => client.Session?.LocalPlayer?.PlayerReplicationInfo?.SvmRank == reset.Rank, 5));
        Assert.Equal(0, pri.SvmLook);
        Assert.Equal(0, pri.SvmLookExtra);
        Assert.Equal(0, client.Session!.LocalPlayer!.PlayerReplicationInfo!.SvmLook);
        Assert.Equal(0, client.Session.LocalPlayer.PlayerReplicationInfo.SvmLookExtra);
        Assert.Equal(original.Appearance.Revision + 1, reset.Appearance.Revision);
        Assert.True(player.MercLook.IsDefault);
        Assert.Equal(reset.Rank, player.Rank);
        Assert.Equal(1, OpenScct.Net.Cosmetics.PlayerRank.Unpack(player.Rank).Level);
        Assert.Equal(-1, OpenScct.Net.Cosmetics.PlayerRank.Unpack(player.Rank).TitleBit);
        Apply(original);
        Assert.True(player.MercLook.IsDefault);
        Assert.Equal(reset.Rank, player.Rank);
        Assert.Same(player, Assert.Single(server.Players));
        Assert.Equal(OnlineClientState.Match, client.State);
    }
}
