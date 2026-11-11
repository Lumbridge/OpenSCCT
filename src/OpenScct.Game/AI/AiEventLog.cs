using System;
using System.Collections.Generic;

namespace OpenScct.Game.AI;

/// <summary>
/// The last things a bot decided and ran into, for answering "why did it do that": goal changes with the scores
/// they were chosen on, routes that failed and why, links and places left out of the plans. A ring of
/// <see cref="Capacity"/> entries; writing one costs a string, so only events are logged, never ticks.
/// </summary>
public sealed class AiEventLog
{
    public const int Capacity = 64;

    private readonly (float Time, string Text)[] _entries = new (float, string)[Capacity];

    /// <summary>Entries written since the bot was made (the newest is number Total - 1).</summary>
    public int Total { get; private set; }

    public void Add(float now, string text)
    {
        _entries[Total % Capacity] = (now, text ?? throw new ArgumentNullException(nameof(text)));
        Total++;
    }

    /// <summary>The entries from number <paramref name="from"/> on that are still held, oldest first.</summary>
    public IEnumerable<(float Time, string Text)> Since(int from)
    {
        for (int i = Math.Max(from, Total - Capacity); i < Total; i++)
        {
            yield return _entries[i % Capacity];
        }
    }
}