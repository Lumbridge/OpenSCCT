using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>A replicated class: wire index, layout and a factory for spawning it on clients.</summary>
public sealed class NetClassInfo
{
    private readonly NetClassRegistry _registry;
    private readonly object _gate = new();
    private RepLayout? _layout;

    internal NetClassInfo(NetClassRegistry registry, int classIndex, Type type, UcClassInfo? ucClass, Func<Actor> factory,
        RepExtension? extension, string? path = null)
    {
        _registry = registry;
        ClassIndex = classIndex;
        Type = type;
        UcClass = ucClass;
        Factory = factory;
        Extension = extension;
        Path = path ?? (extension is null && ucClass is not null && ucClass.Type == type ? ucClass.Path : type.Name);
        ClassName = new Name(type.Name);
    }

    /// <summary>Index sent in actor channel open bunches (registration order).</summary>
    public int ClassIndex { get; }

    public Type Type { get; }

    /// <summary>The generated class (for a runtime-only class, its nearest generated ancestor).</summary>
    public UcClassInfo? UcClass { get; }

    /// <summary>The original class path, or the C# name of a runtime-only class.</summary>
    public string Path { get; }

    public Name ClassName { get; }

    public RepExtension? Extension { get; }

    /// <summary>The layout, built on first use.</summary>
    public RepLayout Layout
    {
        get
        {
            if (_layout is { } layout)
            {
                return layout;
            }

            lock (_gate)
            {
                _layout ??= new RepLayoutBuilder(_registry, UcClass, Type, Extension).Build();
                return _layout;
            }
        }
    }

    internal Func<Actor> Factory { get; }

    public override string ToString() => Path;
}

/// <summary>
/// The classes a server may replicate and a client may spawn (UE2 package map plus class net caches): every
/// concrete actor class of a <see cref="UcClassIndex"/> in index order, with layouts generated from the decompiled
/// replication blocks, followed by runtime-only classes registered with <see cref="Register{T}"/>. Server and
/// client must build the same registry; the protocol hash covers every class and layout signature, so a mismatch is
/// refused at the handshake.
/// <para>A registry may be shared by drivers on different threads: its caches are filled under locks.</para>
/// </summary>
public sealed class NetClassRegistry
{
    private readonly List<NetClassInfo> _classes = new();
    private readonly Dictionary<Type, NetClassInfo> _byType = new();
    private readonly Dictionary<string, NetClassInfo> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private ulong? _protocolHash;

    /// <summary>A registry of every concrete actor class in <paramref name="index"/>.</summary>
    public NetClassRegistry(UcClassIndex index, RepConditionSource conditions = RepConditionSource.Native)
    {
        Index = index ?? throw new ArgumentNullException(nameof(index));
        ConditionSource = conditions;
        Types = new NetTypeMap(index);
        ActorClass = index.Find("Engine.Actor") ?? Actor.UcMeta.Class;
        foreach (UcClassInfo c in index.Classes)
        {
            if (!c.IsActor || c.IsAbstract || c.Factory is null || c.IsIntrinsic)
            {
                continue;
            }

            Func<UObject> create = c.Factory;
            Add(new NetClassInfo(this, _classes.Count, c.Type, c, () => (Actor)create(), null));
        }
    }

    /// <summary>A registry of the Core and Engine classes.</summary>
    public NetClassRegistry()
        : this(new UcClassIndex(CorePackage.Info, EnginePackage.Info))
    {
    }

    public UcClassIndex Index { get; }

    public RepConditionSource ConditionSource { get; }

    public NetTypeMap Types { get; }

    public PropertyReaders Readers { get; } = new();

    internal UcClassInfo ActorClass { get; }

    public int Count => _classes.Count;

    public IReadOnlyList<NetClassInfo> Classes => _classes;

