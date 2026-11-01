using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>One entry of a random, switch or sequence sound.</summary>
public readonly record struct SoundEntry(string? Sound, int Value);

/// <summary>A Sound of the converted content (svm.audio/1 and svm.mapaudio/1, docs/AUDIO.md).</summary>
public sealed class SoundDef
{
    public required string Path { get; init; }

    /// <summary>wave, stream, multichannel, random, switch or sequence.</summary>
    public required string Kind { get; init; }

    public int Flags { get; init; }

    /// <summary>The Sound's Gain (linear).</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>The Sound's own Radius, 0 when it has none.</summary>
    public float Radius { get; init; }

    public bool Loop { get; init; }

    public bool Positional { get; init; } = true;

    /// <summary>Absolute path of the wave or Ogg file, null for list sounds and missing streams.</summary>
    public string? File { get; init; }

    public float Duration { get; init; }

    public int LoopStart { get; init; } = -1;

    public int LoopEnd { get; init; } = -1;

    public int Frames { get; init; }

    public SoundEntry[] Entries { get; init; } = [];

    public bool IsList => Kind is "random" or "switch" or "sequence";
}

/// <summary>
/// The sounds a client can play: the shared library (<c>.content/shared/audio</c>) with the map's own table
/// (<c>svmap/&lt;Map&gt;/audio</c>) on top, which supplies the map's streamed sounds. Loads the audio files on first
/// use and keeps them.
/// </summary>
public sealed class AudioLibrary
{
    private readonly Dictionary<string, SoundDef> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string File, bool Loop), AudioStream?> _streams = new();

    private AudioLibrary(string sharedDir, string? mapDir)
    {
        SharedDir = sharedDir;
        MapDir = mapDir;
    }

    public string SharedDir { get; }

    public string? MapDir { get; }

    public MapAudioDoc? Map { get; private set; }

    public int Count => _sounds.Count;

    /// <summary>Files that failed to load, for reports.</summary>
    public List<string> LoadErrors { get; } = [];

    /// <summary>The shared library folder of a map folder (the audio folder of <see cref="Render.RenderMap.SharedRootOf"/>).</summary>
    public static string SharedDirFor(string mapFolder) =>
        System.IO.Path.Combine(OpenScctGame.Render.RenderMap.SharedRootOf(mapFolder), "audio");

    public static AudioLibrary Load(string sharedDir, string? mapFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedDir);
        string? mapDir = mapFolder is null ? null : System.IO.Path.Combine(mapFolder, "audio");
        var lib = new AudioLibrary(sharedDir, mapDir);
        lib.ReadShared(System.IO.Path.Combine(sharedDir, "audio.json"));
        string sidearms = System.IO.Path.Combine(sharedDir, "sidearms.json");
        if (File.Exists(sidearms)) lib.ReadShared(sidearms);
        string weapons = System.IO.Path.Combine(sharedDir, "weapons.json");
        if (File.Exists(weapons)) lib.ReadShared(weapons);
        if (mapDir is not null && System.IO.File.Exists(System.IO.Path.Combine(mapDir, "audio.json")))
        {
            lib.Map = MapAudioDoc.Load(System.IO.Path.Combine(mapDir, "audio.json"));
            lib.ReadMapSounds(System.IO.Path.Combine(mapDir, "audio.json"));
        }
        else if (mapDir is not null)
        {
            // Without the map's table the map plays none of its own sounds: no zone ambience, reverb, streamed
            // loops or client-only ambient actors. A maps rebuild removes the folder; audio-map puts it back.
            GD.PushWarning($"SVM_AUDIO the map has no audio table at {mapDir}: its ambience, reverb and streamed " +
                           $"sounds are silent (python -m svmbuild audio-map --maps {System.IO.Path.GetFileName(mapFolder)})");
        }

        return lib;
    }

    public SoundDef? Find(string? path) => path is not null && _sounds.TryGetValue(path, out SoundDef? d) ? d : null;

    /// <summary>Decode every possible leaf of a gun's random/switch/sequence sound before its first shot.</summary>
    public void Preload(string path)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Load(string name)
        {
            if (!visited.Add(name) || Find(name) is not { } def) return;
            if (def.IsList)
            {
                foreach (var entry in def.Entries) if (entry.Sound is { } child) Load(child);
            }
            else Stream(def, def.Loop);
        }
        Load(path);
    }

    /// <summary>The playable stream of a wave or streamed sound, looping or not; null when it has no file.</summary>
    public AudioStream? Stream(SoundDef def, bool loop)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def.File is null)
        {
            return null;
        }

        if (_streams.TryGetValue((def.File, loop), out AudioStream? cached))
        {
            return cached;
        }

        AudioStream? stream = null;
        string? error = null;
        try
        {
            if (def.File.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                AudioStreamOggVorbis ogg = AudioStreamOggVorbis.LoadFromFile(def.File);
                ogg.Loop = loop;
                stream = ogg;
            }
            else
            {
                byte[] bytes = WavChunks.WithoutEmptySampler(System.IO.File.ReadAllBytes(def.File));
                AudioStreamWav wav = AudioStreamWav.LoadFromBuffer(bytes);
                if (loop)
                {
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                    wav.LoopBegin = def.LoopStart >= 0 ? def.LoopStart : 0;
                    wav.LoopEnd = def.LoopEnd > 0 ? def.LoopEnd : def.Frames;
                }
                else
                {
                    // A one-shot plays once whatever the file's sampler chunk says (the loop comes from the Sound).
                    wav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
                }

                stream = wav;
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            error = e.Message;
            stream = null;
        }

        if (stream is null)
        {
            LoadErrors.Add($"{def.Path}: {error ?? def.File + " did not load"}");
        }

        _streams[(def.File, loop)] = stream;
        return stream;
    }

    /// <summary>Frees every loaded stream (the players using them must be stopped first).</summary>
    public void ReleaseStreams()
    {
        foreach (AudioStream? s in _streams.Values)
        {
            s?.Dispose();
        }

        _streams.Clear();
    }

    private void ReadShared(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement f) || f.GetString() != "svm.audio/1")
        {
            throw new InvalidDataException($"{path}: not an svm.audio/1 library");
        }

        foreach (JsonProperty p in root.GetProperty("sounds").EnumerateObject())
        {
            _sounds[p.Name] = Read(p.Name, p.Value, SharedDir);
        }
    }

    private void ReadMapSounds(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        foreach (JsonProperty p in doc.RootElement.GetProperty("sounds").EnumerateObject())
        {
            JsonElement v = p.Value;

            // A sound stored in the map package itself (a community map's music): a full record, files in the map folder.
            if (v.TryGetProperty("root", out JsonElement own) && own.GetString() == "map" && v.TryGetProperty("flags", out _))
            {
                _sounds[p.Name] = Read(p.Name, v, MapDir!);
                continue;
            }

            if (!v.TryGetProperty("file", out JsonElement file) || !v.TryGetProperty("root", out JsonElement root) ||
                root.GetString() != "map" || !_sounds.TryGetValue(p.Name, out SoundDef? shared))
            {
                continue;
            }

            // A streamed sound of this map's bank: the shared entry with the map's file and duration.
            _sounds[p.Name] = new SoundDef
            {
                Path = shared.Path, Kind = shared.Kind, Flags = shared.Flags, Volume = shared.Volume, Radius = shared.Radius,
                Loop = shared.Loop, Positional = shared.Positional, Entries = shared.Entries,
                File = System.IO.Path.GetFullPath(System.IO.Path.Combine(MapDir!, file.GetString()!)),
                Duration = v.TryGetProperty("duration", out JsonElement d) ? d.GetSingle() : 0f,
            };
        }
    }

    private static SoundDef Read(string path, JsonElement v, string baseDir)
    {
        var entries = new List<SoundEntry>();
        if (v.TryGetProperty("entries", out JsonElement es))
        {
            foreach (JsonElement e in es.EnumerateArray())
            {
                string? s = e.TryGetProperty("sound", out JsonElement sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() : null;
                entries.Add(new SoundEntry(s, e.GetProperty("value").GetInt32()));
            }
        }

        string kind = v.GetProperty("kind").GetString()!;
        string? file = kind == "wave" && v.TryGetProperty("file", out JsonElement fe)
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, fe.GetString()!))
            : null;
        return new SoundDef
        {
            Path = path,
            Kind = kind,
            Flags = v.GetProperty("flags").GetInt32(),
            Volume = v.GetProperty("volume").GetSingle(),
            Radius = v.GetProperty("radius").GetSingle(),
            Loop = v.GetProperty("loop").GetBoolean(),
            Positional = v.GetProperty("positional").GetBoolean(),
            File = file,
            Duration = v.TryGetProperty("duration", out JsonElement du) ? du.GetSingle() : 0f,
            LoopStart = v.TryGetProperty("loopStart", out JsonElement ls) ? ls.GetInt32() : -1,
            LoopEnd = v.TryGetProperty("loopEnd", out JsonElement le) ? le.GetInt32() : -1,
            Frames = v.TryGetProperty("frames", out JsonElement fr) ? fr.GetInt32() : 0,
            Entries = entries.ToArray(),
        };
    }
}

