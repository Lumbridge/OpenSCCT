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
/// The SteeE Story pieces over the network on the synthetic TestBox map (shaped like SteeE: a mission, three
/// objectives, two terminals, a disk and its drop zone, a door): a server and three clients bind the map actors by
/// SVMap index, replicate objective state under the native property lists, and receive a broadcast call once.
/// </summary>
public sealed class SteeEShapedTests
{
    private readonly ITestOutputHelper _output;

    public SteeEShapedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class Radar
    {
        public readonly List<(SObjectiveTrigger Trigger, bool Add)> Calls = new();
    }

    /// <summary>Records the radar broadcasts a client receives, then runs the port as the default handler would.</summary>
    private static Radar Record(ClientSession client)
    {
        var radar = new Radar();
        client.Driver.Functions.Register("SBase.SObjectiveTrigger", nameof(SObjectiveTrigger.AddRemoveObjectiveForRadar), (self, f, args, from) =>
        {
            radar.Calls.Add(((SObjectiveTrigger)self, args.Values[0].AsBool));
            NetFunctionBinder.Bind(self.GetType(), f)!(self, f, args, from);
        });
        return radar;
    }

    private static T ServerActor<T>(WalkerRig rig, string id)
        where T : Actor =>
        (T)(rig.Server.Map.Loaded!.FindActor(id) ?? throw new InvalidOperationException(id + " is not on the map"));

    private static T Copy<T>(WalkerRig rig, ClientSession client, Actor serverActor)
        where T : Actor
    {
        int index = rig.Server.Driver.MapBinding.IndexOf(serverActor);
        Assert.True(index > 0, $"{serverActor} is not a bound map actor");
        return (T)client.Map!.MapActors[index]!;
    }

