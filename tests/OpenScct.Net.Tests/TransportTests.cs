using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class TransportTests
{
    private static byte[] Payload(int n) => [(byte)n, (byte)(n >> 8), (byte)(n >> 16), 0x5A];

    private static int Number(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);

    [Fact]
    public void LoopbackDeliversInOrderAndNumbersPeers()
    {
        var network = new LoopbackNetwork();
        using LoopbackTransport server = network.Bind("server");
        using LoopbackTransport a = network.Bind("a");
        using LoopbackTransport b = network.Bind("b");
        EndpointId toServerA = a.Connect("server");
        EndpointId toServerB = b.Connect("server");
        Assert.True(toServerA.IsValid && toServerB.IsValid);

        for (int i = 0; i < 100; i++)
        {
            Assert.True(a.Send(toServerA, Payload(i)));
            Assert.True(b.Send(toServerB, Payload(1000 + i)));
        }

        var buffer = new byte[network.MaxDatagramSize];
        var fromA = new List<int>();
        var fromB = new List<int>();
        var ids = new HashSet<EndpointId>();
        while (server.TryReceive(buffer, out EndpointId from, out int length))
        {
            Assert.Equal(4, length);
            ids.Add(from);
            (Number(buffer) >= 1000 ? fromB : fromA).Add(Number(buffer));
        }

        Assert.Equal(2, ids.Count);
        Assert.Equal(100, fromA.Count);
        Assert.Equal(100, fromB.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i, fromA[i]);
            Assert.Equal(1000 + i, fromB[i]);
        }

        // Replies reach the right client.
        foreach (EndpointId id in ids)
        {
            Assert.True(server.Send(id, Payload(7)));
        }

        Assert.True(a.TryReceive(buffer, out EndpointId replyFrom, out _));
        Assert.Equal(toServerA, replyFrom);
        Assert.True(b.TryReceive(buffer, out _, out _));
        Assert.False(a.TryReceive(buffer, out _, out _));
    }

    [Fact]
    public void LoopbackDropsWhenTheQueueIsFull()
    {
        var network = new LoopbackNetwork(queueCapacity: 4);
        using LoopbackTransport server = network.Bind("server");
        using LoopbackTransport client = network.Bind("client");
        EndpointId id = client.Connect("server");
        int sent = 0;
        for (int i = 0; i < 10; i++)
        {
            sent += client.Send(id, Payload(i)) ? 1 : 0;
        }

        Assert.Equal(4, sent);
        Assert.Equal(6, client.DroppedFull);
    }

    [Fact]
    public void LoopbackCrossesThreadsWithoutLossOrReordering()
    {
        var network = new LoopbackNetwork(queueCapacity: 1024);
        using LoopbackTransport server = network.Bind("server");
        using LoopbackTransport client = network.Bind("client");
        EndpointId id = client.Connect("server");
        const int count = 200_000;
        var producer = new Thread(() =>
        {
            for (int i = 0; i < count; i++)
            {
                while (!client.Send(id, Payload(i)))
                {
                    Thread.Yield();
                }
            }
        });

        producer.Start();
        var buffer = new byte[network.MaxDatagramSize];
        int expected = 0;
        var watch = Stopwatch.StartNew();
        while (expected < count && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (server.TryReceive(buffer, out _, out _))
            {
                Assert.Equal(expected & 0xFFFFFF, Number(buffer));
                expected++;
            }
        }

        producer.Join();
        Assert.Equal(count, expected);
    }

    [Fact]
    public void SimLinkProfileAIsInstantAndLossless()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1, SimLinkProfile.A);
        using SimLinkTransport server = network.Bind("server");
        using SimLinkTransport client = network.Bind("client");
        EndpointId id = client.Connect("server");
        for (int i = 0; i < 500; i++)
        {
            client.Send(id, Payload(i));
        }

        var buffer = new byte[network.MaxDatagramSize];
        for (int i = 0; i < 500; i++)
        {
            Assert.True(server.TryReceive(buffer, out _, out _));
            Assert.Equal(i, Number(buffer));
        }

        Assert.False(server.TryReceive(buffer, out _, out _));
    }

    [Fact]
    public void SimLinkDelaysByHalfThePing()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1, new SimLinkProfile("t", 100, 0, 0f));
        using SimLinkTransport server = network.Bind("server");
        using SimLinkTransport client = network.Bind("client");
        client.Send(client.Connect("server"), Payload(1));
        var buffer = new byte[network.MaxDatagramSize];
        clock.AdvanceTo(49_999);
        Assert.False(server.TryReceive(buffer, out _, out _));
        clock.AdvanceTo(50_000);
        Assert.True(server.TryReceive(buffer, out _, out _));
    }

    [Theory]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("D")]
    public void SimLinkLossMatchesTheProfile(string name)
    {
        SimLinkProfile profile = SimLinkProfile.Get(name);
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 42, profile);
        using SimLinkTransport server = network.Bind("server");
        using SimLinkTransport client = network.Bind("client");
        EndpointId id = client.Connect("server");
        const int count = 20_000;
        var buffer = new byte[network.MaxDatagramSize];
        int received = 0;
        int outOfOrder = 0;
        int last = -1;
        for (int i = 0; i < count; i++)
        {
            client.Send(id, Payload(i));
            clock.Advance(16_667);
            while (server.TryReceive(buffer, out _, out _))
            {
                received++;
                int n = Number(buffer);
                if (n < last)
                {
                    outOfOrder++;
                }

                last = Math.Max(last, n);
            }
        }

        clock.Advance(10_000_000);
        while (server.TryReceive(buffer, out _, out _))
        {
            received++;
        }

        double lossPercent = 100.0 * network.Lost / count;
        Assert.InRange(lossPercent, profile.LossPercent * 0.7, profile.LossPercent * 1.3);
        Assert.Equal(count - network.Lost + network.Duplicated, received);
        Assert.True(network.Duplicated > 0 && network.Reordered > 0);
        Assert.True(outOfOrder > 0);
    }

    [Fact]
    public void SimLinkIsDeterministic()
    {
        static List<(long, int)> Run()
        {
            var clock = new VirtualClock();
            var network = new SimLinkNetwork(clock, 99, SimLinkProfile.C);
            using SimLinkTransport server = network.Bind("server");
            using SimLinkTransport client = network.Bind("client");
            EndpointId id = client.Connect("server");
            var buffer = new byte[network.MaxDatagramSize];
            var log = new List<(long, int)>();
            for (int i = 0; i < 3000; i++)
            {
                client.Send(id, Payload(i));
                clock.Advance(5_000);
                while (server.TryReceive(buffer, out _, out _))
                {
                    log.Add((clock.NowMicroseconds, Number(buffer)));
                }
            }

            return log;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void CompositeRoutesToTheRightTransport()
    {
        var loop = new LoopbackNetwork();
        LoopbackTransport hostA = loop.Bind("a");
        var clock = new VirtualClock();
        var sim = new SimLinkNetwork(clock, 1, SimLinkProfile.A);
        SimLinkTransport hostB = sim.Bind("b");
        using var composite = new CompositeTransport(hostA, hostB);
        using LoopbackTransport clientA = loop.Bind("ca");
        using SimLinkTransport clientB = sim.Bind("cb");
        clientA.Send(clientA.Connect("a"), Payload(1));
        clientB.Send(clientB.Connect("b"), Payload(2));

        var buffer = new byte[2048];
        var seen = new Dictionary<int, EndpointId>();
        while (composite.TryReceive(buffer, out EndpointId from, out _))
        {
            seen[Number(buffer)] = from;
        }

        Assert.Equal(2, seen.Count);
        Assert.NotEqual(seen[1], seen[2]);
        Assert.True(composite.Send(seen[1], Payload(10)));
        Assert.True(composite.Send(seen[2], Payload(20)));
        Assert.True(clientA.TryReceive(buffer, out _, out _));
        Assert.Equal(10, Number(buffer));
        Assert.True(clientB.TryReceive(buffer, out _, out _));
        Assert.Equal(20, Number(buffer));
    }

    [Fact]
    public void LiteNetLibExchangesDatagramsOnLocalhost()
    {
        using LiteNetLibTransport server = LiteNetLibTransport.Listen(0, 4, loopbackOnly: true);
        using LiteNetLibTransport client = LiteNetLibTransport.CreateClient(loopbackOnly: true);
        EndpointId toServer = client.Connect("127.0.0.1:" + server.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var buffer = new byte[client.MaxDatagramSize];
        var watch = Stopwatch.StartNew();
        EndpointId toClient = EndpointId.None;
        bool clientGotReply = false;
        int n = 0;
        while (watch.Elapsed < TimeSpan.FromSeconds(5) && !clientGotReply)
        {
            client.Send(toServer, Payload(n++));
            if (server.TryReceive(buffer, out EndpointId from, out int length))
            {
                Assert.Equal(4, length);
                toClient = from;
                server.Send(toClient, Payload(4242));
            }

            while (client.TryReceive(buffer, out EndpointId replyFrom, out _))
            {
                Assert.Equal(toServer, replyFrom);
                Assert.Equal(4242, Number(buffer));
                clientGotReply = true;
            }

            Thread.Sleep(5);
        }

        Assert.True(toClient.IsValid, "The server never received a datagram.");
        Assert.True(clientGotReply, "The client never received the reply.");
    }

    [Fact]
    public void AddressParsing()
    {
        LiteNetLibTransport.ParseAddress("example.org:28888", out string host, out int port);
        Assert.Equal(("example.org", 28888), (host, port));
        LiteNetLibTransport.ParseAddress("[::1]:7777", out host, out port);
        Assert.Equal(("::1", 7777), (host, port));
        Assert.Throws<FormatException>(() => LiteNetLibTransport.ParseAddress("nohost", out _, out _));
        Assert.Throws<FormatException>(() => LiteNetLibTransport.ParseAddress("host:99999", out _, out _));
    }
}
