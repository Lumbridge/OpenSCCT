using System;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Integration.Tests.Online;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Maps;

/// <summary>
/// The maps a session moves to without the host picking one: <c>!next</c> and <c>!prev</c> (Reloaded's chat commands,
/// typed by a player in the match and sent over the network) step through the host list in its order, and Random Map
/// draws from it; neither ever lands on a tutorial, a front end map or one of our own test levels (docs/MAPS.md
/// "Order").
/// </summary>
public sealed class MapRotationTests
{
    private static (OnlineRig Rig, SimLinkNetwork Network) Create(string map)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 7, SimLinkProfile.A);
        OnlineServerSettings settings = OnlineRig.Settings(map) with { Dedicated = true };
        var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        return (new OnlineRig(server, clock), network);
    }

    private static OnlineClient Join(OnlineRig rig, SimLinkNetwork network, string name, int team)
    {
        int n = 0;
        var target = JoinTarget.Custom("Rotation server", () => network.Bind(name + "-" + (n++).ToString(CultureInfo.InvariantCulture)), "server");
        return rig.Add(new OnlineClient(target, OnlineRig.ClientSettings(name, team), rig.Now));
    }

    private static string Describe(OnlineRig rig) =>
        rig.Server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c => $"{c.Settings.PlayerName}: {c.State} {c.CurrentMap} {c.Error}"));

    [Theory]
    [InlineData("next")]
    [InlineData("prev")]
    public void Next_and_prev_typed_in_a_match_step_through_the_host_list(string verb)
    {
        MapCatalog maps = WalkerRig.Maps;
        Assert.SkipWhen(maps.FindFolder("TermD") is null || maps.CatalogData.OriginalOrder.Count == 0,
            "TermD or the map catalog has not been converted into .content.");
        var host = maps.GetHostMaps().Select(e => e.Id).ToList();
        int at = host.IndexOf("TermD");
        string expected = host[(at + (verb == "next" ? 1 : -1) + host.Count) % host.Count];

        (OnlineRig rig, SimLinkNetwork network) = Create("TermD");
        using (rig)
        {
            OnlineClient spy = Join(rig, network, "Spy", 0);
            OnlineClient merc = Join(rig, network, "Merc", 1);
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Lobby && merc.State == OnlineClientState.Lobby &&
                rig.Server.Players.Count == 2, 15), Describe(rig));
            spy.SetReady(true);
            merc.SetReady(true);
            spy.Launch();
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Match && spy.Session?.LocalPlayer?.PlayerReplicationInfo is not null, 60),
                Describe(rig));

            spy.Chat("!" + verb);
            Assert.True(rig.RunUntil(() => !string.Equals(rig.Server.Setup.Map, "TermD", StringComparison.OrdinalIgnoreCase), 10), Describe(rig));
            Assert.Equal(expected, rig.Server.Setup.Map);
            Assert.Equal(MapKind.Versus, maps.GetCatalogEntry(rig.Server.Setup.Map)!.Kind);
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Lobby && spy.Lobby.Setup.Map == expected, 15), Describe(rig));
        }
    }

    [Fact]
    public void The_rotation_is_the_host_list()
    {
        MapCatalog maps = WalkerRig.Maps;
        Assert.SkipWhen(maps.CatalogData.OriginalOrder.Count == 0, "The map catalog has not been converted into .content.");
        Assert.Equal(maps.GetHostMaps().Select(e => e.Id), maps.RotationMaps());
        Assert.DoesNotContain(maps.RotationMaps(), id => id.StartsWith("TSP", StringComparison.Ordinal) || id == "TestRange" || id == "Entry");
    }
}
