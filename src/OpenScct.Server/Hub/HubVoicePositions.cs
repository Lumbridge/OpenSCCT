using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using OpenScct.Net.Voice.Proximity;

namespace OpenScct.Server.Hub;

/// <summary>
/// Where each hub talker is, for proximity voice (docs/HUB.md): the view point (location plus eye height) of every
/// living pawn of a world that has a replication info, by voice talker id (<see cref="VoiceK.TalkerOf"/> of the
/// PlayerID). The server's relay filter and the client's playback both read it; the table is rebuilt at most once per
/// world tick. A player with no pawn (respawning) has no position, so nobody hears him and he hears nobody.
/// </summary>
public sealed class HubVoicePositions : IVoicePositions
{
    private readonly World _world;
    private readonly Dictionary<ushort, FVector> _at = new();
    private float _stamp = float.NaN;

    public HubVoicePositions(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Talkers with a position at the last rebuild.</summary>
    public int Count => _at.Count;

    public bool TryGetPosition(ushort talker, out FVector position)
    {
        Refresh();
        return _at.TryGetValue(talker, out position);
    }

    /// <summary>Rebuilds the table when the world ticked since the last rebuild (or always with <paramref name="force"/>).</summary>
    public void Refresh(bool force = false)
    {
        float now = _world.TimeSeconds;
        if (!force && now == _stamp)
        {
            return;
        }

        _stamp = now;
        _at.Clear();
        foreach (Pawn pawn in _world.Level.DynamicActors<Pawn>())
        {
            if (pawn.bDeleteMe || pawn.Health <= 0 || pawn.PlayerReplicationInfo is not { } pri)
            {
                continue;
            }

            ushort talker = VoiceK.TalkerOf(pri.PlayerID);
            if (talker != 0)
            {
                _at[talker] = EyeOf(pawn);
            }
        }
    }

    /// <summary>A pawn's view point: its location raised by its eye height (the base eye height on a client's proxy that has none).</summary>
    public static FVector EyeOf(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        float eye = pawn.EyeHeight != 0f ? pawn.EyeHeight : pawn.BaseEyeHeight;
        return pawn.Location + new FVector(0f, 0f, eye);
    }
}
