using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.DiffTest.Golden;
using OpenScct.Game.Movement;

namespace OpenScct.DiffTest.Sim;

/// <summary>
/// The converted content a sim runner plays on: <c>.content/svmap/&lt;Map&gt;</c> and the shared characters
/// (<c>.content/shared/chars</c>), built locally by content/svmbuild from the install and never tracked. The root
/// is <c>SVM_CONTENT</c> when set, else <c>.content</c> in the repository. Maps and the animation table are loaded
/// once and shared by every run (both are read-only data).
/// </summary>
public sealed class SimContent
{
    private readonly Dictionary<string, SvMap> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private IPawnAnimationSource? _animations;

    public SimContent(string root)
    {
        Root = root;
    }

    /// <summary>The content root (the folder holding <c>svmap</c> and <c>shared</c>).</summary>
    public string Root { get; }

    public string CharsDir => Path.Combine(Root, "shared", "chars");

    public static SimContent Default()
    {
        string? env = Environment.GetEnvironmentVariable("SVM_CONTENT");
        return new SimContent(env is { Length: > 0 } ? env : Path.Combine(RepoPaths.Root, ".content"));
    }

    public string MapDir(string map) => Path.Combine(Root, "svmap", map);

    /// <summary>Whether <paramref name="map"/> is converted here (its manifest exists).</summary>
    public bool HasMap(string map) => File.Exists(Path.Combine(MapDir(map), "manifest.json"));

    /// <summary>The loaded map, cached.</summary>
    public SvMap Map(string map)
    {
        lock (_lock)
        {
            if (!_maps.TryGetValue(map, out SvMap? loaded))
            {
                if (!HasMap(map))
                {
                    throw new InvalidOperationException($"{map} is not converted under {Root} (run content/svmbuild)");
                }

                loaded = SvMap.Load(MapDir(map));
                _maps[map] = loaded;
            }

            return loaded;
        }
    }

    /// <summary>
    /// The pawns' clip table: the converted SPerso packages when present (frame counts, rates and root tracks),
    /// else the built-in table of the movement port.
    /// </summary>
    public IPawnAnimationSource Animations
    {
        get
        {
            lock (_lock)
            {
                _animations ??= Directory.Exists(CharsDir) ? PawnAnimationSource.FromContent(CharsDir) : PawnAnimationSource.Default;
                return _animations;
            }
        }
    }

    /// <summary>Whether the clip table comes from the converted characters (and not the built-in fallback).</summary>
    public bool HasCharacters => Directory.Exists(CharsDir);
}
