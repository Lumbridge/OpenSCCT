using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>
/// The shaders that include svm_fog.gdshaderinc. It compiles responsive smoke only under
/// <see cref="Define"/>; without it, stubs return the values the smoke code gives while inactive
/// (svm_smoke_active false), so a match without responsive smoke draws the same image without that code.
/// A match picks the variant with <see cref="Use"/> before it builds materials. Each shader is one object whose code
/// follows the flags, so rules that arrive later switch the materials already built.
/// Portal fog (<see cref="PortalFogDefine"/>) is the same kind of switch, set per map by <see cref="UsePortalFog"/>.
/// </summary>
public static class ResponsiveSmokeShaders
{
    public const string Define = "SVM_RESPONSIVE_SMOKE";
    public const string PortalFogDefine = "SVM_PORTAL_FOG";
    private const int SmokeFlag = 1, PortalFogFlag = 2;
    private static readonly Regex ShaderType = new(@"\bshader_type\s+\w+\s*;", RegexOptions.CultureInvariant);
    private static readonly System.Threading.Lock s_lock = new();
    private static readonly Dictionary<string, Shader> s_byCode = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Shader> s_byPath = new(StringComparer.Ordinal);
    private static readonly Dictionary<Shader, Entry> s_entries = new(ReferenceEqualityComparer.Instance);
    // Portal fog stays compiled until a map decides, so a view nothing decided for still draws it.
    private static int s_flags = PortalFogFlag;

    private sealed class Entry(string code)
    {
        public string Code { get; } = code;
        public int Flags;
    }

    /// <summary>
    /// The process-wide Shader of <paramref name="code"/> (without the define), made (named <paramref name="name"/>,
    /// then given to <paramref name="init"/>) on first use. Callers must not change it: other materials share it.
    /// </summary>
    public static Shader Shared(string code, string name, Action<Shader>? init = null)
    {
        lock (s_lock)
        {
            s_used?.Add(code);
            if (s_byCode.TryGetValue(code, out Shader? shader))
            {
                // Made ahead by Prewarm, which knows neither the name nor what init sets.
                if (s_ahead.Remove(shader))
                {
                    shader.ResourceName = name;
                    init?.Invoke(shader);
                }

                return Refresh(shader);
            }

            shader = Make(code, name);
            init?.Invoke(shader);
            s_byCode[code] = shader;
            return shader;
        }
    }

    private static readonly HashSet<Shader> s_ahead = new(ReferenceEqualityComparer.Instance);
    private static HashSet<string>? s_used;
    private static string? s_listPath;
    private static string[] s_listed = [];
    private static int s_listedFlags = -1;
    private static System.Threading.Tasks.Task? s_prewarm;

    /// <summary>SVM_SHADER_PREWARM=0 makes every shader where it is first asked for, as before (a measurement switch).</summary>
    private static bool PrewarmEnabled { get; } = System.Environment.GetEnvironmentVariable("SVM_SHADER_PREWARM") != "0";

    /// <summary>The file <see cref="Prewarm"/> reads: the flags and the codes of a map's last load.</summary>
    private sealed record ShaderList(int Flags, string[] Codes);