    private static void Steps(WalkerRig rig, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(WalkPatterns.For);
        }
    }

    [Theory]
    [InlineData("B", 31UL)]
    [InlineData("C", 32UL)]
    public void ObjectiveStateAndABroadcastReachThreeClients(string profile, ulong seed)
    {
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.Get(profile), seed, 3, new ServerSettings { NetMode = ENetMode.NM_ListenServer });
        Assert.SkipWhen(rig.Server.Map.Loaded is null, "The TestBox fixture is not in this checkout.");
        var radars = new Radar[3];
        for (int i = 0; i < 3; i++)
        {
            radars[i] = Record(rig.Clients[i]);
        }

        rig.WaitForAllWalkers(3, input: WalkPatterns.For);
        Steps(rig, 120);
        IReadOnlyList<PlayerSlot> players = rig.Server.Players;
        var outBytes = new long[3];
        var outPackets = new long[3];
        for (int i = 0; i < 3; i++)
        {
            outBytes[i] = players[i].Connection.Stats.OutBytes;
            outPackets[i] = players[i].Connection.Stats.OutPackets;
        }

        long startFrame = rig.Frame;

        var mission = ServerActor<SMission>(rig, "SMission0");
        var terminalB = ServerActor<SComputerObjectiveTrigger>(rig, "SComputerObjectiveTrigger0");
        var terminalC = ServerActor<SComputerObjectiveTrigger>(rig, "SComputerObjectiveTrigger1");
        var objectiveB = ServerActor<SObjective>(rig, "SObjective0");

        // Every client binds the objective actors to its own map copies, roles exchanged at load.
        foreach (ClientSession client in rig.Clients)
        {
            var copy = Copy<SComputerObjectiveTrigger>(rig, client, terminalB);
            ActorChannel? channel = client.Connection!.FindActorChannel(copy);
            Assert.NotNull(channel);
            Assert.True(channel!.IsMapActor);
            Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.Role);
            Assert.Equal(ENetRole.ROLE_DumbProxy, Copy<SMission>(rig, client, mission).Role);
        }

        // The server takes terminal B over: trigger state, objective, mission, and the merc radar broadcast.
        terminalB.bActive = true;
        terminalB.bTriggering = true;
        terminalB.TriggerTime = 12.5f;
        terminalB.CaptureTime = 7;
        terminalB.SkinState = 2;
        terminalC.bActive = false;
        terminalC.bTriggering = true;
        objectiveB.ObjectiveTaken = true;
        objectiveB.iSimulatedObjectiveIsBeingTookUntil = 42.25f;
        mission.ObjectivesLeft = 1;
        mission.TotalObjectivesLeft = 2;
        terminalB.AddRemoveObjectiveForRadar(true);
        Steps(rig, 180);

        ClientSession late = rig.AddClient();
        Radar lateRadar = Record(late);
        Steps(rig, 300);

        foreach (ClientSession client in (ClientSession[])[.. rig.Clients, late])
        {
            var b = Copy<SComputerObjectiveTrigger>(rig, client, terminalB);
            var c = Copy<SComputerObjectiveTrigger>(rig, client, terminalC);
            Assert.True(b.bActive);
            Assert.True(b.bTriggering);
            Assert.Equal(12.5f, b.TriggerTime);
            Assert.Equal(7, b.CaptureTime);
            Assert.Equal(2, b.SkinState);

            // spec/native/net-replists.md rule 17: bTriggering only while bActive.
            Assert.False(c.bActive);
            Assert.False(c.bTriggering);

            var o = Copy<SObjective>(rig, client, objectiveB);
            Assert.True(o.ObjectiveTaken);
            Assert.Equal(42.25f, o.iSimulatedObjectiveIsBeingTookUntil);
            var m = Copy<SMission>(rig, client, mission);
            Assert.Equal(1, m.ObjectivesLeft);
            Assert.Equal(2, m.TotalObjectivesLeft);
        }

        // The broadcast reached each client once; the late joiner never sees it (rule 20).
        for (int i = 0; i < 3; i++)
        {
            Assert.Single(radars[i].Calls);
            Assert.True(radars[i].Calls[0].Add);
            Assert.Same(Copy<SObjectiveTrigger>(rig, rig.Clients[i], terminalB), radars[i].Calls[0].Trigger);
        }

        Assert.Empty(lateRadar.Calls);

        // Bandwidth from the server to each of the first three clients over the whole exchange, headers included.
        double seconds = (rig.Frame - startFrame) / (double)Sim.Hz;
        for (int i = 0; i < 3; i++)
        {
            ClientSession client = rig.Clients[i];
            NetStats s = client.Connection!.Stats;
            NetStats sent = players[i].Connection.Stats;
            double rate = (sent.OutBytes - outBytes[i] + (sent.OutPackets - outPackets[i]) * NetConnection.PacketOverheadBytes) / seconds;
            _output.WriteLine(FormattableString.Invariant(
                $"{profile} client {client.PlayerId}: {client.ActorChannels.Count} channels, rtt {s.RttMicros / 1000.0:F0} ms, lost {s.InPacketsLost}, server to client {rate:F0} B/s"));
            Assert.True(rate < 12 * 1024, FormattableString.Invariant($"{rate:F0} B/s to client {i} is over 12 KB/s."));
        }
    }

    [Fact]
    public void DedicatedServerFrequencyGateHoldsFiveHertzTriggersAsDecoded()
    {
        var decoded = new ServerSettings { Net = new NetDriverSettings { UpdateGate = NetUpdateGate.Decoded } };
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.A, 33UL, 2, decoded);
        Assert.SkipWhen(rig.Server.Map.Loaded is null, "The TestBox fixture is not in this checkout.");
        rig.WaitForAllWalkers(2, input: WalkPatterns.For);
        Steps(rig, 120);
        var terminal = ServerActor<SComputerObjectiveTrigger>(rig, "SComputerObjectiveTrigger0");
        var objective = ServerActor<SObjective>(rig, "SObjective0");
        Assert.Equal(5f, terminal.NetUpdateFrequency);
        long skippedBefore = rig.Server.Driver.Stats.PassesSkippedSaturated;

        // SkinState goes whenever it changes (the terminal's native list); CaptureTime would wait for bActive.
        terminal.SkinState = SComputerObjectiveTrigger.SKIN_Locked;
        objective.ObjectiveTaken = true;
        Steps(rig, 240);

        // At a steady 20 Hz with budget to spare, 5 × 0.05 s never exceeds 1: the trigger's change waits for a pass
        // that comes 0.2 s after the previous one, while the 100 Hz objective goes at once.
        foreach (ClientSession client in rig.Clients)
        {
            Assert.True(Copy<SObjective>(rig, client, objective).ObjectiveTaken);
            if (rig.Server.Driver.Stats.PassesSkippedSaturated == skippedBefore)
            {
                Assert.NotEqual(SComputerObjectiveTrigger.SKIN_Locked, Copy<SComputerObjectiveTrigger>(rig, client, terminal).SkinState);
            }
        }
    }

    [Fact]
    public void DedicatedServerDefaultGateSendsFiveHertzTriggersAtTheirOwnRate()
    {
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.A, 34UL, 2);
        Assert.SkipWhen(rig.Server.Map.Loaded is null, "The TestBox fixture is not in this checkout.");
        Assert.Equal(NetUpdateGate.DecodedGuarded, rig.Server.Driver.Settings.UpdateGate);
        rig.WaitForAllWalkers(2, input: WalkPatterns.For);
        Steps(rig, 120);
        var terminal = ServerActor<SComputerObjectiveTrigger>(rig, "SComputerObjectiveTrigger0");
        terminal.SkinState = SComputerObjectiveTrigger.SKIN_Locked;

        // The starvation guard lets the 5 Hz trigger go once 0.2 s passed since its last update on each connection:
        // the change is on every client within that, plus the trip.
        int frames = 0;
        while (frames < 60)
        {
            Steps(rig, 1);
            frames++;
            bool everywhere = true;
            foreach (ClientSession client in rig.Clients)
            {
                everywhere &= Copy<SComputerObjectiveTrigger>(rig, client, terminal).SkinState == SComputerObjectiveTrigger.SKIN_Locked;
            }

            if (everywhere)
            {
                break;
            }
        }

        Assert.True(frames <= 0.2f * Sim.Hz + 4, $"The trigger's change took {frames} frames to reach every client.");
    }

    [Fact]
    public void ServerLoadsTheFixtureWithItsHashAndStarts()
    {
        using WalkerRig rig = WalkerRig.Create(null, 0, 1);
        MapInfo map = rig.Server.Map;
        Assert.SkipWhen(map.Loaded is null, "The TestBox fixture is not in this checkout.");
        Assert.Equal(MapCatalog.ContentHashOf(map.SimHash), map.ContentHash);
        Assert.Equal(4, map.Starts.Count);
        Assert.NotNull(map.Physics);
        Assert.Equal(3, map.Files.Count);
        Assert.Null(map.FirstDifferingFile(map.FileListing));
        Assert.Equal("TestBox/sim/gameplay.json", map.FirstDifferingFile(map.FileListing.Replace("gameplay.json=", "gameplay.json=0", StringComparison.Ordinal)));
        Assert.True(ulong.TryParse(map.SimHash.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong head));
        Assert.Equal(head, map.ContentHash);
    }
}
