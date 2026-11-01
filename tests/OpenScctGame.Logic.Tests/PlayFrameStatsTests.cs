using System;
using OpenScctGame.Play;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class PlayFrameStatsTests
{
    [Fact]
    public void NormalPlayRetainsNoSamplesOrPerFrameAllocationsDuringALongSession()
    {
        var stats = new PlayFrameStats();
        for (int i = 0; i < 1000; i++) stats.Add(1, 2, 4);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2_000_000; i++) stats.Add(1, 2, 4);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(2_001_000, stats.Frames);
        Assert.Equal(0, stats.HistoryFrames);
        Assert.Equal(1, stats.LastSimMs);
        Assert.Equal(2, stats.LastViewMs);
        Assert.Contains("sim ms/frame mean=1.00 p95=not-captured max=1.00", stats.Summary());
        Assert.Contains("frame ms mean=4.00", stats.Summary());
    }

    [Fact]
    public void ExplicitDiagnosticsPreserveOriginalFullHistoryPercentileAndAggregates()
    {
        var stats = new PlayFrameStats(captureHistory: true) { Ticks = 60, PilotMs = 7 };
        for (int i = 100; i >= 1; i--) stats.Add(i, i * 2, i * 3);
        Assert.Equal(100, stats.HistoryFrames);
        Assert.Equal("frames=100 ticks=60 pilot ms=7 sim ms/frame mean=50.50 p95=96.00 max=100.00 view ms/frame mean=101.00 p95=192.00 max=200.00 frame ms mean=151.50", stats.Summary());
        // Sorting for a summary must not affect later sampling.
        stats.Add(0, 0, 0);
        Assert.Equal(101, stats.Frames);
        Assert.Equal(101, stats.HistoryFrames);
        Assert.Contains("sim ms/frame mean=50.00 p95=95.00 max=100.00", stats.Summary());
    }

    [Fact]
    public void EmptyDiagnosticsHaveFiniteZeroStatistics()
    {
        var stats = new PlayFrameStats(captureHistory: true);
        Assert.Equal("frames=0 ticks=0 pilot ms=0 sim ms/frame mean=0.00 p95=0.00 max=0.00 view ms/frame mean=0.00 p95=0.00 max=0.00 frame ms mean=0.00", stats.Summary());
    }
}