    /// <summary>
    /// Makes the shaders the last load of <paramref name="map"/> asked <see cref="Shared"/> for, on worker threads,
    /// while the load goes on. Godot makes the server's shader when its RID is first asked for, on the thread that asks
    /// (20 ms each with the world template: its code parsed, its variants read from the shader cache or compiled), and
    /// its renderer allows that from any thread. Made one by one as materials first use them, they were 3.5 s of
    /// AquaE's load on the main thread. A material that needs one before the workers reach it waits for it.
    /// The list is a file in the user folder (load_shaders), written by <see cref="SaveList"/>. It holds the flags of
    /// that load too, set here so the shaders are made as that load made them; a match with other rules sets its own
    /// flags afterwards as always, and then only loses the time saved. Call it as early in a load as the map is known.
    /// </summary>
    public static void Prewarm(string map)
    {
        if (!PrewarmEnabled) return;
        ShaderList? list = null;
        string path = System.IO.Path.Combine(OS.GetUserDataDir(), "load_shaders", map + ".json");
        try
        {
            if (System.IO.File.Exists(path))
            {
                list = System.Text.Json.JsonSerializer.Deserialize<ShaderList>(System.IO.File.ReadAllBytes(path));
            }
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            list = null;
        }

        // A map this machine has not loaded yet: the list the build carries for it (BundledList).
        list ??= BundledList(map);
        string[] codes = list?.Codes ?? [];
        if (list is not null && codes.Length != 0)
        {
            SetFlag(SmokeFlag, (list.Flags & SmokeFlag) != 0);
            SetFlag(PortalFogFlag, (list.Flags & PortalFogFlag) != 0);
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var made = new List<Shader>();
        lock (s_lock)
        {
            s_listPath = path;
            s_listed = codes;
            s_listedFlags = list?.Flags ?? -1;
            s_used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string code in codes)
            {
                if (string.IsNullOrEmpty(code) || s_byCode.ContainsKey(code)) continue;
                Shader shader = Make(code, "svm prewarmed");
                s_byCode[code] = shader;
                s_ahead.Add(shader);
                made.Add(shader);
            }
        }

        if (made.Count != 0)
        {
            s_prewarm = System.Threading.Tasks.Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.ForEach(made,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount - 2) },
                    shader => _ = shader.GetRid());
                OpenScctGame.Diagnostics.LoadTimer.Add("shader-prewarm-threads", started);
            });
        }

        OpenScctGame.Diagnostics.LoadTimer.Add("shader-prewarm", started);
    }

    /// <summary>Where the build keeps the lists it carries: one gzip-compressed <see cref="ShaderList"/> a map.</summary>
    private const string BundledFolder = "res://LoadShaders/";

    /// <summary>
    /// SVM_SHADER_LIST_OUT=folder (release tooling, tools/ci/bake-shader-lists.ps1): every load also writes its list
    /// there, compressed, as the file a build carries; <see cref="Baking"/> then ends the game.
    /// </summary>
    private static readonly string? s_bakeFolder = System.Environment.GetEnvironmentVariable("SVM_SHADER_LIST_OUT") is { Length: > 0 } folder ? folder : null;

    /// <summary>True while the game runs only to write a map's shader list (SVM_SHADER_LIST_OUT).</summary>
    public static bool Baking => s_bakeFolder is not null;

    /// <summary>
    /// The list the build carries for a map (res://LoadShaders/&lt;map&gt;.json.gz, written by the bake at release time
    /// from a load with first-run settings), or null. It lets the first load of a map on a machine make its shaders on
    /// worker threads as every later load does. A list from other settings or an older template only names shaders the
    /// load does not ask for: time lost on worker threads, nothing drawn differently.
    /// </summary>
    private static ShaderList? BundledList(string map)
    {
        if (Baking) return null;
        string path = BundledFolder + map + ".json.gz";
        try
        {
            if (!FileAccess.FileExists(path)) return null;
            using var packed = new System.IO.MemoryStream(FileAccess.GetFileAsBytes(path));
            using var gzip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionMode.Decompress);
            return System.Text.Json.JsonSerializer.Deserialize<ShaderList>(gzip);
        }
        catch (Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the list <see cref="Prewarm"/> reads, when the load asked for other shaders than it holds.</summary>
    public static void SaveList()
    {
        string? path;
        string[] used;
        int flags;
        lock (s_lock)
        {
            if (s_used is null || s_listPath is null) return;
            path = s_listPath;
            used = [.. s_used];
            flags = s_flags;
            bool same = flags == s_listedFlags && used.Length == s_listed.Length && s_used.IsSupersetOf(s_listed);
            s_used = null;
            s_listPath = null;
            if (same && !Baking) return;
        }

        if (s_bakeFolder is not null)
        {
            Array.Sort(used, StringComparer.Ordinal);
            System.IO.Directory.CreateDirectory(s_bakeFolder);
            using System.IO.FileStream file = System.IO.File.Create(System.IO.Path.Combine(s_bakeFolder, System.IO.Path.GetFileNameWithoutExtension(path) + ".json.gz"));
            using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.SmallestSize);
            System.Text.Json.JsonSerializer.Serialize(gzip, new ShaderList(flags, used));
            return;
        }

        try
        {
            Array.Sort(used, StringComparer.Ordinal);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new ShaderList(flags, used)));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            // The next load makes its shaders one by one again.
        }
    }

    /// <summary>A process-wide copy of the .gdshader file at <paramref name="path"/> that follows the flag.</summary>
    public static Shader Load(string path)
    {
        lock (s_lock)
        {
            if (s_byPath.TryGetValue(path, out Shader? shader)) return Refresh(shader);
            shader = Make(GD.Load<Shader>(path).Code, path);
            s_byPath[path] = shader;
            return shader;
        }
    }

    /// <summary><paramref name="shader"/> with the current variant's code, for callers that keep their own cache.</summary>
    public static Shader Current(Shader shader)
    {
        lock (s_lock) return Refresh(shader);
    }

    /// <summary>The code <paramref name="shader"/> was made from, without the defines.</summary>
    public static string BaseCode(Shader shader)
    {
        lock (s_lock) return s_entries.TryGetValue(shader, out Entry? entry) ? entry.Code : shader.Code;
    }

    /// <summary>True when the shaders compile responsive smoke (<see cref="Use"/>).</summary>
    public static bool Enabled
    {
        get
        {
            lock (s_lock) return (s_flags & SmokeFlag) != 0;
        }
    }

    /// <summary>True when the shaders compile portal fog (<see cref="UsePortalFog"/>).</summary>
    public static bool PortalFogEnabled
    {
        get
        {
            lock (s_lock) return (s_flags & PortalFogFlag) != 0;
        }
    }

    /// <summary>
    /// Compile responsive smoke into the shaders (or leave it out). Shaders that materials use switch now; idle cached
    /// ones switch when next handed out (<see cref="Shared"/>, <see cref="Load"/>, <see cref="Current"/>).
    /// </summary>
    public static void Use(bool enabled) => SetFlag(SmokeFlag, enabled);

    /// <summary>
    /// Compile portal fog into the shaders (or leave it out), switching them as <see cref="Use"/> does. MapBuilder
    /// decides it per map (<see cref="Post.PortalFogFrame.MapUses"/>) before the map's materials are built; a map
    /// without it never draws portal fog.
    /// </summary>
    public static void UsePortalFog(bool enabled) => SetFlag(PortalFogFlag, enabled);

    private static void SetFlag(int flag, bool enabled)
    {
        lock (s_lock)
        {
            if (s_flags == (enabled ? s_flags | flag : s_flags & ~flag)) return;
        }

        // The workers of Prewarm are making shaders with the flags as they are: a shader's code is not changed under them.
        try
        {
            s_prewarm?.Wait();
        }
        catch (AggregateException)
        {
            // A shader the workers failed on is made where it is used, as without the list.
        }

        lock (s_lock)
        {
            int flags = enabled ? s_flags | flag : s_flags & ~flag;
            if (s_flags == flags) return;
            s_flags = flags;
            foreach ((Shader shader, Entry entry) in s_entries)
            {
                // The registry's own wrapper holds one reference; a material holds another.
                if (GodotObject.IsInstanceValid(shader) && shader.GetReferenceCount() > 1) Apply(shader, entry);
            }
        }
    }

    private static Shader Make(string code, string name)
    {
        var shader = new Shader { ResourceName = name };
        // Only an include can reach svm_fog.gdshaderinc; other code never changes.
        if (!code.Contains("#include", StringComparison.Ordinal))
        {
            shader.Code = code;
            return shader;
        }

        var entry = new Entry(code);
        s_entries[shader] = entry;
        Apply(shader, entry);
        return shader;
    }

    private static Shader Refresh(Shader shader)
    {
        if (s_entries.TryGetValue(shader, out Entry? entry) && entry.Flags != s_flags) Apply(shader, entry);
        return shader;
    }

    private static void Apply(Shader shader, Entry entry)
    {
        entry.Flags = s_flags;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        shader.Code = s_flags == 0 ? entry.Code : WithDefines(entry.Code, s_flags);
        OpenScctGame.Diagnostics.LoadTimer.Add("shader-code", started);
    }

    // The defines go after the shader_type statement, which must stay the code's first statement.
    private static string WithDefines(string code, int flags)
    {
        System.Text.RegularExpressions.Match match = ShaderType.Match(code);
        string lines = "\n" + ((flags & SmokeFlag) != 0 ? "#define " + Define + "\n" : "")
            + ((flags & PortalFogFlag) != 0 ? "#define " + PortalFogDefine + "\n" : "");
        return match.Success ? code.Insert(match.Index + match.Length, lines) : lines + code;
    }
}
