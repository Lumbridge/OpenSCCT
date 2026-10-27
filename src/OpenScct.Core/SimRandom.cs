using System;

namespace OpenScct.Core;

/// <summary>Complete state of a <see cref="SimRandom"/>, for snapshots and replays.</summary>
public readonly record struct SimRandomState(uint S0, uint S1, uint S2, uint S3);

/// <summary>
/// Seeded simulation random stream (xoshiro128**). Each stream is derived from the match seed plus a stream number
/// (for example a player or actor index) and a purpose (weapon spread, bots, effects...), so independent systems never
/// share or disturb each other's sequence. Results are identical on every platform.
/// </summary>
public sealed class SimRandom
{
    private uint _s0;
    private uint _s1;
    private uint _s2;
    private uint _s3;

    /// <summary>Creates the stream for (<paramref name="matchSeed"/>, <paramref name="stream"/>, <paramref name="purpose"/>).</summary>
    public SimRandom(ulong matchSeed, int stream, int purpose)
    {
        ulong z = SplitMix64(matchSeed);
        z = SplitMix64(z ^ (uint)stream);
        z = SplitMix64(z ^ ((ulong)(uint)purpose << 32));

        ulong a = SplitMix64(z);
        ulong b = SplitMix64(a);
        _s0 = (uint)a;
        _s1 = (uint)(a >> 32);
        _s2 = (uint)b;
        _s3 = (uint)(b >> 32);
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 0x9E3779B9u;
        }
    }

    private SimRandom(SimRandomState state)
    {
        SetState(state);
    }

    /// <summary>Recreates a stream from saved state.</summary>
    public static SimRandom FromState(SimRandomState state) => new(state);

    public SimRandomState GetState() => new(_s0, _s1, _s2, _s3);

    public void SetState(SimRandomState state)
    {
        if ((state.S0 | state.S1 | state.S2 | state.S3) == 0)
        {
            throw new ArgumentException("xoshiro state must not be all zero.", nameof(state));
        }

        _s0 = state.S0;
        _s1 = state.S1;
        _s2 = state.S2;
        _s3 = state.S3;
    }

    /// <summary>Next raw 32-bit value.</summary>
    public uint NextUInt()
    {
        uint result = RotateLeft(_s1 * 5u, 7) * 9u;
        uint t = _s1 << 9;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 11);
        return result;
    }

    /// <summary>Uniform float in [0, 1) with 24 bits of resolution (UnrealScript FRand).</summary>
    public float FRand() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>UnrealScript Rand: 0 when <paramref name="Max"/> is not positive, else a value in [0, Max).</summary>
    public int Rand(int Max) => Max > 0 ? (int)(NextUInt() % (uint)Max) : 0;

    /// <summary>UnrealScript RandRange: Min + (Max - Min) * FRand().</summary>
    public float RandRange(float Min, float Max) => Min + (Max - Min) * FRand();

    /// <summary>Uniformly distributed unit vector (UnrealScript VRand), by rejection sampling in the unit cube.</summary>
    public FVector VRand()
    {
        FVector result;
        float sizeSquared;
        do
        {
            result.X = FRand() * 2f - 1f;
            result.Y = FRand() * 2f - 1f;
            result.Z = FRand() * 2f - 1f;
            sizeSquared = result.SizeSquared();
        }
        while (sizeSquared > 1f || sizeSquared < SvmMath.SMALL_NUMBER);

        return result.UnsafeNormal();
    }

    private static uint RotateLeft(uint x, int k) => (x << k) | (x >> (32 - k));

    private static ulong SplitMix64(ulong x)
    {
        unchecked
        {
            ulong z = x + 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }
}
