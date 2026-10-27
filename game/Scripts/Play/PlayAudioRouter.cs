using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScct.Server;

namespace OpenScctGame.Play;

/// <summary>
/// The client world's event sink in play: passes every tick's events to the sink that was there (the HUD's tap) and
/// the sound events to the sound system, where a sound this machine played itself and the server's copy of it
/// (ClientHearSound, <see cref="SimEvent.Heard"/>) play once. The original's script runs on both machines for many
/// actors (the pawns' footsteps, their gear), and the server also sends each player what it plays; whichever copy of
/// a call on the same actor arrives first plays, the other is dropped when it comes within
/// <see cref="DuplicateSeconds"/>. Godot-free.
/// </summary>
public sealed class PlayAudioRouter : ISimEventSink
{
    /// <summary>
    /// How close two copies of a sound on the same actor must be to count as one call: below the shortest gap
    /// between two calls of a repeating sound (a running step every 0.3 s or so), above the gap between the two
    /// machines' copies (the client's prediction, the proxies' interpolation delay).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Network adaptation of the rewrite: the client's own and the server's copy of one call")]
    public const float DuplicateSeconds = 0.2f;

    private readonly ISimEventSink _sound;
    private readonly Dictionary<(int Actor, Name Sound), (long Tick, bool Heard)> _last = [];
    private readonly List<(int Actor, Name Sound)> _stale = [];
    private readonly List<SimEvent> _pass = [];
    private long _prunedAt;

    public PlayAudioRouter(ISimEventSink sound, ISimEventSink? next = null)
    {
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        Next = next;
    }

    /// <summary>The sink that was installed before (it gets every event).</summary>
    public ISimEventSink? Next { get; }

    private ClientSession? _client;

    /// <summary>
    /// A router in front of <paramref name="sound"/> as <paramref name="client"/>'s world event sink (wrapping the sink
    /// there), also taking the late events before each of its steps. Install it before the HUD wraps the sink.
    /// </summary>
    public static PlayAudioRouter Install(ClientSession client, ISimEventSink sound)
    {
        ArgumentNullException.ThrowIfNull(client);
        var router = new PlayAudioRouter(sound, client.World.EventSink) { _client = client };
        client.World.EventSink = router;
        client.BeforeWorldTick += router.BeforeWorldTick;
        return router;
    }

    /// <summary>Takes the router out of its client again.</summary>
    public void Uninstall()
    {
        if (_client is not { } client)
        {
            return;
        }

        client.BeforeWorldTick -= BeforeWorldTick;
        if (ReferenceEquals(client.World.EventSink, this))
        {
            client.World.EventSink = Next;
        }

        _client = null;
    }

    private void BeforeWorldTick(ClientSession client) => TakeLate(client.World);

    /// <summary>Copies dropped as the second copy of a call.</summary>
    public long Duplicates { get; private set; }

    /// <summary>Sound events of the server (heard) passed on.</summary>
    public long HeardPlayed { get; private set; }

    /// <summary>Sound events of this machine's own simulation passed on.</summary>
    public long OwnPlayed { get; private set; }

    private static long WindowTicks => (long)Math.Ceiling(DuplicateSeconds * Sim.Hz);

    public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
    {
        Next?.OnSimEvents(tick, events);
        Route(tick, events);
    }

    /// <summary>
    /// The sound events <paramref name="world"/> raised since its last tick (a call received before the step, an input
    /// command): they play with the next tick. Call it before each step; events already taken are not taken again.
    /// </summary>
    public void TakeLate(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ReadOnlySpan<SimEvent> late = world.Events.Late;
        if (world.TickCount != _lateTick)
        {
            _lateTick = world.TickCount;
            _lateTaken = 0;
        }

        if (late.Length > _lateTaken)
        {
            ReadOnlySpan<SimEvent> fresh = late[_lateTaken..];
            _lateTaken = late.Length;
            Route(world.TickCount + 1, fresh);
        }
    }

    private long _lateTick = -1;
    private int _lateTaken;

    private void Route(long tick, ReadOnlySpan<SimEvent> events)
    {
        _pass.Clear();
        foreach (SimEvent e in events)
        {
            if (e.Kind == SimEventKind.PlaySound && !Admit(tick, e))
            {
                Duplicates++;
                continue;
            }

            if (e.Kind == SimEventKind.PlaySound)
            {
                if (e.Heard)
                {
                    HeardPlayed++;
                }
                else
                {
                    OwnPlayed++;
                }
            }

            _pass.Add(e);
        }

        if (_pass.Count > 0)
        {
            _sound.OnSimEvents(tick, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pass));
        }

        Prune(tick);
    }

    /// <summary>False when <paramref name="e"/> is the other machine's copy of a call that already played.</summary>
    private bool Admit(long tick, in SimEvent e)
    {
        if (e.ActorId == 0 || e.What.IsNone)
        {
            return true;
        }

        var key = (e.ActorId, e.What);
        if (_last.TryGetValue(key, out (long Tick, bool Heard) last) && last.Heard != e.Heard && tick - last.Tick <= WindowTicks)
        {
            // The pair is complete: the next call of either side plays again.
            _last.Remove(key);
            return false;
        }

        _last[key] = (tick, e.Heard);
        return true;
    }

    private void Prune(long tick)
    {
        if (tick - _prunedAt < Sim.Hz)
        {
            return;
        }

        _prunedAt = tick;
        _stale.Clear();
        foreach (KeyValuePair<(int Actor, Name Sound), (long Tick, bool Heard)> kv in _last)
        {
            if (tick - kv.Value.Tick > WindowTicks)
            {
                _stale.Add(kv.Key);
            }
        }

        foreach ((int Actor, Name Sound) k in _stale)
        {
            _last.Remove(k);
        }
    }
}
