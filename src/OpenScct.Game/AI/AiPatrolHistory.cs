using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Game.AI;

/// <summary>Small, decaying map memory of approaches Merc bots have directly seen.</summary>
public sealed class AiPatrolHistory
{
    private const int MaxCells = 64;
    private readonly Dictionary<(int X, int Y, int Z), (FVector Point, float Count)> _seen = new();

    internal static (int X, int Y, int Z) Cell(FVector p) =>
        ((int)MathF.Floor(p.X / 420f), (int)MathF.Floor(p.Y / 420f), (int)MathF.Floor(p.Z / 240f));

    internal void Record(FVector point)
    {
        var key = Cell(point);
        if (_seen.TryGetValue(key, out var previous))
        {
            _seen[key] = ((previous.Point + point) * .5f, MathF.Min(8f, previous.Count + 1f));
            return;
        }

        if (_seen.Count >= MaxCells)
        {
            var weakest = key;
            float least = float.MaxValue;
            foreach (var item in _seen)
            {
                if (item.Value.Count >= least) continue;
                weakest = item.Key;
                least = item.Value.Count;
            }
            _seen.Remove(weakest);
        }

        _seen[key] = (point, 1f);
    }

    public void StartRound()
    {
        foreach (var key in new List<(int, int, int)>(_seen.Keys))
        {
            var previous = _seen[key];
            float count = previous.Count * .75f;
            if (count < .7f) _seen.Remove(key);
            else _seen[key] = (previous.Point, count);
        }
    }

    internal float Interest(FVector at)
    {
        float result = 0f;
        foreach (var entry in _seen.Values)
        {
            if (entry.Count < 1.4f || MathF.Abs(at.Z - entry.Point.Z) > 175f) continue;
            float distance = (at - entry.Point).Size();
            result = MathF.Max(result, MathF.Min(4f, entry.Count) * MathF.Max(0f, 1f - distance / 650f));
        }
        return result;
    }
}