/// <summary>A sound actor of the map's audio.json (also the client-only AmbientSound actors).</summary>
public sealed record MapSoundActor(
    string Id,
    string Class,
    string Category,
    OpenScct.Core.FVector Location,
    string? AmbientSound,
    byte SoundVolume,
    float SoundRadius,
    byte SoundPitch,
    int LightType,
    float LightBrightness,
    byte SoundOcclusion = 0);

/// <summary>A zone of the map's audio.json: its ambience and its I3DL2 reverb parameters.</summary>
public sealed record MapSoundZone(string Id, string? AmbientSound, byte SoundVolume, byte SoundPitch, string? EffectClass,
    IReadOnlyDictionary<string, float> Effect);

/// <summary>svmap/&lt;Map&gt;/audio/audio.json (svm.mapaudio/1): actors and zones.</summary>
public sealed class MapAudioDoc
{
    public List<MapSoundActor> Actors { get; } = [];

    public Dictionary<string, MapSoundZone> Zones { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ZoneDist (ULevel, game 0x109ea770): <see cref="ZoneCount"/> x <see cref="ZoneCount"/> portal distances, row-major.</summary>
    public byte[] ZoneDist { get; private set; } = [];

    /// <summary>LevelInfo.Song: the music the engine starts when the map loads (TermD; the menu levels).</summary>
    public string? LevelSong { get; private set; }

    public int ZoneCount { get; private set; }

    /// <summary>The portal distance between two zone numbers; 0 when the map has no table (no zone term then).</summary>
    public int ZoneDistance(int a, int b) =>
        a >= 0 && b >= 0 && a < ZoneCount && b < ZoneCount ? ZoneDist[(a * ZoneCount) + b] : 0;

    public static MapAudioDoc Load(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement f) || f.GetString() != "svm.mapaudio/1")
        {
            throw new InvalidDataException($"{path}: not an svm.mapaudio/1 file");
        }

