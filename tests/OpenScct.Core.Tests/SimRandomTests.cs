using System;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class SimRandomTests
{
    [Fact]
    public void Xoshiro128StarStarReferenceOutputs()
    {
        // Reference outputs of xoshiro128** for the state {1, 2, 3, 4}.
        SimRandom r = SimRandom.FromState(new SimRandomState(1, 2, 3, 4));
        Assert.Equal(11520u, r.NextUInt());
        Assert.Equal(0u, r.NextUInt());
        Assert.Equal(5927040u, r.NextUInt());
        Assert.Equal(70819200u, r.NextUInt());
    }

    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new SimRandom(0xDEADBEEFCAFEul, 3, 7);
        var b = new SimRandom(0xDEADBEEFCAFEul, 3, 7);
        for (int i = 0; i < 10000; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void StreamsAndPurposesAreIndependent()
    {
        ulong seed = 42;
        uint[] Take(SimRandom r)
        {
            var values = new uint[16];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = r.NextUInt();
            }

            return values;
        }

        uint[] baseSeq = Take(new SimRandom(seed, 0, 0));
        Assert.NotEqual(baseSeq, Take(new SimRandom(seed, 1, 0)));
        Assert.NotEqual(baseSeq, Take(new SimRandom(seed, 0, 1)));
        Assert.NotEqual(baseSeq, Take(new SimRandom(seed + 1, 0, 0)));
        Assert.NotEqual(Take(new SimRandom(seed, 1, 2)), Take(new SimRandom(seed, 2, 1)));
    }

    [Fact]
    public void SeededSequenceIsStableAcrossRuns()
    {
        // Pinned values: if these change, every recorded replay breaks.
        var r = new SimRandom(12345ul, 0, 0);
        uint first = r.NextUInt();
        uint second = r.NextUInt();
        var again = new SimRandom(12345ul, 0, 0);
        Assert.Equal(first, again.NextUInt());
        Assert.Equal(second, again.NextUInt());
        Assert.Equal(PinnedFirst, first);
    }

    // Recorded from this implementation; guards against accidental changes to seeding.
    private const uint PinnedFirst = 2650970188u;

    [Fact]
    public void FRandIsInUnitInterval()
    {
        var r = new SimRandom(1, 2, 3);
        double sum = 0;
        for (int i = 0; i < 100000; i++)
        {
            float f = r.FRand();
            Assert.InRange(f, 0f, 0.99999994f);
            sum += f;
        }

        Assert.InRange(sum / 100000, 0.49, 0.51);
    }

    [Fact]
    public void RandLikeUe2()
    {
        var r = new SimRandom(9, 9, 9);
        Assert.Equal(0, r.Rand(0));
        Assert.Equal(0, r.Rand(-5));
        var seen = new bool[10];
        for (int i = 0; i < 10000; i++)
        {
            int v = r.Rand(10);
            Assert.InRange(v, 0, 9);
            seen[v] = true;
        }

        Assert.All(seen, Assert.True);
        for (int i = 0; i < 1000; i++)
        {
            Assert.InRange(r.RandRange(-3f, 5f), -3f, 5f);
        }
    }

    [Fact]
    public void VRandIsUnitLength()
    {
        var r = new SimRandom(5, 0, 0);
        for (int i = 0; i < 10000; i++)
        {
            FVector v = r.VRand();
            Assert.True(MathF.Abs(v.Size() - 1f) < 1e-5f);
        }
    }

    [Fact]
    public void StateRoundTrips()
    {
        var r = new SimRandom(77, 1, 1);
        for (int i = 0; i < 37; i++)
        {
            r.NextUInt();
        }

        SimRandomState saved = r.GetState();
        uint[] expected = [r.NextUInt(), r.NextUInt(), r.NextUInt()];
        r.SetState(saved);
        Assert.Equal(expected, new[] { r.NextUInt(), r.NextUInt(), r.NextUInt() });
        SimRandom copy = SimRandom.FromState(saved);
        Assert.Equal(expected[0], copy.NextUInt());
        Assert.Throws<ArgumentException>(() => r.SetState(new SimRandomState(0, 0, 0, 0)));
    }

    [Fact]
    public void WorldStreamsDeriveFromMatchSeed()
    {
        var w1 = new World(1000);
        var w2 = new World(1000);
        Assert.Equal(w1.Random.NextUInt(), w2.Random.NextUInt());
        Assert.Equal(w1.CreateRandom(4, 2).NextUInt(), w2.CreateRandom(4, 2).NextUInt());
        Assert.Equal(new SimRandom(1000, 0, 0).NextUInt(), new World(1000).Random.NextUInt());
    }
}