    /// <summary>
    /// Registers a runtime-only class (not an original one): it replicates with the generated layout of its nearest
    /// generated ancestor, followed by <paramref name="extension"/>'s hand-written members. Register the same classes
    /// in the same order on server and client.
    /// </summary>
    public NetClassInfo Register<T>(RepExtension? extension = null)
        where T : Actor, new()
    {
        lock (_classes)
        {
            if (_protocolHash is not null)
            {
                throw new InvalidOperationException("Classes cannot be registered after the protocol hash was computed.");
            }
        }

        if (_byType.ContainsKey(typeof(T)))
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already registered.");
        }

        UcClassInfo? nearest = Index.FindNearest(typeof(T));
        var info = new NetClassInfo(this, _classes.Count, typeof(T), nearest, static () => new T(), extension);
        Add(info);
        return info;
    }

    /// <summary>
    /// Appends hand-written members to the layout of an original class (for example a net-layer function that
    /// carries a port's calls). The class keeps its index and path; extend the same classes with the same members on
    /// server and client, before the protocol hash is computed.
    /// </summary>
    public NetClassInfo Extend<T>(RepExtension extension)
        where T : Actor
    {
        ArgumentNullException.ThrowIfNull(extension);
        lock (_classes)
        {
            if (_protocolHash is not null)
            {
                throw new InvalidOperationException("Classes cannot be extended after the protocol hash was computed.");
            }

            if (!_byType.TryGetValue(typeof(T), out NetClassInfo? old))
            {
                throw new InvalidOperationException($"{typeof(T).Name} is not a registered class.");
            }

            if (old.Extension is not null)
            {
                throw new InvalidOperationException($"{typeof(T).Name} already has an extension.");
            }

            var info = new NetClassInfo(this, old.ClassIndex, old.Type, old.UcClass, old.Factory, extension, old.Path);
            _classes[old.ClassIndex] = info;
            _byType[info.Type] = info;
            _byPath[info.Path] = info;
            return info;
        }
    }

    private void Add(NetClassInfo info)
    {
        _classes.Add(info);
        _byType[info.Type] = info;
        _byPath.TryAdd(info.Path, info);
    }

    public NetClassInfo? Get(int classIndex) =>
        (uint)classIndex < (uint)_classes.Count ? _classes[classIndex] : null;

    /// <summary>The registration for exactly this type, or null when it does not replicate.</summary>
    public NetClassInfo? Find(Type type) => _byType.GetValueOrDefault(type);

    /// <summary>The registration of a class path (or a runtime-only class name).</summary>
    public NetClassInfo? Find(string path) => _byPath.GetValueOrDefault(path);

    /// <summary>Hash of the protocol version, every class and every layout signature, in order. Builds every layout.</summary>
    public ulong ComputeProtocolHash()
    {
        lock (_classes)
        {
            return _protocolHash ??= Hash();
        }
    }

    private ulong Hash()
    {
        ulong hash = NetHash.Fnv1a64(NetProtocol.Version);
        hash = NetHash.Fnv1a64(ConditionSource.ToString(), hash);
        foreach (NetClassInfo info in _classes)
        {
            hash = NetHash.Fnv1a64(info.Path, hash);
            hash = NetHash.Fnv1a64(":", hash);
            hash = NetHash.Fnv1a64(info.Layout.Signature, hash);
            hash = NetHash.Fnv1a64(";", hash);
        }

        return hash;
    }
}

/// <summary>Version tag of our wire format, part of the protocol hash.</summary>
public static class NetProtocol
{
    /// <summary>0.3: the packet header carries the connection salt; the handshake has the player key challenge.</summary>
    public const string Version = "svm-net/0.3";
}

/// <summary>FNV-1a 64-bit over UTF-16 code units: stable across processes and platforms.</summary>
public static class NetHash
{
    public const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Fnv1a64(ReadOnlySpan<char> text, ulong hash = OffsetBasis)
    {
        foreach (char c in text)
        {
            hash ^= (byte)c;
            hash *= Prime;
            hash ^= (byte)(c >> 8);
            hash *= Prime;
        }

        return hash;
    }
}
