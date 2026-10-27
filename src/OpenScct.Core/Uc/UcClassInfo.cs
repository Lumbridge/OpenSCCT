using System;
using System.Collections.Generic;

namespace OpenScct.Core.Uc;

/// <summary>
/// Generated metadata of one UnrealScript class: where it sits in the hierarchy, its script properties, the
/// functions and states it declares (the unit manifest: ids, bytecode SHA-1, flags, signatures) and its
/// replication statement as data. Every generated class publishes one as <c>UcMeta.Class</c> and returns it from
/// <see cref="UObject.GetUcClass"/>. Immutable.
/// </summary>
public sealed class UcClassInfo
{
    private readonly Dictionary<string, UcPropertyInfo> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UcFunctionInfo> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UcStateInfo> _states = new(StringComparer.OrdinalIgnoreCase);

    public UcClassInfo(string path, Type type, UcClassInfo? super, UcClassFlags flags, string? config, int propertiesSize,
        Func<UObject>? factory, UcPropertyInfo[] properties, UcFunctionInfo[] functions, UcStateInfo[] states,
        UcReplication? replication, bool intrinsic = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == path.Length - 1)
        {
            throw new ArgumentException($"'{path}' is not a class path (Package.Class).", nameof(path));
        }

        Path = path;
        Package = path[..dot];
        Name = path[(dot + 1)..];
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Super = super;
        Flags = flags;
        Config = config;
        PropertiesSize = propertiesSize;
        Factory = factory;
        Properties = properties ?? [];
        Functions = functions ?? [];
        States = states ?? [];
        Replication = replication ?? UcReplication.Empty;
        IsIntrinsic = intrinsic;

        foreach (UcPropertyInfo p in Properties)
        {
            p.Class = this;
            _properties.Add(p.Name, p);
        }

        foreach (UcFunctionInfo f in Functions)
        {
            f.Class = this;
            _functions.Add(f.Name, f);
        }

        foreach (UcStateInfo s in States)
        {
            s.Class = this;
            _states.Add(s.Name, s);
            foreach (UcFunctionInfo f in s.Functions)
            {
                f.Class = this;
                f.State = s;
            }
        }
    }

    /// <summary><c>Package.Class</c>, for example <c>SBase.SInterrupteur</c>.</summary>
    public string Path { get; }

    public string Package { get; }

    /// <summary>The UnrealScript class name.</summary>
    public string Name { get; }

    /// <summary>The C# type (UObject classes outside the actor tree carry a U prefix, for example UInteraction).</summary>
    public Type Type { get; }

    public UcClassInfo? Super { get; }

    public UcClassFlags Flags { get; }

    /// <summary>The config file name (<c>System</c>, <c>User</c>...), or null.</summary>
    public string? Config { get; }

    /// <summary>Native object size of the game build in bytes, or 0 when unknown.</summary>
    public int PropertiesSize { get; }

    /// <summary>Creates an instance, or null for abstract classes and classes without a public constructor.</summary>
    public Func<UObject>? Factory { get; }

    /// <summary>Script properties declared by this class, in declaration order.</summary>
    public UcPropertyInfo[] Properties { get; }

    /// <summary>Class-level functions declared by this class.</summary>
    public UcFunctionInfo[] Functions { get; }

    /// <summary>States declared (or redefined) by this class.</summary>
    public UcStateInfo[] States { get; }

    public UcReplication Replication { get; }

    /// <summary>True for an engine class without script (declared in C++ only); only its fields are known.</summary>
    public bool IsIntrinsic { get; }

    public bool IsAbstract => (Flags & UcClassFlags.CLASS_Abstract) != 0;

    public bool IsPlaceable => (Flags & UcClassFlags.CLASS_Placeable) != 0;

    public bool HasNativeReplication => (Flags & UcClassFlags.CLASS_NativeReplication) != 0;

    /// <summary>True for Engine.Actor and its subclasses.</summary>
    public bool IsActor => typeof(Engine.Actor).IsAssignableFrom(Type);

    /// <summary>Coverage unit of the class defaults, <c>def:Pkg.Class</c>.</summary>
    public string DefaultsUnitId => "def:" + Path;

    /// <summary>True when this class is <paramref name="other"/> or derives from it.</summary>
    public bool IsA(UcClassInfo other)
    {
        for (UcClassInfo? c = this; c is not null; c = c.Super)
        {
            if (ReferenceEquals(c, other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A property declared by this class only.</summary>
    public UcPropertyInfo? FindOwnProperty(string name) => _properties.GetValueOrDefault(name);

    /// <summary>A property of this class or an ancestor (ASCII case ignored).</summary>
    public UcPropertyInfo? FindProperty(string name)
    {
        for (UcClassInfo? c = this; c is not null; c = c.Super)
        {
            if (c._properties.TryGetValue(name, out UcPropertyInfo? p))
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>A class-level function declared by this class only.</summary>
    public UcFunctionInfo? FindOwnFunction(string name) => _functions.GetValueOrDefault(name);

    /// <summary>The nearest class-level declaration of a function in this class or an ancestor.</summary>
    public UcFunctionInfo? FindFunction(string name)
    {
        for (UcClassInfo? c = this; c is not null; c = c.Super)
        {
            if (c._functions.TryGetValue(name, out UcFunctionInfo? f))
            {
                return f;
            }
        }

        return null;
    }

    /// <summary>A state declared (or redefined) by this class only.</summary>
    public UcStateInfo? FindOwnState(string name) => _states.GetValueOrDefault(name);

    /// <summary>The nearest declaration of a state in this class or an ancestor.</summary>
    public UcStateInfo? FindState(string name)
    {
        for (UcClassInfo? c = this; c is not null; c = c.Super)
        {
            if (c._states.TryGetValue(name, out UcStateInfo? s))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>A state of this class by name; throws when the class does not declare it.</summary>
    public UcStateInfo State(string name) =>
        FindOwnState(name) ?? throw new KeyNotFoundException($"{Path} declares no state {name}.");

    /// <summary>Every function this class declares, class-level first, then per state in declaration order.</summary>
    public IEnumerable<UcFunctionInfo> AllOwnFunctions()
    {
        foreach (UcFunctionInfo f in Functions)
        {
            yield return f;
        }

        foreach (UcStateInfo s in States)
        {
            foreach (UcFunctionInfo f in s.Functions)
            {
                yield return f;
            }
        }
    }

    public UObject? Create() => Factory?.Invoke();

    public override string ToString() => Path;
}
