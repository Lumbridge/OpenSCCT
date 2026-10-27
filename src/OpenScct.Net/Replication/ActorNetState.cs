using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Server-side network state of one actor: the snapshot of its replicated properties for the current pass (taken
/// once, compared by every channel), the snapshot of the previous pass (to find changes), and its open channels.
/// <para>
/// The original keeps up to four (connection, channel) pairs per actor in the space of
/// <c>ActorNetChannels</c> and its padding, and every server lookup goes through that cache, so it supports four
/// client connections per actor (spec/native/net-replication.md rule 22). The port keeps the pairs in the same
/// order, opening order, but without the limit.
/// </para>
/// </summary>
[UcNative("AActor::FindNetChannel", Exe = 0x10992150, Editor = 0x110a6320)]
public sealed class ActorNetState
{
    private readonly List<ActorChannel> _channels = new(4);
    private long _captureStamp = -1;

    internal ActorNetState(Actor actor, NetClassInfo info)
    {
        Actor = actor;
        Info = info;
        RepLayout layout = info.Layout;
        Words = new int[layout.WordCount];
        Objects = new object?[layout.ObjectCount];
        PreviousWords = new int[layout.WordCount];
        PreviousObjects = new object?[layout.ObjectCount];
    }

    public Actor Actor { get; }

    public NetClassInfo Info { get; }

    /// <summary>The current pass's snapshot, quantised.</summary>
    internal int[] Words { get; }

    internal object?[] Objects { get; }

    internal int[] PreviousWords { get; }

    internal object?[] PreviousObjects { get; }

    internal bool HasPrevious { get; set; }

    /// <summary>Open channels for this actor, in opening order (the channel cache).</summary>
    public IReadOnlyList<ActorChannel> Channels => _channels;

    internal void AddChannel(ActorChannel channel)
    {
        if (!_channels.Contains(channel))
        {
            _channels.Add(channel);
        }
    }

    internal void RemoveChannel(ActorChannel channel) => _channels.Remove(channel);

    /// <summary>The channel on <paramref name="connection"/>, or null.</summary>
    public ActorChannel? FindChannel(NetConnection connection)
    {
        for (int i = 0; i < _channels.Count; i++)
        {
            if (ReferenceEquals(_channels[i].Connection, connection))
            {
                return _channels[i];
            }
        }

        return null;
    }

    /// <summary>Takes the snapshot of <paramref name="stamp"/> unless it already exists.</summary>
    internal void EnsureCaptured(long stamp)
    {
        if (_captureStamp == stamp)
        {
            return;
        }

        _captureStamp = stamp;
        Info.Layout.Capture(Actor, Words, Objects);
    }

    /// <summary>
    /// Takes the snapshot of <paramref name="stamp"/> and reports whether any replicated value changed since the
    /// previous call. This stands in for the engine marking an actor bNetDirty when script writes a replicated
    /// property (spec/native/net-replication.md rule 1; the writers were not traced).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "bNetDirty from changed replicated values: who sets bNetDirty in this build was not traced")]
    internal bool CaptureAndCompare(long stamp)
    {
        EnsureCaptured(stamp);
        bool changed = !HasPrevious || !Words.AsSpan().SequenceEqual(PreviousWords) || !SameObjects();
        Words.CopyTo(PreviousWords, 0);
        Objects.CopyTo(PreviousObjects, 0);
        HasPrevious = true;
        return changed;
    }

    private bool SameObjects()
    {
        for (int i = 0; i < Objects.Length; i++)
        {
            object? a = Objects[i];
            object? b = PreviousObjects[i];
            if (ReferenceEquals(a, b))
            {
                continue;
            }

            if (a is string sa && b is string sb && string.Equals(sa, sb, StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