        var m = new MapAudioDoc();
        foreach (JsonElement a in root.GetProperty("actors").EnumerateArray())
        {
            JsonElement loc = a.GetProperty("location");
            if (a.GetProperty("class").GetString() is { } cls && cls.EndsWith("LevelInfo", StringComparison.Ordinal) &&
                a.TryGetProperty("sounds", out JsonElement levelSounds) && levelSounds.TryGetProperty("Song", out JsonElement song))
            {
                m.LevelSong = song.GetString();
            }

            m.Actors.Add(new MapSoundActor(
                a.GetProperty("id").GetString()!,
                a.GetProperty("class").GetString()!,
                a.GetProperty("category").GetString()!,
                new OpenScct.Core.FVector(loc.GetProperty("X").GetSingle(), loc.GetProperty("Y").GetSingle(), loc.GetProperty("Z").GetSingle()),
                a.TryGetProperty("ambientSound", out JsonElement s) ? s.GetString() : null,
                (byte)Num(a, "soundVolume", 128),
                Num(a, "soundRadius", 64f),
                (byte)Num(a, "soundPitch", 64),
                (int)Num(a, "lightType", 0),
                Num(a, "lightBrightness", 64f),
                (byte)Num(a, "soundOcclusion", 0)));
        }

        // The portal distance between zones (rows and columns by zone number), absent in older content.
        if (root.TryGetProperty("zoneDist", out JsonElement dist) && dist.ValueKind == JsonValueKind.Array)
        {
            int n = dist.GetArrayLength();
            m.ZoneDist = new byte[n * n];
            m.ZoneCount = n;
            int i = 0;
            foreach (JsonElement row in dist.EnumerateArray())
            {
                int j = 0;
                foreach (JsonElement v in row.EnumerateArray())
                {
                    if (j < n)
                    {
                        m.ZoneDist[(i * n) + j] = (byte)Math.Clamp(v.GetInt32(), 0, 255);
                    }

                    j++;
                }

                i++;
            }
        }

        foreach (JsonElement z in root.GetProperty("zones").EnumerateArray())
        {
            var effect = new Dictionary<string, float>(StringComparer.Ordinal);
            string? cls = null;
            if (z.TryGetProperty("effect", out JsonElement e))
            {
                cls = e.TryGetProperty("class", out JsonElement c) ? c.GetString() : null;
                if (e.TryGetProperty("params", out JsonElement ps))
                {
                    foreach (JsonProperty p in ps.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Number)
                        {
                            effect[p.Name] = p.Value.GetSingle();
                        }
                        else if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            effect[p.Name] = p.Value.GetBoolean() ? 1f : 0f;
                        }
                    }
                }
            }

            string id = z.GetProperty("id").GetString()!;
            m.Zones[id] = new MapSoundZone(id, z.TryGetProperty("ambientSound", out JsonElement s) ? s.GetString() : null,
                (byte)Num(z, "soundVolume", 128), (byte)Num(z, "soundPitch", 64), cls, effect);
        }

        return m;
    }

    private static float Num(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}
