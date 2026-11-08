using System;
using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>A native mover stop, relative to BasePos and BaseRot, in Unreal units.</summary>
public readonly record struct MoverPathKey(FVector Offset, FRotator Turn);

/// <summary>An OpenSCCT path. Legacy maps continue using the original eight-element arrays.</summary>
public sealed class MoverPath
{
    // A resource guard, independent of the original game's byte indexes and fixed arrays.
    public const int MaxKeys = 65536;
    public MoverPath(IReadOnlyList<MoverPathKey> keys, int start = 0)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count is < 1 or > MaxKeys || start < 0 || start >= keys.Count)
            throw new ArgumentOutOfRangeException(nameof(keys), "Invalid mover path length or start stop");
        var copy = new MoverPathKey[keys.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            var p = keys[i].Offset;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                throw new ArgumentException("Mover positions must be finite", nameof(keys));
            copy[i] = keys[i];
        }
        Keys = Array.AsReadOnly(copy);
        Start = start;
    }
    public IReadOnlyList<MoverPathKey> Keys { get; }
    public int Start { get; }
}

public partial class Mover
{
    private MoverPath? _path;
    /// <summary>Loaded identically on authority and clients before play begins.</summary>
    public MoverPath? Path
    {
        get => _path;
        set
        {
            _path = value;
            if (value is not null) CurrentKey = PreviousKey = value.Start;
        }
    }

    // Separate integer replication leaves the original UC layout and vector packing intact.
    public int PathTarget;
    public int PathPrevious;
    public int KeyCount => Path?.Keys.Count ?? Math.Clamp((int)NumKeys, 1, MoverK.MaxKeys);
    public int KeyCapacity => Path?.Keys.Count ?? MoverK.MaxKeys;
    public int CurrentKey
    {
        get => Path is null ? KeyNum : PathTarget;
        set
        {
            if (Path is null) { KeyNum = (byte)Math.Clamp(value, 0, MoverK.MaxKeys - 1); return; }
            PathTarget = Math.Clamp(value, 0, KeyCapacity - 1); KeyNum = (byte)Math.Min(PathTarget, MoverK.MaxKeys - 1);
        }
    }
    public int PreviousKey
    {
        get => Path is null ? PrevKeyNum : PathPrevious;
        set
        {
            if (Path is null) { PrevKeyNum = (byte)Math.Clamp(value, 0, MoverK.MaxKeys - 1); return; }
            PathPrevious = Math.Clamp(value, 0, KeyCapacity - 1); PrevKeyNum = (byte)Math.Min(PathPrevious, MoverK.MaxKeys - 1);
        }
    }
    public FVector KeyPosition(int key) => Path is { } p ? p.Keys[Math.Clamp(key, 0, p.Keys.Count - 1)].Offset : KeyPos[Math.Clamp(key, 0, MoverK.MaxKeys - 1)];
    public FRotator KeyRotation(int key) => Path is { } p ? p.Keys[Math.Clamp(key, 0, p.Keys.Count - 1)].Turn : KeyRot[Math.Clamp(key, 0, MoverK.MaxKeys - 1)];
}
