using System;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>After warm-up a server tick (receive, moves, world tick, replication, send) allocates nothing.</summary>
public sealed class ServerAllocationTests
{
    private readonly ITestOutputHelper _output;

    public ServerAllocationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("C")]
    public void ServerTickDoesNotAllocateAfterWarmUp(string? profileName)
    {
        SimLinkProfile? profile = profileName is null ? null : SimLinkProfile.Get(profileName);
        using WalkerRig rig = WalkerRig.Create(profile, 9UL, 4);
        rig.WaitForAllWalkers(4, input: WalkPatterns.For);

        // Warm up: every buffer and pool reaches its working size, every path is compiled.
        for (int i = 0; i < 600; i++)
        {
            rig.Step(WalkPatterns.For);
        }

        long allocated = 0;
        const int ticks = 600;
        for (int i = 0; i < ticks; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.TickServerOnly();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            rig.TickClients(WalkPatterns.For);
        }

        _output.WriteLine($"{profileName ?? "loopback"}: {allocated} bytes allocated over {ticks} server ticks");
        Assert.Equal(0, allocated);
    }
}
