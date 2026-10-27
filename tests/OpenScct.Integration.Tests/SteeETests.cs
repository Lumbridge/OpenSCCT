using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// The real SteeE, converted locally into <c>.content/svmap</c> (skipped when it is not there): a dedicated server and
/// three clients over SimLink load the map, bind its actors by SVMap index, replicate objective state and a radar
/// broadcast, and stay inside the bandwidth target. Actors are picked by class at run time, so nothing from the map
/// is written here.
/// </summary>
public sealed class SteeETests
{
    private const string MapName = "SteeE";
    private const double BandwidthLimit = 12 * 1024;

    private readonly ITestOutputHelper _output;

    public SteeETests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static List<T> ServerActors<T>(WalkerRig rig)
        where T : Actor
    {
        var found = new List<T>();
        foreach (Actor? actor in rig.Server.Map.MapActors)
        {
            if (actor is T match && !match.bDeleteMe)
            {
                found.Add(match);
            }
        }

        return found;
    }

    private static T Copy<T>(WalkerRig rig, ClientSession client, Actor serverActor)
        where T : Actor
    {
        int index = rig.Server.Driver.MapBinding.IndexOf(serverActor);
        Assert.True(index > 0, $"{serverActor} is not a bound map actor");
        return (T)client.Map!.MapActors[index]!;
    }

    /// <summary>Every client has a channel for each of <paramref name="actors"/>.</summary>
    private static bool AllOpen(WalkerRig rig, List<Actor> actors)
    {
        foreach (Actor actor in actors)
        {
            foreach (ClientSession client in rig.Clients)
            {
                if (client.Connection!.FindActorChannel(Copy<Actor>(rig, client, actor)) is null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// A listen server (no tick cap, so the 5 Hz objective triggers pass the frequency gate every pass; on a
    /// dedicated server at a steady 20 Hz they only go when passes stall, spec/native/net-replication.md).
    /// </summary>
    [Theory]
    [InlineData("B", 41UL)]
    [InlineData("C", 42UL)]
    public void ObjectivesAndARadarBroadcastReplicateOnSteeE(string profile, ulong seed)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapName) is null, "SteeE has not been converted into .content/svmap.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Get(profile), seed, 3,
            new ServerSettings { Map = MapName, NetMode = ENetMode.NM_ListenServer });
        Assert.NotNull(rig.Server.Map.Loaded);
        Assert.NotEmpty(rig.Server.Map.PlayerStarts);
        var radar = new int[3];
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            rig.Clients[i].Driver.Functions.Register("SBase.SObjectiveTrigger", nameof(SObjectiveTrigger.AddRemoveObjectiveForRadar),
                (self, f, args, from) =>
                {
                    radar[slot]++;
                    NetFunctionBinder.Bind(self.GetType(), f)!(self, f, args, from);
                });
        }

        // The objective actors are always relevant: every client gets all of them once the first burst is through.
        List<SComputerObjectiveTrigger> terminals = ServerActors<SComputerObjectiveTrigger>(rig);
        List<SObjective> objectives = ServerActors<SObjective>(rig);
        List<SMission> missions = ServerActors<SMission>(rig);
        Assert.NotEmpty(terminals);
        Assert.NotEmpty(objectives);
        Assert.NotEmpty(missions);
        var watched = new List<Actor>();
        watched.AddRange(terminals);
        watched.AddRange(objectives);
        rig.WaitForAllWalkers(3, input: WalkPatterns.For);
        foreach (ClientSession client in rig.Clients)
        {
            Assert.Equal(rig.Server.Map.MapActors.Count, client.Map!.MapActors.Count);
        }

        int waited = 0;
        while (!AllOpen(rig, watched) && waited < 1800)
        {
            rig.Step(WalkPatterns.For);
            waited++;
        }

        Assert.True(AllOpen(rig, watched), "The objective actors did not reach every client in 30 s.");
        IReadOnlyList<PlayerSlot> players = rig.Server.Players;
        var outBytes = new long[3];
        var outPackets = new long[3];
        for (int i = 0; i < 3; i++)
        {
            outBytes[i] = players[i].Connection.Stats.OutBytes;
            outPackets[i] = players[i].Connection.Stats.OutPackets;
        }

        long startFrame = rig.Frame;
        SComputerObjectiveTrigger terminal = terminals[0];
        SObjective objective = objectives[0];
        SMission mission = missions[0];
        foreach (ClientSession client in rig.Clients)
        {
            var copy = Copy<SComputerObjectiveTrigger>(rig, client, terminal);
            Assert.Equal(ENetRole.ROLE_Authority, copy.RemoteRole);
            Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.Role);
            Assert.True(client.Connection!.FindActorChannel(copy)!.IsMapActor);
        }

        terminal.bActive = true;
        terminal.bTriggering = true;
        terminal.CaptureTime = 11;
        objective.ObjectiveTaken = true;
        mission.ObjectivesLeft = 1;
        terminal.AddRemoveObjectiveForRadar(true);
        for (int n = 0; n < 300; n++)
        {
            rig.Step(WalkPatterns.For);
        }

        double seconds = (rig.Frame - startFrame) / (double)Sim.Hz;
        for (int i = 0; i < 3; i++)
        {
            ClientSession client = rig.Clients[i];
            var t = Copy<SComputerObjectiveTrigger>(rig, client, terminal);
            Assert.True(t.bActive);
            Assert.True(t.bTriggering);
            Assert.Equal(11, t.CaptureTime);
            Assert.True(Copy<SObjective>(rig, client, objective).ObjectiveTaken);
            Assert.Equal(1, Copy<SMission>(rig, client, mission).ObjectivesLeft);
            Assert.Equal(1, radar[i]);

            NetConnection connection = players[i].Connection;
            long bytes = connection.Stats.OutBytes - outBytes[i];
            long packets = connection.Stats.OutPackets - outPackets[i];
            double rate = (bytes + packets * NetConnection.PacketOverheadBytes) / seconds;
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{profile} client {client.PlayerId}: {client.ActorChannels.Count} channels after {waited} frames of first burst, server to client {rate:F0} B/s"));
            Assert.True(rate < BandwidthLimit, $"{rate} B/s to client {i} is over 12 KB/s.");
        }
    }
}
