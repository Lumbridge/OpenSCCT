using System;
using System.Collections.Generic;

namespace OpenScct.Core.Uc;

/// <summary>The generated classes of one UnrealScript package, in a stable order (sorted by class name).</summary>
public sealed class UcPackageInfo
{
    public UcPackageInfo(string name, UcClassInfo[] classes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Classes = classes ?? [];
    }

    public string Name { get; }

    public UcClassInfo[] Classes { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Lookup over the generated classes of several packages (for example Core, Engine and SBase): classes by path or
/// name, class references, and coverage units by id or <c>[Uc]</c> path. Build one where it is needed; there is no
/// global registry.
/// </summary>
public sealed class UcClassIndex
{
    private readonly List<UcClassInfo> _classes = new();
    private readonly Dictionary<string, UcClassInfo> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UcClassInfo?> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, UcClassInfo> _byType = new();
    private readonly Dictionary<string, UcFunctionInfo> _functionsByUcPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UcStateInfo> _stateCodeByUcPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UcFunctionInfo> _functionsByUnitId = new(StringComparer.OrdinalIgnoreCase);

    public UcClassIndex(params ReadOnlySpan<UcPackageInfo> packages)
    {
        foreach (UcPackageInfo package in packages)
        {
            ArgumentNullException.ThrowIfNull(package);
            foreach (UcClassInfo c in package.Classes)
            {
                Add(c);
            }
        }
    }

    /// <summary>Every class, in package order then class order.</summary>
    public IReadOnlyList<UcClassInfo> Classes => _classes;

    /// <summary>A class by <c>Package.Class</c> or by bare class name (bare names that occur twice resolve to null).</summary>
    public UcClassInfo? Find(string pathOrName)
    {
        if (string.IsNullOrEmpty(pathOrName))
        {
            return null;
        }

        return _byPath.TryGetValue(pathOrName, out UcClassInfo? c) ? c : _byName.GetValueOrDefault(pathOrName);
    }

    public UcClassInfo? Find(ClassRef classRef) => classRef.IsNone ? null : Find(classRef.Path!);

    public UcClassInfo? Find(Type type) => _byType.GetValueOrDefault(type);

    /// <summary>The generated class of <paramref name="type"/> or of its nearest generated base type.</summary>
    public UcClassInfo? FindNearest(Type type)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
        {
            if (_byType.TryGetValue(t, out UcClassInfo? c))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// A function by its <c>[Uc]</c> path (<c>Pkg.Class[.State].Func</c>, or without the package) or by its
    /// unit id (<c>uc:...</c>, <c>nat:...</c>).
    /// </summary>
    public UcFunctionInfo? FindFunction(string pathOrUnitId)
    {
        if (string.IsNullOrEmpty(pathOrUnitId))
        {
            return null;
        }

        if (_functionsByUnitId.TryGetValue(pathOrUnitId, out UcFunctionInfo? byId))
        {
            return byId;
        }

        if (_functionsByUcPath.TryGetValue(pathOrUnitId, out UcFunctionInfo? byPath))
        {
            return byPath;
        }

        return WithPackage(pathOrUnitId) is { } qualified ? _functionsByUcPath.GetValueOrDefault(qualified) : null;
    }

    /// <summary>A state with code by its <c>[Uc]</c> path (<c>Pkg.Class.State@code</c>, package optional) or unit id.</summary>
    public UcStateInfo? FindStateCode(string pathOrUnitId)
    {
        if (string.IsNullOrEmpty(pathOrUnitId))
        {
            return null;
        }

        string path = pathOrUnitId.StartsWith("uc:", StringComparison.Ordinal)
            ? pathOrUnitId[3..].Replace(':', '.')
            : pathOrUnitId;
        if (_stateCodeByUcPath.TryGetValue(path, out UcStateInfo? state))
        {
            return state;
        }

        return WithPackage(path) is { } qualified ? _stateCodeByUcPath.GetValueOrDefault(qualified) : null;
    }

    private string? WithPackage(string path)
    {
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return null;
        }

        UcClassInfo? c = _byName.GetValueOrDefault(path[..dot]);
        return c is null ? null : c.Package + "." + path;
    }

    private void Add(UcClassInfo c)
    {
        if (!_byPath.TryAdd(c.Path, c))
        {
            throw new ArgumentException($"Class {c.Path} is listed twice.");
        }

        _classes.Add(c);
        _byType[c.Type] = c;
        _byName[c.Name] = _byName.ContainsKey(c.Name) ? null : c;
        foreach (UcFunctionInfo f in c.AllOwnFunctions())
        {
            _functionsByUcPath[f.UcPath] = f;
            _functionsByUnitId[f.UnitId] = f;
        }

        foreach (UcStateInfo s in c.States)
        {
            if (s.HasCode)
            {
                _stateCodeByUcPath[s.CodeUcPath] = s;
            }
        }
    }
}
