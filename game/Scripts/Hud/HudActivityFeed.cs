using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScctGame.Hud;

public enum HudActivityKind
{
    Kill, Death, Suicide, Resign, Sleep, Knockout, Objective, Join, Leave, Match, Alarm, LastLife,
}

public enum HudActivityCause
{
    None, Firearm, Headshot, Explosion, Charge, DropAttack, Elbow, Grab, NeckBreak, Choke,
    Fall, Crush, Poison, StickyShock, StickyBlast, Smoke, StickyCamera, Other,
}

/// <summary>Receipt-time identity: rows remain readable after a player leaves, changes team or renames.</summary>
public sealed record HudActivityParticipant(int PlayerId, string Name, int Team);

/// <summary>One captured match event, independent of the live replicated objects and Godot.</summary>
public sealed record HudActivityEntry(
    long Id,
    HudActivityKind Kind,
    HudActivityParticipant? Actor,
    HudActivityParticipant? Victim,
    HudActivityCause Cause,
    string Label,
    string Text,
    float ReceivedAt,
    bool InvolvesLocalPlayer)
{
    // An opaque identity only: the drawer never reads mutable alarm properties from it.
    internal object? AlarmSource { get; init; }

    public bool IsCombat => Kind is HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide or
        HudActivityKind.Resign or HudActivityKind.Sleep or HudActivityKind.Knockout;
}

/// <summary>A small immediate feed, owned by one HUD rather than one pawn or active HUD page.</summary>
public sealed class HudActivityFeed
{
    public const int Capacity = 5;
    public const float Lifetime = 6f;
    public const float EnterTime = 0.15f;
    public const float FadeTime = 0.4f;

    private readonly List<HudActivityEntry> _entries = new(Capacity);
    private readonly IReadOnlyList<HudActivityEntry> _view;
    private PlayerController? _owner;
    private bool _bound;
    private float? _time;
    private long _nextId;

    public HudActivityFeed() => _view = _entries.AsReadOnly();

    /// <summary>Newest first, with no delayed backlog.</summary>
    public IReadOnlyList<HudActivityEntry> Entries => _view;

    /// <summary>Call on receipt as well as on HUD update so the first frame preserves early arrivals.</summary>
    public void Bind(PlayerController? owner, float now)
    {
        if (_bound && !ReferenceEquals(_owner, owner)) Clear();
        _owner = owner;
        _bound = true;
        Tick(now);
    }

    /// <summary>Uses the simulation clock, including while the HUD is hidden.</summary>
    public void Tick(float now)
    {
        if (!float.IsFinite(now)) return;
        if (_time is { } previous && now < previous) _entries.Clear();
        _time = now;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (now - _entries[i].ReceivedAt >= Lifetime) _entries.RemoveAt(i);
        }
    }

    public void Add(HudActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!float.IsFinite(entry.ReceivedAt)) return;
        Tick(entry.ReceivedAt);
        if (entry.Kind == HudActivityKind.Alarm && entry.AlarmSource is { } source &&
            _entries.Exists(e => e.Kind == HudActivityKind.Alarm && ReferenceEquals(e.AlarmSource, source)))
        {
            // A retrigger neither adds another row nor pins the current notice indefinitely.
            return;
        }

        _entries.Insert(0, entry with { Id = ++_nextId });
        if (_entries.Count > Capacity) _entries.RemoveAt(Capacity);
    }

    public void Clear()
    {
        _entries.Clear();
        _time = null;
    }

    /// <summary>A different connection must not inherit the previous controller binding.</summary>
    public void Reset()
    {
        Clear();
        _owner = null;
        _bound = false;
    }
}
