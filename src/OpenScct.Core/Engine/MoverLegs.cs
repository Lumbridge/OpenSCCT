using System;
using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>
/// One mover key's own timing (OpenSCCT, spec/additions/mover-legs.md): <see cref="Time"/> is the seconds of the leg
/// between this key and the key before it, run either way; <see cref="Smooth"/> says whether the mover eases in and
/// out at this key. Null keeps the mover's own <c>MoveTime</c> or <c>MoverGlideType</c>.
/// </summary>
public readonly record struct MoverKeyTiming(float? Time = null, bool? Smooth = null)
{
    public bool IsDefault => Time is null && Smooth is null;
}

/// <summary>
/// A mover's per-key timing (SVMap 1.2 <c>sim/movers.json</c>). The game's mover has one <c>MoveTime</c> for every leg
/// and one glide type; this gives each leg between two neighbouring keys its own time, and each key its own easing, so
/// a lift can crawl into a floor and race between floors. A leg eases at a key that is smooth: smooth at both ends is
/// the game's glide (3a^2 - 2a^3), at one end a quadratic ease, at neither a constant speed.
/// </summary>
public sealed class MoverLegs
{
    public MoverLegs(IReadOnlyList<MoverKeyTiming> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count > MoverK.MaxKeys)
        {
            throw new ArgumentException($"a mover has at most {MoverK.MaxKeys} keys", nameof(keys));
        }

        if (keys.Count > 0 && keys[0].Time is not null)
        {
            throw new ArgumentException("key 0 has no leg before it, so it takes no time", nameof(keys));
        }

        Keys = keys;
    }

    /// <summary>Key 0 first; keys past the end keep the mover's own timing.</summary>
    public IReadOnlyList<MoverKeyTiming> Keys { get; }

    /// <summary>The seconds of the leg between neighbouring keys <paramref name="a"/> and <paramref name="b"/> (either
    /// way), or null for the mover's own time (no entry, or keys that are not neighbours).</summary>
    public float? LegTime(int a, int b) =>
        Math.Abs(a - b) == 1 && Math.Max(a, b) < Keys.Count ? Keys[Math.Max(a, b)].Time : null;

    /// <summary>True when the mover eases at <paramref name="key"/>: its own setting, else the mover's glide.</summary>
    public bool SmoothAt(int key, bool moverGlides) =>
        key >= 0 && key < Keys.Count && Keys[key].Smooth is { } smooth ? smooth : moverGlides;

    /// <summary>
    /// How far along a leg the mover is after <paramref name="alpha"/> of its time, easing at the start and the end as
    /// asked. Each shape turned round is the shape with its ends swapped, so a leg reversed half way does not jump.
    /// </summary>
    public static double Ease(double alpha, bool smoothStart, bool smoothEnd) => (smoothStart, smoothEnd) switch
    {
        (true, true) => 3.0 * alpha * alpha - 2.0 * alpha * alpha * alpha,
        (true, false) => alpha * alpha,
        (false, true) => alpha * (2.0 - alpha),
        _ => alpha,
    };
}
