using System;
using System.Numerics;
using OpenScct.Server;

namespace OpenScctGame.Controls;

/// <summary>One local tap or held selection. A change of context invalidates both the gesture and queued release.</summary>
public sealed class TeamPingInput
{
    internal const ulong HoldMilliseconds = 250;
    private const float MouseRadius = 72f;
    private const float Deadzone = 0.25f;

    private string? _source;
    private object? _context;
    private ulong _pressedAt;
    private bool _merc;
    private Vector2 _mouseDirection;
    private Vector2 _stickDirection;
    private (string Source, object Context, TeamPingKind Kind, bool Locked)? _pending;

    /// <summary>Return an identity for the current pawn, team and view, or null when pinging is unavailable.</summary>
    public Func<object?>? Context { get; set; }

    public event Action? WheelOpened;
    public event Action<TeamPingKind, bool>? Submitted;
    public bool WheelOpen { get; private set; }
    public TeamPingKind? Selection { get; private set; }
    internal bool Active => _source is not null || _pending is not null;

    internal void Press(string source, ulong now, bool merc)
    {
        if (_source is not null || Context?.Invoke() is not { } context) return;
        _source = source;
        _context = context;
        _pressedAt = now;
        _merc = merc;
    }

    internal void Release(string source, ulong now)
    {
        if (!SameSource(source, _source) || _context is not { } context) return;
        TeamPingKind? choice = Selection;
        if (!WheelOpen && now - _pressedAt < HoldMilliseconds) choice = TeamPingKind.Generic;
        if (choice is { } kind) _pending ??= (source, context, kind, WheelOpen);
        Close();
    }

    internal void Advance(ulong now, bool inputAvailable)
    {
        if (!Active) return;
        if (!inputAvailable || Context?.Invoke() is not { } context)
        {
            Cancel();
            return;
        }

        var pending = _pending;
        _pending = null;
        if (pending is { } release && Equals(context, release.Context)) Submitted?.Invoke(release.Kind, release.Locked);

        if (_source is null) return;
        if (!Equals(context, _context))
        {
            Close();
            return;
        }
        if (WheelOpen || now - _pressedAt < HoldMilliseconds) return;
        WheelOpen = true;
        WheelOpened?.Invoke();
    }

    internal void MouseMotion(float x, float y)
    {
        _mouseDirection += new Vector2(x, y) / MouseRadius;
        if (_mouseDirection.LengthSquared() > 1f) _mouseDirection = Vector2.Normalize(_mouseDirection);
        Selection = Select(_mouseDirection);
    }

    internal void StickDirection(float x, float y)
    {
        var direction = new Vector2(x, y);
        if (direction == _stickDirection) return;
        _stickDirection = direction;
        Selection = Select(direction);
    }

    private TeamPingKind? Select(Vector2 direction)
    {
        if (direction.LengthSquared() < Deadzone * Deadzone) return null;
        if (Math.Abs(direction.X) > Math.Abs(direction.Y))
        {
            if (direction.X > 0f) return TeamPingKind.Check;
            return _merc ? null : TeamPingKind.Mine;
        }
        if (direction.Y < 0f) return TeamPingKind.Watch;
        return _merc ? null : TeamPingKind.CoopBoost;
    }

    internal void CancelSource(string source)
    {
        if (SameSource(source, _source)) Close();
        if (_pending is { } pending && SameSource(source, pending.Source)) _pending = null;
    }

    public void Cancel()
    {
        Close();
        _pending = null;
    }

    private void Close()
    {
        _source = null;
        _context = null;
        WheelOpen = false;
        Selection = null;
        _mouseDirection = _stickDirection = Vector2.Zero;
    }

    private static bool SameSource(string left, string? right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
