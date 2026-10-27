using System;
using System.Collections.Generic;
using OpenScct.Content.Actors;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Content.Loading;

/// <summary>
/// Maps original class paths (<c>Package.Class</c>, matched ignoring ASCII case) to the C# classes that create their
/// actors: the generated class of every package registered with <see cref="RegisterPackage"/> (fields, exact class
/// defaults and metadata, plus whatever behaviour is ported in its partial files), and explicit registrations that
/// replace them (<see cref="Register{T}"/>). <see cref="MapLoader"/> creates an <see cref="UnportedActor"/> marker for
/// classes with no C# class at all.
/// </summary>
public sealed class ActorClassRegistry
{
    private readonly Dictionary<string, Entry> _entries = new(AsciiCaseComparer.Instance);
    private readonly List<string> _order = new();

    /// <summary>A registry with the generated actor classes of Core and Engine (Engine.PlayerStart, Engine.Mover...).</summary>
    public static ActorClassRegistry CreateDefault() => Create(CorePackage.Info, EnginePackage.Info);

    /// <summary>A registry with the generated actor classes of <paramref name="packages"/>, in order.</summary>
    public static ActorClassRegistry Create(params ReadOnlySpan<UcPackageInfo> packages)
    {
        var registry = new ActorClassRegistry();
        foreach (UcPackageInfo package in packages)
        {
            registry.RegisterPackage(package);
        }

        return registry;
    }

    /// <summary>Registered class paths in registration order.</summary>
    public IReadOnlyList<string> ClassPaths => _order;

    /// <summary>
    /// Registers every concrete actor class of a generated package (abstract classes and non-actors are skipped).
    /// Classes registered before keep their registration unless <paramref name="replace"/> is set. Returns the
    /// number of classes added or replaced.
    /// </summary>
    public int RegisterPackage(UcPackageInfo package, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        int count = 0;
        foreach (UcClassInfo info in package.Classes)
        {
            if (!info.IsActor || info.IsAbstract || info.Factory is null)
            {
                continue;
            }

            if (replace || !_entries.ContainsKey(info.Path))
            {
                Func<UObject> factory = info.Factory;
                Set(info.Path, new Entry(() => (Actor)factory(), info, Generated: true));
                count++;
            }
        }

        return count;
    }

    /// <summary>Registers (or replaces) the class that creates actors of <paramref name="classPath"/>.</summary>
    public void Register<T>(string classPath)
        where T : Actor, new() => Register(classPath, static () => new T());

    /// <summary>Registers (or replaces) a factory for <paramref name="classPath"/>.</summary>
    public void Register(string classPath, Func<Actor> factory)
    {
        ArgumentNullException.ThrowIfNull(classPath);
        ArgumentNullException.ThrowIfNull(factory);
        Set(classPath, new Entry(factory, null, Generated: false));
    }

    public bool IsKnown(string classPath) => _entries.ContainsKey(classPath);

    /// <summary>
    /// True when the class was registered from a generated package (its actors are the generated class, ported
    /// partly or not at all) rather than by an explicit <see cref="Register{T}"/>.
    /// </summary>
    public bool IsGenerated(string classPath) => _entries.TryGetValue(classPath, out Entry? e) && e.Generated;

    /// <summary>The generated metadata of a class registered from a package, or null.</summary>
    public UcClassInfo? GeneratedClass(string classPath) => _entries.TryGetValue(classPath, out Entry? e) ? e.Info : null;

    /// <summary>Creates the registered class, or returns false for an unknown class.</summary>
    public bool TryCreate(string classPath, out Actor? actor)
    {
        if (_entries.TryGetValue(classPath, out Entry? entry))
        {
            actor = entry.Factory() ?? throw new InvalidOperationException($"The factory for {classPath} returned null.");
            return true;
        }

        actor = null;
        return false;
    }

    private void Set(string classPath, Entry entry)
    {
        if (!PropJson.IsClassPath(classPath))
        {
            throw new ArgumentException($"'{classPath}' is not Package.Class.", nameof(classPath));
        }

        if (!_entries.ContainsKey(classPath))
        {
            _order.Add(classPath);
        }

        _entries[classPath] = entry;
    }

    private sealed record Entry(Func<Actor> Factory, UcClassInfo? Info, bool Generated);
}
