using System;
using System.Collections.Generic;

namespace OpenScctGame.Render;

/// <summary>One enabled map projector, ranked by its lit volume rather than just the lamp's position.</summary>
public readonly record struct MapSpotLightCandidate(int Index, float Distance, bool Visible);

/// <summary>Keeps visible projectors stable while sharing a bounded shadow atlas.</summary>
public sealed class MapSpotLightBudget(int capacity)
{
    // Small movements at the budget boundary must not repeatedly recreate shadow maps.
    private const float RetentionDistance = 96f;
    private const float FadeSeconds = 0.15f;
    private readonly List<MapSpotLightCandidate> _ranked = [];
    private readonly Dictionary<int, MapSpotLightCandidate> _candidates = [];
    private readonly Dictionary<int, float> _active = [];
    private readonly HashSet<int> _wanted = [];
    private readonly List<int> _previous = [];
    private bool _initialized;

    /// <summary>Atlas occupants and their visibility gain, including occupants fading out before replacement.</summary>
    public IReadOnlyDictionary<int, float> Active => _active;

    public void Update(IReadOnlyList<MapSpotLightCandidate> candidates, float delta)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _ranked.Clear();
        _candidates.Clear();
        foreach (MapSpotLightCandidate candidate in candidates)
        {
            _ranked.Add(candidate);
            _candidates.Add(candidate.Index, candidate);
        }

        _ranked.Sort(Compare);
        _wanted.Clear();
        for (int i = 0; i < _ranked.Count && i < capacity; i++) _wanted.Add(_ranked[i].Index);

        // Bound individual steps after a hitch; a budget change must still pass through intermediate brightness.
        float step = float.IsFinite(delta) ? Math.Clamp(delta, 0f, 0.05f) / FadeSeconds : 0f;
        _previous.Clear();
        _previous.AddRange(_active.Keys);
        foreach (int index in _previous)
        {
            // Actual switches are immediate. Offscreen occupants can yield without a visible transition.
            if (!_candidates.TryGetValue(index, out MapSpotLightCandidate candidate)
                || (!_wanted.Contains(index) && !candidate.Visible))
            {
                _active.Remove(index);
                continue;
            }

            float gain = Math.Clamp(_active[index] + (_wanted.Contains(index) ? step : -step), 0f, 1f);
            if (gain == 0f && !_wanted.Contains(index)) _active.Remove(index);
            else _active[index] = gain;
        }

        foreach (MapSpotLightCandidate candidate in _ranked)
        {
            if (_active.Count >= capacity) break;
            if (_wanted.Contains(candidate.Index) && !_active.ContainsKey(candidate.Index))
                _active.Add(candidate.Index, _initialized && candidate.Visible ? 0f : 1f);
        }
        _initialized = true;
    }

    private int Compare(MapSpotLightCandidate a, MapSpotLightCandidate b)
    {
        int visible = b.Visible.CompareTo(a.Visible);
        if (visible != 0) return visible;
        bool keepA = _active.ContainsKey(a.Index);
        bool keepB = _active.ContainsKey(b.Index);
        float distanceA = MathF.Max(0f, a.Distance - (keepA ? RetentionDistance : 0f));
        float distanceB = MathF.Max(0f, b.Distance - (keepB ? RetentionDistance : 0f));
        int distance = distanceA.CompareTo(distanceB);
        if (distance != 0) return distance;
        int retained = keepB.CompareTo(keepA);
        return retained != 0 ? retained : a.Index.CompareTo(b.Index);
    }
}
