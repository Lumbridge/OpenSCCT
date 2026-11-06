using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Net;

namespace OpenScct.Server;

public sealed partial class ClientSession
{
    private List<DemoPresentationSample>? _demoCheckpointPresentation;

    /// <summary>The current bootstrap supplies the presentation history needed for an accelerated rewind.</summary>
    public bool HasDemoCheckpointPresentation { get; private set; }

    /// <summary>Appends the selected checkpoint's presentation inputs and drains the session queue.</summary>
    public void TakeDemoCheckpointPresentation(List<DemoPresentationSample> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_demoCheckpointPresentation is not { Count: > 0 } history) return;
        into.AddRange(history);
        history.Clear();
    }

    private bool ReceiveDemoCheckpointPresentation(string? text)
    {
        if (_transport is not DemoPlaybackTransport { IsBootstrapping: true } playback || text is null) return false;
        if (text.StartsWith(DemoPresentationWire.Prefix, StringComparison.Ordinal))
        {
            if ((_demoCheckpointPresentation?.Count ?? 0) < DemoPresentationWire.MaxSamples &&
                DemoPresentationWire.TryDecode(text.AsSpan(DemoPresentationWire.Prefix.Length), Driver, playback.StartTick, out var sample))
                (_demoCheckpointPresentation ??= new()).Add(sample);
            return true;
        }
        if (text.StartsWith(DemoPresentationWire.MusicPrefix, StringComparison.Ordinal))
        {
            ReadOnlySpan<char> track = text.AsSpan(DemoPresentationWire.MusicPrefix.Length);
            MusicState.Of(World.Level).Current = track.IsEmpty ? AssetRef.None : new AssetRef(track.ToString());
            HasDemoCheckpointPresentation = true;
            return true;
        }
        return PawnSoundReplication.RestoreDemoCheckpointName(Driver, text);
    }
}

// These controls are consumed only during bootstrap. Old readers can ignore them without changing the packet format.
internal static class DemoPresentationWire
{
    internal const string Prefix = "#demo-checkpoint-presentation ";
    internal const string MusicPrefix = "#demo-checkpoint-music ";
    internal const int LiveEmitter = 1;
    internal const int MaxSamples = 2400;
    private const int MaxBytes = 348;
    private const int MaxNameBytes = 256;
    private const int MaxBoneBytes = 36;

    internal static string? Encode(in DemoPresentationSample sample, NetDriver driver)
    {
        SimEvent e = sample.Event;
        string name = e.What.IsNone ? string.Empty : e.What.ToString();
        string bone = e.Bone.IsNone ? string.Empty : e.Bone.ToString();
        if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes || Encoding.UTF8.GetByteCount(bone) > MaxBoneBytes) return null;

        uint source = (uint)e.ActorId << 1;
        if (e.ActorId > 0 && driver.World?.Level.FindActor(e.ActorId) is { } actor && (actor.bStatic || actor.bNoDelete) &&
            driver.MapBinding.IndexOf(actor) is int index && index >= 0)
            source = ((uint)index << 1) | 1;

        var w = new FBitWriter(MaxBytes * 8);
        w.WriteUInt32(sample.Tick);
        w.WriteByte((byte)e.Kind);
        w.WriteUInt32(source);
        w.WriteByte((byte)((e.NoOverride ? 1 : 0) | (e.NoAttenuate ? 2 : 0) | (e.Heard ? 4 : 0) | (e.Flat ? 8 : 0)));
        w.WriteInt32(e.Slot);
        w.WriteFloat(e.Location.X); w.WriteFloat(e.Location.Y); w.WriteFloat(e.Location.Z);
        w.WriteInt32(e.Rotation.Pitch); w.WriteInt32(e.Rotation.Yaw); w.WriteInt32(e.Rotation.Roll);
        w.WriteFloat(e.Volume); w.WriteFloat(e.Radius); w.WriteFloat(e.Pitch);
        w.WriteString(name, MaxNameBytes); w.WriteString(bone, MaxBoneBytes);
        return w.IsError ? null : Prefix + Convert.ToBase64String(w.Data);
    }

    internal static bool TryDecode(ReadOnlySpan<char> encoded, NetDriver driver, uint checkpointTick, out DemoPresentationSample sample)
    {
        sample = default;
        var bytes = new byte[MaxBytes];
        if (!Convert.TryFromBase64Chars(encoded, bytes, out int count)) return false;
        var r = new FBitReader(bytes, count * 8);
        uint tick = r.ReadUInt32();
        var kind = (SimEventKind)r.ReadByte();
        uint source = r.ReadUInt32();
        byte flags = r.ReadByte();
        var e = new SimEvent
        {
            Tick = tick, Kind = kind,
            Slot = r.ReadInt32(),
            Location = new FVector(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()),
            Rotation = new FRotator(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()),
            Volume = r.ReadFloat(), Radius = r.ReadFloat(), Pitch = r.ReadFloat(),
            What = r.ReadName(MaxNameBytes), Bone = r.ReadName(MaxBoneBytes),
            NoOverride = (flags & 1) != 0, NoAttenuate = (flags & 2) != 0,
            Heard = (flags & 4) != 0, Flat = (flags & 8) != 0,
        };
        if (r.IsError || !r.AtEnd || tick > checkpointTick || !Enum.IsDefined(kind) ||
            !Finite(e.Location.X) || !Finite(e.Location.Y) || !Finite(e.Location.Z) ||
            !Finite(e.Volume) || !Finite(e.Radius) || !Finite(e.Pitch))
            return false;

        Actor? actor = source == 0 ? null : (source & 1) != 0
            ? driver.MapBinding.Get((int)(source >> 1)) : driver.FindActorByNetGuid(source >> 1);
        if (actor is { bDeleteMe: true }) actor = null;
        if (source != 0 && actor is null && (kind is SimEventKind.StopSound or SimEventKind.Notify ||
            kind == SimEventKind.SpawnEmitter && e.Slot == LiveEmitter))
            return false;
        e.ActorId = actor?.ActorId ?? 0;
        if (kind == SimEventKind.SpawnEmitter && e.Slot == LiveEmitter && actor is Emitter emitter && e.Radius >= 0f)
            emitter.LifeSpan = e.Radius;
        sample = new DemoPresentationSample(tick, e) { MissingSource = source != 0 && actor is null };
        return true;
    }

    private static bool Finite(float value) => MathF.Abs(value) <= 10_000_000f;
}
