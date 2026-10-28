using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// Fire prediction (Reloaded's rl:net.fireprediction, spec/net/lag-compensation.md): a client with this level service
/// draws the world impacts of its own bullets at once, from its own trace, instead of waiting for the server's. The
/// shot tells the server it was predicted, and the server then leaves that player out when it sends the shot's
/// impacts, so nothing is drawn twice. Damage and every pawn reaction stay with the server.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Reloaded predicts impacts on the shooter's client (v4.0 strings and hooked names); this is our own design of it")]
public sealed class FirePrediction
{
    /// <summary>True on a client that predicts its own shots.</summary>
    public static bool IsOn(Actor A) => A.XLevel?.GetService<FirePrediction>() is not null;
}

/// <summary>
/// The spread numbers of a shot both ends can compute: the shooter's client for its predicted impacts and the server
/// for the real trace. As in Reloaded's FRand hook (spec/native/reloaded-hooks.md rule 1), the first two random numbers
/// of a merc gun or spy pistol trace come from a hash of the shot's number, its aim and the side instead of the random
/// stream; our hash is our own (only the idea and the 24-bit mantissa are Reloaded's).
/// </summary>
public static class ShotSpread
{
    /// <summary>Side tags mixed into the hash (Reloaded tags merc and spy shots 'M' and 'S').</summary>
    public const uint Merc = 'M';

    public const uint Spy = 'S';

    /// <summary>
    /// A number in [0, 1) for random call <paramref name="Call"/> of shot <paramref name="ShotId"/> aimed at
    /// <paramref name="Aim"/> (the aim yaw) by side <paramref name="Side"/>.
    /// </summary>
    [PortOf("rl:Core.Object.FRand", Spec = "spec/native/reloaded-hooks.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x100242f0: top 24 bits of the hash times 2^-24")]
    public static float Unit(int ShotId, int Aim, uint Side, int Call)
    {
        uint h = 0x9E3779B9u;
        h = Mix(h ^ (uint)ShotId);
        h = Mix(h ^ (uint)Aim);
        h = Mix(h ^ (uint)Call);
        h = Mix(h ^ Side);
        return (h >> 8) * (1f / 16777216f);
    }

    // A multiply-xorshift round (murmur3's finaliser).
    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x85EBCA6Bu;
        x ^= x >> 13;
        x *= 0xC2B2AE35u;
        x ^= x >> 16;
        return x;
    }
}
